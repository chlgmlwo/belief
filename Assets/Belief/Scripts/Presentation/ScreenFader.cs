using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Belief.Presentation
{
    /// <summary>
    /// 씬 전환을 검은 화면으로 가려 주는 유일한 영속 오버레이(DontDestroyOnLoad).
    ///
    /// 두 가지를 동시에 해결한다.
    /// <list type="bullet">
    /// <item><b>전환이 뚝 끊기던 것</b> - 나가는 화면을 검게 덮고, 새 씬이 준비된 뒤 걷어낸다.</item>
    /// <item><b>브리핑 전에 인게임이 잠깐 보이던 것</b> - 새 씬은 로드되자마자 이미 검은 화면
    /// 아래에 있으므로, 브리핑이 자리를 잡기 전의 한두 프레임이 새어 나오지 않는다.</item>
    /// </list>
    ///
    /// 자기 Canvas를 코드로 짓고 정렬 최대값(short.MaxValue)으로 올린다 - 게임 안의 어떤 캔버스
    /// (HudCanvas 0, StageBriefingCanvas 50)보다 확실히 위에 있어야 가리는 의미가 있다.
    /// </summary>
    public class ScreenFader : MonoBehaviour
    {
        public static ScreenFader Instance { get; private set; }

        public const float DefaultDuration = 0.35f;

        CanvasGroup group;
        Coroutine routine;

        /// <summary>지금 씬 전환을 위해 어두워지는 중인지 - 중복 요청을 막는다.</summary>
        bool leaving;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Bootstrap()
        {
            if (Instance != null) return;
            var go = new GameObject("ScreenFader");
            DontDestroyOnLoad(go);
            go.AddComponent<ScreenFader>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            Build();
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        void Build()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Canvas.sortingOrder는 short 범위로 잘린다 - int.MaxValue를 넣으면 -1로 넘쳐서
            // 커튼이 오히려 맨 뒤에 그려진다(실제로 그래서 아무것도 안 가려졌다).
            canvas.sortingOrder = short.MaxValue;

            group = gameObject.AddComponent<CanvasGroup>();
            // 덮개는 클릭을 삼키기만 하면 되고, 자기 자신은 아무 입력도 받지 않는다.
            group.interactable = false;

            var imageGo = new GameObject("Curtain", typeof(RectTransform));
            imageGo.transform.SetParent(transform, false);
            var rt = (RectTransform)imageGo.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;

            var image = imageGo.AddComponent<Image>();
            image.color = Color.black;
            image.raycastTarget = true;

            // 첫 씬이 뜨기 전부터 덮여 있어야 시작 순간이 새지 않는다.
            SetAlpha(1f);
        }

        /// <summary>씬이 바뀌면 무조건 덮인 상태에서 시작해 걷어낸다. sceneLoaded는 새 씬이 처음
        /// 그려지기 전에 오므로, 여기서 덮으면 인게임 화면이 한 프레임도 새어 나오지 않는다.</summary>
        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            leaving = false;
            SetAlpha(1f);
            Restart(FadeRoutine(1f, 0f, DefaultDuration, null));
        }

        /// <summary>화면을 검게 덮은 뒤 씬을 로드한다. 씬 로드 자체를 이 메서드로만 하게 해야
        /// 어느 경로로 나가든 전환이 같은 모양이 된다.</summary>
        public void LoadScene(string sceneName)
        {
            if (leaving || string.IsNullOrEmpty(sceneName)) return;
            leaving = true;
            Restart(FadeRoutine(group.alpha, 1f, DefaultDuration, () => SceneManager.LoadScene(sceneName)));
        }

        void Restart(IEnumerator r)
        {
            if (routine != null) StopCoroutine(routine);
            routine = StartCoroutine(r);
        }

        IEnumerator FadeRoutine(float from, float to, float duration, Action onComplete)
        {
            SetAlpha(from);
            float t = 0f;
            while (t < duration)
            {
                // 결과 팝업 등에서 timeScale이 0이어도 전환은 돌아야 한다.
                t += Time.unscaledDeltaTime;
                SetAlpha(Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / duration)));
                yield return null;
            }
            SetAlpha(to);
            routine = null;
            onComplete?.Invoke();
        }

        void SetAlpha(float a)
        {
            group.alpha = a;
            // 완전히 걷힌 뒤에는 클릭을 삼키면 안 된다.
            group.blocksRaycasts = a > 0.01f;
        }
    }
}
