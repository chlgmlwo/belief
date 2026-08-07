using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Belief.Presentation.HUD
{
    /// <summary>밑줄이 깔린 글자 링크 버튼의 호버 연출 - 글자와 밑줄이 함께 살짝 커지고 밑줄이
    /// 굵어진다. 결과창의 "메인 화면"과 일시정지 메뉴의 항목들이 함께 쓴다.
    ///
    /// 결과창의 진행 버튼(NEXT/RETRY)은 아트에 그려진 폴더 탭이라 탭 자체를 들어 올리지만
    /// (<see cref="ResultTabHoverFeedback"/>), 이쪽은 순수한 글자라 같은 연출을 쓸 수 없다.
    /// 대신 밑줄을 상시로 깔아 두었으므로 그 밑줄을 강조하는 쪽이 자연스럽다 - 메인 메뉴 버튼이
    /// 호버에서 밑줄을 켜는 것과 같은 어법이다.
    ///
    /// 배율은 이 오브젝트(버튼) 자체에 건다. 클릭 판정도 함께 커지지만, 커서가 이미 안에 있는
    /// 상태에서만 커지므로 들어왔다 나갔다 깜빡이지 않는다.</summary>
    [RequireComponent(typeof(RectTransform))]
    public class TextLinkHoverFeedback : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        const float TweenDuration = 0.14f;
        const float HoverScale = 1.08f;
        const float UnderlineRestHeight = 2f;
        const float UnderlineHoverHeight = 3.5f;

        [SerializeField] RectTransform underline;

        RectTransform selfRect;
        Coroutine tween;
        bool hovered;

        void Awake()
        {
            selfRect = (RectTransform)transform;
            Apply(0f);
        }

        public void OnPointerEnter(PointerEventData eventData) => Play(true);
        public void OnPointerExit(PointerEventData eventData) => Play(false);

        void OnEnable()
        {
            // 결과창/일시정지 메뉴 모두 껐다 켜지므로 켜질 때마다 기본 상태에서 시작한다.
            hovered = false;
            Apply(0f);
        }

        void OnDisable()
        {
            // 화면이 닫히는 순간 커서가 위에 있었으면 OnPointerExit이 오지 않는다.
            hovered = false;
            if (tween != null) { StopCoroutine(tween); tween = null; }
            Apply(0f);
        }

        void Play(bool on)
        {
            if (hovered == on) return;
            hovered = on;
            if (!isActiveAndEnabled) { Apply(on ? 1f : 0f); return; }
            if (tween != null) StopCoroutine(tween);
            tween = StartCoroutine(TweenRoutine(on ? 1f : 0f));
        }

        IEnumerator TweenRoutine(float to)
        {
            if (selfRect == null) selfRect = (RectTransform)transform;
            float from = Mathf.InverseLerp(1f, HoverScale, selfRect.localScale.x);
            float t = 0f;
            while (t < TweenDuration)
            {
                // 결과창은 timeScale이 0인 상태에서도 떠 있고, 일시정지 메뉴는 아예 멈춘 채로 뜬다.
                t += Time.unscaledDeltaTime;
                Apply(Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / TweenDuration)));
                yield return null;
            }
            Apply(to);
            tween = null;
        }

        void Apply(float k)
        {
            if (selfRect == null) selfRect = (RectTransform)transform;
            selfRect.localScale = Vector3.one * Mathf.Lerp(1f, HoverScale, k);
            if (underline != null)
                underline.sizeDelta = new Vector2(
                    underline.sizeDelta.x, Mathf.Lerp(UnderlineRestHeight, UnderlineHoverHeight, k));
        }
    }
}
