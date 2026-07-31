using System;

namespace Belief.AI.LLM
{
    /// <summary>
    /// API 키는 어떤 코드나 ScriptableObject Asset에도 저장하지 않는다. 우선순위:
    /// 1) 환경 변수 BELIEF_LLM_API_KEY_{PROVIDER} (빌드/CI 등 모든 환경에서 동작)
    /// 2) (Editor 전용) EditorPrefs - OS 사용자 레지스트리에 저장되어 프로젝트 파일/버전관리와
    ///    완전히 분리된다. LlmApiKeySettingsWindow로 값을 넣고 뺄 수 있다.
    /// 이 프로젝트는 아직 git 저장소가 아니지만(별도 .gitignore 대상이 없음), 두 경로 모두
    /// 프로젝트 파일 자체에는 키를 절대 쓰지 않으므로 나중에 git을 붙여도 안전하다.
    /// </summary>
    public static class ApiKeyProvider
    {
        const string EnvVarPrefix = "BELIEF_LLM_API_KEY_";

        public static bool TryGetApiKey(string providerId, out string apiKey)
        {
            string envVarName = EnvVarPrefix + providerId.ToUpperInvariant();
            string fromEnv = Environment.GetEnvironmentVariable(envVarName);
            if (!string.IsNullOrEmpty(fromEnv))
            {
                apiKey = fromEnv;
                return true;
            }

#if UNITY_EDITOR
            string fromEditorPrefs = UnityEditor.EditorPrefs.GetString(EditorPrefsKey(providerId), "");
            if (!string.IsNullOrEmpty(fromEditorPrefs))
            {
                apiKey = fromEditorPrefs;
                return true;
            }
#endif
            apiKey = null;
            return false;
        }

#if UNITY_EDITOR
        public static string EditorPrefsKey(string providerId) => "Belief.LlmApiKey." + providerId;
#endif
    }
}
