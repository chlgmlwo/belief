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

        /// <summary>endpoint가 AI 회사 서버가 아니라 <b>우리가 띄운 중계 서버</b>일 때 켠다.
        /// 켜면 클라이언트는 Authorization 헤더를 아예 보내지 않고, API 키가 없어도 요청을 진행한다
        /// (키는 중계 서버가 갖고 있다).
        ///
        /// 웹 빌드에서는 이 옵션이 <b>사실상 필수</b>다. 세 가지가 동시에 막기 때문이다:
        /// 1) 브라우저가 CORS로 AI 회사 서버 직접 호출을 차단한다
        /// 2) 빌드에 넣은 키는 누구나 꺼낼 수 있다(요금은 우리가 낸다)
        /// 3) 애초에 웹 빌드에는 키를 넣어 줄 경로가 없다 - ApiKeyProvider가 쓰는 환경 변수도
        ///    EditorPrefs도 브라우저에는 존재하지 않는다</summary>
        [Tooltip("우리가 띄운 중계 서버를 호출할 때 켠다. 클라이언트는 API 키를 보내지 않는다. 웹 빌드에서는 필수.")]
        public bool useProxy;

        [Header("Call Settings")]
        [Min(1)] public int timeoutSeconds = 30;
        [Min(1)] public int maxOutputTokens = 300;
        [Range(0f, 2f)] public float temperature = 0.7f;
        public bool structuredOutput = true;
    }
}
