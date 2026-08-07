using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Belief.Core;
using Belief.Data;
using Belief.Presentation.HUD;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Belief.EditorTools.Diagnostics
{
    /// <summary>
    /// Zone1부터 대도시까지 <b>실제 플레이 경로 그대로</b> 자동으로 한 번 진행시켜, 빌드 직전에
    /// 화면·씬 전환·결과 리포트가 정상인지 확인하는 도구. Editor 폴더에 있으므로 빌드에 포함되지 않는다.
    ///
    /// 게임 규칙을 우회하지 않는다 - 카드 선택과 전달/확산은 플레이어가 쓰는 것과 같은
    /// <see cref="Belief.Systems.TurnSystem"/> 진입점을 통하고, 결과 리포트는 아트의 진행 버튼을
    /// 실제로 눌러 넘긴다. 턴 수·미션 조건·카드 데이터는 건드리지 않는다.
    ///
    /// LLM은 켜지 않는다 - 씬의 thinkerMode(RuleOnly)를 그대로 쓰므로 <b>API 호출이 0회</b>이고
    /// 빌드될 게임과 동일한 판단 경로를 탄다.
    /// </summary>
    [InitializeOnLoad]
    public static class FullPlaythroughDriver
    {
        const string ActiveKey = "Belief.FullPlaythroughDriver.Active";
        const string StartScene = "Assets/Belief/Scenes/Zone1.unity";

        /// <summary>같은 미션을 이만큼 실패하면 진행이 막힌 것으로 보고 중단한다.</summary>
        const int MaxRetriesPerMission = 3;

        /// <summary>폭주 방지용 상한 - 게임 규칙이 아니라 도구의 관측 창이다.
        /// 씬이 IntegratedLlm이면 판단마다 실제 API 왕복이 있어 한 수가 몇 초씩 걸린다.</summary>
        const int MaxMoves = 400;
        const double MaxMinutes = 45.0;

        /// <summary>결과 리포트가 뜬 뒤 화면 저장과 버튼 클릭까지의 대기(초).
        /// <b>프레임 수로 세면 안 된다</b> - EditorApplication.update는 게임 렌더 프레임보다 훨씬 자주
        /// 돌아서, 프레임 기준으로는 캡처가 파일로 기록되기 전에 버튼이 눌리고 다음 화면이 찍힌다.</summary>
        const double CaptureDelay = 0.8;
        const double ClickDelay = 2.0;
        const double ResetDelay = 4.0;

        static string OutDir => Path.Combine(
            @"C:\Users\CHJ\AppData\Local\Temp\claude\C--Users-CHJ-Desktop-belief",
            "c4c439d0-042b-4970-ac3c-e612d8a0905b", "scratchpad", "playthrough");

        // Play 진입 시 도메인 리로드가 일어나면 static 필드가 전부 초기화된다 - 실행을 가로지르는
        // 값은 SessionState(에디터 세션 동안 유지)에 둔다. 첫 시도에서 deadline이 0으로 리셋돼
        // "시간 초과"로 즉시 끝나던 원인이 이것이었다.
        const string KeyDeadline = "Belief.FPD.Deadline";
        const string KeyMoves = "Belief.FPD.Moves";
        const string KeyCard = "Belief.FPD.Card";
        const string KeyTarget = "Belief.FPD.Target";
        const string KeyShots = "Belief.FPD.Shots";
        const string KeyMission = "Belief.FPD.Mission";
        const string KeyRetries = "Belief.FPD.Retries";

        static double Deadline
        {
            get { double.TryParse(SessionState.GetString(KeyDeadline, "0"), out double d); return d; }
            set { SessionState.SetString(KeyDeadline, value.ToString("R")); }
        }
        static int MoveCount { get { return SessionState.GetInt(KeyMoves, 0); } set { SessionState.SetInt(KeyMoves, value); } }
        static int CardIndex { get { return SessionState.GetInt(KeyCard, 0); } set { SessionState.SetInt(KeyCard, value); } }
        static int TargetIndex { get { return SessionState.GetInt(KeyTarget, 0); } set { SessionState.SetInt(KeyTarget, value); } }
        static int Shots { get { return SessionState.GetInt(KeyShots, 0); } set { SessionState.SetInt(KeyShots, value); } }
        static string LastMissionId { get { return SessionState.GetString(KeyMission, ""); } set { SessionState.SetString(KeyMission, value ?? ""); } }

        static int GetRetries(string mission)
        {
            foreach (var part in SessionState.GetString(KeyRetries, "").Split(';'))
            {
                int k = part.LastIndexOf('=');
                if (k > 0 && part.Substring(0, k) == mission && int.TryParse(part.Substring(k + 1), out int n)) return n;
            }
            return 0;
        }

        static void SetRetries(string mission, int count)
        {
            var kept = SessionState.GetString(KeyRetries, "").Split(';')
                .Where(p => p.Length > 0 && !(p.LastIndexOf('=') > 0 && p.Substring(0, p.LastIndexOf('=')) == mission));
            SessionState.SetString(KeyRetries, string.Join(";", kept.Concat(new[] { mission + "=" + count })));
        }

        // 프레임 단위 상태 - 도메인 리로드로 초기화돼도 무해하다.
        static Task<bool> pending;
        static double resultShownAt = -1;
        static bool capturedThisResult, clickedThisResult;

        /// <summary>플레이 도중 터진 예외·오류. 콘솔은 이 도구 자신의 진행 기록으로 금세 덮여
        /// 나중에 뒤져도 남아 있지 않으므로, 도는 동안 붙잡아 로그에 함께 남긴다.
        /// 같은 오류가 매 프레임 쏟아지는 경우가 흔해 첫 줄 기준으로 중복을 접는다.</summary>
        static readonly Dictionary<string, int> caughtErrors = new Dictionary<string, int>();
        const int MaxDistinctErrors = 30;

        static FullPlaythroughDriver()
        {
            EditorApplication.update -= OnUpdate;
            EditorApplication.update += OnUpdate;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            Application.logMessageReceived -= OnLogMessage;
            Application.logMessageReceived += OnLogMessage;
        }

        static void OnLogMessage(string condition, string stack, LogType type)
        {
            if (!EditorPrefs.GetBool(ActiveKey, false)) return;
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;

            string key = condition.Split('\n')[0];
            if (key.Length > 200) key = key.Substring(0, 200);
            if (caughtErrors.ContainsKey(key)) { caughtErrors[key]++; return; }
            if (caughtErrors.Count >= MaxDistinctErrors) return;

            caughtErrors[key] = 1;
            // 첫 발생만 스택과 함께 남긴다 - 같은 오류의 반복은 개수만 세어 끝에서 보고한다.
            Report($"  !! [{type}] {key}");
            string firstFrame = stack != null ? stack.Split('\n')[0] : "";
            if (!string.IsNullOrEmpty(firstFrame)) Report("       " + firstFrame);
        }

        [MenuItem("BELIEF/Diagnostics/전체 플레이 자동 진행", priority = 130)]
        static void Run()
        {
            if (Application.isPlaying)
            {
                EditorUtility.DisplayDialog("전체 플레이", "Play Mode를 먼저 끄세요.", "확인");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            bool go = EditorUtility.DisplayDialog("전체 플레이 자동 진행",
                "Zone1 → Zone2 → Zone3 → 대도시를 자동으로 한 번 플레이합니다.\n\n"
                + "• 씬에 저장된 thinkerMode를 그대로 씁니다 - IntegratedLlm이면 실제 API 요금이 발생합니다\n"
                + "• 씬 · 미션 · 카드 데이터를 수정하지 않습니다\n"
                + "• 결과 리포트가 뜰 때마다 화면을 저장합니다\n"
                + $"• 같은 미션 {MaxRetriesPerMission}회 실패 또는 {MaxMinutes}분 경과 시 중단합니다\n\n"
                + "중단하려면 Play를 끄세요.",
                "실행", "취소");
            if (!go) return;

            StartHeadless();
        }

        /// <summary>확인 대화상자 없이 시작한다 - 자동화(에디터 커맨드)에서 호출하는 진입점.
        /// 대화상자가 뜨면 사람이 눌러줄 때까지 멈추므로 별도로 둔다.</summary>
        public static void StartHeadless()
        {
            Directory.CreateDirectory(OutDir);
            foreach (var f in Directory.GetFiles(OutDir, "*.png")) File.Delete(f);
            string logPath = Path.Combine(OutDir, "log.txt");
            if (File.Exists(logPath)) File.Delete(logPath);

            EditorSceneManager.OpenScene(StartScene, OpenSceneMode.Single);

            // Game 뷰가 뒤에 있으면 렌더가 갱신되지 않아 ScreenCapture가 옛 화면을 저장한다.
            EditorApplication.ExecuteMenuItem("Window/General/Game");

            pending = null;
            resultShownAt = -1; capturedThisResult = false; clickedThisResult = false;
            caughtErrors.Clear();
            MoveCount = 0; CardIndex = 0; TargetIndex = 0; Shots = 0;
            LastMissionId = "";
            SessionState.SetString(KeyRetries, "");
            Deadline = EditorApplication.timeSinceStartup + MaxMinutes * 60.0;

            EditorPrefs.SetBool(ActiveKey, true);
            Report("=== 전체 플레이 자동 진행 시작 (RuleOnly) ===");
            EditorApplication.isPlaying = true;
        }

        [MenuItem("BELIEF/Diagnostics/전체 플레이 자동 진행 - 중단", priority = 131)]
        static void StopMenu() => Stop("수동 중단");

        static void Stop(string reason)
        {
            if (!EditorPrefs.GetBool(ActiveKey, false)) return;
            EditorPrefs.SetBool(ActiveKey, false);
            Report($"=== 종료: {reason} (진행 {MoveCount}수, 스크린샷 {Shots}장) ===");
            if (caughtErrors.Count == 0) Report("    오류/예외 없음");
            else
                foreach (var pair in caughtErrors.OrderByDescending(p => p.Value))
                    Report($"    오류 x{pair.Value}: {pair.Key}");
            Report($"    LLM 호출 {IntegratedLlmPilotSession.CallsUsed}회 / 거부 {IntegratedLlmPilotSession.CallsDenied}회"
                   + $" / 모드 {IntegratedLlmPilotSession.RunMode}");
            if (Application.isPlaying) EditorApplication.isPlaying = false;
        }

        static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredEditMode && EditorPrefs.GetBool(ActiveKey, false))
                Stop("Play Mode 종료");
        }

        static void OnUpdate()
        {
            if (!EditorPrefs.GetBool(ActiveKey, false)) return;
            if (!Application.isPlaying) return;

            // 에디터 창이 포커스를 잃어도 계속 돌아야 한다 - PlayerSettings는 건드리지 않고 런타임에만 켠다.
            if (!Application.runInBackground) Application.runInBackground = true;

            // Game 뷰가 앞에 없으면 다시 그리지 않아 ScreenCapture가 옛 프레임을 저장한다.
            // 스크린샷이 전부 첫 화면(브리핑)으로 찍히던 원인이라 매 틱 갱신을 요청한다.
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();

            if (EditorApplication.timeSinceStartup > Deadline) { Stop("시간 초과"); return; }
            if (MoveCount > MaxMoves) { Stop("수 상한 도달"); return; }

            // 진행 중인 행동(전달/확산 → NPC 판단 → 턴 종료)이 끝날 때까지 기다린다.
            if (pending != null)
            {
                if (!pending.IsCompleted) return;
                if (pending.IsFaulted)
                    Report("  ! 행동 중 예외: " + pending.Exception?.GetBaseException().Message);
                pending = null;
                return;
            }

            var installer = UnityEngine.Object.FindFirstObjectByType<GameInstaller>();
            var view = UnityEngine.Object.FindFirstObjectByType<HudView>();

            // 최종 승리 후 메인 메뉴로 돌아오면 완주한 것이다.
            if (installer == null)
            {
                if (SceneManager.GetActiveScene().name == "MainMenu")
                    Stop("완주 - 메인 메뉴 복귀");
                return;
            }
            if (installer.Turns == null || view == null) return;

            if (view.ResultScreenGo != null && view.ResultScreenGo.activeInHierarchy)
            {
                HandleResultScreen(view, installer);
                return;
            }

            resultShownAt = -1;
            capturedThisResult = false;
            clickedThisResult = false;

            // 결과가 확정됐는데 리포트가 아직 안 떴으면 연출이 재생 중이다 - 기다린다.
            if (installer.Turns.IsGameOver) return;

            PlayOneMove(installer);
        }

        /// <summary>리포트 화면을 저장한 뒤 아트의 진행 버튼(NEXT/RETRY)을 실제로 누른다.</summary>
        static void HandleResultScreen(HudView view, GameInstaller installer)
        {
            double now = EditorApplication.timeSinceStartup;
            if (resultShownAt < 0)
            {
                resultShownAt = now;
                capturedThisResult = false;
                clickedThisResult = false;
                return;
            }
            double elapsed = now - resultShownAt;

            bool won = view.ResultPanelImg != null && view.ResultPanelImg.sprite != null
                       && view.ResultPanelImg.sprite.name.Contains("성공");
            string mission = ProgressionController.Instance != null
                             && ProgressionController.Instance.CurrentObjective() != null
                ? ProgressionController.Instance.CurrentObjective().missionId : "-";
            string zone = SceneManager.GetActiveScene().name;

            if (!capturedThisResult)
            {
                if (elapsed < CaptureDelay) return;
                capturedThisResult = true;
                Shots++;
                string file = Path.Combine(OutDir,
                    $"{Shots:D2}_{zone}_{Sanitize(mission)}_{(won ? "win" : "lose")}.png");
                ScreenCapture.CaptureScreenshot(file);
                Report($"  [리포트] {zone} / {mission} / {(won ? "성공" : "실패")}"
                       + $" / DAY {installer.Turns.CurrentTurn} → {Path.GetFileName(file)}");

                // 스크린샷과 별개로 화면에 실제로 들어간 값을 남긴다 - 캡처가 어긋나도 검증이 가능하다.
                var turnsText = view.ResultTurnsText;
                if (turnsText != null)
                    Report($"      Turns=\"{turnsText.text}\" pos={turnsText.rectTransform.anchoredPosition}"
                           + $" 패널={(view.ResultPanelImg != null && view.ResultPanelImg.sprite != null ? view.ResultPanelImg.sprite.name : "-")}");
                var missionTurn = view.MissionTurnText;
                if (missionTurn != null) Report($"      HUD 턴카드=\"{missionTurn.text}\"");
                return;
            }

            if (!clickedThisResult)
            {
                if (elapsed < ClickDelay) return;

                if (!won)
                {
                    int n = GetRetries(mission) + 1;
                    SetRetries(mission, n);
                    if (n >= MaxRetriesPerMission)
                    {
                        Stop($"{mission} 미션을 {MaxRetriesPerMission}회 실패 - 진행이 막혔습니다");
                        return;
                    }
                }

                // 엔딩 화면에는 진행 버튼이 없다 - 남는 행동이 [메인 화면] 하나뿐이라 진행 탭은
                // 글자 없는 탭으로 덮이고 버튼도 잠긴다. 그때는 보조 버튼을 눌러야 한다.
                // (이걸 몰라 빈 버튼만 계속 누르며 엔딩 화면에서 무한히 맴돌았다.)
                var button = view.ResultPrimaryButton != null && view.ResultPrimaryButton.interactable
                    ? view.ResultPrimaryButton
                    : view.ResultSecondaryButton;
                if (button == null) { Stop("리포트의 진행 버튼을 찾을 수 없습니다"); return; }
                if (button == view.ResultSecondaryButton) Report("  (진행 버튼이 없어 [메인 화면]을 누릅니다 - 엔딩)");
                button.onClick.Invoke();
                clickedThisResult = true;
                return;
            }

            // 눌렀는데도 리포트가 계속 떠 있으면(전환 연출 등) 한 번 더 시도한다.
            if (elapsed > ResetDelay) resultShownAt = -1;
        }

        /// <summary>한 수 = 카드 한 장을 미션 조건에 등장하는 대상에게 쓴다.
        /// 카드와 대상은 각각 다른 주기로 회전시켜 여러 조합을 훑는다 - 특정 답을 강제하지 않는다.</summary>
        static void PlayOneMove(GameInstaller installer)
        {
            var turns = installer.Turns;
            var owned = turns.OwnedInformationCards;
            if (owned == null || owned.Count == 0) return;   // 카드 지급 대기

            var mission = ProgressionController.Instance != null
                ? ProgressionController.Instance.CurrentObjective() : null;
            if (mission != null && mission.missionId != LastMissionId)
            {
                LastMissionId = mission.missionId;
                Report($"[미션] {SceneManager.GetActiveScene().name} / {mission.missionId}"
                       + $" \"{mission.displayTitle}\" (제한 {turns.MaxTurns}일)"
                       + $"  판단={installer.CurrentThinkerMode}"
                       + $" 호출={IntegratedLlmPilotSession.CallsUsed} 거부={IntegratedLlmPilotSession.CallsDenied}");
            }

            var wantNpcs = new List<NpcData>();
            var wantLocs = new List<LocationData>();
            if (mission != null && mission.successConditions != null)
                foreach (var c in mission.successConditions) Collect(c, wantNpcs, wantLocs);

            var card = owned[CardIndex % owned.Count];
            turns.SelectCard(card);
            CardIndex++;

            if (card.cardType == InfoCardType.Deliver)
            {
                var pool = wantNpcs.Where(n => n != null && installer.Npcs.ContainsKey(n)).ToList();
                if (pool.Count == 0) pool = installer.Npcs.Keys.ToList();
                if (pool.Count == 0) { Report("  ! 전달할 NPC가 없습니다"); return; }

                var npc = pool[TargetIndex % pool.Count];
                TargetIndex++;
                MoveCount++;
                Report($"  DAY {turns.CurrentTurn}  전달 {card.cardId} → {npc.npcId}");
                pending = turns.PlayCardOnNpcAsync(installer.Npcs[npc]);
            }
            else
            {
                var pool = wantLocs.Where(l => l != null && installer.Locations.ContainsKey(l)).ToList();
                if (pool.Count == 0) pool = installer.Locations.Keys.ToList();
                if (pool.Count == 0) { Report("  ! 확산할 장소가 없습니다"); return; }

                var loc = pool[TargetIndex % pool.Count];
                TargetIndex++;
                MoveCount++;
                Report($"  DAY {turns.CurrentTurn}  확산 {card.cardId} → {loc.locationId}");
                pending = turns.PlayCardOnLocationAsync(loc);
            }
        }

        /// <summary>미션 조건에서 대상 NPC/장소를 모은다 - 복합 조건은 재귀로 내려간다.</summary>
        static void Collect(MissionConditionData c, List<NpcData> npcs, List<LocationData> locs)
        {
            switch (c)
            {
                case null: return;
                case AllOfConditions all:
                    if (all.subConditions != null) foreach (var s in all.subConditions) Collect(s, npcs, locs);
                    return;
                case AnyOfConditions any:
                    if (any.subConditions != null) foreach (var s in any.subConditions) Collect(s, npcs, locs);
                    return;
                case NpcAtLocationCondition x: Add(npcs, x.targetNpc); Add(locs, x.targetLocation); return;
                case NpcAnyBeliefReachedCondition x: Add(npcs, x.targetNpc); return;
                case NpcBeliefReachedCondition x: Add(npcs, x.targetNpc); return;
                case NpcsLeaveLocationCondition x: AddAll(npcs, x.watchedNpcs); Add(locs, x.watchedLocation); return;
                case NpcsGatherAtLocationCondition x: AddAll(npcs, x.watchedNpcs); Add(locs, x.watchedLocation); return;
                case LocationRumorActiveCondition x: Add(locs, x.targetLocation); Add(npcs, x.requiredPropagator); return;
                case InformationWorldStateCondition x: Add(locs, x.targetLocation); Add(npcs, x.requiredActor); return;
                case LocationStateCondition x: Add(locs, x.targetLocation); return;
            }
        }

        static void Add<T>(List<T> list, T item) where T : class
        {
            if (item != null && !list.Contains(item)) list.Add(item);
        }

        static void AddAll<T>(List<T> list, T[] items) where T : class
        {
            if (items == null) return;
            foreach (var i in items) Add(list, i);
        }

        static string Sanitize(string s) =>
            string.IsNullOrEmpty(s) ? "-" : string.Concat(s.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));

        static void Report(string line)
        {
            Debug.Log("[전체플레이] " + line);
            try
            {
                Directory.CreateDirectory(OutDir);
                File.AppendAllText(Path.Combine(OutDir, "log.txt"), line + Environment.NewLine);
            }
            catch (Exception) { /* 로그 파일이 안 써져도 진행은 계속한다 */ }
        }
    }
}
