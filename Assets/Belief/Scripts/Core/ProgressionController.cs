using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using Belief.Data;
using Belief.Events;
using Belief.Presentation;

namespace Belief.Core
{
    /// <summary>씬 전환 간 유지되는 진행 상태. 쓰기는 ProgressionController 전용.</summary>
    public class GameProgressState
    {
        public readonly HashSet<string> CompletedStageIds = new HashSet<string>();
        public readonly HashSet<string> CompletedMissionIds = new HashSet<string>();
        public int CurrentStageIndex = -1;
        public bool MetropolisUnlocked;
    }

    /// <summary>
    /// 씬(구역) 진행을 담당하는 유일한 영속 오브젝트(DontDestroyOnLoad). 각 구역의
    /// GameInstaller.mission(싱글 필드, 항상 완료되지 않는 더미로 배선됨)과는 별개로,
    /// ProgressionData에 지정된 진짜 목표(MissionData 2~4개)를 매 턴 종료마다 직접 재평가해
    /// 구역 완료 여부를 판정한다 - "미션 완료 상태"(기존 HUD MISSION 패널)와
    /// "씬 진행 조건"(이 클래스)을 분리해서 관리한다.
    ///
    /// 미션/구역 완료는 즉시 진행되지 않는다 - 목표가 완료되는 순간 턴 진행을 얼려두고(FreezeTurnAdvance)
    /// Pending* 이벤트만 발행한 뒤, HUD가 보여주는 확인 팝업에서 플레이어가 버튼을 눌러야만
    /// Confirm* 메서드가 실제로 턴을 리셋하거나 다음 씬을 로드한다.
    /// </summary>
    public class ProgressionController : MonoBehaviour
    {
        public static ProgressionController Instance { get; private set; }

        public ProgressionData Data { get; private set; }
        public GameProgressState Progress { get; } = new GameProgressState();

        /// <summary>HUD가 구독해 현재 진행 중인 미션 표시(진행도 등)를 갱신한다.</summary>
        public event Action ObjectivesChanged;

        /// <summary>목표 하나가 완료되었지만 아직 확인 대기 중일 때(구역의 마지막 목표는 아님) 발행된다.
        /// HUD는 이 시점에 "MISSION COMPLETE" 팝업을 띄우고 ConfirmMissionComplete 호출을 기다린다.</summary>
        public event Action<MissionData> ObjectiveCompletedPendingConfirm;

        /// <summary>구역의 마지막 목표가 완료되어 다음 구역으로 넘어갈 수 있지만 아직 확인 대기 중일 때
        /// 발행된다. HUD는 "ZONE COMPLETE" 팝업을 띄우고 ConfirmZoneComplete 호출을 기다린다.</summary>
        public event Action StageCompletedPendingConfirm;

        GameInstaller currentInstaller;
        StageInfo currentStage;
        Action<TurnEndedEvent> turnEndedHandler;

        /// <summary>씬(구역)에 원래 설정된 MaxTurns - 미션에 개별 maxTurns가 없을 때의 대체값.
        /// 씬이 로드된 시점(=아직 아무 미션 전환도 일어나기 전)의 값을 그대로 고정해서 쓴다.</summary>
        int sceneDefaultMaxTurns;

        /// <summary>미션 완료/구역 완료 팝업이 확인 대기 중인지 여부 - 대기 중에는 재평가를 건너뛴다.</summary>
        bool awaitingConfirmation;

        /// <summary>구역 완료 확인 시 로드할 다음 단계 인덱스 - 완료 판정 시점에 고정해 둔다.</summary>
        int pendingNextStageIndex;

        Coroutine deferredEvaluateRoutine;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Bootstrap()
        {
            if (Instance != null) return;
            var go = new GameObject("ProgressionController");
            DontDestroyOnLoad(go);
            go.AddComponent<ProgressionController>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            Data = Resources.Load<ProgressionData>("ProgressionData");
            if (Data == null)
                Debug.LogError("ProgressionController: Resources/ProgressionData.asset을 찾을 수 없습니다.");

            turnEndedHandler = _ => ReevaluateCurrentStage();
            SceneManager.sceneLoaded += OnSceneLoaded;
            // 씬 로드 이벤트를 놓쳤을 경우를 대비한 안전망 - 이미 로드된 씬이 있다면 즉시 한 번 시도한다.
            OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            DetachFromCurrentInstaller();
            awaitingConfirmation = false;

            if (Data == null || Data.stages == null) return;

            currentStage = Data.stages.FirstOrDefault(s => s.sceneName == scene.name);
            if (currentStage == null) return;

            Progress.CurrentStageIndex = Array.IndexOf(Data.stages, currentStage);

            currentInstaller = FindFirstObjectByType<GameInstaller>();
            if (currentInstaller == null) return;

            sceneDefaultMaxTurns = currentInstaller.Turns.MaxTurns;

            // 이 구역의 첫 미션이 자체 turnLimit을 지정했다면 시작 시점부터 그 값을 적용한다.
            var firstObjective = CurrentObjective();
            if (firstObjective != null && firstObjective.turnLimit > 0 && firstObjective.turnLimit != currentInstaller.Turns.MaxTurns)
                currentInstaller.Turns.ResetForNewMission(firstObjective.turnLimit);

            currentInstaller.EventBus.Subscribe(turnEndedHandler);

            // 이 콜백(SceneManager.sceneLoaded, 그리고 이를 대신 호출하는 Awake 안전망)은 새 씬의
            // 모든 Start()보다 먼저 실행된다 - 여기서 곧바로 재평가하면, 씬 시작부터 이미 만족된 목표가
            // 있을 때 HudPresenter가 Pending 이벤트를 구독하기 전에 요청이 발행되어 유실되고, 확인 팝업이
            // 뜨지 않은 채 턴만 얼어붙어 진행이 멈춘다. 한 프레임 미뤄 모든 Start()가 끝난 뒤 평가한다.
            if (deferredEvaluateRoutine != null) StopCoroutine(deferredEvaluateRoutine);
            deferredEvaluateRoutine = StartCoroutine(DeferredInitialEvaluate());
        }

        IEnumerator DeferredInitialEvaluate()
        {
            yield return null;
            deferredEvaluateRoutine = null;
            ReevaluateCurrentStage();
        }

        void DetachFromCurrentInstaller()
        {
            if (currentInstaller != null) currentInstaller.EventBus.Unsubscribe(turnEndedHandler);
            currentInstaller = null;
        }

        void ReevaluateCurrentStage()
        {
            if (currentStage == null || currentInstaller == null || awaitingConfirmation) return;

            var context = new MissionEvaluationContext(
                currentInstaller.Locations, currentInstaller.Npcs, currentInstaller.Turns.DeliveredInformationCards);

            // failureConditions는 "지금 목표인 미션"에 한해서만 검사한다 - GameInstaller.instantFailCondition
            // (씬 레벨, 항상 검사됨)과는 별개의 추가 판정 레이어. 완료 판정보다 먼저 봐서, 실패와 완료가
            // 같은 턴에 동시에 충족되면 실패가 우선하도록 한다.
            var current = CurrentObjective();
            if (current != null && current.IsAnyFailureConditionMet(context))
            {
                currentInstaller.Turns.FreezeTurnAdvance();
                var installer = currentInstaller;
                installer.EventBus.Publish(new GameOverEvent(false));
                return;
            }

            MissionData newlyCompleted = null;
            bool allDone = true;
            foreach (var objective in EffectiveObjectives())
            {
                bool done = objective.GetSuccessProgress(context) >= objective.SuccessTarget;
                // HashSet.Add는 새로 추가됐을 때만 true를 반환한다 - 완료 판정이 한 번만 발생하도록 보장.
                if (done && Progress.CompletedMissionIds.Add(objective.missionId))
                    newlyCompleted = objective;
                allDone &= done;
            }

            if (newlyCompleted == null)
            {
                // 완료는 없지만 진행도(X/Y)는 바뀌었을 수 있다 - HUD 갱신만 알린다.
                ObjectivesChanged?.Invoke();
                return;
            }

            // 확인 팝업이 뜨기 전까지 턴이 계속 흘러가거나 이 시점의 턴 소진이 "미션 실패"로 잘못
            // 판정되지 않도록, 이번 턴 종료 처리의 증가/게임오버 판정을 얼려 둔다.
            currentInstaller.Turns.FreezeTurnAdvance();

            if (allDone && !Progress.CompletedStageIds.Contains(currentStage.stageId))
            {
                Progress.CompletedStageIds.Add(currentStage.stageId);
                int nextIndex = Progress.CurrentStageIndex + 1;

                if (nextIndex < Data.stages.Length)
                {
                    // 다음 구역이 있다 - ZONE COMPLETE 팝업에서 [다음 구역] 확인을 기다린다.
                    awaitingConfirmation = true;
                    pendingNextStageIndex = nextIndex;
                    StageCompletedPendingConfirm?.Invoke();
                }
                else
                {
                    // 마지막 단계(대도시) 완료 - 넘어갈 다음 구역이 없으므로 팝업 확인 없이 곧바로
                    // 기존 GameOverEvent/HUD 오버레이를 재사용해 MVP 종료 화면으로 삼는다.
                    var installer = currentInstaller;
                    DetachFromCurrentInstaller();
                    installer.EventBus.Publish(new GameOverEvent(true));
                }
                return;
            }

            // 같은 구역 안에서 다음 미션이 남아 있다 - MISSION COMPLETE 팝업에서 [다음] 확인을 기다린다.
            awaitingConfirmation = true;
            ObjectiveCompletedPendingConfirm?.Invoke(newlyCompleted);
        }

        /// <summary>HUD의 "MISSION COMPLETE" 팝업에서 [다음] 버튼을 눌렀을 때 호출된다 - 이 시점에야
        /// 비로소 다음 미션 기준으로 턴을 리셋한다(씬 전환 없음, 같은 구역 안에서의 전환).</summary>
        public void ConfirmMissionComplete()
        {
            if (!awaitingConfirmation || currentInstaller == null) return;
            awaitingConfirmation = false;

            var newObjective = CurrentObjective();
            int newMaxTurns = newObjective != null && newObjective.turnLimit > 0 ? newObjective.turnLimit : sceneDefaultMaxTurns;
            // GameInstaller.Mission(=TurnSystem.IsGameOver가 매 턴 참조하는 MissionSystem)은 생성 시점의
            // 미션 하나만 계속 추적했다 - 여기서 다음 미션으로 갈아끼우지 않으면 완료된 미션 1의
            // IsComplete==true가 영원히 남아 미션 2 첫 턴에 곧바로 GameOverEvent(true)가 잘못 발행된다.
            if (newObjective != null)
                currentInstaller.Mission.LoadMission(newObjective);
            currentInstaller.Turns.ResetForNewMission(newMaxTurns);
            PlaybackDirector.Instance?.SkipAll();
            ObjectivesChanged?.Invoke();
        }

        /// <summary>HUD의 "ZONE COMPLETE" 팝업에서 [다음 구역] 버튼을 눌렀을 때 호출된다 - 이 시점에야
        /// 비로소 다음 Scene을 로드한다. 턴 초기화는 다음 씬의 GameInstaller.Awake()가 처리한다.</summary>
        public void ConfirmZoneComplete()
        {
            if (!awaitingConfirmation) return;
            awaitingConfirmation = false;

            int nextIndex = pendingNextStageIndex;
            var installer = currentInstaller;
            DetachFromCurrentInstaller();

            if (nextIndex == Data.stages.Length - 1) Progress.MetropolisUnlocked = true;
            SceneManager.LoadScene(Data.stages[nextIndex].sceneName);
        }

        /// <summary>HUD의 "MISSION FAILED" 팝업에서 [재시작] 버튼을 눌렀을 때 호출된다 - 목표 완료
        /// 진행(CompletedMissionIds)은 건드리지 않고, 지금 진행 중인 미션을 같은 maxTurns로 처음부터
        /// 다시 시작한다. 구역 내 세계 상태(NPC 위치/Belief/전달 기록/보유 카드)는 그대로 유지된다.</summary>
        public void RestartCurrentMission()
        {
            if (currentInstaller == null) return;

            var objective = CurrentObjective();
            int maxTurns = objective != null && objective.turnLimit > 0 ? objective.turnLimit : sceneDefaultMaxTurns;
            currentInstaller.Turns.ResetForNewMission(maxTurns);
            PlaybackDirector.Instance?.SkipAll();
            ObjectivesChanged?.Invoke();
        }

        public IReadOnlyList<(string title, bool done)> CurrentObjectiveStatus()
        {
            var list = new List<(string, bool)>();
            if (currentStage == null) return list;
            foreach (var objective in EffectiveObjectives())
                list.Add((objective.displayTitle, Progress.CompletedMissionIds.Contains(objective.missionId)));
            return list;
        }

        /// <summary>플레이어에게 지금 보여줘야 할 미션 하나 - 이 단계에서 아직 완료 안 된 것 중 첫 번째.
        /// 전부 완료됐으면(확인 대기/씬 전환 직전) null.</summary>
        public MissionData CurrentObjective()
        {
            if (currentStage == null) return null;
            foreach (var objective in EffectiveObjectives())
                if (!Progress.CompletedMissionIds.Contains(objective.missionId))
                    return objective;
            return null;
        }

        /// <summary>이 구역의 목표 목록 - 현재 GameInstaller에 StageData가 지정되어 있고 missions가
        /// 채워져 있으면 그쪽을 우선 사용(startMission을 맨 앞으로 정렬)하고, 아니면 기존
        /// ProgressionData.StageInfo.objectives를 그대로 쓴다(하위 호환). 완료 판정 로직 자체
        /// (GetSuccessProgress 등)는 건드리지 않는다 - 목표 배열의 출처만 바꾼다.</summary>
        MissionData[] EffectiveObjectives()
        {
            var stageData = currentInstaller != null ? currentInstaller.StageAsset : null;
            if (stageData != null && stageData.missions != null && stageData.missions.Length > 0)
                return OrderStartFirst(stageData.missions, stageData.startMission);
            return currentStage.objectives;
        }

        static MissionData[] OrderStartFirst(MissionData[] missions, MissionData start)
        {
            if (start == null || Array.IndexOf(missions, start) < 0) return missions;

            var ordered = new MissionData[missions.Length];
            ordered[0] = start;
            int idx = 1;
            foreach (var m in missions)
                if (m != start) ordered[idx++] = m;
            return ordered;
        }

        public int CurrentObjectiveProgress()
        {
            var objective = CurrentObjective();
            if (objective == null || currentInstaller == null) return 0;

            var context = new MissionEvaluationContext(
                currentInstaller.Locations, currentInstaller.Npcs, currentInstaller.Turns.DeliveredInformationCards);
            return objective.GetSuccessProgress(context);
        }

        public string CurrentStageDisplayName => currentStage != null ? currentStage.displayName : "";
        public string CurrentStageIntroSubtitle => currentStage != null ? currentStage.introSubtitle : "";
    }
}
