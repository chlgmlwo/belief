namespace Belief.Data
{
    /// <summary>BeliefState의 점수 순서(BeliefTuning 임계값 내림차순)는 enum 선언 순서와 다르다 -
    /// 조건 비교는 반드시 이 랭크를 거쳐야 한다. Unknown은 "아직 어느 임계값도 만족하지 않음"이므로
    /// 최솟값으로 두지 않고 호출자가 atOrBelow/atOrAbove 양쪽 모두에서 걸러내야 한다.</summary>
    internal static class BeliefRank
    {
        public static int Of(BeliefState state) => state switch
        {
            BeliefState.Trusted => 4,
            BeliefState.Plausible => 3,
            BeliefState.NeedsVerification => 2,
            BeliefState.Doubtful => 1,
            BeliefState.Denied => 0,
            _ => int.MinValue
        };
    }
}
