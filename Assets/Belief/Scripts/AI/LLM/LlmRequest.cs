namespace Belief.AI.LLM
{
    /// <summary>Transport에 실제로 보내는 값(현재는 프롬프트 문자열 하나)을 감싼다 - 나중에
    /// temperature 등 생성 파라미터가 필요해지면 이 구조체에만 필드를 추가하면 된다.</summary>
    public readonly struct LlmRequest
    {
        public readonly string Prompt;

        public LlmRequest(string prompt)
        {
            Prompt = prompt;
        }
    }
}
