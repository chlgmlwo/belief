namespace Belief.Systems
{
    /// <summary>
    /// Belief 판단의 실제 계산 로직을 담는 단위. 새 판단 요소가 필요하면
    /// 이 인터페이스의 구현체를 하나 추가하고 조립 리스트에 넣기만 하면 된다 -
    /// BeliefSystem 코드는 손대지 않는다.
    /// </summary>
    public interface IBeliefEvaluator
    {
        BeliefContribution Evaluate(BeliefContext context);
    }
}
