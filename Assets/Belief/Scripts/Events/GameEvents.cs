using System.Collections.Generic;
using Belief.Data;
using Belief.Domain;

namespace Belief.Events
{
    public readonly struct GameInitializedEvent
    {
        public readonly int LocationCount;
        public readonly int NpcCount;
        public readonly int CardCount;

        public GameInitializedEvent(int locationCount, int npcCount, int cardCount)
        {
            LocationCount = locationCount;
            NpcCount = npcCount;
            CardCount = cardCount;
        }
    }

    /// <summary>ActionEffect가 명시적으로 기억을 만들고 싶을 때 통보하는 용도. MemorySystem만 구독해서 실제로 기록한다.</summary>
    public readonly struct MemoryWorthyEventOccurred
    {
        public readonly NpcData Target;
        public readonly MemoryEntry Entry;

        public MemoryWorthyEventOccurred(NpcData target, MemoryEntry entry)
        {
            Target = target;
            Entry = entry;
        }
    }

    /// <summary>NpcThinkingSystem이 판단을 마칠 때마다 발행. MemorySystem이 반복 패턴 감지에 사용.</summary>
    public readonly struct CardJudgedEvent
    {
        public readonly NpcData Npc;
        public readonly InformationCardData Card;
        public readonly BeliefState ResultBelief;
        public readonly int Turn;

        public CardJudgedEvent(NpcData npc, InformationCardData card, BeliefState resultBelief, int turn)
        {
            Npc = npc;
            Card = card;
            ResultBelief = resultBelief;
            Turn = turn;
        }
    }

    public readonly struct NpcSpokeEvent
    {
        public readonly NpcData Npc;
        public readonly Belief.Systems.DialogueContent Dialogue;

        public NpcSpokeEvent(NpcData npc, Belief.Systems.DialogueContent dialogue)
        {
            Npc = npc;
            Dialogue = dialogue;
        }
    }

    public readonly struct WorldEventOccurred
    {
        public readonly string Message;

        public WorldEventOccurred(string message)
        {
            Message = message;
        }
    }

    /// <summary>ActionResolutionSystem(및 그 Effect)만 발행한다 - 상태를 바꾸는 유일한 지점의 통보.</summary>
    public readonly struct NpcRelocatedEvent
    {
        public readonly NpcData Npc;
        public readonly LocationData From;
        public readonly LocationData To;

        public NpcRelocatedEvent(NpcData npc, LocationData from, LocationData to)
        {
            Npc = npc;
            From = from;
            To = to;
        }
    }

    public readonly struct LocationStateChangedEvent
    {
        public readonly LocationData Location;
        public readonly LocationSiteState NewState;

        public LocationStateChangedEvent(LocationData location, LocationSiteState newState)
        {
            Location = location;
            NewState = newState;
        }
    }

    public readonly struct NpcBehaviorModifierChangedEvent
    {
        public readonly NpcData Npc;
        public readonly string ModifierId;

        public NpcBehaviorModifierChangedEvent(NpcData npc, string modifierId)
        {
            Npc = npc;
            ModifierId = modifierId;
        }
    }

    public readonly struct TurnStartedEvent
    {
        public readonly int Turn;
        public readonly int MaxTurns;

        public TurnStartedEvent(int turn, int maxTurns)
        {
            Turn = turn;
            MaxTurns = maxTurns;
        }
    }

    public readonly struct TurnEndedEvent
    {
        public readonly int Turn;

        /// <summary>이번 턴 종료 시 씬 레벨 즉시 실패 조건(GameInstaller.instantFailCondition)이
        /// 충족됐는지 - TurnSystem이 계산만 해서 전달하고, 실제 실패 판정과 GameOverEvent 발행은
        /// 이 신호를 읽는 구독자(ProgressionController, 없으면 TurnSystem 자신의 레거시 폴백)가
        /// 전담한다. 최종 판정 권한을 한 곳으로 모으기 위한 필드일 뿐, 별도의 새 이벤트 체계는
        /// 아니다.</summary>
        public readonly bool InstantFailTriggered;

        public TurnEndedEvent(int turn, bool instantFailTriggered)
        {
            Turn = turn;
            InstantFailTriggered = instantFailTriggered;
        }
    }

    /// <summary>정보 획득 배치 1회분(게임 시작 5개 또는 턴 시작 보충 2개 이하)을 통째로 알린다 -
    /// HUD가 "새로운 정보 N개를 획득했습니다."를 한 번에 표시할 수 있도록.</summary>
    public readonly struct InformationAcquiredEvent
    {
        public readonly IReadOnlyList<InformationCardData> Cards;
        public readonly bool IsInitialSupply;

        public InformationAcquiredEvent(IReadOnlyList<InformationCardData> cards, bool isInitialSupply)
        {
            Cards = cards;
            IsInitialSupply = isInitialSupply;
        }
    }

    public readonly struct CardSelectedEvent
    {
        public readonly InformationCardData Card;

        public CardSelectedEvent(InformationCardData card)
        {
            Card = card;
        }
    }

    public readonly struct CardPlayedEvent
    {
        public readonly InformationCardData Card;

        public CardPlayedEvent(InformationCardData card)
        {
            Card = card;
        }
    }

    public readonly struct InfoSpreadEvent
    {
        public readonly InformationCardData Card;
        public readonly LocationData Location;

        public InfoSpreadEvent(InformationCardData card, LocationData location)
        {
            Card = card;
            Location = location;
        }
    }

    public readonly struct InfoDeliveredEvent
    {
        public readonly InformationCardData Card;
        public readonly NpcData Target;

        public InfoDeliveredEvent(InformationCardData card, NpcData target)
        {
            Card = card;
            Target = target;
        }
    }

    public readonly struct MissionProgressChangedEvent
    {
        public readonly int CurrentProgress;
        public readonly int TargetCount;

        public MissionProgressChangedEvent(int currentProgress, int targetCount)
        {
            CurrentProgress = currentProgress;
            TargetCount = targetCount;
        }
    }

    public readonly struct MissionCompletedEvent
    {
    }

    /// <summary>추적 중인 미션 자체가 교체됐을 때(MissionSystem.LoadMission) 발행된다 - 진행도(X/Y)만
    /// 바뀐 MissionProgressChangedEvent와 달리, 미션 UI가 제목/목표/조건 목록 전체를 다시 그려야
    /// 함을 알리는 용도.</summary>
    public readonly struct MissionChangedEvent
    {
        public readonly Belief.Data.MissionData Mission;
        public readonly Belief.Domain.MissionState State;

        public MissionChangedEvent(Belief.Data.MissionData mission, Belief.Domain.MissionState state)
        {
            Mission = mission;
            State = state;
        }
    }

    public readonly struct GameOverEvent
    {
        public readonly bool Won;

        public GameOverEvent(bool won)
        {
            Won = won;
        }
    }
}
