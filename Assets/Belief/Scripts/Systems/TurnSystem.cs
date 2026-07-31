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
    /// 턴 종료 시 Minor NPC 배회 + 미션 재평가 + 승패 판정을 수행하고, 다음 턴 시작 시 보유 정보
    /// 보충 여부를 InformationCardSystem에 위임한다.
    /// </summary>
    public class TurnSystem
    {
        readonly InformationCardSystem cards;
        readonly InfoDeliverySystem delivery;
        readonly MinorNpcBehaviorSystem minorBehavior;
        readonly MajorNpcMovementSystem majorMovement;
        readonly MissionSystem mission;
        readonly IReadOnlyDictionary<NpcData, NpcState> allNpcs;
        readonly IReadOnlyDictionary<LocationData, LocationState> allLocations;
        readonly MissionConditionData instantFailCondition;
        readonly IGameEventBus eventBus;
        readonly LocationMechanicsSettings locationMechanics;

        public int CurrentTurn { get; private set; } = 1;
        public int MaxTurns { get; private set; }
        public bool TurnsExhausted => CurrentTurn > MaxTurns;
        public bool IsGameOver => TurnsExhausted || mission.State.IsComplete;

        /// <summary>구역 전체를 통틀어 누적되는 턴 수(UI 표시 전용) - CurrentTurn/MaxTurns와 달리
        /// ResetForNewMission이 호출돼도 리셋되지 않는다. 턴 진행/판정 로직에는 전혀 관여하지 않는다.</summary>
        public int StageTurn { get; private set; } = 1;

        /// <summary>구역 생성 시점의 MaxTurns(=StageData.maxTurns) - 이후 미션별 turnLimit으로
        /// MaxTurns가 바뀌어도 이 값은 고정된다. StageTurn과 함께 UI의 "STAGE TURN X/Y" 표시 전용.</summary>
        public int StageMaxTurns { get; }

        public InformationCardData SelectedCard { get; private set; }
        public IReadOnlyList<InformationCardData> OwnedInformationCards => cards.OwnedInformationCards;
        public IReadOnlyList<DeliveredCardRecord> DeliveredInformationCards => cards.DeliveredInformationCards;

        bool gameOverAnnounced;

        /// <summary>같은 턴 종료 처리 안에서 ResetForNewMission이 호출됐는지 표시한다 - FinishTurn의
        /// 뒤이은 CurrentTurn++/TurnStartedEvent가 방금 초기화한 값을 덮어쓰지 않게 막는 용도.</summary>
        bool resetRequestedThisTurn;

        public TurnSystem(
            InformationCardSystem cards,
            InfoDeliverySystem delivery,
            MinorNpcBehaviorSystem minorBehavior,
            MajorNpcMovementSystem majorMovement,
            MissionSystem mission,
            IReadOnlyDictionary<NpcData, NpcState> allNpcs,
            IReadOnlyDictionary<LocationData, LocationState> allLocations,
            int maxTurns,
            MissionConditionData instantFailCondition,
            IGameEventBus eventBus,
            LocationMechanicsSettings locationMechanics)
        {
            this.cards = cards;
            this.delivery = delivery;
            this.minorBehavior = minorBehavior;
            this.majorMovement = majorMovement;
            this.mission = mission;
            this.allNpcs = allNpcs;
            this.allLocations = allLocations;
            MaxTurns = maxTurns;
            StageMaxTurns = maxTurns;
            this.instantFailCondition = instantFailCondition;
            this.eventBus = eventBus;
            this.locationMechanics = locationMechanics;
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
            mission.Evaluate(BuildMissionContext());
            eventBus.Publish(new TurnStartedEvent(CurrentTurn, MaxTurns));
        }

        public void SelectCard(InformationCardData card)
        {
            if (TurnsExhausted || card == null || !cards.OwnedInformationCards.Contains(card)) return;
            SelectedCard = card;
            eventBus.Publish(new CardSelectedEvent(card));
        }

        /// <summary>정보원을 통해 전달한다 - 대상(장소/사람)만 지정하면 그 자리에서 즉시 실행된다.
        /// 비동기(LLM 판단이 Timeout까지 대기할 수 있음) - Unity 메인 스레드를 막지 않는다.</summary>
        public async Task<bool> PlayCardOnLocationAsync(LocationData location)
        {
            if (TurnsExhausted || SelectedCard == null || SelectedCard.cardType != InfoCardType.Spread) return false;
            if (!CanTargetLocationDirectly(location)) return false; // accessType 차단 - 카드 소비/턴 진행 전에 막는다(§7)

            var card = SelectedCard;
            cards.Deliver(card, CurrentTurn);
            await delivery.ExposeCardAtLocationAsync(card, location);
            await FinishTurnAsync();
            return true;
        }

        public async Task<bool> PlayCardOnNpcAsync(NpcState target)
        {
            if (TurnsExhausted || SelectedCard == null || SelectedCard.cardType != InfoCardType.Deliver) return false;

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
            SelectedCard = null;
            resetRequestedThisTurn = true;

            // 미션이 방금 완료된 턴은 FinishTurn()이 FreezeTurnAdvance로 얼어붙어 자신의
            // cards.RefillIfNeeded() 호출까지 도달하지 못하고 반환한다 - 여기서 대신 보충하지
            // 않으면 다음 미션 첫 행동 시점에 손패가 3장으로 남는다(같은 턴 안에서 이 호출과
            // FinishTurn의 보충 호출이 동시에 일어나는 경우는 없으므로 중복 보충 걱정은 없다).
            cards.RefillIfNeeded();

            eventBus.Publish(new TurnStartedEvent(CurrentTurn, MaxTurns));
        }

        /// <summary>ResetForNewMission과 같은 가드를 사용하되 CurrentTurn/MaxTurns는 건드리지 않는다 -
        /// 미션/구역 완료 팝업이 확인 대기 중인 동안(플레이어가 [다음]을 누르기 전까지) 턴이 계속
        /// 흘러가거나 그 사이에 턴 소진으로 오판되지 않도록, 이번 턴 종료 처리에서 증가/게임오버
        /// 판정만 건너뛴다. 실제 턴 값 갱신은 나중에 ResetForNewMission이 담당한다.</summary>
        public void FreezeTurnAdvance() => resetRequestedThisTurn = true;

        async Task FinishTurnAsync()
        {
            minorBehavior.MoveMinorNpcs(allNpcs.Values);
            await majorMovement.MoveMajorNpcsAsync(allNpcs.Values, CurrentTurn);
            mission.Evaluate(BuildMissionContext());

            bool instantFail = instantFailCondition != null &&
                instantFailCondition.GetCurrentProgress(BuildMissionContext()) >= instantFailCondition.TargetCount;

            SelectedCard = null;
            resetRequestedThisTurn = false;
            eventBus.Publish(new TurnEndedEvent(CurrentTurn));

            if (instantFail)
            {
                if (!gameOverAnnounced)
                {
                    gameOverAnnounced = true;
                    eventBus.Publish(new GameOverEvent(false));
                }
                return;
            }

            // TurnEndedEvent 구독자(ProgressionController)가 방금 다음 미션으로 넘어가며
            // ResetForNewMission을 호출했다면, 그 값(턴 1 / 새 MaxTurns)을 그대로 두고 여기서는
            // 더 이상 증가시키거나 게임오버를 판정하지 않는다 - 새 미션의 턴 1을 곧바로 소모한 것으로
            // 잘못 계산하지 않기 위함이다.
            if (resetRequestedThisTurn) return;

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
