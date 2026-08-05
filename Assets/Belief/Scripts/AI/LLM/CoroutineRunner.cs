using System.Collections;
using System.Threading.Tasks;
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

        /// <summary><b>Task.Delay의 WebGL 안전 대체품.</b> Task.Delay는 스레드 풀 타이머에 기대는데
        /// WebGL 빌드는 싱글스레드라 그 타이머가 돌지 않는다 - 즉 브라우저에서는 await가 영영
        /// 깨어나지 않는다. LLM Timeout이 Task.Delay로 구현돼 있으면 응답이 늦을 때 RuleBased
        /// 대체로 넘어가지 못하고 그 NPC의 판단이 통째로 멈춰 버린다(AI가 필수 요소인 이 게임에서는
        /// 게임 진행 자체가 막히는 문제다).
        ///
        /// 여기서는 Unity의 업데이트 루프가 직접 돌리는 코루틴으로 시간을 재므로 모든 플랫폼에서
        /// 동일하게 동작한다. WaitForSecondsRealtime을 쓰는 이유는 Time.timeScale의 영향을 받으면
        /// 안 되기 때문이다 - 네트워크 대기 시간은 게임 시간이 아니라 실제 시간이다.</summary>
        public static Task DelayAsync(int milliseconds)
        {
            var tcs = new TaskCompletionSource<bool>();
            Instance.StartCoroutine(DelayRoutine(milliseconds, tcs));
            return tcs.Task;
        }

        static IEnumerator DelayRoutine(int milliseconds, TaskCompletionSource<bool> tcs)
        {
            if (milliseconds > 0) yield return new WaitForSecondsRealtime(milliseconds / 1000f);
            tcs.TrySetResult(true);
        }
    }
}
