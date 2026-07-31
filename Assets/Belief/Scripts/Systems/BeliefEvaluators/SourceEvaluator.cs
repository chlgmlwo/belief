namespace Belief.Systems.BeliefEvaluators
{
    /// <summary>DeclaredSource(card.source)의 baseTrustModifier만 반영한다.</summary>
    public class SourceEvaluator : IBeliefEvaluator
    {
        public BeliefContribution Evaluate(BeliefContext context)
        {
            float score = context.Card.source != null
                ? (context.Card.source.baseTrustModifier - 0.5f) * 0.4f
                : 0f;
            return new BeliefContribution(BeliefContributionType.Source, score, isExceptional: false);
        }
    }
}
