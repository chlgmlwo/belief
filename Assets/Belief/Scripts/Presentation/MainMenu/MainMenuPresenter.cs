using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using Belief.Presentation.HUD;

namespace Belief.Presentation.MainMenu
{
    /// <summary>
    /// 게임 실행 시 가장 먼저 표시되는 Main Menu. 하이어라키는 런타임에 Instantiate하지 않고,
    /// MainMenuCanvas.prefab의 인스턴스를 씬 파일에 직접 배치해 둔다(CardTileView/HudView와 동일한
    /// View 패턴이되, 프리팹 "에셋"이 아니라 씬에 미리 놓인 "인스턴스"를 참조한다) - Edit 모드의
    /// Hierarchy/Scene 뷰에서 바로 보이고 드래그로 조절할 수 있어야 하기 때문이다. 게임 방법 팝업은
    /// HowToPlayPopup을 그대로 재사용해 게임 중 [?] 버튼과 동일한 코드/데이터를 공유한다(이 컴포넌트는
    /// 여전히 절차적으로 자기 UI를 짓는다 - 이번 전환 범위 밖).
    /// </summary>
    public class MainMenuPresenter : MonoBehaviour
    {
        [SerializeField] TMP_FontAsset koreanFont;
        [SerializeField] public Belief.Data.PlayHudSkin skin;
        [SerializeField] MainMenuView view;

        static readonly Color PanelColor = new Color(0.09f, 0.12f, 0.10f, 0.95f);
        static readonly Color AccentColor = new Color(0.30f, 0.85f, 0.55f);

        const float FadeDuration = 0.25f;

        CanvasGroup rootCanvasGroup;
        HowToPlayPopup howToPlayPopup;
        bool transitioning;

        /// <summary>씬 에셋에 Inspector로 직접 드래그해 넣는 대신 코드로 씬을 생성할 때 쓴다 -
        /// SerializedObject를 거치지 않는 평범한 필드 대입이라 씬 저장 시 그대로 직렬화된다.</summary>
        public void SetKoreanFont(TMP_FontAsset font) => koreanFont = font;

        void Start()
        {
            EnsureEventSystem();
            BuildUI();
            StartCoroutine(FadeCanvasGroup(rootCanvasGroup, 0f, 1f));
        }

        void BuildUI()
        {
            rootCanvasGroup = view.RootCanvasGroup;
            rootCanvasGroup.alpha = 0f;

            view.StartButton.onClick.AddListener(OnStartClicked);
            view.HowToPlayButton.onClick.AddListener(() => howToPlayPopup?.Show());
            view.QuitButton.onClick.AddListener(OnQuitClicked);

            WireHover(view.StartButton);
            WireHover(view.HowToPlayButton);
            WireHover(view.QuitButton);

            howToPlayPopup = view.gameObject.AddComponent<HowToPlayPopup>();
            howToPlayPopup.Build(view.transform, koreanFont);
        }

        /// <summary>Hover 시 살짝 밝아지는 최소한의 반응 - 과도한 연출 없이 존재감만 준다. 색상 값
        /// 자체는 런타임에만 정해지는 상태가 아니라 static 상수지만, ButtonHoverFeedback.Init이 받는
        /// Image 참조는 프리팹 인스턴스 고유의 컴포넌트라 Instantiate 이후에만 연결할 수 있다.</summary>
        void WireHover(Button btn)
        {
            if (btn == null) return;
            var img = btn.GetComponent<Image>();
            if (img == null) return;
            var hoverProxy = btn.gameObject.AddComponent<ButtonHoverFeedback>();
            hoverProxy.Init(img, PanelColor, AccentColor * 0.35f + PanelColor * 0.65f);
        }

        void OnStartClicked()
        {
            if (transitioning) return;

            string sceneName = ResolveFirstSceneName();
            if (string.IsNullOrEmpty(sceneName)) return; // 원인은 ResolveFirstSceneName이 이미 Error로 남겼다.

            transitioning = true;
            StartCoroutine(StartGameRoutine(sceneName));
        }

        /// <summary>ProgressionController.Awake()와 동일한 경로(Resources/ProgressionData)에서 첫
        /// 스테이지의 sceneName을 읽는다 - 씬 이름을 여기 하드코딩하지 않는다. ProgressionData를 읽기만
        /// 할 뿐 ProgressionController/게임 진행 로직 자체는 건드리지 않는다. 이름이 비어 있거나 활성
        /// Build Profile/Shared Scene List에 등록되지 않은 씬이면 LoadScene을 시도하기 전에 명확한
        /// Error를 남기고 null을 반환해 호출부가 안전하게 중단하게 한다.</summary>
        string ResolveFirstSceneName()
        {
            var data = Resources.Load<Belief.Data.ProgressionData>("ProgressionData");
            if (data == null || data.stages == null || data.stages.Length == 0)
            {
                Debug.LogError("[MainMenu] ProgressionData(Resources/ProgressionData)를 찾을 수 없거나 stages가 비어 있어 시작할 씬을 결정할 수 없습니다.");
                return null;
            }

            string sceneName = data.stages[0].sceneName;
            if (string.IsNullOrEmpty(sceneName))
            {
                Debug.LogError($"[MainMenu] ProgressionData 첫 스테이지({data.stages[0].stageId})의 sceneName이 비어 있어 시작할 씬을 결정할 수 없습니다.");
                return null;
            }

            if (!Application.CanStreamedLevelBeLoaded(sceneName))
            {
                Debug.LogError($"[MainMenu] 씬 '{sceneName}'이 활성 Build Profile/Shared Scene List에 등록되어 있지 않아 로드할 수 없습니다.");
                return null;
            }

            return sceneName;
        }

        IEnumerator StartGameRoutine(string sceneName)
        {
            yield return FadeCanvasGroup(rootCanvasGroup, 1f, 0f);
            SceneManager.LoadScene(sceneName);
        }

        void OnQuitClicked()
        {
#if UNITY_EDITOR
            Debug.Log("[MainMenu] 종료 버튼 클릭 - Editor에서는 Application.Quit()이 동작하지 않아 Play Mode를 대신 종료합니다.");
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        IEnumerator FadeCanvasGroup(CanvasGroup cg, float from, float to)
        {
            float t = 0f;
            cg.alpha = from;
            while (t < FadeDuration)
            {
                t += Time.deltaTime;
                cg.alpha = Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / FadeDuration));
                yield return null;
            }
            cg.alpha = to;
        }

        void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>();
        }
    }
}
