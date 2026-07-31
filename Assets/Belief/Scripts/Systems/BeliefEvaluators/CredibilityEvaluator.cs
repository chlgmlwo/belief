namespace Belief.Systems.BeliefEvaluators
{
    /// <summary>정보(주장) 자체의 신뢰도 - 출처 신뢰도(SourceEvaluator)와는 별개.
    /// 회의적인(skepticism) NPC일수록 신뢰도 편차를 덜 반영한다. 카드 원본 baseCredibility가 아니라
    /// BeliefSystem.Evaluate가 미리 계산해 둔 EffectiveCredibility(장소 credibilityModifier +
    /// sensitiveInformationType 일치 보너스 반영)를 입력으로 쓴다 - Location Mechanics V1.</summary>
    public class CredibilityEvaluator : IBeliefEvaluator
    {
        public BeliefContribution Evaluate(BeliefContext context)
        {
            float score = (context.EffectiveCredibility - 0.5f) * (1f - context.Npc.Data.skepticism);
            return new BeliefContribution(BeliefContributionType.Credibility, score, isExceptional: false);
        }
    }
}
