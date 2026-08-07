using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Belief.Presentation.HUD
{
    /// <summary>작전 결과 화면의 진행 버튼(성공=NEXT / 실패=RETRY) 호버 연출 - <b>폴더 탭 자체가
    /// 손에 잡혀 들리는</b> 모양이다.
    ///
    /// 이 버튼은 글자와 탭이 배경 아트(작전 성공UI / 작전 실패 UI)에 인쇄돼 있고 그 위에 투명한
    /// 클릭 영역만 얹혀 있어, 색을 바꾸거나 크기를 키울 대상이 원래 없었다. 그래서 아트에서 탭
    /// 삼각형만 오려낸 그림(NEXT 탭.png / RETRY 탭.png)을 원래 자리에 <b>정확히 겹쳐</b> 두고,
    /// 커서가 올라오면 그 사본만 살짝 키워서 바깥으로 당긴다.
    ///
    /// 오려낸 그림은 <b>패널과 같은 1607x1057 크기</b>다. 탭 주변만 남기고 전부 투명하므로 패널과
    /// 같은 자리에 두기만 하면 픽셀이 정확히 포개진다 - 잘라낸 조각을 좌표로 맞추다 1px씩
    /// 어긋나 테두리가 두 겹으로 보이는 사고를 구조적으로 없앤다.
    ///
    /// 원래 탭을 아트에서 지울 필요가 없는 이유: <b>확대가 이동보다 크다.</b> 사본이 항상 원본보다
    /// 크게 자라 그 위를 덮으므로, 밑에 남아 있는 원래 탭이 삐져나오지 않는다. 확대량보다 큰 값을
    /// 당기면 뒤쪽이 드러나므로 PullDistance는 반드시 그보다 작게 잡는다.</summary>
    [RequireComponent(typeof(RectTransform))]
    public class ResultTabHoverFeedback : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        const float TweenDuration = 0.16f;
        const float HoverScale = 1.10f;
        /// <summary>바깥으로 당기는 거리. 확대로 벌어지는 여유(탭 반폭 x 0.10 = 8px 남짓)보다
        /// 작아야 원래 탭이 뒤로 드러나지 않는다.</summary>
        const float PullDistance = 4f;

        /// <summary>탭 중심에 놓인 축 - 이걸 키워야 탭이 제자리에서 자란다(패널 중심 기준으로
        /// 키우면 탭이 화면 밖으로 날아간다).</summary>
        [SerializeField] RectTransform tabPivot;
        [SerializeField] Image tabArt;
        [SerializeField] Sprite successTab;
        [SerializeField] Sprite failureTab;
        /// <summary>클릭 영역 중심에서 탭 중심까지의 어긋남 - 클릭 영역이 탭보다 넉넉해 정확히
        /// 겹치지 않는다. 오려낸 그림의 불투명 영역을 실측해 넣은 값이다.</summary>
        [SerializeField] Vector2 successPivotOffset;
        [SerializeField] Vector2 failurePivotOffset;
        /// <summary>탭이 삐져나온 방향 - 성공은 오른쪽 아래, 실패는 왼쪽 아래 모서리에 붙어 있다.</summary>
        [SerializeField] Vector2 successPullDirection = new Vector2(1f, 0f);
        [SerializeField] Vector2 failurePullDirection = new Vector2(-1f, 0f);

        RectTransform selfRect;
        Vector2 restPosition;
        Vector2 pullDirection = Vector2.right;
        Coroutine tween;
        bool hovered;

        void Awake()
        {
            selfRect = (RectTransform)transform;
            Rest();
        }

        /// <summary>결과가 정해질 때 한 번 호출 - 어느 탭 그림을 쓸지와 축 위치를 맞춘다.</summary>
        public void SetResult(bool won)
        {
            if (selfRect == null) selfRect = (RectTransform)transform;
            if (tabArt != null) tabArt.sprite = won ? successTab : failureTab;

            restPosition = won ? successPivotOffset : failurePivotOffset;
            pullDirection = (won ? successPullDirection : failurePullDirection).normalized;

            if (tabPivot != null)
            {
                tabPivot.anchoredPosition = restPosition;
                // 오려낸 그림이 패널과 정확히 같은 자리에 오도록 되돌린다 - 이 오브젝트가
                // ResultScreen 안에서 어디에 있든 항상 패널 중심(0,0)을 가리키게 된다.
                if (tabArt != null)
                    tabArt.rectTransform.anchoredPosition = -(selfRect.anchoredPosition + restPosition);
            }

            hovered = false;
            if (tween != null) { StopCoroutine(tween); tween = null; }
            Rest();
        }

        public void OnPointerEnter(PointerEventData eventData) => Play(true);
        public void OnPointerExit(PointerEventData eventData) => Play(false);

        void OnDisable()
        {
            // 결과 화면이 닫히는 순간 커서가 위에 있었으면 OnPointerExit이 오지 않는다.
            hovered = false;
            if (tween != null) { StopCoroutine(tween); tween = null; }
            Rest();
        }

        void Play(bool on)
        {
            if (tabPivot == null || hovered == on) return;
            hovered = on;
            if (!isActiveAndEnabled) { Apply(on ? 1f : 0f); return; }
            if (tween != null) StopCoroutine(tween);
            tween = StartCoroutine(TweenRoutine(on ? 1f : 0f));
        }

        IEnumerator TweenRoutine(float to)
        {
            float from = Mathf.InverseLerp(1f, HoverScale, tabPivot.localScale.x);
            float t = 0f;
            while (t < TweenDuration)
            {
                // 결과 화면은 timeScale이 0인 상태에서도 떠 있을 수 있다.
                t += Time.unscaledDeltaTime;
                Apply(Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / TweenDuration)));
                yield return null;
            }
            Apply(to);
            tween = null;
        }

        void Rest() => Apply(0f);

        /// <summary>k=0 완전히 제자리(아트와 픽셀이 겹쳐 보이지 않음), k=1 들린 상태.</summary>
        void Apply(float k)
        {
            if (tabPivot == null) return;
            tabPivot.localScale = Vector3.one * Mathf.Lerp(1f, HoverScale, k);
            tabPivot.anchoredPosition = restPosition + pullDirection * (PullDistance * k);
        }
    }
}
