using System;
using System.Linq;
using Belief.Data;
using UnityEngine;

namespace Belief.AI.LLM
{
    /// <summary>통합 판단 응답 DTO. JsonUtility로 역직렬화하므로 필드명이 JSON 키와 정확히 같아야 한다.</summary>
    [Serializable]
    public class UnifiedLlmResponse
    {
        public string interpretation;
        public string belief;
        public string goal;
        public string action;
        public string destinationId;
        public string dialogue;
        public string primaryReason;
        public string profileInfluence;
        public string relationshipInfluence;
    }

    /// <summary>
    /// 통합 판단 응답을 검증한다. 어떤 경우에도 예외를 던지지 않고 NpcJudgmentValidation으로만
    /// 성공/실패를 알린다. 검증 규칙은 기존 ResponseParser와 같은 원칙이다 - Unity가 제공한
    /// 후보/enum 안의 값만 통과시키고, 지어낸 값이 하나라도 있으면 응답 전체를 무효로 본다.
    ///
    /// interpretation/goal은 화이트리스트를 만들 수 없는 자유 텍스트라 길이만 제한한다.
    /// Shadow Mode에서는 이 값이 월드에 적용되지 않으므로 안전하지만, 실제 적용 단계로 넘어갈 때는
    /// goal을 어떻게 제약할지 반드시 다시 정해야 한다.
    /// </summary>
    public static class UnifiedResponseParser
    {
        public static NpcJudgmentValidation Parse(string raw, NpcJudgmentContext ctx)
        {
            if (string.IsNullOrWhiteSpace(raw)) return NpcJudgmentValidation.Failure("EmptyResponse");

            UnifiedLlmResponse p;
            try { p = JsonUtility.FromJson<UnifiedLlmResponse>(raw); }
            catch { return NpcJudgmentValidation.Failure("JsonParseFailure"); }
            if (p == null) return NpcJudgmentValidation.Failure("JsonParseFailure");

            // belief - 5개 enum 화이트리스트
            if (string.IsNullOrWhiteSpace(p.belief)) return NpcJudgmentValidation.Failure("MissingBelief");
            if (!Enum.TryParse<BeliefState>(p.belief.Trim(), ignoreCase: true, out var belief)
                || belief == BeliefState.Unknown)
                return NpcJudgmentValidation.Failure("InvalidBelief");

            // action - availableActions 화이트리스트
            if (string.IsNullOrWhiteSpace(p.action)) return NpcJudgmentValidation.Failure("MissingAction");
            if (ctx.ActionCandidates == null || ctx.ActionCandidates.Count == 0)
                return NpcJudgmentValidation.Failure("NoActionCandidates");
            var action = ctx.ActionCandidates.FirstOrDefault(a => a != null && a.actionId == p.action.Trim());
            if (action == null) return NpcJudgmentValidation.Failure("InvalidAction");

            // destinationId - movementCandidates 화이트리스트 또는 stay
            LocationData destination = null;
            if (string.IsNullOrWhiteSpace(p.destinationId)) return NpcJudgmentValidation.Failure("MissingDestination");
            string dest = p.destinationId.Trim();
            if (!string.Equals(dest, "stay", StringComparison.OrdinalIgnoreCase))
            {
                if (ctx.MoveCandidates == null || ctx.MoveCandidates.Count == 0)
                    return NpcJudgmentValidation.Failure("InvalidDestination");
                destination = ctx.MoveCandidates.FirstOrDefault(l => l != null && l.locationId == dest);
                if (destination == null) return NpcJudgmentValidation.Failure("InvalidDestination");
                // 현재 위치를 목적지로 답한 경우는 stay와 같은 뜻으로 정규화한다.
                if (ctx.Where != null && destination == ctx.Where.Data) destination = null;
            }

            // 자유 텍스트 3종 - 길이만 검사
            if (p.dialogue == null) return NpcJudgmentValidation.Failure("MissingDialogue");
            if (p.dialogue.Length > UnifiedPromptBuilder.MaxDialogueLength) return NpcJudgmentValidation.Failure("DialogueTooLong");
            string interpretation = p.interpretation ?? "";
            if (interpretation.Length > UnifiedPromptBuilder.MaxInterpretationLength) return NpcJudgmentValidation.Failure("InterpretationTooLong");
            string goal = p.goal ?? "";
            if (goal.Length > UnifiedPromptBuilder.MaxGoalLength) return NpcJudgmentValidation.Failure("GoalTooLong");

            // 근거 3필드 - 1단계에서 만든 검증기를 그대로 재사용한다
            if (!JudgmentGroundsValidator.TryValidate(
                    p.primaryReason, p.profileInfluence, p.relationshipInfluence,
                    ctx.Npc, ctx.PresentNpcs, ctx.Propagator, out var grounds, out var groundsFailure))
                return NpcJudgmentValidation.Failure(groundsFailure);

            return NpcJudgmentValidation.Success(new NpcJudgment(
                interpretation, belief, goal, action, destination, p.dialogue, grounds));
        }
    }
}
