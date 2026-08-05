using System.Collections.Generic;
using System.Threading.Tasks;
using Belief.AI.LLM;
using Belief.Data;
using Belief.Domain;
using Belief.Systems;

namespace Belief.AI
{
    /// <summary>
    /// 통합 판단 형태로 <b>기존 규칙 기반 결과를 그대로 재현</b>하는 구현체.
    /// IntegratedLlm 모드에서 LLM이 실패했을 때 쓰는 전체 fallback이자, "통합 결과 타입이
    /// 기존 동작을 손실 없이 표현할 수 있는가"를 검증하는 기준선이다.
    ///
    /// <b>공식을 복사하지 않는다.</b> Belief는 BeliefSystem.Evaluate, 행동·대사는
    /// RuleBasedMajorThinker.DecideAsync, 목적지는 RuleBasedMajorThinker.DecideMoveAsync가
    /// 계산한 값을 그대로 옮겨 담기만 한다 - 여기에 새 판단 규칙이 생기면 fallback이 "기존 동작"이
    /// 아니게 되어 폴백의 의미가 사라진다.
    ///
    /// <b>아무 상태도 바꾸지 않는다.</b> BeliefSystem.Apply를 호출하지 않고(Evaluate만 한다),
    /// ActionResolutionSystem도 참조하지 않는다 - 결과를 만들 뿐 적용은 호출자 몫이다.
    /// </summary>
    public class RuleBasedUnifiedThinker : IIntegratedNpcThinker
    {
        readonly BeliefSystem beliefSystem;
        readonly RuleBasedMajorThinker ruleBased;

        public RuleBasedUnifiedThinker(BeliefSystem beliefSystem, RuleBasedMajorThinker ruleBased)
        {
            this.beliefSystem = beliefSystem;
            this.ruleBased = ruleBased;
        }

        public async Task<NpcJudgmentValidation> DecideAsync(NpcJudgmentContext context, object trace)
        {
            if (!(context.Npc.Data is MajorNpcData))
                return NpcJudgmentValidation.Failure("NotMajorNpc");

            // ① Belief - 기존 공식 그대로. Apply는 하지 않는다(적용은 호출자 책임).
            var beliefResult = beliefSystem.Evaluate(
                context.Npc, context.Card, context.Where, context.Memory, context.Turn);
            var belief = beliefResult.FinalBelief;

            // ② 행동·대사 - 기존 RuleBasedMajorThinker가 쓰는 것과 같은 입력으로 같은 메서드를 호출한다.
            var thinkContext = new NpcThinkContext(
                context.Npc, context.Card, belief, context.Memory, context.Where,
                context.ActionCandidates, context.Turn, context.PresentNpcs, context.Propagator);
            var thinkResult = await ruleBased.DecideAsync(thinkContext, trace);

            // ③ 목적지 - 역시 기존 이동 점수식을 그대로 호출한다. 동점 tie-break의 무작위성까지
            //    기존과 동일하게 유지되므로, 같은 시드에서는 기존 경로와 같은 결과가 나온다.
            var moveContext = new NpcMoveContext(
                context.Npc, context.Where != null ? context.Where.Data : context.Npc.CurrentLocation,
                context.MoveCandidates, context.Turn, context.PresentNpcs);
            var moveResult = await ruleBased.DecideMoveAsync(moveContext, trace);

            // ④ Goal - 규칙 기반은 목표를 바꾸지 않는다. 지금 값을 그대로 유지한다.
            string goal = context.Npc.CurrentGoal ?? "";

            // ⑤ Interpretation - 규칙 기반에는 해석이라는 개념 자체가 없다. 없는 것을 지어내지 않고
            //    "이 경로에서는 해석이 산출되지 않는다"는 사실만 남긴다.
            const string interpretation = "";

            string dialogue = ExtractDialogue(thinkResult.Dialogue);

            // 규칙 기반은 근거를 스스로 설명하지 않는다. 무엇이 판단을 결정했는지는 공식상 Belief이므로
            // primaryReason만 belief로 두고 영향 필드는 비운다(LLM 근거와 섞이지 않게 한다).
            var grounds = new JudgmentGrounds("belief", null, null);

            var judgment = new NpcJudgment(
                interpretation, belief, goal, thinkResult.ChosenAction, moveResult.Destination, dialogue, grounds);

            // 규칙 기반 경로에는 파싱 대상 원문이 없다. 목적지 계측은 "무엇을 골랐는가"만 남긴다.
            var destReason = moveResult.Destination == null
                ? DestinationNormalizationReason.ExplicitStay
                : DestinationNormalizationReason.ValidMoveCandidate;
            string rawDest = moveResult.Destination != null ? moveResult.Destination.locationId : "stay";

            return NpcJudgmentValidation.Success(judgment, rawDest, destReason);
        }

        /// <summary>NpcThinkingSystem이 대사를 꺼내는 방식과 동일하게 맞춘다 - 여기서 다른 규칙을
        /// 쓰면 기존 경로와 대사가 어긋난다.</summary>
        static string ExtractDialogue(DialogueContent content)
        {
            if (content == null) return "";
            if (content.IsGenerated) return content.GeneratedText ?? "";
            return content.PredefinedLine != null ? content.PredefinedLine.text : "";
        }
    }
}
