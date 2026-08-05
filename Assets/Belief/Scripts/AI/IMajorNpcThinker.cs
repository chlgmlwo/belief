using System.Collections.Generic;
using System.Threading.Tasks;
using Belief.Data;
using Belief.Domain;

namespace Belief.AI
{
    public readonly struct NpcThinkContext
    {
        public readonly NpcState Npc;
        public readonly InformationCardData Card;
        public readonly BeliefState CurrentBelief;
        public readonly WorkingMemory WorkingMemory;
        public readonly LocationState CurrentLocation;
        public readonly IReadOnlyList<NpcActionData> CandidateActions;
        public readonly int CurrentTurn;

        /// <summary>지금 같은 장소에 있는 다른 NPC들(본인 제외). 관계를 판단 근거로 쓸 수 있는
        /// 대상을 "이번 문맥에 실제로 등장하는 인물"로 한정하기 위한 값이다 - 프로필에 관계가
        /// 적혀 있다는 이유만으로 이번 정보와 무관한 인물을 근거로 삼지 못하게 한다.</summary>
        public readonly IReadOnlyList<NpcState> PresentNpcs;

        /// <summary>이 정보를 실제로 전달한 NPC. 재확산 경로에서만 채워지고, 플레이어가 정보원을
        /// 통해 직접 전달한 경우에는 항상 null이다 - 없는 전달자를 억지로 만들지 않는다.</summary>
        public readonly NpcState Propagator;

        public NpcThinkContext(
            NpcState npc, InformationCardData card, BeliefState currentBelief, WorkingMemory workingMemory,
            LocationState currentLocation, IReadOnlyList<NpcActionData> candidateActions,
            int currentTurn)
            : this(npc, card, currentBelief, workingMemory, currentLocation, candidateActions, currentTurn, null, null)
        {
        }

        public NpcThinkContext(
            NpcState npc, InformationCardData card, BeliefState currentBelief, WorkingMemory workingMemory,
            LocationState currentLocation, IReadOnlyList<NpcActionData> candidateActions,
            int currentTurn, IReadOnlyList<NpcState> presentNpcs, NpcState propagator)
        {
            Npc = npc;
            Card = card;
            CurrentBelief = currentBelief;
            WorkingMemory = workingMemory;
            CurrentLocation = currentLocation;
            CandidateActions = candidateActions;
            CurrentTurn = currentTurn;
            PresentNpcs = presentNpcs;
            Propagator = propagator;
        }
    }

    public readonly struct NpcThinkResult
    {
        public readonly NpcActionData ChosenAction;
        public readonly Belief.Systems.DialogueContent Dialogue;

        public NpcThinkResult(NpcActionData chosenAction, Belief.Systems.DialogueContent dialogue)
        {
            ChosenAction = chosenAction;
            Dialogue = dialogue;
        }
    }

    /// <summary>매 턴 종료 시 Major NPC 이동 판단에 필요한 입력. 특정 카드 노출과 무관하게(=Card 없이)
    /// 전원에 대해 매 턴 평가된다는 점이 NpcThinkContext와 다르다.</summary>
    public readonly struct NpcMoveContext
    {
        public readonly NpcState Npc;
        public readonly LocationData CurrentLocation;
        public readonly IReadOnlyList<LocationData> Candidates;
        public readonly int CurrentTurn;

        /// <summary>지금 같은 장소에 있는 다른 NPC들(본인 제외). 이동 판단에서 관계를 근거로 쓸 수
        /// 있는 유일한 통로다 - 이동 판단은 특정 카드와 무관하게 매 턴 호출되므로 전달자(propagator)
        /// 개념이 없다.</summary>
        public readonly IReadOnlyList<NpcState> PresentNpcs;

        public NpcMoveContext(NpcState npc, LocationData currentLocation, IReadOnlyList<LocationData> candidates, int currentTurn)
            : this(npc, currentLocation, candidates, currentTurn, null)
        {
        }

        public NpcMoveContext(NpcState npc, LocationData currentLocation, IReadOnlyList<LocationData> candidates,
            int currentTurn, IReadOnlyList<NpcState> presentNpcs)
        {
            Npc = npc;
            CurrentLocation = currentLocation;
            Candidates = candidates;
            CurrentTurn = currentTurn;
            PresentNpcs = presentNpcs;
        }
    }

    /// <summary>Destination이 null이면 "현재 위치에 머문다"를 뜻한다.</summary>
    public readonly struct NpcMoveResult
    {
        public readonly LocationData Destination;

        public NpcMoveResult(LocationData destination)
        {
            Destination = destination;
        }
    }

    /// <summary>
    /// Major NPC의 행동 선택 + 대사 생성 + 이동 목적지 결정을 전담한다. Belief는 절대 반환하지 않는다
    /// (이미 확정된 값을 읽기만 함). CandidateActions 밖의 행동이나 Candidates 밖의 목적지를 반환하면
    /// 호출자가 무효 처리하고 폴백해야 한다.
    ///
    /// 비동기 계약(중요): 두 메서드 모두 Task를 반환하며, 구현체는 Unity 메인 스레드를 절대
    /// 동기적으로 막으면 안 된다 - GetAwaiter().GetResult()/.Result/.Wait()를 어디서도 쓰지 않는다.
    /// RuleBasedMajorThinker처럼 실제로 기다릴 것이 없는 구현체는 Task.FromResult로 즉시 완료된
    /// Task를 반환해도 된다(이 경우 진짜 비동기 지점이 없으므로 블로킹 위험도 없다).
    ///
    /// trace 매개변수: NpcDecisionTraceBuilder(Editor 전용 타입)를 object로 넘긴다 - 판단 1건에
    /// 대해 호출자(오케스트레이션 시스템)가 만든 레코드를 명시적으로 전달받아 자신의 구간만 채운다.
    /// 예전처럼 static 공유 필드(NpcDecisionTraceContext.CurrentBuilder)에 의존하지 않는다 -
    /// 비동기 요청이 겹치거나 늦게 도착해도 다른 판단의 레코드를 건드릴 수 없다. 리스너가 없으면
    /// null이 넘어온다. 이 메서드들은 trace에 Publish()를 호출하지 않는다 - Publish는 항상
    /// 호출자(오케스트레이션 시스템)가 최종 결과를 알게 된 뒤 한 번만 수행한다.</summary>
    public interface IMajorNpcThinker
    {
        Task<NpcThinkResult> DecideAsync(NpcThinkContext context, object trace);

        /// <summary>LLM을 쓸 수 없을 때(연결 실패/타임아웃/파싱 실패)는 RuleBased 판단으로 대체해
        /// 반드시 값을 반환한다 - "이동하지 않고 머문다"는 더 이상 고정 폴백이 아니라, 유효한 이동
        /// 필요성이나 목적지가 없을 때만 나오는 결과 중 하나(Destination == null)다.</summary>
        Task<NpcMoveResult> DecideMoveAsync(NpcMoveContext context, object trace);
    }
}
