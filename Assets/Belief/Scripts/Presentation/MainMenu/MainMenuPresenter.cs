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
    ///
    /// <b>Hover 연출은 여기서 붙이지 않는다.</b> 서류철 톤에 맞춰 각 버튼이 프리팹 안에
    /// <see cref="Belief.Presentation.HUD.HoverUnderlineFeedback"/>과 밑줄 스프라이트를 직접 들고 있다 -
    /// 게임 내 다른 화면(브리핑·HUD)과 같은 연출을 같은 컴포넌트로 공유하기 위해서다.
    ///
    /// <b>종료 버튼은 없다.</b> 웹(WebGL) 빌드에서는 Application.Quit()이 아무 동작도 하지 않아
    /// 눌러도 반응이 없는 버튼이 되기 때문이다 - 데스크톱 빌드를 다시 낼 때 되살리면 된다.
    /// </summary>
    public class MainMenuPresenter : MonoBehaviour
    {
        [SerializeField] TMP_FontAsset koreanFont;
        [SerializeField] public Belief.Data.PlayHudSkin skin;
        [SerializeField] MainMenuView view;

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

            howToPlayPopup = view.gameObject.AddComponent<HowToPlayPopup>();
            howToPlayPopup.Build(view.transform, koreanFont);
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
