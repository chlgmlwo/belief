namespace Belief.Systems
{
    public readonly struct BeliefContribution
    {
        public readonly BeliefContributionType Type;
        public readonly float ScoreDelta;
        public readonly bool IsExceptional;

        public BeliefContribution(BeliefContributionType type, float scoreDelta, bool isExceptional)
        {
            Type = type;
            ScoreDelta = scoreDelta;
            IsExceptional = isExceptional;
        }
    }
}
