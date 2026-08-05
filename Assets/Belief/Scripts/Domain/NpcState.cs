using System.Collections.Generic;
using Belief.Data;

namespace Belief.Domain
{
    /// <summary>
    /// NPC의 런타임 가변 상태. 정적 정의(성격/관계 등)는 NpcData에 있다.
    /// beliefs 쓰기는 BeliefSystem, LongMemory 쓰기는 MemorySystem,
    /// BehaviorModifier 쓰기는 ActionResolutionSystem(Effect 경유) 전용이다.
    /// </summary>
    /// <summary>NPC가 받은 정보 카드 한 건의 기록. 전달 여부만 보는 InformationCardSystem.DeliveredCardRecord와
    /// 달리 "누가 받았는지"까지 NpcState에 귀속시켜 보관한다.</summary>
    public readonly struct ReceivedInformationEntry
    {
        public readonly InformationCardData Card;
        public readonly int Turn;

        public ReceivedInformationEntry(InformationCardData card, int turn)
        {
            Card = card;
            Turn = turn;
        }
    }

    public class NpcState
    {
        public NpcData Data { get; }
        public LocationData CurrentLocation { get; set; }

        /// <summary>NPC의 목표. NpcData.InitialGoal(Frozen AI Profile, 고정 데이터)에서 초기화된다 -
        /// 목표가 없는 NPC 유형은 항상 null. 쓰기는 SetGoal을 통해서만(현재는 아무 시스템도 호출하지 않는다 -
        /// 목표를 바꾸는 판단 로직은 아직 없다).</summary>
        public string CurrentGoal { get; private set; }

        /// <summary>ApplyNpcBehaviorModifierEffect가 설정하는 행동 모드 태그. 없으면 null.</summary>
        public string CurrentBehaviorModifier { get; private set; }

        /// <summary>가장 최근 ActionResolutionSystem이 적용한 행동. 없으면 null. 쓰기는 ActionResolutionSystem 전용.</summary>
        public NpcActionData CurrentAction { get; private set; }

        readonly Dictionary<InformationCardData, BeliefState> beliefs = new Dictionary<InformationCardData, BeliefState>();
        readonly List<MemoryEntry> longMemory = new List<MemoryEntry>();
        readonly List<ReceivedInformationEntry> receivedInformation = new List<ReceivedInformationEntry>();

        public IReadOnlyList<MemoryEntry> LongMemory => longMemory;

        /// <summary>이 NPC가 지금까지 받은(노출된) 정보 카드 기록. 쓰기는 RecordReceivedInformation을 통해서만
        /// (InfoDeliverySystem이 판단 직전에 기록한다).</summary>
        public IReadOnlyList<ReceivedInformationEntry> ReceivedInformation => receivedInformation;

        /// <summary>Debug Overlay 등 읽기 전용 조회용. 쓰기는 SetBelief를 통해서만.</summary>
        public IReadOnlyDictionary<InformationCardData, BeliefState> Beliefs => beliefs;

        public NpcState(NpcData data)
        {
            Data = data;
            CurrentLocation = data.homeLocation;
            CurrentGoal = data.InitialGoal;
        }

        public void SetGoal(string goal) => CurrentGoal = goal;

        public void RecordReceivedInformation(InformationCardData card, int turn) =>
            receivedInformation.Add(new ReceivedInformationEntry(card, turn));

        public BeliefState GetBelief(InformationCardData card) =>
            beliefs.TryGetValue(card, out var state) ? state : BeliefState.Unknown;

        public void SetBelief(InformationCardData card, BeliefState state) => beliefs[card] = state;

        /// <summary>
        /// 현재 Plausible/Trusted로 믿는 카드 개수 - 정보가 실제로 이 NPC를 움직이는 지렛대가 되도록
        /// (예전에는 Minor 전용 무작위 배회 확률이 이 값에 비례했다 - 등급 구분 제거로 그 경로는 사라졌다.)
        /// 확장 메모(지금 구현 안 함): Fear/Suspicion/Stress 등 다른 심리 축이 실제로 필요해지면
        /// 이 필드 하나를 늘리지 말고 NpcMentalState(혹은 NpcInfluenceState) 같은 별도 값 타입으로 묶어서
        /// NpcState가 그 인스턴스 하나만 들고 있게 리팩터링할 것. 지금은 축이 하나뿐이라 과설계다.
        /// </summary>
        public int ConvictionCount
        {
            get
            {
                int count = 0;
                foreach (var kvp in beliefs)
                    if (kvp.Value == BeliefState.Plausible || kvp.Value == BeliefState.Trusted) count++;
                return count;
            }
        }

        public void RecordMemory(MemoryEntry entry) => longMemory.Add(entry);

        public void SetBehaviorModifier(string modifierId) => CurrentBehaviorModifier = modifierId;

        public void SetCurrentAction(NpcActionData action) => CurrentAction = action;

        /// <summary>미션 시도 시작 시점의 가변 상태 스냅샷(RestartCurrentMission 복원용). 컬렉션은
        /// 생성 시점에 방어적으로 복사해 원본과 별개의 리스트/딕셔너리를 갖는다 - 이후 원본이
        /// 계속 바뀌어도 스냅샷 내용은 캡처 시점 그대로 남는다.</summary>
        public readonly struct NpcStateSnapshot
        {
            public readonly LocationData Location;
            public readonly string Goal;
            public readonly string BehaviorModifier;
            public readonly NpcActionData Action;
            public readonly Dictionary<InformationCardData, BeliefState> Beliefs;
            public readonly List<MemoryEntry> LongMemory;
            public readonly List<ReceivedInformationEntry> ReceivedInformation;

            public NpcStateSnapshot(LocationData location, string goal, string behaviorModifier, NpcActionData action,
                Dictionary<InformationCardData, BeliefState> beliefs, List<MemoryEntry> longMemory,
                List<ReceivedInformationEntry> receivedInformation)
            {
                Location = location;
                Goal = goal;
                BehaviorModifier = behaviorModifier;
                Action = action;
                Beliefs = new Dictionary<InformationCardData, BeliefState>(beliefs);
                LongMemory = new List<MemoryEntry>(longMemory);
                ReceivedInformation = new List<ReceivedInformationEntry>(receivedInformation);
            }
        }

        public NpcStateSnapshot CaptureSnapshot() =>
            new NpcStateSnapshot(CurrentLocation, CurrentGoal, CurrentBehaviorModifier, CurrentAction, beliefs, longMemory, receivedInformation);

        /// <summary>스냅샷 시점의 가변 상태로 되돌린다. CurrentLocation만 되돌리고 LocationState.PresentNpcs는
        /// 건드리지 않는다 - 여러 NPC를 한꺼번에 복원할 때 호출자(TurnSystem)가 전체 NPC의 위치가 확정된
        /// 뒤 한 번에 PresentNpcs를 재구성해야 장소별 목록이 어긋나지 않는다.</summary>
        public void RestoreSnapshot(NpcStateSnapshot snapshot)
        {
            CurrentLocation = snapshot.Location;
            CurrentGoal = snapshot.Goal;
            CurrentBehaviorModifier = snapshot.BehaviorModifier;
            CurrentAction = snapshot.Action;

            beliefs.Clear();
            foreach (var kv in snapshot.Beliefs) beliefs[kv.Key] = kv.Value;

            longMemory.Clear();
            longMemory.AddRange(snapshot.LongMemory);

            receivedInformation.Clear();
            receivedInformation.AddRange(snapshot.ReceivedInformation);
        }
    }
}
