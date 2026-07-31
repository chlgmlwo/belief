using Belief.Data;

namespace Belief.Systems.BeliefEvaluators
{
    /// <summary>기존 믿음과 일관된 방향으로 소폭 가중 - 매 턴 급변하지 않도록 한다.</summary>
    public class ExistingBeliefEvaluator : IBeliefEvaluator
    {
        public BeliefContribution Evaluate(BeliefContext context)
        {
            float delta = context.Npc.GetBelief(context.Card) switch
            {
                BeliefState.Trusted => 0.05f,
                BeliefState.Plausible => 0.03f,
                BeliefState.Doubtful => -0.03f,
                BeliefState.Denied => -0.05f,
                _ => 0f
            };
            return new BeliefContribution(BeliefContributionType.ExistingBelief, delta, isExceptional: false);
        }
    }
}
