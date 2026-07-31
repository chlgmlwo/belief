namespace Belief.Systems.BeliefEvaluators
{
    /// <summary>trustBias를 기본 앵커 점수로 사용한다.</summary>
    public class PersonalityEvaluator : IBeliefEvaluator
    {
        public BeliefContribution Evaluate(BeliefContext context)
        {
            float score = context.Npc.Data.trustBias;
            return new BeliefContribution(BeliefContributionType.Personality, score, isExceptional: false);
        }
    }
}
