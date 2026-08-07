using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Belief.Data;
using Belief.Domain;
using Belief.Events;

namespace Belief.Systems
{
    /// <summary>
    /// 턴 루프 오케스트레이션. 플레이어가 정보 카드를 선택하고 장소/NPC에 전달하면 한 턴이 소모된다.
    /// 턴 종료 시 NPC 이동 + 미션 재평가 + 승패 판정을 수행하고, 다음 턴 시작 시 보유 정보
    /// 보충 여부를 InformationCardSystem에 위임한다.
    /// </summary>
    public class TurnSystem
    {
        readonly InformationCardSystem cards;
        readonly InfoDeliverySystem delivery;
        readonly NpcMovementSystem movement;
        readonly MissionSystem mission;
        readonly IReadOnlyDictionary<NpcData, NpcState> allNpcs;
        readonly IReadOnlyDictionary<LocationData, LocationState> allLocations;
        readonly MissionConditionData instantFailCondition;
        readonly IGameEventBus eventBus;
        readonly LocationMechanicsSettings locationMechanics;
        readonly MemorySystem memorySystem;

        /// <summary>"현재 미션이 시작된 시점"의 카드/NPC/장소/기억 스트릭 상태 - StartGame과
        /// ResetForNewMission(새 미션으로 전환할 때)에서만 새로 캡처되고, RestartMissionAttempt는
        /// 이 스냅샷을 다시 캡처하지 않고 그대로 복원만 한다. 그래야 몇 번을 재시도해도 항상 같은
        /// "미션 시작 지점"으로 돌아간다(재시도 도중 상태가 누적되지 않는다).</summary>
        InformationCardSystem.CardSystemSnapshot cardSnapshot;
        Dictionary<NpcData, NpcState.NpcStateSnapshot> npcSnapshots;
        Dictionary<LocationData, LocationState.LocationStateSnapshot> locationSnapshots;
        MemorySystem.StreakSnapshot memoryStreakSnapshot;
        bool hasSnapshot;

        public int CurrentTurn { get; private set; } = 1;
        public int MaxTurns { get; private set; }
        public bool TurnsExhausted => CurrentTurn > MaxTurns;

        /// <summary>이번 미션 시도의 결과(성공/실패)가 이미 확정됐는지. 세 가지를 모두 본다:
        /// <list type="number">
        /// <item>턴 소진(CurrentTurn이 MaxTurns를 넘어선 레거시 폴백 경로)</item>
        /// <item>현재 미션 완료(완료 팝업 확인 대기 구간, 구역 마지막 미션 후 씬 로드 직전 구간)</item>
        /// <item>ProgressionController가 GameOverEvent로 선언한 결과 확정</item>
        /// </list>
        /// 3번이 없으면 <b>턴 소진 실패를 못 잡는다</b>. 정식 스테이지에서는 턴 소진 시
        /// ProgressionController가 FreezeTurnAdvance()를 걸어 CurrentTurn을 MaxTurns에서 멈춰
        /// 세우므로 TurnsExhausted(= CurrentTurn &gt; MaxTurns)가 영원히 false로 남고, 실패했으니
        /// mission.State.IsComplete도 false다 - 그 결과 실패가 확정된 뒤에도 카드가 계속 먹혀
        /// 손패와 카드 풀이 0까지 고갈됐다(실측: 실패한 시도마다 카드 4~6장 추가 소모).
        /// 실패 팝업은 WaitForPlaybackThen으로 NPC 이동 연출이 끝난 뒤에야 뜨기 때문에 화면을
        /// 막아 주는 것도 그때까지는 없다.</summary>
        public bool IsGameOver => TurnsExhausted || mission.State.IsComplete || resultDeclared;

        /// <summary>구역 전체를 통틀어 누적되는 턴 수(UI 표시 전용) - CurrentTurn/MaxTurns와 달리
        /// ResetForNewMission이 호출돼도 리셋되지 않는다. 턴 진행/판정 로직에는 전혀 관여하지 않는다.</summary>
        public int StageTurn { get; private set; } = 1;

        /// <summary>구역 생성 시점의 MaxTurns(=StageData.maxTurns) - 이후 미션별 turnLimit으로
        /// MaxTurns가 바뀌어도 이 값은 고정된다. StageTurn과 함께 UI의 "STAGE TURN X/Y" 표시 전용.</summary>
        public int StageMaxTurns { get; }

        public InformationCardData SelectedCard { get; private set; }
        public IReadOnlyList<InformationCardData> OwnedInformationCards => cards.OwnedInformationCards;
        public IReadOnlyList<DeliveredCardRecord> DeliveredInformationCards => cards.DeliveredInformationCards;

        /// <summary>디버그/QA 전용 진단 값(InformationCardSystem.RemainingInPoolCount 그대로 통과). 게임
        /// 로직은 이 값을 판단에 사용하지 않는다.</summary>
        public int RemainingCardPoolCount => cards.RemainingInPoolCount;

        bool gameOverAnnounced;

        /// <summary>GameOverEvent가 한 번이라도 발행됐는지 - 즉 이번 미션 시도의 결과가 확정됐는지.
        /// 최종 판정 권한은 ProgressionController에 있고 TurnSystem은 그 결과를 이벤트로만 알 수 있어서,
        /// 상태를 직접 계산하지 않고 선언을 받아 적기만 한다. 다음 미션 시작(ResetForNewMission)이나
        /// 재도전(RestartMissionAttempt)에서만 풀린다 - 그 두 곳이 "새 시도가 시작되는" 유일한 지점이다.</summary>
        bool resultDeclared;

        /// <summary>같은 턴 종료 처리 안에서 ResetForNewMission이 호출됐는지 표시한다 - FinishTurn의
        /// 뒤이은 CurrentTurn++/TurnStartedEvent가 방금 초기화한 값을 덮어쓰지 않게 막는 용도.</summary>
        bool resetRequestedThisTurn;

        public TurnSystem(
            InformationCardSystem cards,
            InfoDeliverySystem delivery,
            NpcMovementSystem movement,
            MissionSystem mission,
            IReadOnlyDictionary<NpcData, NpcState> allNpcs,
            IReadOnlyDictionary<LocationData, LocationState> allLocations,
            int maxTurns,
            MissionConditionData instantFailCondition,
            IGameEventBus eventBus,
            LocationMechanicsSettings locationMechanics,
            MemorySystem memorySystem)
        {
            this.cards = cards;
            this.delivery = delivery;
            this.movement = movement;
            this.mission = mission;
            this.allNpcs = allNpcs;
            this.allLocations = allLocations;
            MaxTurns = maxTurns;
            StageMaxTurns = maxTurns;
            this.instantFailCondition = instantFailCondition;
            this.eventBus = eventBus;
            this.locationMechanics = locationMechanics;
            this.memorySystem = memorySystem;

            // 결과가 확정되는 순간을 알 수 있는 유일한 신호. 승리(Won=true)도 똑같이 잡는다 -
            // 최종 승리 화면이 떠 있는 동안에도 카드가 먹혀선 안 된다.
            eventBus.Subscribe<GameOverEvent>(_ => resultDeclared = true);
        }

        /// <summary>Location Mechanics V1(§7) - 이 장소를 "장소 전체" 대상으로 직접 지정할 수 있는지.
        /// NPC 개별 지정(PlayCardOnNpcAsync)이나 NPC 자체 이동에는 전혀 영향을 주지 않는다.
        /// Presentation(TargetingController)이 선택 단계에서 미리 안내 메시지를 보여줄 때도 이 값을 쓴다.</summary>
        public bool CanTargetLocationDirectly(LocationData location) =>
            locationMechanics == null || locationMechanics.CanTargetLocationDirectly(location);

        public string RestrictedLocationTargetMessage =>
            locationMechanics != null ? locationMechanics.restrictedLocationTargetMessage : "이 장소는 지금 대상으로 지정할 수 없습니다.";

        public void StartGame()
        {
            cards.GrantInitialSupply();
            CaptureMissionAttemptSnapshot();
            mission.BeginAttempt(BuildMissionContext()); // 반드시 첫 Evaluate보다 먼저.
            mission.Evaluate(BuildMissionContext());
            eventBus.Publish(new TurnStartedEvent(CurrentTurn, MaxTurns));
        }

        /// <summary>카드 선택. 결과가 이미 확정된 구간(<see cref="IsGameOver"/>)에서는 아무것도 하지
        /// 않는다 - 성공/실패 팝업이 확인 대기 중이거나 다음 씬이 로드되기 직전인 구간에서 입력이
        /// 그대로 먹혀 카드가 헛되이 소모되던 문제를 막는다. 레거시 씬은 완료되지 않는 더미 미션을
        /// 쓰고 GameOverEvent도 자체 폴백 경로로만 발행하므로 기존과 동일하게 동작한다.</summary>
        public void SelectCard(InformationCardData card)
        {
            if (IsGameOver || card == null || !cards.OwnedInformationCards.Contains(card)) return;
            SelectedCard = card;
            eventBus.Publish(new CardSelectedEvent(card));
        }

        /// <summary>고른 카드를 도로 내려놓는다 - 손패에서 꺼낸 카드를 다시 눌러 집어넣는 동작.
        ///
        /// <b>이건 화면만의 일이 아니다.</b> 예전엔 손패 쪽에서 카드를 시각적으로 내리기만 하고
        /// SelectedCard는 그대로 뒀는데, 그 결과 (1) 지도에 골라 둔 대상의 강조색이 커서를 올리지도
        /// 않았는데 계속 남았고 (2) 같은 카드를 다시 눌러도 이미 "선택된 카드"라 아무 일도 일어나지
        /// 않아 카드가 영영 안 올라왔다. 선택 해제도 선택과 같은 신호로 알려야 두 곳이 함께 풀린다.</summary>
        public void DeselectCard()
        {
            if (IsGameOver || SelectedCard == null) return;
            SelectedCard = null;
            eventBus.Publish(new CardSelectedEvent(null));
        }

        /// <summary>정보원을 통해 전달한다 - 대상(장소/사람)만 지정하면 그 자리에서 즉시 실행된다.
        /// 비동기(LLM 판단이 Timeout까지 대기할 수 있음) - Unity 메인 스레드를 막지 않는다.</summary>
        public async Task<bool> PlayCardOnLocationAsync(LocationData location)
        {
            if (IsGameOver || SelectedCard == null || SelectedCard.cardType != InfoCardType.Spread) return false;
            if (!CanTargetLocationDirectly(location)) return false; // accessType 차단 - 카드 소비/턴 진행 전에 막는다(§7)

            var card = SelectedCard;
            cards.Deliver(card, CurrentTurn);
            await delivery.ExposeCardAtLocationAsync(card, location);
            await FinishTurnAsync();
            return true;
        }

        public async Task<bool> PlayCardOnNpcAsync(NpcState target)
        {
            if (IsGameOver || SelectedCard == null || SelectedCard.cardType != InfoCardType.Deliver) return false;

            var card = SelectedCard;
            cards.Deliver(card, CurrentTurn);
            await delivery.DeliverCardToNpcAsync(card, target);
            await FinishTurnAsync();
            return true;
        }

        /// <summary>BELIEF MVP는 턴을 구역 단위가 아니라 미션 단위로 관리한다 - 미션 하나가 끝나고
        /// 같은 구역 안에서 다음 미션이 시작될 때(씬 재로드 없이) 호출된다. 구역 내 NPC 위치/Belief/
        /// 전달 기록/보유 카드처럼 세계 상태에 속하는 것은 전혀 건드리지 않고, 턴 진행 상태만 새 미션
        /// 기준으로 되돌린다.</summary>
        public void ResetForNewMission(int newMaxTurns)
        {
            CurrentTurn = 1;
            MaxTurns = newMaxTurns;
            gameOverAnnounced = false;
            resultDeclared = false; // 새 미션 = 새 시도. 여기서 풀지 않으면 입력이 영영 막힌다.
            SelectedCard = null;
            resetRequestedThisTurn = true;

            // 미션이 방금 완료된 턴은 FinishTurn()이 FreezeTurnAdvance로 얼어붙어 자신의
            // cards.RefillIfNeeded() 호출까지 도달하지 못하고 반환한다 - 여기서 대신 보충하지
            // 않으면 다음 미션 첫 행동 시점에 손패가 3장으로 남는다(같은 턴 안에서 이 호출과
            // FinishTurn의 보충 호출이 동시에 일어나는 경우는 없으므로 중복 보충 걱정은 없다).
            cards.RefillIfNeeded();

            // 여기서부터가 "새 미션(또는 최초 진입)의 시작 지점"이다 - 이후 RestartCurrentMission이
            // 실패한 시도를 몇 번 되돌리든 항상 이 지점으로 복원되도록 스냅샷을 다시 찍는다.
            CaptureMissionAttemptSnapshot();

            // 이 호출은 ProgressionController.ConfirmMissionComplete가 Mission.LoadMission(다음 미션)을
            // 이미 끝낸 뒤에 도달하므로, 여기서 만드는 기준점은 정확히 "다음 미션이 활성화된 직후,
            // 그 미션의 첫 Mission Check보다 앞선" 시점이다.
            mission.BeginAttempt(BuildMissionContext());

            eventBus.Publish(new TurnStartedEvent(CurrentTurn, MaxTurns));
        }

        /// <summary>HUD의 "MISSION FAILED" 팝업에서 [재시작]을 눌렀을 때 호출된다. ResetForNewMission과
        /// 달리 새 스냅샷을 찍지 않고, 이 미션이 시작될 때(StartGame 또는 마지막 ResetForNewMission
        /// 시점)의 카드 pool/delivered, NPC 위치/Belief/기억/받은 정보, 장소의 소문/조사기록/SiteState,
        /// 반복거짓말 스트릭을 전부 그 시점 값으로 되돌린 뒤 턴만 재설정한다 - 실패한 시도가 남긴 카드
        /// 소모/오염된 NPC 상태가 재시도마다 누적되어 카드 pool이 고갈되는 문제를 막는다. 손패(owned)는
        /// 예외적으로 그 시점 그대로 복원하지 않고 새로 뽑는다 - 그대로 복원하면 나쁜 손패가 재시도마다
        /// 고정되어 버리기 때문(InformationCardSystem.RestoreSnapshotForRestart 참고).
        /// ProgressionController.Progress(CompletedMissionIds 등)는 건드리지 않는다.</summary>
        public void RestartMissionAttempt(int newMaxTurns)
        {
            CurrentTurn = 1;
            MaxTurns = newMaxTurns;
            gameOverAnnounced = false;
            resultDeclared = false; // 재도전 = 새 시도.
            SelectedCard = null;
            resetRequestedThisTurn = true;

            RestoreMissionAttemptSnapshot();

            // 복원이 끝난 뒤에 기준점을 다시 만든다 - 복원으로 상태와 스탬프가 전부 시작 시점으로
            // 되돌아갔으므로, 실패한 이전 시도가 만든 변화는 새 시도에서 "새 진척"으로 인정되지 않는다.
            mission.BeginAttempt(BuildMissionContext());

            eventBus.Publish(new TurnStartedEvent(CurrentTurn, MaxTurns));
        }

        /// <summary>오토세이브로 세계 상태를 되살린 직후 호출한다 - 그 상태가 이번 미션 시도의
        /// 시작점이 되어야 [재시작]이 복원 이전의 초기 배치로 되돌아가지 않는다. StartGame이 이미
        /// 찍어 둔 스냅샷을 지금 상태로 갈아끼우는 것이 전부다.</summary>
        public void RecaptureMissionAttemptSnapshot() => CaptureMissionAttemptSnapshot();

        void CaptureMissionAttemptSnapshot()
        {
            cardSnapshot = cards.CaptureSnapshot();

            npcSnapshots = new Dictionary<NpcData, NpcState.NpcStateSnapshot>();
            foreach (var kv in allNpcs)
                npcSnapshots[kv.Key] = kv.Value.CaptureSnapshot();

            locationSnapshots = new Dictionary<LocationData, LocationState.LocationStateSnapshot>();
            foreach (var kv in allLocations)
                locationSnapshots[kv.Key] = kv.Value.CaptureSnapshot();

            if (memorySystem != null) memoryStreakSnapshot = memorySystem.CaptureSnapshot();

            hasSnapshot = true;
        }

        void RestoreMissionAttemptSnapshot()
        {
            if (!hasSnapshot) return; // StartGame이 항상 먼저 캡처하므로 정상 흐름에서는 발생하지 않는다.

            cards.RestoreSnapshotForRestart(cardSnapshot);

            foreach (var kv in allNpcs)
                if (npcSnapshots.TryGetValue(kv.Key, out var snap))
                    kv.Value.RestoreSnapshot(snap);

            foreach (var kv in allLocations)
            {
                kv.Value.PresentNpcs.Clear();
                if (locationSnapshots.TryGetValue(kv.Key, out var snap))
                    kv.Value.RestoreSnapshot(snap);
            }

            // PresentNpcs는 스냅샷 대상이 아니다 - 방금 되돌린 NpcState.CurrentLocation을 유일한
            // 출처로 삼아 모든 장소의 재실 목록을 다시 구성한다(GameInstaller.BuildDomainState와
            // 동일한 원칙).
            foreach (var kv in allNpcs)
            {
                var loc = kv.Value.CurrentLocation;
                if (loc != null && allLocations.TryGetValue(loc, out var locState))
                    locState.PresentNpcs.Add(kv.Value);
            }

            if (memorySystem != null) memorySystem.RestoreSnapshot(memoryStreakSnapshot);
        }

        /// <summary>ResetForNewMission과 같은 가드를 사용하되 CurrentTurn/MaxTurns는 건드리지 않는다 -
        /// 미션/구역 완료 팝업이 확인 대기 중인 동안(플레이어가 [다음]을 누르기 전까지) 턴이 계속
        /// 흘러가거나 그 사이에 턴 소진으로 오판되지 않도록, 이번 턴 종료 처리에서 증가/게임오버
        /// 판정만 건너뛴다. 실제 턴 값 갱신은 나중에 ResetForNewMission이 담당한다.</summary>
        public void FreezeTurnAdvance() => resetRequestedThisTurn = true;

        async Task FinishTurnAsync()
        {
            // 예전에는 여기서 Minor NPC만 확률적으로 무작위 배회시키는 별도 처리가 먼저 돌았다.
            // NPC 등급 구분을 없애면서 모든 NPC가 아래 한 경로(판단 기반 이동)로 통일됐다.
            await movement.MoveNpcsAsync(allNpcs.Values, CurrentTurn);
            mission.Evaluate(BuildMissionContext());

            // 씬 레벨 즉시 실패 조건 - 계산만 여기서 하고 TurnEndedEvent에 실어 보낸다. 실제 실패
            // 판정/GameOverEvent 발행 권한은 이 신호를 읽는 ProgressionController에 있다(최종 판정
            // 권한 단일화) - TurnSystem은 더 이상 이 조건만으로 직접 GameOverEvent를 끝내지 않는다.
            bool instantFail = instantFailCondition != null &&
                instantFailCondition.GetCurrentProgress(BuildMissionContext()) >= instantFailCondition.TargetCount;

            SelectedCard = null;
            resetRequestedThisTurn = false;
            eventBus.Publish(new TurnEndedEvent(CurrentTurn, instantFail));

            // TurnEndedEvent 구독자(ProgressionController)가 이번 턴에 성공/실패/즉시실패/턴소진 중
            // 하나를 확정하며 FreezeTurnAdvance(또는 그 결과로 ResetForNewMission)를 호출했다면, 그
            // 결과를 그대로 두고 여기서는 더 이상 증가시키거나 게임오버를 판정하지 않는다 - 최종 판정
            // 권한은 ProgressionController에 있다.
            if (resetRequestedThisTurn) return;

            // ---- 아래는 ProgressionController가 관여하지 않을 때(StageData 미배선 레거시/테스트
            // 씬)만 실행되는 폴백 경로다. 정식 4개 스테이지(StageData 배선, 전부 ProgressionData에
            // 등록됨)에서는 위 freeze가 항상 먼저 걸려 이 블록에 도달하지 않는다. ----
            CurrentTurn++;
            StageTurn++;

            if (!IsGameOver)
            {
                cards.RefillIfNeeded();
                eventBus.Publish(new TurnStartedEvent(CurrentTurn, MaxTurns));
            }
            else if (!gameOverAnnounced)
            {
                gameOverAnnounced = true;
                eventBus.Publish(new GameOverEvent(mission.State.IsComplete));
            }
        }

        MissionEvaluationContext BuildMissionContext() =>
            new MissionEvaluationContext(allLocations, allNpcs, cards.DeliveredInformationCards);
    }
}
