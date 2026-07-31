using Belief.Domain;

namespace Belief.Systems.BeliefEvaluators
{
    /// <summary>장소의 spreadModifier와 현재 장소 상태(SiteState)를 반영한다.</summary>
    public class SituationEvaluator : IBeliefEvaluator
    {
        public BeliefContribution Evaluate(BeliefContext context)
        {
            if (context.CurrentLocation == null)
                return new BeliefContribution(BeliefContributionType.Situation, 0f, isExceptional: false);

            float score = (context.CurrentLocation.Data.spreadModifier - 1f) * 0.2f;

            if (context.CurrentLocation.SiteState == LocationSiteState.Alert) score -= 0.05f;
            else if (context.CurrentLocation.SiteState == LocationSiteState.Locked) score -= 0.1f;

            return new BeliefContribution(BeliefContributionType.Situation, score, isExceptional: false);
        }
    }
}
