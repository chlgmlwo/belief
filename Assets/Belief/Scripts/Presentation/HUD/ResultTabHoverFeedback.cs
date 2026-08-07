using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Belief.Presentation.HUD
{
    /// <summary>작전 결과 화면의 진행 버튼(성공=NEXT / 실패=RETRY) 호버 연출.
    ///
    /// 이 버튼은 <b>글자가 배경 아트에 인쇄돼 있고 자기 그래픽이 없다</b> - 클릭 영역만 투명하게
    /// 얹혀 있어서 색을 바꾸거나 크기를 키울 대상 자체가 없었다. 그래서 폴더 탭 자리에 은은한
    /// 따뜻한 빛 한 장을 겹쳐 두고, 커서가 올라오면 그것만 밝히고 살짝 키운다.
    ///
    /// 빛은 클릭 영역의 <b>자식</b>이다 - 클릭 판정 크기는 그대로 두고 빛만 탭에 맞춰 줄이기
    /// 위해서다(클릭 영역은 탭보다 넉넉하게 잡혀 있다).
    ///
    /// 성공/실패는 아트 구성이 좌우로 뒤집히고 탭 크기도 달라서(NEXT는 세로로 긴 오각형,
    /// RETRY는 가로로 넓다) <see cref="SetResult"/>로 그때그때 크기를 바꿔 준다. 위치는
    /// ResultScreenLayout이 버튼을 옮겨 주므로 따로 손대지 않는다.</summary>
    [RequireComponent(typeof(RectTransform))]
    public class ResultTabHoverFeedback : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        const float TweenDuration = 0.16f;
        const float HoverScale = 1.08f;
        const float HoverAlpha = 0.26f;

        /// <summary>NPC/장소 호버(NpcActorView.HoverColor)와 같은 따뜻한 색 - 화면마다 호버가
        /// 다른 색으로 빛나지 않게 맞춘다.</summary>
        static readonly Color GlowColor = new Color(1f, 0.93f, 0.72f);

        /// <summary>가장자리가 완전히 투명해지는 원형 그라데이션(`UI/Result/호버 글로우.png`)이어야
        /// 한다. 처음에 둥근 사각형 스프라이트를 썼더니 탭이 오각형이라 테두리가 그대로 드러나
        /// "빛"이 아니라 "붙여 놓은 사각형"으로 보였다(사용자 지적).</summary>
        [SerializeField] Image glow;

        /// <summary>빛의 크기와, 클릭 영역 중심에서 탭 중심까지의 어긋남. 크기는 탭보다 <b>훨씬 크게</b>
        /// 잡는다 - 진한 부분(안쪽 40%)이 탭을 덮고 나머지가 주변으로 번져 사라져야 빛으로 읽힌다.
        /// 중심 보정은 원본 1607x1057 픽셀에서 잰 탭 위치(NEXT x1295~1455 y645~835 /
        /// RETRY x45~245 y645~830)를 캔버스 좌표로 옮긴 값이다 - 클릭 영역이 탭보다 넉넉해
        /// 중심이 정확히 겹치지 않는다.</summary>
        [SerializeField] Vector2 successTabSize = new Vector2(320f, 370f);
        [SerializeField] Vector2 successTabOffset = Vector2.zero;
        [SerializeField] Vector2 failureTabSize = new Vector2(390f, 360f);
        [SerializeField] Vector2 failureTabOffset = new Vector2(-12.5f, -5f);

        Coroutine tween;
        bool hovered;

        /// <summary>Awake에 캐시해 두지 않는다 - SetResult가 Awake보다 먼저 불릴 수도 있고,
        /// 에디터에서 미리보기로 호출할 때는 Awake 자체가 돌지 않는다.</summary>
        RectTransform glowRect => glow != null ? glow.rectTransform : null;

        void Awake() => SetGlow(0f, 1f);

        /// <summary>결과가 정해질 때 한 번 호출 - 탭 크기를 그 아트에 맞춘다.</summary>
        public void SetResult(bool won)
        {
            if (glowRect == null) return;
            glowRect.sizeDelta = won ? successTabSize : failureTabSize;
            glowRect.anchoredPosition = won ? successTabOffset : failureTabOffset;
            // 결과 화면이 다시 뜰 때 이전 호버 상태가 남아 있으면 안 된다.
            hovered = false;
            if (tween != null) { StopCoroutine(tween); tween = null; }
            SetGlow(0f, 1f);
        }

        public void OnPointerEnter(PointerEventData eventData) => Play(true);
        public void OnPointerExit(PointerEventData eventData) => Play(false);

        void OnDisable()
        {
            // 결과 화면이 닫히는 순간 커서가 위에 있었으면 OnPointerExit이 오지 않는다.
            hovered = false;
            if (tween != null) { StopCoroutine(tween); tween = null; }
            SetGlow(0f, 1f);
        }

        void Play(bool on)
        {
            if (glow == null || hovered == on) return;
            hovered = on;
            if (!isActiveAndEnabled) { SetGlow(on ? HoverAlpha : 0f, on ? HoverScale : 1f); return; }
            if (tween != null) StopCoroutine(tween);
            tween = StartCoroutine(TweenRoutine(on ? HoverAlpha : 0f, on ? HoverScale : 1f));
        }

        IEnumerator TweenRoutine(float toAlpha, float toScale)
        {
            float fromAlpha = glow.color.a;
            float fromScale = glowRect.localScale.x;
            float t = 0f;
            while (t < TweenDuration)
            {
                // 결과 화면은 timeScale이 0인 상태에서도 떠 있을 수 있다.
                t += Time.unscaledDeltaTime;
                float e = Mathf.SmoothStep(0f, 1f, t / TweenDuration);
                SetGlow(Mathf.Lerp(fromAlpha, toAlpha, e), Mathf.Lerp(fromScale, toScale, e));
                yield return null;
            }
            SetGlow(toAlpha, toScale);
            tween = null;
        }

        void SetGlow(float alpha, float scale)
        {
            if (glow == null) return;
            glow.color = new Color(GlowColor.r, GlowColor.g, GlowColor.b, alpha);
            if (glowRect != null) glowRect.localScale = Vector3.one * scale;
        }
    }
}
