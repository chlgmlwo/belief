namespace Belief.AI.LLM
{
    public enum ThinkerMode
    {
        RuleOnly,
        FakeLlm
    }

    /// <summary>
    /// 어떤 Thinker를 쓸지 결정하는 유일한 조립 지점. 새 AI 서비스를 붙이려면
    /// ILlmTransport 구현체 하나만 추가하고 여기 분기를 하나 넣으면 된다 -
    /// LlmMajorThinker/PromptBuilder/ResponseParser는 손대지 않는다.
    /// </summary>
    public static class ThinkerFactory
    {
        /// <summary>timeoutMs는 LlmMajorThinker에 그대로 전달되는 것 외에 여기서 별도로 해석/가공하지
        /// 않는다 - 실제 사용값을 한 곳(LlmMajorThinker.DefaultTimeoutMs 또는 이 인자)에서만 관리하기
        /// 위함. RuleOnly 모드는 이 값을 아예 쓰지 않는다(LlmMajorThinker 자체를 생성하지 않음).</summary>
        public static IMajorNpcThinker Create(
            ThinkerMode mode, PromptRepository promptRepository, FakeTransportMode fakeTransportMode,
            int timeoutMs = LlmMajorThinker.DefaultTimeoutMs)
        {
            var ruleBased = new RuleBasedMajorThinker();

            switch (mode)
            {
                case ThinkerMode.FakeLlm:
                    return new LlmMajorThinker(new FakeTransport(fakeTransportMode), ruleBased, promptRepository, timeoutMs);
                default:
                    return ruleBased;
            }
        }
    }
}
