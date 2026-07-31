using UnityEngine;

namespace Belief.Data
{
    public enum LlmProviderType
    {
        OpenAi
    }

    /// <summary>
    /// 어떤 모델을 어떤 설정으로 호출할지만 담는다. API 키는 절대 이 Asset에 저장하지 않는다 -
    /// 키는 항상 ApiKeyProvider(환경 변수 / Editor 전용 EditorPrefs)를 통해서만 읽는다.
    /// </summary>
    [CreateAssetMenu(fileName = "LlmProviderConfig_", menuName = "Belief/AI/LLM Provider Config", order = 20)]
    public class LlmProviderConfig : ScriptableObject
    {
        [Header("Provider")]
        public LlmProviderType provider = LlmProviderType.OpenAi;
        public string modelId = "gpt-4o-mini";
        public string endpoint = "https://api.openai.com/v1/chat/completions";

        [Header("Call Settings")]
        [Min(1)] public int timeoutSeconds = 30;
        [Min(1)] public int maxOutputTokens = 300;
        [Range(0f, 2f)] public float temperature = 0.7f;
        public bool structuredOutput = true;
    }
}
