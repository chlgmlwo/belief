using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Belief.Core;
using Belief.Data;

namespace Belief.Presentation.HUD
{
    /// <summary>스테이지 진입 직후, 실제 입력이 가능해지기 전에 한 번 표시되는 브리핑/미니맵 화면
    /// (section 12 - "스테이지 선택 UI" 자산 조사 결과 이 폴더는 그리드형 선택 화면이 아니라 이
    /// 브리핑 화면 자산이었다). HudPresenter/TurnSystem 등 기존 게임 로직은 전혀 건드리지 않는다 -
    /// 이 화면은 HudCanvas보다 sortingOrder가 높은 별도 Canvas로 화면 전체를 덮어 입력만 차단하고,
    /// "작전 실행" 클릭 시 자기 자신을 비활성화할 뿐이다(아래 진행 중이던 게임 상태는 그대로 유지).
    /// ProgressionData/StageData의 기존 필드만 읽는다 - 새 데이터를 추가하지 않는다.</summary>
    public class StageBriefingPresenter : MonoBehaviour
    {
        [SerializeField] public PlayHudSkin skin;
        [SerializeField] public TMP_FontAsset koreanFont;

        static readonly Color PanelColor = new Color(0.09f, 0.12f, 0.10f, 0.95f);
        static readonly Color AccentColor = new Color(0.30f, 0.85f, 0.55f);
        static readonly Color MutedText = new Color(0.72f, 0.78f, 0.74f);

        GameObject rootGo;
        GameObject canvasGo;
        CanvasGroup canvasGroup;

        void Start()
        {
            EnsureEventSystem();
            BuildUI();
            StartCoroutine(FadeIn());
        }

        void BuildUI()
        {
            canvasGo = new GameObject("StageBriefingCanvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 50; // HudCanvas(기본 0)보다 항상 위에 그려지도록.
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            canvasGo.AddComponent<GraphicRaycaster>();

            rootGo = CreatePanel(canvasGo.transform, "Background", skin?.briefingBackground, new Color(0.04f, 0.05f, 0.04f, 1f), Image.Type.Simple, blocksInput: true);
            AnchorFill(rootGo.GetComponent<RectTransform>());
            canvasGroup = rootGo.AddComponent<CanvasGroup>();

            var pc = ProgressionController.Instance;
            var installer = FindFirstObjectByType<GameInstaller>();
            var objective = pc != null ? pc.CurrentObjective() : null;
            var stageAsset = installer != null ? installer.StageAsset : null;

            int stageIndex = pc != null ? pc.Progress.CurrentStageIndex : 0;
            int stageNumber = stageAsset != null && stageAsset.stageNumber > 0 ? stageAsset.stageNumber : stageIndex + 1;
            string title = objective != null ? objective.displayTitle : (stageAsset != null ? stageAsset.stageName : "");
            string objectiveText = objective != null ? objective.objectiveText : (stageAsset != null ? stageAsset.objective : "");
            int turnLimit = installer != null ? installer.Turns.StageMaxTurns : 0;
            string blurb = stageAsset != null && !string.IsNullOrEmpty(stageAsset.regionDescription)
                ? stageAsset.regionDescription
                : (pc != null ? pc.CurrentStageIntroSubtitle : "");

            BuildTextBlock(canvasGo.transform, stageNumber, title, objectiveText, turnLimit, blurb);
            BuildMap(canvasGo.transform, pc, stageIndex);
            BuildLaunchButton(canvasGo.transform);
        }

        void BuildTextBlock(Transform canvasT, int stageNumber, string title, string objectiveText, int turnLimit, string blurb)
        {
            var stageLabel = CreateText(canvasT, "StageLabel", $"STAGE {stageNumber}", 20, TextAlignmentOptions.TopLeft, skin?.numberFont);
            stageLabel.color = new Color(0.85f, 0.35f, 0.30f);
            stageLabel.fontStyle = FontStyles.Bold;
            SetAnchors(stageLabel.rectTransform, 0.05f, 0.86f, 0.4f, 0.93f);

            var titleText = CreateText(canvasT, "Title", title, 34, TextAlignmentOptions.TopLeft, skin?.titleFont);
            titleText.fontStyle = FontStyles.Bold;
            titleText.textWrappingMode = TextWrappingModes.Normal;
            SetAnchors(titleText.rectTransform, 0.05f, 0.78f, 0.45f, 0.87f);

            var objText = CreateText(canvasT, "Objective", objectiveText, 18, TextAlignmentOptions.TopLeft, skin?.lightFont);
            objText.textWrappingMode = TextWrappingModes.Normal;
            objText.color = MutedText;
            SetAnchors(objText.rectTransform, 0.05f, 0.70f, 0.45f, 0.78f);

            var turnLabel = CreateText(canvasT, "TurnLimitLabel", "TURN LIMIT", 16, TextAlignmentOptions.TopLeft, skin?.numberFont);
            turnLabel.color = new Color(0.5f, 0.35f, 0.2f);
            SetAnchors(turnLabel.rectTransform, 0.05f, 0.55f, 0.3f, 0.60f);

            var turnValue = CreateText(canvasT, "TurnLimitValue", turnLimit > 0 ? turnLimit.ToString() : "-", 64, TextAlignmentOptions.TopLeft, skin?.numberFont);
            turnValue.color = new Color(0.5f, 0.35f, 0.2f);
            turnValue.fontStyle = FontStyles.Bold;
            SetAnchors(turnValue.rectTransform, 0.05f, 0.38f, 0.3f, 0.55f);

            var blurbGo = CreatePanel(canvasT, "BlurbCard", (Sprite)null, new Color(0.35f, 0.28f, 0.18f, 0.85f));
            SetAnchors(blurbGo.GetComponent<RectTransform>(), 0.05f, 0.08f, 0.42f, 0.36f);
            var blurbText = CreateText(blurbGo.transform, "Text", blurb, 15, TextAlignmentOptions.TopLeft, skin?.lightFont);
            blurbText.textWrappingMode = TextWrappingModes.Normal;
            blurbText.color = new Color(0.92f, 0.88f, 0.78f);
            blurbText.rectTransform.anchorMin = new Vector2(0f, 0f);
            blurbText.rectTransform.anchorMax = new Vector2(1f, 1f);
            blurbText.rectTransform.offsetMin = new Vector2(16, 12);
            blurbText.rectTransform.offsetMax = new Vector2(-16, -12);
        }

        /// <summary>미니맵(section 12) - 실제 지리적 좌표 데이터가 없으므로(StageData/ProgressionData에
        /// 지도 좌표 필드가 없음) 세로 스택으로 근사한다. 현재 스테이지는 currentStageIcon, 이후
        /// 스테이지는 lockedStageIcon으로, 이미 지난 스테이지는 표시하지 않는다(가이드 원본도 현재+
        /// 이후만 보여준다).</summary>
        void BuildMap(Transform canvasT, ProgressionController pc, int currentIndex)
        {
            if (pc == null || pc.Data == null || pc.Data.stages == null) return;

            var mapRoot = new GameObject("Map", typeof(RectTransform));
            mapRoot.transform.SetParent(canvasT, false);
            SetAnchors((RectTransform)mapRoot.transform, 0.55f, 0.1f, 0.97f, 0.85f);
            var vlg = mapRoot.AddComponent<VerticalLayoutGroup>();
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.spacing = 18;
            vlg.childForceExpandWidth = false;
            vlg.childForceExpandHeight = false;

            var stages = pc.Data.stages;
            for (int i = currentIndex; i < stages.Length; i++)
            {
                bool isCurrent = i == currentIndex;
                Sprite icon = isCurrent ? skin?.currentStageIcon : skin?.lockedStageIcon;

                var markerGo = CreatePanel(mapRoot.transform, "Marker" + i, icon, Color.clear);
                var le = markerGo.AddComponent<LayoutElement>();
                le.preferredWidth = isCurrent ? 96 : 56;
                le.preferredHeight = isCurrent ? 96 : 56;
                var img = markerGo.GetComponent<Image>();
                if (img != null) img.preserveAspect = true;

                var nameText = CreateText(markerGo.transform, "Name", isCurrent ? stages[i].displayName : "???", 13,
                    TextAlignmentOptions.Center, skin?.boldFont);
                nameText.color = isCurrent ? Color.white : MutedText;
                var nrt = nameText.rectTransform;
                nrt.anchorMin = new Vector2(0f, -0.35f);
                nrt.anchorMax = new Vector2(1f, 0f);
            }
        }

        void BuildLaunchButton(Transform canvasT)
        {
            var btnGo = CreatePanel(canvasT, "LaunchButton", skin?.launchButton, AccentColor);
            SetAnchors(btnGo.GetComponent<RectTransform>(), 0.80f, 0.06f, 0.97f, 0.22f);
            var img = btnGo.GetComponent<Image>();
            if (img != null) img.preserveAspect = skin?.launchButton != null;
            var btn = btnGo.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(Dismiss);
            var label = CreateText(btnGo.transform, "Label", "작전 실행", 22, TextAlignmentOptions.Center, skin?.boldFont);
            label.color = skin?.launchButton != null ? new Color(0.25f, 0.18f, 0.1f) : Color.black;
            label.fontStyle = FontStyles.Bold;
            AnchorFill(label.rectTransform);

            var backLink = CreateText(canvasT, "BackToTitle", "타이틀로", 16, TextAlignmentOptions.Center, skin?.extraLightFont);
            backLink.color = MutedText;
            SetAnchors(backLink.rectTransform, 0.80f, 0.0f, 0.97f, 0.05f);
            var backGo = new GameObject("BackButton", typeof(RectTransform));
            backGo.transform.SetParent(backLink.transform.parent, false);
            var backRt = (RectTransform)backGo.transform;
            backRt.anchorMin = backLink.rectTransform.anchorMin;
            backRt.anchorMax = backLink.rectTransform.anchorMax;
            backRt.offsetMin = Vector2.zero; backRt.offsetMax = Vector2.zero;
            var backImg = backGo.AddComponent<Image>();
            backImg.color = new Color(0, 0, 0, 0);
            var backBtn = backGo.AddComponent<Button>();
            backBtn.targetGraphic = backImg;
            backBtn.onClick.AddListener(() => SceneManager.LoadScene("MainMenu"));
            backLink.transform.SetParent(backGo.transform, false);
            AnchorFill(backLink.rectTransform);
        }

        void Dismiss()
        {
            StartCoroutine(FadeOutAndDisable());
        }

        IEnumerator FadeIn()
        {
            canvasGroup.alpha = 0f;
            float t = 0f;
            while (t < 0.3f) { t += Time.deltaTime; canvasGroup.alpha = Mathf.SmoothStep(0f, 1f, t / 0.3f); yield return null; }
            canvasGroup.alpha = 1f;
        }

        IEnumerator FadeOutAndDisable()
        {
            float t = 0f;
            float start = canvasGroup.alpha;
            while (t < 0.25f) { t += Time.deltaTime; canvasGroup.alpha = Mathf.Lerp(start, 0f, t / 0.25f); yield return null; }
            canvasGroup.alpha = 0f;

            // gameObject 전체(= HudPresenter와 같은 호스트)가 아니라 이 화면 전용 Canvas만 끈다 -
            // 예전엔 gameObject.SetActive(false)를 썼는데, StageBriefingPresenter가 HudPresenter와
            // 같은 GameObject에 붙어 있어서 HUD 전체가 함께 꺼져버리는 버그가 있었다.
            canvasGo.SetActive(false);
        }

        // ------------------------------------------------------------ helpers (HudPresenter와 같은 관례)

        void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        }

        void SetAnchors(RectTransform rt, float xMin, float yMin, float xMax, float yMax)
        {
            rt.anchorMin = new Vector2(xMin, yMin);
            rt.anchorMax = new Vector2(xMax, yMax);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        GameObject CreatePanel(Transform parent, string name, Sprite sprite, Color fallbackColor, Image.Type imageType = Image.Type.Simple, bool blocksInput = false)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            if (sprite == null && fallbackColor.a <= 0f) return go;

            var img = go.AddComponent<Image>();
            if (sprite != null) { img.sprite = sprite; img.type = imageType; }
            else img.color = fallbackColor;
            img.raycastTarget = blocksInput;
            return go;
        }

        TMP_Text CreateText(Transform parent, string name, string content, int size, TextAlignmentOptions align, TMP_FontAsset font)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<TextMeshProUGUI>();
            text.font = font != null ? font : koreanFont;
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
