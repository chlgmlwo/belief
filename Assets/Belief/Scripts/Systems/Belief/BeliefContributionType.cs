namespace Belief.Systems
{
    /// <summary>어느 Evaluator가 기여했는지 나타내는 코드값. 사람이 읽는 문자열은 UI/디버그 계층에서만 만든다.</summary>
    public enum BeliefContributionType
    {
        Personality,
        ExistingBelief,
        Credibility,
        Source,
        Goal,
        Situation,
        Relationship,
        Memory
    }
}
