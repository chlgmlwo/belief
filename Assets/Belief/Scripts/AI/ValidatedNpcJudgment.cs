using System.Collections.Generic;
using Belief.AI.LLM;
using Belief.Data;
using Belief.Domain;

namespace Belief.AI
{
    /// <summary>
    /// 이 판단이 "어느 시도의 어느 턴, 누구의 어떤 카드에 대한 것인가"를 고정한다.
    /// 늦게 도착한 응답이 지난 시도에 섞이는 것과, 같은 NPC·같은 턴에 두 번 적용되는 것을
    /// 막는 유일한 근거다.
    /// </summary>
    public readonly struct JudgmentRequestIdentity
    {
        public readonly string StageId;
        public readonly string MissionId;
        public readonly int MissionAttemptId;
        public readonly int Turn;
        public readonly string NpcId;
        public readonly string CardId;
        public readonly string RequestId;

        public JudgmentRequestIdentity(string stageId, string missionId, int missionAttemptId,
            int turn, string npcId, string cardId, string requestId)
        {
            StageId = stageId; MissionId = missionId; MissionAttemptId = missionAttemptId;
            Turn = turn; NpcId = npcId; CardId = cardId; RequestId = requestId;
        }

        /// <summary>중복 적용 방지 키. 같은 시도의 같은 턴에 같은 NPC가 같은 카드를 두 번
        /// 판단 적용하는 것을 막는다(요청이 여러 번 떠도 적용은 한 번).</summary>
        public string ApplicationKey => $"{MissionAttemptId}|{Turn}|{NpcId}|{CardId}";

        public override string ToString() =>
            $"{StageId}/{MissionId}#{MissionAttemptId} T{Turn} {NpcId}·{CardId} ({RequestId})";
    }

    /// <summary>
    /// 검증 과정에서 관찰된 것들. <b>Hard 실패는 여기 담기지 않는다</b> - Hard가 하나라도 있으면
    /// ValidatedNpcJudgment 자체가 만들어지지 않기 때문이다. 여기 남는 것은 적용은 가능하지만
    /// 사람이 나중에 봐야 할 의미적 모순(Soft)뿐이다.
    /// </summary>
    public readonly struct ValidationSummary
    {
        /// <summary>구조적으로 확정할 수 없어 로그로만 남기는 모순들. 비어 있을 수 있다.</summary>
        public readonly IReadOnlyList<string> SoftWarnings;

        /// <summary>이 판단이 어디서 나왔는지 - RuleBased / IntegratedLlm / IntegratedLlm-Fallback.</summary>
        public readonly string Source;

        public ValidationSummary(string source, IReadOnlyList<string> softWarnings)
        {
            Source = source;
            SoftWarnings = softWarnings ?? System.Array.Empty<string>();
        }

        public bool HasSoftWarnings => SoftWarnings != null && SoftWarnings.Count > 0;
    }

    /// <summary>
    /// <b>실제 월드에 적용해도 되는 것이 확정된</b> 통합 판단. 모든 Hard 검증을 통과한 뒤에만
    /// 만들어진다 - 그래서 이 타입을 손에 쥐었다는 것 자체가 "적용해도 안전하다"는 뜻이다.
    ///
    /// 생성자를 private으로 두고 <see cref="TryCreate"/>만 남긴 이유: 검증을 건너뛰고 값을 채워
    /// 넣는 코드를 애초에 쓸 수 없게 하기 위해서다. 부분 적용 금지 원칙은 "적용 도중 실패했을 때
    /// 되돌린다"가 아니라 <b>"적용을 시작하기 전에 실패 가능성을 없앤다"</b>로 지킨다.
    /// </summary>
    public readonly struct ValidatedNpcJudgment
    {
        public readonly string Interpretation;
        public readonly BeliefState Belief;
        public readonly string Goal;
        public readonly NpcActionData Action;

        /// <summary>null이면 이번 턴에 이동하지 않는다(stay).</summary>
        public readonly LocationData Destination;

        public readonly string Dialogue;
        public readonly JudgmentGrounds Grounds;
        public readonly JudgmentRequestIdentity Identity;
        public readonly ValidationSummary Summary;

        ValidatedNpcJudgment(string interpretation, BeliefState belief, string goal, NpcActionData action,
            LocationData destination, string dialogue, JudgmentGrounds grounds,
            JudgmentRequestIdentity identity, ValidationSummary summary)
        {
            Interpretation = interpretation; Belief = belief; Goal = goal; Action = action;
            Destination = destination; Dialogue = dialogue; Grounds = grounds;
            Identity = identity; Summary = summary;
        }

        /// <summary>
        /// 마지막 게이트. 여기서 막는 것은 <b>구조적으로 확실한 것</b>뿐이다 - 후보 화이트리스트는
        /// 이미 파서가 검사했고, 여기서는 그 결과가 실제로 이 NPC의 것인지만 최종 확인한다.
        /// 의미적 모순은 막지 않고 Soft 경고로 넘긴다.
        /// </summary>
        public static bool TryCreate(
            string interpretation, BeliefState belief, string goal, NpcActionData action,
            LocationData destination, string dialogue, JudgmentGrounds grounds,
            JudgmentRequestIdentity identity, ValidationSummary summary,
            NpcJudgmentContext ctx, out ValidatedNpcJudgment judgment, out string failureReason)
        {
            judgment = default;
            failureReason = null;

            // 여기서 보는 것은 어떤 판단 경로에서도 반드시 참이어야 하는 것뿐이다.
            // "Interpretation/Goal이 비면 안 된다" 같은 LLM 응답 품질 규칙은 UnifiedResponseParser의
            // 책임이다 - 규칙 기반 경로는 애초에 Interpretation 개념이 없고 목표가 없는 NPC도 있어서,
            // 그 규칙을 여기 두면 정상적인 규칙 기반 판단이 통과하지 못한다.
            if (belief == BeliefState.Unknown) { failureReason = "InvalidBelief"; return false; }
            if (action == null) { failureReason = "MissingAction"; return false; }
            if (dialogue == null) { failureReason = "MissingDialogue"; return false; }   // 빈 문자열은 허용, null만 실패
            if (interpretation == null) { failureReason = "MissingInterpretation"; return false; }

            // 행동이 정말 이 NPC의 후보인지 - 파서를 우회한 경로가 생겨도 여기서 걸린다.
            if (ctx.ActionCandidates == null || !Contains(ctx.ActionCandidates, action))
            { failureReason = "ActionNotInCandidates"; return false; }

            // 목적지가 정말 이 NPC의 이동 후보인지. null(stay)은 언제나 허용.
            if (destination != null)
            {
                if (ctx.MoveCandidates == null || !Contains(ctx.MoveCandidates, destination))
                { failureReason = "DestinationNotInCandidates"; return false; }
                // 현재 위치가 목적지로 남아 있으면 정규화가 누락된 것 - 적용 단계에서 잡지 않는다.
                if (ctx.Where != null && destination == ctx.Where.Data)
                { failureReason = "DestinationNotNormalized"; return false; }
            }

            judgment = new ValidatedNpcJudgment(interpretation, belief, goal, action,
                destination, dialogue, grounds, identity, summary);
            return true;
        }

        static bool Contains<T>(IReadOnlyList<T> list, T item) where T : class
        {
            for (int i = 0; i < list.Count; i++) if (ReferenceEquals(list[i], item)) return true;
            return false;
        }

        public override string ToString() =>
            $"{Identity} → {Belief} / {(Action != null ? Action.actionId : "-")} / "
            + $"{(Destination != null ? Destination.locationId : "stay")} [{Summary.Source}]";
    }
}
