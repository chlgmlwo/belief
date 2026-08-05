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
            // 검증·정규화 규칙은 그대로 두고, 어떤 경로를 거쳤는지만 함께 기록한다.
            LocationData destination = null;
            var destReason = DestinationNormalizationReason.None;
            if (string.IsNullOrWhiteSpace(p.destinationId)) return NpcJudgmentValidation.Failure("MissingDestination");
            string dest = p.destinationId.Trim();
            if (string.Equals(dest, "stay", StringComparison.OrdinalIgnoreCase))
            {
                destReason = DestinationNormalizationReason.ExplicitStay;
            }
            else
            {
                if (ctx.MoveCandidates == null || ctx.MoveCandidates.Count == 0)
                    return NpcJudgmentValidation.Failure("InvalidDestination", dest, DestinationNormalizationReason.InvalidDestination);
                destination = ctx.MoveCandidates.FirstOrDefault(l => l != null && l.locationId == dest);
                if (destination == null)
                    return NpcJudgmentValidation.Failure("InvalidDestination", dest, DestinationNormalizationReason.InvalidDestination);

                // 현재 위치를 목적지로 답한 경우는 stay와 같은 뜻으로 정규화한다.
                if (ctx.Where != null && destination == ctx.Where.Data)
                {
                    destination = null;
                    destReason = DestinationNormalizationReason.CurrentLocationNormalizedToStay;
                }
                else destReason = DestinationNormalizationReason.ValidMoveCandidate;
            }

            // 자유 텍스트 3종 - 길이만 검사
            // 자유 텍스트 3종. 여기가 <b>LLM 응답 품질 규칙의 소유자</b>다 - ValidatedNpcJudgment는
            // 규칙 기반 결과도 담아야 해서 빈 문자열을 허용하므로, "비어 있으면 안 된다"는 LLM에만
            // 해당하는 요구를 공통 생성자가 아니라 이 파서가 책임진다.
            if (p.dialogue == null) return NpcJudgmentValidation.Failure("MissingDialogue", dest, destReason);
            if (p.dialogue.Length > UnifiedPromptBuilder.MaxDialogueLength) return NpcJudgmentValidation.Failure("DialogueTooLong", dest, destReason);
            // Dialogue는 빈 문자열을 허용한다(규칙 기반도 대사가 없을 수 있다). null만 형식 위반이다.

            string interpretation = p.interpretation ?? "";
            if (string.IsNullOrWhiteSpace(interpretation)) return NpcJudgmentValidation.Failure("EmptyInterpretation", dest, destReason);
            if (interpretation.Length > UnifiedPromptBuilder.MaxInterpretationLength) return NpcJudgmentValidation.Failure("InterpretationTooLong", dest, destReason);

            string goal = p.goal ?? "";
            if (string.IsNullOrWhiteSpace(goal)) return NpcJudgmentValidation.Failure("EmptyGoal", dest, destReason);
            if (goal.Length > UnifiedPromptBuilder.MaxGoalLength) return NpcJudgmentValidation.Failure("GoalTooLong", dest, destReason);

            // 근거 3필드 - 1단계에서 만든 검증기를 그대로 재사용한다
            if (!JudgmentGroundsValidator.TryValidate(
                    p.primaryReason, p.profileInfluence, p.relationshipInfluence,
                    ctx.Npc, ctx.PresentNpcs, ctx.Propagator, out var grounds, out var groundsFailure))
                return NpcJudgmentValidation.Failure(groundsFailure, dest, destReason);

            return NpcJudgmentValidation.Success(new NpcJudgment(
                interpretation, belief, goal, action, destination, p.dialogue, grounds), dest, destReason);
        }
    }
}
