using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using Belief.Data;

namespace Belief.Presentation.HUD
{
    /// <summary>
    /// "게임 방법" 팝업. 메인 메뉴의 [게임 방법]과 게임 중 [?] 버튼이 각자의 Canvas 위에 이 컴포넌트를
    /// 하나씩 붙여 재사용한다 - 구현은 이 클래스 하나뿐이라 두 곳의 모양이 갈라지지 않는다.
    ///
    /// <b>페이지는 통째로 이미지다.</b> 사이드바 탭·본문·닫기 X까지 한 장에 그려져 있고, 어느 탭이
    /// 선택됐는지도 페이지마다 이미 그려져 있다. 그래서 이 클래스는 텍스트를 조립하지 않고
    /// <see cref="PlayHudSkin.helpPages"/>에서 한 장을 골라 띄운 뒤, 그림 위 좌표에 <b>투명 버튼만</b>
    /// 얹는다(사이드바 4칸 + 닫기). 문구를 고치려면 이미지를 교체하면 되고 코드는 손댈 필요가 없다.
    ///
    /// 클릭 영역은 원본 이미지(1207x731) 기준 정규화 좌표로 잡아 두므로, 화면 해상도가 바뀌어도
    /// 그림과 같은 비율로 함께 움직인다.
    /// </summary>
    public class HowToPlayPopup : MonoBehaviour
    {
        const float FadeDuration = 0.25f;

        /// <summary>원본 페이지 이미지 크기 - 클릭 영역 좌표의 기준이다.</summary>
        const float PageWidth = 1207f;
        const float PageHeight = 731f;

        /// <summary>사이드바 탭 4칸(원본 픽셀 기준 x 78~316). 위에서부터 순서대로 페이지 1~4에 대응한다.</summary>
        static readonly Rect[] TabRects =
        {
            new Rect(78f, 158f, 238f, 58f),
            new Rect(78f, 222f, 238f, 58f),
            new Rect(78f, 283f, 238f, 58f),
            new Rect(78f, 345f, 238f, 58f),
        };

        /// <summary>우측 상단 닫기 X.</summary>
        static readonly Rect CloseRect = new Rect(1064f, 68f, 60f, 50f);

        Sprite[] pages;
        int pageIndex;

        GameObject overlayGo;
        CanvasGroup canvasGroup;
        RectTransform pageRect;
        Image pageImage;

        public bool IsVisible => overlayGo != null && overlayGo.activeSelf;

        public void Build(Transform canvasParent, PlayHudSkin skin)
        {
            pages = skin != null ? skin.helpPages : null;

            overlayGo = new GameObject("HowToPlayPopup", typeof(RectTransform));
            overlayGo.transform.SetParent(canvasParent, false);
            AnchorFill((RectTransform)overlayGo.transform);
            canvasGroup = overlayGo.AddComponent<CanvasGroup>();

            // 뒤쪽 화면을 눌러도 반응하지 않도록 막는 어두운 막.
            var dim = overlayGo.AddComponent<Image>();
            dim.color = new Color(0.05f, 0.04f, 0.03f, 0.72f);
            dim.raycastTarget = true;

            var pageGo = new GameObject("Page", typeof(RectTransform));
            pageGo.transform.SetParent(overlayGo.transform, false);
            pageRect = (RectTransform)pageGo.transform;
            pageRect.anchorMin = pageRect.anchorMax = new Vector2(0.5f, 0.5f);
            pageRect.sizeDelta = new Vector2(PageWidth, PageHeight);
            pageImage = pageGo.AddComponent<Image>();
            pageImage.preserveAspect = true;
            pageImage.raycastTarget = true;   // 그림 위 클릭이 뒤로 새지 않게 막는다

            for (int i = 0; i < TabRects.Length; i++)
            {
                int target = i;
                AddHotspot($"Tab{i + 1}", TabRects[i], () => GoToPage(target));
            }
            AddHotspot("Close", CloseRect, Hide);

            overlayGo.SetActive(false);
        }

        public void Show()
        {
            if (pages == null || pages.Length == 0)
            {
                Debug.LogWarning("[HowToPlayPopup] PlayHudSkin.helpPages가 비어 있어 표시할 페이지가 없습니다.");
                return;
            }

            GoToPage(0);
            overlayGo.SetActive(true);
            canvasGroup.alpha = 0f;
            pageRect.localScale = Vector3.one * 0.97f;
            StopAllCoroutines();
            StartCoroutine(FadeRoutine(0f, 1f, Vector3.one * 0.97f, Vector3.one, null));
        }

        public void Hide()
        {
            StopAllCoroutines();
            StartCoroutine(FadeRoutine(1f, 0f, Vector3.one, Vector3.one * 0.98f, () => overlayGo.SetActive(false)));
        }

        void GoToPage(int index)
        {
            if (pages == null || pages.Length == 0) return;
            pageIndex = Mathf.Clamp(index, 0, pages.Length - 1);
            pageImage.sprite = pages[pageIndex];
        }

        /// <summary>원본 이미지 좌표(왼쪽 위 기준)로 잡은 사각형에 투명 버튼을 얹는다.
        /// Page의 앵커를 그대로 쓰므로 이미지가 커지거나 작아져도 같은 자리에 붙어 있는다.</summary>
        void AddHotspot(string name, Rect rectInPage, Action onClick)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(pageRect, false);
            var rt = (RectTransform)go.transform;

            rt.anchorMin = new Vector2(rectInPage.xMin / PageWidth, 1f - rectInPage.yMax / PageHeight);
            rt.anchorMax = new Vector2(rectInPage.xMax / PageWidth, 1f - rectInPage.yMin / PageHeight);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var img = go.AddComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0f);   // 보이지 않지만 클릭은 받는다
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.transition = Selectable.Transition.None;  // 선택 표시는 이미지가 이미 갖고 있다
            btn.onClick.AddListener(() => onClick?.Invoke());
        }

        IEnumerator FadeRoutine(float fromAlpha, float toAlpha, Vector3 fromScale, Vector3 toScale, Action onComplete)
        {
            float t = 0f;
            while (t < FadeDuration)
            {
                t += Time.deltaTime;
                float e = Mathf.SmoothStep(0f, 1f, t / FadeDuration);
                canvasGroup.alpha = Mathf.Lerp(fromAlpha, toAlpha, e);
                pageRect.localScale = Vector3.Lerp(fromScale, toScale, e);
                yield return null;
            }
            canvasGroup.alpha = toAlpha;
            pageRect.localScale = toScale;
            onComplete?.Invoke();
        }

        static void AnchorFill(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
