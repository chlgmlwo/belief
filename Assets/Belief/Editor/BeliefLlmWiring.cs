using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Belief.AI.LLM;
using Belief.Core;
using Belief.Data;

namespace Belief.EditorTools
{
    /// <summary>AI 연결 설정을 모든 스테이지 씬에 한 번에 적용하는 도구.
    ///
    /// 씬마다 GameInstaller를 열어 Thinker Mode와 LlmProviderConfig를 손으로 맞추면 하나만 빠뜨려도
    /// 그 구역에서만 조용히 규칙 기반으로 동작해(설정이 없으면 예외 대신 폴백하도록 만들어 뒀기 때문에)
    /// 원인을 찾기 어렵다. 여기서 한 번에 적용하고 결과를 로그로 확인한다.
    ///
    /// 중계 서버 주소가 바뀌면 <see cref="ProxyEndpoint"/>만 고치고 메뉴를 다시 실행하면 된다.</summary>
    public static class BeliefLlmWiring
    {
        const string ConfigPath = "Assets/Belief/Data/AI/LlmProviderConfig_Proxy.asset";
        const string ProxyEndpoint = "https://belief-llm-proxy.itr1823er.workers.dev";

        [MenuItem("Belief/AI/중계 서버 연결 (전 씬 Llm 모드)", priority = 20)]
        public static void WireProxy() => Apply(ThinkerMode.Llm);

        [MenuItem("Belief/AI/AI 끄기 (전 씬 RuleOnly)", priority = 21)]
        public static void DisableLlm() => Apply(ThinkerMode.RuleOnly);

        static void Apply(ThinkerMode mode)
        {
            var cfg = AssetDatabase.LoadAssetAtPath<LlmProviderConfig>(ConfigPath);
            if (cfg == null)
            {
                Debug.LogError($"[LlmWiring] 설정 애셋을 찾을 수 없습니다: {ConfigPath}");
                return;
            }

            cfg.endpoint = ProxyEndpoint;
            cfg.useProxy = true;   // 키는 중계 서버가 갖고 있다 - 클라이언트는 보내지 않는다
            EditorUtility.SetDirty(cfg);

            var sb = new StringBuilder();
            sb.AppendLine($"[LlmWiring] mode={mode} / endpoint={cfg.endpoint}");

            string current = EditorSceneManager.GetActiveScene().path;

            foreach (var entry in EditorBuildSettings.scenes.Where(s => s.enabled))
            {
                var scene = EditorSceneManager.OpenScene(entry.path, OpenSceneMode.Single);
                var installer = Object.FindFirstObjectByType<GameInstaller>();
                if (installer == null) { sb.AppendLine($"  {System.IO.Path.GetFileName(entry.path)} : GameInstaller 없음(건너뜀)"); continue; }

                var so = new SerializedObject(installer);
                so.FindProperty("thinkerMode").enumValueIndex = (int)mode;
                so.FindProperty("llmProviderConfig").objectReferenceValue = mode == ThinkerMode.Llm ? cfg : null;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);

                // 저장 후 실제로 들어갔는지 다시 읽어 확인한다 - 조용히 실패하면 의미가 없다.
                var verify = new SerializedObject(installer);
                var vc = verify.FindProperty("llmProviderConfig").objectReferenceValue;
                sb.AppendLine($"  {System.IO.Path.GetFileName(entry.path),-18} {installer.CurrentTransportDescription}"
                              + (mode == ThinkerMode.Llm && vc == null ? "   ★설정 연결 실패" : ""));
            }

            AssetDatabase.SaveAssets();
            if (!string.IsNullOrEmpty(current)) EditorSceneManager.OpenScene(current, OpenSceneMode.Single);
            Debug.Log(sb.ToString());
        }
    }
}
