using System.Collections.Generic;
using Belief.Data;

namespace Belief.Domain
{
    /// <summary>
    /// NPC의 런타임 가변 상태. 정적 정의(성격/관계 등)는 NpcData에 있다.
    ///
    /// 쓰기 소유권:
    /// <list type="bullet">
    /// <item><b>beliefs</b> - 일반(RuleOnly/FakeLlm/Llm) 판단에서는 BeliefSystem이 소유한다.
    ///   통합 판단(IntegratedLlm) 경로에서는 <b>검증을 마친 결과</b>를 JudgmentApplicationSystem이
    ///   적용한다. 그 두 곳 외에서 직접 바꾸지 않는다.</item>
    /// <item><b>CurrentGoal</b> - 통합 판단 경로에서 JudgmentApplicationSystem만 바꾼다.</item>
    /// <item><b>LongMemory</b> - MemorySystem 전용.</item>
    /// <item><b>BehaviorModifier</b> - ActionResolutionSystem(Effect 경유) 전용.</item>
    /// </list>
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

        LocationData currentLocation;

        /// <summary>쓰기 경로가 여러 곳(NpcMovementService, GameInstaller 초기 배치, 스냅샷 복원)이라
        /// 스탬프를 호출처에서 찍게 하면 반드시 하나를 빠뜨린다 - setter 안에서 찍어 구조적으로 막는다.
        /// 같은 값을 다시 넣는 것은 이동이 아니므로 스탬프를 올리지 않는다.</summary>
        public LocationData CurrentLocation
        {
            get => currentLocation;
            set
            {
                if (currentLocation == value) return;
                currentLocation = value;
                LocationChangeStamp = WorldChangeClock.Next();
            }
        }

        /// <summary>이 NPC가 마지막으로 실제 이동한 시점의 WorldChangeClock 값. 0이면 초기 배치 이후
        /// 한 번도 움직이지 않았다는 뜻이다.</summary>
        public long LocationChangeStamp { get; private set; }

        /// <summary>이 NPC가 어떤 카드에 대해서든 마지막으로 판단을 기록한 시점의 스탬프.
        /// NpcAnyBeliefReachedCondition처럼 카드를 특정하지 않는 조건이 읽는다.</summary>
        public long LatestBeliefStamp { get; private set; }

        /// <summary>NPC의 목표. NpcData.InitialGoal(Frozen AI Profile, 고정 데이터)에서 초기화된다 -
        /// 목표가 없는 NPC 유형은 항상 null. 쓰기는 SetGoal을 통해서만 이루어지며, 이를 호출하는 곳은
        /// 통합 판단(IntegratedLlm) 경로의 JudgmentApplicationSystem 하나다 - 규칙 기반 판단은
        /// 목표를 바꾸지 않으므로 그 경로에서는 초기값이 그대로 유지된다.</summary>
        public string CurrentGoal { get; private set; }

        /// <summary>ApplyNpcBehaviorModifierEffect가 설정하는 행동 모드 태그. 없으면 null.</summary>
        public string CurrentBehaviorModifier { get; private set; }

        /// <summary>가장 최근 ActionResolutionSystem이 적용한 행동. 없으면 null. 쓰기는 ActionResolutionSystem 전용.</summary>
        public NpcActionData CurrentAction { get; private set; }

        /// <summary>이번 턴에 이 NPC의 Belief가 실제로 다른 값으로 바뀌었는지. NpcThinkingSystem이
        /// 판단 전/후를 비교해 기록하고, NpcMovementSystem이 "LLM 이동 판단이 필요한 NPC"를 고르는
        /// 유일한 기준으로 읽는다. 턴 스코프 값이라 이동 처리 끝에서 전원 초기화된다.</summary>
        public bool BeliefChangedThisTurn { get; private set; }

        /// <summary>이번 턴에 CurrentGoal이 바뀌었는지. 구조만 미리 만들어 둔다 - 현재 SetGoal을
        /// 호출하는 시스템이 하나도 없어서(목표를 바꾸는 판단 로직 미구현) 실제로는 절대 true가
        /// 되지 않는다. 이 값이 참이 되는 경로가 생기기 전까지는 선별 성능의 근거로 삼지 말 것.</summary>
        public bool GoalChangedThisTurn { get; private set; }

        /// <summary>이번 턴에 새 판단이 필요해졌는지. 지금은 Belief 변화만 실질적으로 기여한다.
        /// 목적지 도착/행동 실패/Intent 재평가 같은 다른 사유는 그 개념 자체가 아직 도메인에 없다 -
        /// 생기면 여기에 OR로 추가하고, NpcMovementSystem 쪽은 손대지 않는다.</summary>
        public bool NeedsFreshDecision => BeliefChangedThisTurn || GoalChangedThisTurn;

        public void MarkBeliefChanged() => BeliefChangedThisTurn = true;

        public void MarkGoalChanged() => GoalChangedThisTurn = true;

        /// <summary>턴 스코프 마커를 모두 내린다. 호출 시점은 NpcMovementSystem이 선별을 끝낸
        /// 뒤(finally)로 한 곳뿐이다 - 선별보다 먼저 초기화되면 그 턴의 대상이 통째로 사라진다.</summary>
        public void ClearDecisionMarkers()
        {
            BeliefChangedThisTurn = false;
            GoalChangedThisTurn = false;
        }

        readonly Dictionary<InformationCardData, BeliefState> beliefs = new Dictionary<InformationCardData, BeliefState>();

        /// <summary>카드별로 마지막 판단이 기록된 시점의 스탬프. 값이 그대로여도 "다시 판단했다"는
        /// 사실 자체가 새 진척이므로, Belief가 바뀌었는지와 무관하게 SetBelief마다 갱신한다.</summary>
        readonly Dictionary<InformationCardData, long> beliefStamps = new Dictionary<InformationCardData, long>();
        readonly List<MemoryEntry> longMemory = new List<MemoryEntry>();
        readonly List<ReceivedInformationEntry> receivedInformation = new List<ReceivedInformationEntry>();

        public IReadOnlyList<MemoryEntry> LongMemory => longMemory;

        /// <summary>이 NPC가 지금까지 받은(노출된) 정보 카드 기록. 쓰기는 RecordReceivedInformation을 통해서만
        /// (InfoDeliverySystem이 판단 직전에 기록한다).</summary>
        public IReadOnlyList<ReceivedInformationEntry> ReceivedInformation => receivedInformation;

        /// <summary>Debug Overlay 등 읽기 전용 조회용. 쓰기는 SetBelief를 통해서만 이루어지며,
        /// 이를 호출해도 되는 곳은 BeliefSystem.Apply(일반 판단)와 JudgmentApplicationSystem
        /// (검증을 마친 통합 판단) 둘뿐이다 - 새로운 직접 호출 경로를 만들지 않는다.</summary>
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

        public void SetBelief(InformationCardData card, BeliefState state)
        {
            beliefs[card] = state;

            long stamp = WorldChangeClock.Next();
            if (card != null) beliefStamps[card] = stamp;
            LatestBeliefStamp = stamp;
        }

        /// <summary>이 카드에 대해 마지막으로 판단이 기록된 시점의 스탬프. 판단한 적이 없으면 0.</summary>
        public long GetBeliefStamp(InformationCardData card) =>
            card != null && beliefStamps.TryGetValue(card, out var stamp) ? stamp : 0L;

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

            /// <summary>스탬프도 함께 되돌려야 한다 - 복원 후 상태는 미션 시작 시점과 같으므로,
            /// 실패한 시도가 남긴 높은 스탬프가 그대로 남으면 그 시도의 변화가 새 시도에서
            /// "미션 시작 이후의 새 진척"으로 잘못 인정된다.</summary>
            public readonly long LocationChangeStamp;
            public readonly long LatestBeliefStamp;
            public readonly Dictionary<InformationCardData, long> BeliefStamps;

            public NpcStateSnapshot(LocationData location, string goal, string behaviorModifier, NpcActionData action,
                Dictionary<InformationCardData, BeliefState> beliefs, List<MemoryEntry> longMemory,
                List<ReceivedInformationEntry> receivedInformation,
                long locationChangeStamp, long latestBeliefStamp, Dictionary<InformationCardData, long> beliefStamps)
            {
                Location = location;
                Goal = goal;
                BehaviorModifier = behaviorModifier;
                Action = action;
                Beliefs = new Dictionary<InformationCardData, BeliefState>(beliefs);
                LongMemory = new List<MemoryEntry>(longMemory);
                ReceivedInformation = new List<ReceivedInformationEntry>(receivedInformation);
                LocationChangeStamp = locationChangeStamp;
                LatestBeliefStamp = latestBeliefStamp;
                BeliefStamps = new Dictionary<InformationCardData, long>(beliefStamps);
            }
        }

        public NpcStateSnapshot CaptureSnapshot() =>
            new NpcStateSnapshot(CurrentLocation, CurrentGoal, CurrentBehaviorModifier, CurrentAction,
                beliefs, longMemory, receivedInformation, LocationChangeStamp, LatestBeliefStamp, beliefStamps);

        /// <summary>스냅샷 시점의 가변 상태로 되돌린다. CurrentLocation만 되돌리고 LocationState.PresentNpcs는
        /// 건드리지 않는다 - 여러 NPC를 한꺼번에 복원할 때 호출자(TurnSystem)가 전체 NPC의 위치가 확정된
        /// 뒤 한 번에 PresentNpcs를 재구성해야 장소별 목록이 어긋나지 않는다.
        ///
        /// 턴 스코프 마커는 스냅샷에 담지 않고 여기서 그냥 내린다 - 복원은 항상 "새 시도의 1턴 시작"
        /// 이라 이전 시도가 남긴 마커가 이어질 이유가 없다.</summary>
        public void RestoreSnapshot(NpcStateSnapshot snapshot)
        {
            ClearDecisionMarkers();
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

            // 스탬프는 위 CurrentLocation/SetBelief가 새로 찍은 값을 덮어써서 스냅샷 시점으로 되돌린다 -
            // 복원 자체는 "세계의 새 변화"가 아니므로 반드시 마지막에 수행한다.
            LocationChangeStamp = snapshot.LocationChangeStamp;
            LatestBeliefStamp = snapshot.LatestBeliefStamp;
            beliefStamps.Clear();
            foreach (var kv in snapshot.BeliefStamps) beliefStamps[kv.Key] = kv.Value;
        }
    }
}
