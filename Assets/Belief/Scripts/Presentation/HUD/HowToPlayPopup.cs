using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Belief.Data;

namespace Belief.Presentation.HUD
{
    /// <summary>
    /// "게임 방법" 페이지형 팝업. 메인 메뉴의 [게임 방법]과 게임 중 [?] 버튼이 각자의 Canvas 위에
    /// 이 컴포넌트를 하나씩 붙여 재사용한다 - 구현은 이 클래스 하나뿐이고, 내용도 HowToPlayData
    /// 에셋 하나를 공유해서 읽으므로 두 곳에 같은 설명을 중복 작성하지 않는다.
    /// 기존 Overlay(Mission Complete 등)와 같은 배경 색/버튼 스타일/페이드 속도를 따르되, 여러 페이지를
    /// 넘겨봐야 하므로 이전/다음 버튼과 페이지 표시가 추가된 구조다.
    /// </summary>
    public class HowToPlayPopup : MonoBehaviour
    {
        const float FadeDuration = 0.25f;
        static readonly Color PanelColor = new Color(0.09f, 0.12f, 0.10f, 0.95f);
        static readonly Color AccentColor = new Color(0.30f, 0.85f, 0.55f);
        static readonly Color MutedText = new Color(0.72f, 0.78f, 0.74f);

        TMP_FontAsset font;
        HowToPlayData data;
        int pageIndex;

        GameObject overlayGo;
        CanvasGroup canvasGroup;
        Transform box;
        TMP_Text titleText;
        TMP_Text bodyText;
        TMP_Text pageIndicatorText;
        Button prevButton;
        Button nextButton;

        public bool IsVisible => overlayGo != null && overlayGo.activeSelf;

        public void Build(Transform canvasParent, TMP_FontAsset koreanFont)
        {
            font = koreanFont;
            data = Resources.Load<HowToPlayData>("HowToPlayData");

            overlayGo = CreatePanel(canvasParent, "HowToPlayPopup", new Color(0.02f, 0.03f, 0.02f, 0.88f), blocksInput: true);
            AnchorFill(overlayGo.GetComponent<RectTransform>());
            canvasGroup = overlayGo.AddComponent<CanvasGroup>();

            var boxGo = CreatePanel(overlayGo.transform, "Box", PanelColor);
            box = boxGo.transform;
            var brt = boxGo.GetComponent<RectTransform>();
            brt.anchorMin = new Vector2(0.28f, 0.18f);
            brt.anchorMax = new Vector2(0.72f, 0.82f);
            brt.offsetMin = Vector2.zero; brt.offsetMax = Vector2.zero;

            titleText = CreateText(box, "Title", "", 24, TextAlignmentOptions.Center);
            titleText.fontStyle = FontStyles.Bold;
            titleText.color = AccentColor;
            titleText.rectTransform.anchorMin = new Vector2(0.06f, 0.88f);
            titleText.rectTransform.anchorMax = new Vector2(0.94f, 0.97f);

            bodyText = CreateText(box, "Body", "", 16, TextAlignmentOptions.Center);
            bodyText.textWrappingMode = TextWrappingModes.Normal;
            bodyText.rectTransform.anchorMin = new Vector2(0.08f, 0.30f);
            bodyText.rectTransform.anchorMax = new Vector2(0.92f, 0.86f);

            pageIndicatorText = CreateText(box, "PageIndicator", "", 12, TextAlignmentOptions.Center);
            pageIndicatorText.color = MutedText;
            pageIndicatorText.rectTransform.anchorMin = new Vector2(0f, 0.20f);
            pageIndicatorText.rectTransform.anchorMax = new Vector2(1f, 0.28f);

            prevButton = CreateSmallButton(box, "PrevButton", "이전", new Vector2(0.06f, 0.04f), new Vector2(0.30f, 0.15f), OnPrevClicked);
            CreateSmallButton(box, "CloseButton", "닫기", new Vector2(0.38f, 0.04f), new Vector2(0.62f, 0.15f), Hide);
            nextButton = CreateSmallButton(box, "NextButton", "다음", new Vector2(0.70f, 0.04f), new Vector2(0.94f, 0.15f), OnNextClicked);

            overlayGo.SetActive(false);
        }

        public void Show()
        {
            if (data == null || data.pages == null || data.pages.Length == 0)
            {
                Debug.LogWarning("HowToPlayPopup: Resources/HowToPlayData를 찾을 수 없어 표시할 내용이 없습니다.");
                return;
            }

            pageIndex = 0;
            RefreshPage();
            overlayGo.SetActive(true);
            canvasGroup.alpha = 0f;
            box.localScale = Vector3.one * 0.96f;
            StopAllCoroutines();
            StartCoroutine(FadeRoutine(0f, 1f, Vector3.one * 0.96f, Vector3.one, null));
        }

        public void Hide()
        {
            StopAllCoroutines();
            StartCoroutine(FadeRoutine(1f, 0f, Vector3.one, Vector3.one * 0.97f, () => overlayGo.SetActive(false)));
        }

        IEnumerator FadeRoutine(float fromAlpha, float toAlpha, Vector3 fromScale, Vector3 toScale, Action onComplete)
        {
            float t = 0f;
            while (t < FadeDuration)
            {
                t += Time.deltaTime;
                float e = Mathf.SmoothStep(0f, 1f, t / FadeDuration);
                canvasGroup.alpha = Mathf.Lerp(fromAlpha, toAlpha, e);
                box.localScale = Vector3.Lerp(fromScale, toScale, e);
                yield return null;
            }
            canvasGroup.alpha = toAlpha;
            box.localScale = toScale;
            onComplete?.Invoke();
        }

        void OnPrevClicked()
        {
            if (pageIndex <= 0) return;
            pageIndex--;
            RefreshPage();
        }

        void OnNextClicked()
        {
            if (data.pages == null || pageIndex >= data.pages.Length - 1) return;
            pageIndex++;
            RefreshPage();
        }

        void RefreshPage()
        {
            var page = data.pages[pageIndex];
            titleText.text = page.title;
            bodyText.text = page.body;
            pageIndicatorText.text = $"{pageIndex + 1} / {data.pages.Length}";
            prevButton.interactable = pageIndex > 0;
            nextButton.interactable = pageIndex < data.pages.Length - 1;
        }

        // ------------------------------------------------------------ runtime UI helpers (HudPresenter와 같은 관례)

        Button CreateSmallButton(Transform parent, string name, string label, Vector2 anchorMin, Vector2 anchorMax, Action onClick)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;

            var img = go.AddComponent<Image>();
            img.color = AccentColor;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick?.Invoke());

            var labelText = CreateText(go.transform, "Label", label, 13, TextAlignmentOptions.Center);
            labelText.color = Color.black;
            labelText.fontStyle = FontStyles.Bold;
            AnchorFill(labelText.rectTransform);

            return btn;
        }

        GameObject CreatePanel(Transform parent, string name, Color color, bool blocksInput = false)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            if (color.a > 0f)
            {
                var img = go.AddComponent<Image>();
                img.color = color;
                img.raycastTarget = blocksInput;
            }
            return go;
        }

        TMP_Text CreateText(Transform parent, string name, string content, int size, TextAlignmentOptions align)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<TextMeshProUGUI>();
            if (font != null) text.font = font;
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
