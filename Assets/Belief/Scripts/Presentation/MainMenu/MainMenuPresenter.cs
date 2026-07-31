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
    /// 게임 실행 시 가장 먼저 표시되는 Main Menu. HudPresenter와 같은 "코드로 런타임 UI를 직접
    /// 구성하는" 관례를 그대로 따른다 - 새 UI 시스템이 아니라 같은 CreatePanel/CreateText 패턴,
    /// 같은 CanvasGroup 페이드, 같은 버튼 스타일을 재사용한다. 게임 방법 팝업도 HowToPlayPopup을
    /// 그대로 재사용해 게임 중 [?] 버튼과 동일한 코드/데이터를 공유한다.
    /// </summary>
    public class MainMenuPresenter : MonoBehaviour
    {
        [SerializeField] TMP_FontAsset koreanFont;
        [SerializeField] string firstSceneName = "City";

        static readonly Color PanelColor = new Color(0.09f, 0.12f, 0.10f, 0.95f);
        static readonly Color AccentColor = new Color(0.30f, 0.85f, 0.55f);
        static readonly Color MutedText = new Color(0.72f, 0.78f, 0.74f);
        static readonly Color DisabledColor = new Color(0.28f, 0.32f, 0.30f);
        static readonly Color BackgroundColor = new Color(0.04f, 0.05f, 0.07f);

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
            var canvasGo = new GameObject("MainMenuCanvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            canvasGo.AddComponent<GraphicRaycaster>();
            rootCanvasGroup = canvasGo.AddComponent<CanvasGroup>();
            rootCanvasGroup.alpha = 0f;

            // 3.배경: 새 아트 리소스 없이 어두운 단색 배경 + 아주 옅은 상단/하단 밴드로 은은한
            // 세로 Gradient 느낌만 낸다("왕도의 어두운 실루엣" 대신 최소한의 색 대비로 표현).
            var bg = CreatePanel(canvasGo.transform, "Background", BackgroundColor);
            AnchorFill(bg.GetComponent<RectTransform>());

            var topBand = CreatePanel(canvasGo.transform, "TopBand", new Color(0f, 0f, 0f, 0.25f));
            var tbrt = topBand.GetComponent<RectTransform>();
            tbrt.anchorMin = new Vector2(0f, 0.7f);
            tbrt.anchorMax = new Vector2(1f, 1f);
            tbrt.offsetMin = Vector2.zero; tbrt.offsetMax = Vector2.zero;

            var bottomBand = CreatePanel(canvasGo.transform, "BottomBand", new Color(0f, 0f, 0f, 0.30f));
            var bbrt = bottomBand.GetComponent<RectTransform>();
            bbrt.anchorMin = new Vector2(0f, 0f);
            bbrt.anchorMax = new Vector2(1f, 0.3f);
            bbrt.offsetMin = Vector2.zero; bbrt.offsetMax = Vector2.zero;

            var title = CreateText(canvasGo.transform, "Title", "BELIEF", 72, TextAlignmentOptions.Center);
            title.fontStyle = FontStyles.Bold;
            title.color = AccentColor;
            title.rectTransform.anchorMin = new Vector2(0.2f, 0.60f);
            title.rectTransform.anchorMax = new Vector2(0.8f, 0.78f);

            var subtitle = CreateText(canvasGo.transform, "Subtitle", "PROTOTYPE / MVP", 16, TextAlignmentOptions.Center);
            subtitle.color = MutedText;
            subtitle.rectTransform.anchorMin = new Vector2(0.2f, 0.55f);
            subtitle.rectTransform.anchorMax = new Vector2(0.8f, 0.60f);

            BuildButtons(canvasGo.transform);

            howToPlayPopup = canvasGo.AddComponent<HowToPlayPopup>();
            howToPlayPopup.Build(canvasGo.transform, koreanFont);
        }

        void BuildButtons(Transform canvasT)
        {
            var row = new GameObject("ButtonRow", typeof(RectTransform));
            row.transform.SetParent(canvasT, false);
            var rrt = (RectTransform)row.transform;
            rrt.anchorMin = new Vector2(0.30f, 0.22f);
            rrt.anchorMax = new Vector2(0.70f, 0.40f);
            rrt.offsetMin = Vector2.zero; rrt.offsetMax = Vector2.zero;
            var layout = row.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 14;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childAlignment = TextAnchor.UpperCenter;

            CreateMenuButton(row.transform, "StartButton", "게임 시작", true, OnStartClicked);
            CreateMenuButton(row.transform, "HowToPlayButton", "게임 방법", true, () => howToPlayPopup.Show());
            CreateMenuButton(row.transform, "SettingsButton", "설정 (준비 중)", false, null);
            CreateMenuButton(row.transform, "QuitButton", "종료", true, OnQuitClicked);
        }

        void CreateMenuButton(Transform parent, string name, string label, bool isEnabled, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 46;

            var img = go.AddComponent<Image>();
            img.color = isEnabled ? PanelColor : new Color(PanelColor.r, PanelColor.g, PanelColor.b, 0.6f);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.interactable = isEnabled;
            if (isEnabled && onClick != null) btn.onClick.AddListener(onClick);

            var labelText = CreateText(go.transform, "Label", label, 18, TextAlignmentOptions.Center);
            labelText.fontStyle = FontStyles.Bold;
            labelText.color = isEnabled ? AccentColor : DisabledColor;
            AnchorFill(labelText.rectTransform);

            // Hover 시 살짝 밝아지는 최소한의 반응 - 과도한 연출 없이 존재감만 준다.
            if (isEnabled)
            {
                var hoverProxy = go.AddComponent<ButtonHoverFeedback>();
                hoverProxy.Init(img, PanelColor, AccentColor * 0.35f + PanelColor * 0.65f);
            }
        }

        void OnStartClicked()
        {
            if (transitioning) return;
            transitioning = true;
            StartCoroutine(StartGameRoutine());
        }

        IEnumerator StartGameRoutine()
        {
            yield return FadeCanvasGroup(rootCanvasGroup, 1f, 0f);
            SceneManager.LoadScene(firstSceneName);
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

        // ------------------------------------------------------------ runtime UI helpers (HudPresenter와 같은 관례)

        void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>();
        }

        GameObject CreatePanel(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return go;
        }

        TMP_Text CreateText(Transform parent, string name, string content, int size, TextAlignmentOptions align)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<TextMeshProUGUI>();
            if (koreanFont != null) text.font = koreanFont;
            text.text = content;
            text.fontSize = size;
            text.alignment = align;
            text.color = Color.white;
            text.raycastTarget = false;
            AnchorFill(text.rectTransform);
            return text;
        }

        void AnchorFill(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
