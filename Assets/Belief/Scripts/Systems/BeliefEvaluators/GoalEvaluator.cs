namespace Belief.Systems.BeliefEvaluators
{
    /// <summary>
    /// Major NPC의 목표와 카드의 관련성. 목표-정보 연관성을 판정할 데이터 모델(태그/카테고리)이
    /// 아직 설계되지 않아 현재는 항상 0을 반환하는 정직한 자리표시자다 - 별도 승인 후 고도화 예정.
    /// </summary>
    public class GoalEvaluator : IBeliefEvaluator
    {
        public BeliefContribution Evaluate(BeliefContext context)
        {
            return new BeliefContribution(BeliefContributionType.Goal, 0f, isExceptional: false);
        }
    }
}
