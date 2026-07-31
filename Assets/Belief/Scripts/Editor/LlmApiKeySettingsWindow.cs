using UnityEditor;
using UnityEngine;
using Belief.AI.LLM;
using Belief.Data;

namespace Belief.EditorTools
{
    /// <summary>
    /// 로컬 개발용으로 API 키를 EditorPrefs에 넣고 빼는 최소한의 창. 값은 OS 사용자 레지스트리에만
    /// 저장되고 프로젝트 파일(Asset/Scene/코드) 어디에도 기록되지 않는다 - Play 모드에서
    /// ApiKeyProvider가 이 값을 읽는다. 프로젝트/버전관리에 전혀 남지 않는다.
    /// </summary>
    public class LlmApiKeySettingsWindow : EditorWindow
    {
        LlmProviderType provider = LlmProviderType.OpenAi;
        string inputValue = "";

        [MenuItem("Belief/AI/Set LLM API Key...")]
        public static void Open()
        {
            var win = GetWindow<LlmApiKeySettingsWindow>("LLM API Key");
            win.minSize = new Vector2(380, 140);
        }

        void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "여기서 설정한 키는 이 컴퓨터의 Editor 설정(EditorPrefs)에만 저장됩니다. " +
                "프로젝트 파일이나 Git 이력에는 절대 포함되지 않습니다.\n" +
                "환경 변수 BELIEF_LLM_API_KEY_<PROVIDER>가 설정되어 있으면 그쪽이 항상 우선합니다.",
                MessageType.Info);

            provider = (LlmProviderType)EditorGUILayout.EnumPopup("Provider", provider);

            string key = ApiKeyProvider.EditorPrefsKey(provider.ToString());
            bool hasStored = !string.IsNullOrEmpty(EditorPrefs.GetString(key, ""));
            EditorGUILayout.LabelField("현재 상태", hasStored ? "설정됨" : "설정 안 됨");

            inputValue = EditorGUILayout.PasswordField("새 API Key", inputValue);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("저장"))
            {
                if (string.IsNullOrEmpty(inputValue))
                {
                    EditorUtility.DisplayDialog("BELIEF", "빈 값은 저장하지 않습니다.", "확인");
                }
                else
                {
                    EditorPrefs.SetString(key, inputValue);
                    inputValue = "";
                    EditorUtility.DisplayDialog("BELIEF", $"{provider} 키를 이 컴퓨터에 저장했습니다.", "확인");
                }
            }
            if (GUILayout.Button("삭제"))
            {
                EditorPrefs.DeleteKey(key);
                EditorUtility.DisplayDialog("BELIEF", $"{provider} 키를 삭제했습니다.", "확인");
            }
            EditorGUILayout.EndHorizontal();
        }
    }
}
