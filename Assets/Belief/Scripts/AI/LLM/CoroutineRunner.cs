using UnityEngine;

namespace Belief.AI.LLM
{
    /// <summary>
    /// OpenAiTransport처럼 MonoBehaviour가 아닌 순수 C# 클래스가 UnityWebRequest 코루틴을
    /// 실행하기 위한 최소한의 숨은 호스트. 씬 계층에 보이지 않고, 필요할 때 자동 생성되며
    /// 씬 전환에도 살아남는다(진행 중인 호출이 도중에 끊기지 않도록).
    /// </summary>
    public class CoroutineRunner : MonoBehaviour
    {
        static CoroutineRunner instance;

        public static CoroutineRunner Instance
        {
            get
            {
                if (instance != null) return instance;

                var go = new GameObject("~BeliefLlmCoroutineRunner");
                go.hideFlags = HideFlags.HideInHierarchy;
                Object.DontDestroyOnLoad(go);
                instance = go.AddComponent<CoroutineRunner>();
                return instance;
            }
        }
    }
}
