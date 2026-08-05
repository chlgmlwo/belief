using System.Collections.Generic;
using Belief.AI.LLM;
using Belief.Data;
using Belief.Domain;

namespace Belief.AI
{
    /// <summary>
    /// 통합 판단(Interpretation/Belief/Goal/Action/Destination/Dialogue) 하나에 필요한 입력을
    /// <b>값으로 고정해</b> 담는다. Shadow Mode는 실제 판단이 적용된 뒤에 발사되므로, 이 구조체가
    /// "판단 직전의 세계"를 붙잡아 두지 않으면 이미 바뀐 상태를 보고 비교하게 된다.
    ///
    /// NpcState 참조를 들고 있긴 하지만 이는 프로필·관계 같은 <b>변하지 않는 정의</b>를 읽기
    /// 위해서다 - 판단 중 바뀌는 값(Belief/Goal)은 전부 아래 Before 필드에 복사해 둔다.
    /// </summary>
    public readonly struct NpcJudgmentContext
    {
        public readonly NpcState Npc;
        public readonly InformationCardData Card;
        public readonly LocationState Where;
        public readonly int Turn;

        /// <summary>규칙 기반 BeliefSystem이 이 카드에 대해 값을 확정하기 <b>전</b>의 믿음.</summary>
        public readonly BeliefState BeliefBefore;
        public readonly string GoalBefore;

        public readonly WorkingMemory Memory;
        public readonly IReadOnlyList<NpcActionData> ActionCandidates;
        public readonly IReadOnlyList<LocationData> MoveCandidates;
        public readonly IReadOnlyList<NpcState> PresentNpcs;
        public readonly NpcState Propagator;

        public NpcJudgmentContext(
            NpcState npc, InformationCardData card, LocationState where, int turn,
            BeliefState beliefBefore, string goalBefore, WorkingMemory memory,
            IReadOnlyList<NpcActionData> actionCandidates, IReadOnlyList<LocationData> moveCandidates,
            IReadOnlyList<NpcState> presentNpcs, NpcState propagator)
        {
            Npc = npc; Card = card; Where = where; Turn = turn;
            BeliefBefore = beliefBefore; GoalBefore = goalBefore; Memory = memory;
            ActionCandidates = actionCandidates; MoveCandidates = moveCandidates;
            PresentNpcs = presentNpcs; Propagator = propagator;
        }
    }

    /// <summary>
    /// 통합 판단 결과. 검증을 통과한 것만 만들어지므로 Action/Destination/Belief는 항상 Unity가
    /// 제공한 후보 안의 값이다.
    ///
    /// <b>이 타입은 월드를 바꿀 수단을 갖지 않는다.</b> Shadow Mode에서 이 결과는 비교 로그로만
    /// 흘러가며, ShadowJudgmentSystem 역시 ActionResolutionSystem·BeliefSystem.Apply·NpcState의
    /// 변경 API를 전혀 참조하지 않는다 - 실수로 적용하는 코드를 쓰는 것 자체가 불가능하다.
    /// </summary>
    public readonly struct NpcJudgment
    {
        public readonly string Interpretation;
        public readonly BeliefState Belief;
        public readonly string Goal;
        public readonly NpcActionData Action;

        /// <summary>null이면 "이동하지 않는다"(stay). 후보 밖 목적지는 검증에서 걸러진다.</summary>
        public readonly LocationData Destination;

        public readonly string Dialogue;
        public readonly JudgmentGrounds Grounds;

        public NpcJudgment(string interpretation, BeliefState belief, string goal,
            NpcActionData action, LocationData destination, string dialogue, JudgmentGrounds grounds)
        {
            Interpretation = interpretation; Belief = belief; Goal = goal;
            Action = action; Destination = destination; Dialogue = dialogue; Grounds = grounds;
        }
    }

    /// <summary>통합 판단 응답 검증 결과. 실패해도 예외를 던지지 않는다.</summary>
    public readonly struct NpcJudgmentValidation
    {
        public readonly bool IsValid;
        public readonly string FailureReason;
        public readonly NpcJudgment Judgment;

        NpcJudgmentValidation(bool isValid, string failureReason, NpcJudgment judgment)
        {
            IsValid = isValid; FailureReason = failureReason; Judgment = judgment;
        }

        public static NpcJudgmentValidation Success(NpcJudgment j) => new NpcJudgmentValidation(true, null, j);
        public static NpcJudgmentValidation Failure(string reason) => new NpcJudgmentValidation(false, reason, default);
    }
}
