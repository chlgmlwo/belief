namespace Belief.AI.LLM
{
    /// <summary>
    /// 마지막 호출의 토큰 사용량을 보고할 수 있는 Transport가 선택적으로 구현한다.
    /// 이 정보가 없는 Transport(FakeTransport 등)는 그냥 구현하지 않으면 되고, 호출자는
    /// "is ITokenUsageReporting"으로 확인 후 없으면 tokensAvailable=false로 기록한다.
    /// </summary>
    public interface ITokenUsageReporting
    {
        bool TryGetLastUsage(out int inputTokens, out int outputTokens, out int totalTokens);
    }
}
