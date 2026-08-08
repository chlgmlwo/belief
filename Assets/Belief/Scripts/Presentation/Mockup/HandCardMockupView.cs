using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Belief.Presentation.Mockup
{
    /// <summary>UI_PlayHudMockup 전용 손패 카드 뷰. 카드 전달/장소/NPC 선택 등 실제 기능은
    /// 다루지 않고, 위/아래로 부드럽게 움직이는 선택 상승과 커서 호버 반응만 담당한다.</summary>
    public class HandCardMockupView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField] RectTransform cardRoot;
        [SerializeField] Button clickButton;

        public event Action<HandCardMockupView> Clicked;

        public bool IsSelected { get; private set; }
        public Vector2 CollapsedPosition { get; private set; }
        public Vector2 ExpandedPosition { get; private set; }

        // 선택 상승(230px)에 비해 훨씬 작게 - 호버는 "이걸 가리키고 있다"는 안내일 뿐이라
        // 선택만큼 올라오면 둘을 구분할 수 없다.
        const float HoverRaise = 40f;
        const float HoverScaleMultiplier = 1.02f;
        /// <summary>커서를 따라다니는 반응이라 선택 연출(0.25초)보다 짧아야 손이 끌리는 느낌이 안 난다.</summary>
        const float HoverAnimDuration = 0.12f;
        /// <summary>카드 아트가 흰색으로 곱해져 있어 색으로는 밝게 만들 수 없다 - 대신 종이가 따뜻한
        /// 조명을 받은 것처럼 살짝 물들인다.</summary>
        static readonly Color HoverTint = new Color(1f, 0.95f, 0.82f);

        /// <summary>전달 처리 중 손패가 입력을 받지 않는 동안 눌러 두는 색. 카드 아트가 흰색 곱셈이라
        /// 이 값이 그대로 "덜 밝은 종이"로 보인다.</summary>
        static readonly Color LockedTint = new Color(0.60f, 0.58f, 0.56f);

        /// <summary>이번 턴에 쓴 카드가 아래로 빠지는 연출. 카드 높이(230)보다 넉넉히 내려야
        /// 화면 밖으로 완전히 사라진다.</summary>
        public const float ConsumeDuration = 0.32f;
        const float ConsumeDropDistance = 300f;

        float animationDuration = 0.25f;
        float selectedScale = 1f;
        int collapsedSiblingIndex;
        bool hovered;
        bool locked;
        Image background;
        CanvasGroup canvasGroup;

        Coroutine moveRoutine;

        void Awake()
        {
            if (cardRoot == null) cardRoot = (RectTransform)transform;
            if (clickButton == null) clickButton = GetComponentInChildren<Button>(true);
            // Button.targetGraphic이 곧 카드 배경 아트다 - 이름이 아니라 이 연결을 따라가야
            // 칩(Chip1~3) 같은 다른 Image를 잘못 집지 않는다.
            background = clickButton != null ? clickButton.targetGraphic as Image : null;

            // 씬에 이미 배치된 위치를 그대로 기본(Collapsed) 위치로 캡처한다 - 임의의 값으로 덮어쓰지 않는다.
            CollapsedPosition = cardRoot.anchoredPosition;
            ExpandedPosition = CollapsedPosition;
            collapsedSiblingIndex = cardRoot.GetSiblingIndex();

            if (clickButton != null) clickButton.onClick.AddListener(HandleClicked);
        }

        public void OnPointerEnter(PointerEventData eventData) => SetHovered(true);
        public void OnPointerExit(PointerEventData eventData) => SetHovered(false);

        /// <summary>전달이 처리되는 동안 손패 전체를 눌러 둔다 - 이 구간은 LLM이 붙으면 수십 초까지
        /// 가는데, 예전에는 카드가 평소와 똑같이 보이고 호버까지 되면서 클릭만 조용히 무시돼
        /// "카드가 죽었다"로 읽혔다.
        ///
        /// <b>레이캐스트는 일부러 살려 둔다.</b> 클릭이 막히는 게 아니라 HudPresenter까지 가서
        /// "전달하는 중입니다"라고 답해야 하기 때문이다 - 여기서 raycast를 끄면 그 안내도 사라진다.</summary>
        public void SetLocked(bool value)
        {
            if (locked == value) return;
            locked = value;
            StartStateTween(HoverAnimDuration);
        }

        /// <summary>눌려 있는 동안에는 커서를 올려도 카드가 따라 올라오지 않는다 - 올라오면 "지금
        /// 고를 수 있다"는 신호가 되어 잠금 표시와 정면으로 어긋난다.</summary>
        bool HoverActive => hovered && !locked;

        void SetHovered(bool value)
        {
            if (hovered == value) return;
            hovered = value;
            StartStateTween(HoverAnimDuration);
        }

        /// <summary>카드가 커서 밑에 있는 채로 비활성화되면 OnPointerExit가 오지 않아 올라간 채로
        /// 굳는다 - 다시 켜질 때 제자리에서 시작하도록 정리한다.</summary>
        void OnDisable()
        {
            if (!hovered) return;
            hovered = false;
            if (moveRoutine != null) { StopCoroutine(moveRoutine); moveRoutine = null; }
            ApplyTargetsImmediately();
        }

        void OnDestroy()
        {
            if (clickButton != null) clickButton.onClick.RemoveListener(HandleClicked);
        }

        void HandleClicked()
        {
            Clicked?.Invoke(this);
        }

        /// <summary>컨트롤러가 공용 파라미터(상승 거리/시간/확대 비율)를 모든 카드에 동일하게 배분한다.</summary>
        public void Configure(float expandedYOffset, float duration, float scale)
        {
            ExpandedPosition = CollapsedPosition + new Vector2(0f, expandedYOffset);
            animationDuration = duration;
            selectedScale = scale;
        }

        /// <summary>이번 턴에 사용된 카드가 아래로 미끄러져 사라지는 연출. 끝나면 이 자리는 다음
        /// 카드가 그대로 물려받으므로 위치/투명도/선택 상태를 전부 처음으로 되돌려 둔다.
        ///
        /// 호출자(HandCardHudBridge)는 이 연출이 끝날 때까지 손패 재배치를 미뤄야 한다 - 미루지
        /// 않으면 떨어지는 카드가 이미 다음 카드의 내용으로 바뀌어 엉뚱한 카드가 떨어진다.</summary>
        /// <summary>소멸 연출이 도는 동안 참. 이 사이에 들어오는 선택/해제 트윈은 무시한다 -
        /// 무시하지 않으면 같은 프레임에 이어지는 CollapseSelectedVisual이 moveRoutine을 덮어써
        /// 낙하가 시작하자마자 취소된다(실제로 그래서 연출이 아예 안 보였다).</summary>
        bool consuming;

        public void PlayConsumed()
        {
            if (!isActiveAndEnabled) return;
            if (moveRoutine != null) StopCoroutine(moveRoutine);
            consuming = true;
            moveRoutine = StartCoroutine(ConsumeRoutine());
        }

        IEnumerator ConsumeRoutine()
        {
            EnsureCanvasGroup();
            cardRoot.SetAsLastSibling();   // 떨어지는 동안 옆 카드에 가리지 않게

            Vector2 from = cardRoot.anchoredPosition;
            Vector2 to = CollapsedPosition + new Vector2(0f, -ConsumeDropDistance);
            float startScale = cardRoot.localScale.x;

            float t = 0f;
            while (t < 1f)
            {
                t += Time.unscaledDeltaTime / ConsumeDuration;
                float e = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
                cardRoot.anchoredPosition = Vector2.Lerp(from, to, e);
                cardRoot.localScale = Vector3.one * Mathf.Lerp(startScale, 1f, e);
                canvasGroup.alpha = 1f - e;
                yield return null;
            }

            IsSelected = false;
            hovered = false;
            cardRoot.anchoredPosition = CollapsedPosition;
            cardRoot.localScale = Vector3.one;
            cardRoot.SetSiblingIndex(collapsedSiblingIndex);
            // 흰색으로 못 박지 않는다 - 낙하가 도는 동안 손패가 잠겼을 수 있고(카드를 내면 바로
            // 전달 처리가 시작되므로 사실상 항상 그렇다), 그러면 이 자리를 물려받는 다음 카드만
            // 혼자 밝게 남는다.
            SettleTint();
            canvasGroup.alpha = 1f;
            moveRoutine = null;
            consuming = false;
        }

        public const float SlideDuration = 0.24f;
        public const float DrawDuration = 0.26f;
        /// <summary>새로 뽑힌 카드가 밑에서 올라오는 거리. 소멸 낙하(300)보다 훨씬 짧게 잡아
        /// "빠져나간다"와 "채워진다"가 다른 동작으로 읽히게 한다.</summary>
        const float DrawRiseDistance = 90f;

        /// <summary>옆 슬롯에서 미끄러져 들어오는 연출. 슬롯은 고정 좌표라 내용만 갈아끼우는데,
        /// 새 내용을 <b>원래 있던 슬롯 자리에서 출발</b>시켜 제자리로 보내면 카드가 옆으로 옮겨온
        /// 것처럼 읽힌다. xOffset은 직전에 그 카드가 있던 슬롯까지의 거리다.</summary>
        public void PlaySlideFrom(float xOffset)
        {
            if (consuming || !isActiveAndEnabled) return;
            if (moveRoutine != null) StopCoroutine(moveRoutine);
            moveRoutine = StartCoroutine(SlideRoutine(xOffset));
        }

        IEnumerator SlideRoutine(float xOffset)
        {
            EnsureCanvasGroup();
            canvasGroup.alpha = 1f;
            SettleTint();
            Vector2 from = CollapsedPosition + new Vector2(xOffset, 0f);
            float t = 0f;
            while (t < 1f)
            {
                t += Time.unscaledDeltaTime / SlideDuration;
                float e = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
                cardRoot.anchoredPosition = Vector2.Lerp(from, CollapsedPosition, e);
                yield return null;
            }
            cardRoot.anchoredPosition = CollapsedPosition;
            SettleTint();
            moveRoutine = null;
        }

        /// <summary>색을 지금 있어야 할 값으로 즉시 맞춘다.
        ///
        /// 등장/이동 연출은 <b>색을 다루지 않으면서도</b> moveRoutine 자리를 빼앗아 색 트윈을 죽인다.
        /// 실제로 그래서 새로 뽑힌 카드만 잠금 회색에 굳었다 - 전달이 끝나 SetLocked(false)가 흰색
        /// 복귀 트윈을 시작한 바로 그 LateUpdate에서, 같은 프레임에 채워진 새 카드의 PlayDrawn이
        /// 그 트윈을 한 프레임도 돌기 전에 중단시킨다(색은 LockedTint 그대로, 되돌릴 사람 없음).
        /// 내용이 그대로인 옆 카드들은 연출을 걸지 않으니 멀쩡히 흰색으로 돌아와, 그 한 장만 떠 보였다.</summary>
        void SettleTint()
        {
            if (background != null) background.color = TargetTint();
        }

        /// <summary>새로 뽑힌 카드가 손패에 채워지는 연출 - 밑에서 살짝 올라오며 나타난다.</summary>
        public void PlayDrawn()
        {
            if (consuming || !isActiveAndEnabled) return;
            if (moveRoutine != null) StopCoroutine(moveRoutine);
            moveRoutine = StartCoroutine(DrawRoutine());
        }

        IEnumerator DrawRoutine()
        {
            EnsureCanvasGroup();
            SettleTint();
            Vector2 from = CollapsedPosition + new Vector2(0f, -DrawRiseDistance);
            float t = 0f;
            while (t < 1f)
            {
                t += Time.unscaledDeltaTime / DrawDuration;
                float e = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
                cardRoot.anchoredPosition = Vector2.Lerp(from, CollapsedPosition, e);
                cardRoot.localScale = Vector3.one * Mathf.Lerp(0.92f, 1f, e);
                canvasGroup.alpha = e;
                yield return null;
            }
            cardRoot.anchoredPosition = CollapsedPosition;
            cardRoot.localScale = Vector3.one;
            canvasGroup.alpha = 1f;
            SettleTint();
            moveRoutine = null;
        }

        /// <summary>손패가 4장보다 적을 때 남는 빈 슬롯을 감춘다 - 감추지 않으면 직전 카드의
        /// 내용이 그대로 남아 "쓴 카드가 아직 있다"처럼 보인다.</summary>
        public void SetEmpty(bool empty)
        {
            EnsureCanvasGroup();
            canvasGroup.alpha = empty ? 0f : 1f;
            canvasGroup.blocksRaycasts = !empty;
        }

        void EnsureCanvasGroup()
        {
            if (canvasGroup != null) return;
            canvasGroup = cardRoot.GetComponent<CanvasGroup>();
            if (canvasGroup == null) canvasGroup = cardRoot.gameObject.AddComponent<CanvasGroup>();
        }

        public void SetSelected(bool selected)
        {
            if (IsSelected == selected) return;
            IsSelected = selected;
            StartStateTween(animationDuration);
        }

        /// <summary>지금 상태에서 카드가 있어야 할 자리. 호버 상승은 선택 상승 위에 더해진다 -
        /// 선택된 카드를 가리켰을 때도 반응이 없으면 "이건 못 누르나" 싶어진다.</summary>
        Vector2 TargetPosition() =>
            (IsSelected ? ExpandedPosition : CollapsedPosition) + new Vector2(0f, HoverActive ? HoverRaise : 0f);

        float TargetScale() => (IsSelected ? selectedScale : 1f) * (HoverActive ? HoverScaleMultiplier : 1f);

        Color TargetTint() => locked ? LockedTint : (HoverActive ? HoverTint : Color.white);

        /// <summary>선택과 호버가 각자 코루틴을 돌리면 둘 다 매 프레임 같은 위치/크기에 써서 나중에
        /// 시작된 쪽만 남는다(선택된 카드를 가리켰다 떼면 카드가 내려앉는 식). 그래서 어느 쪽이
        /// 바뀌든 항상 이 하나의 트윈만 돌리고, 목표는 그때그때 다시 계산한다.</summary>
        void StartStateTween(float duration)
        {
            // 사라지는 중인 카드는 선택/호버 상태를 더 이상 따르지 않는다 - 낙하가 최우선이다.
            if (consuming) return;

            // 카드는 460px 폭에 간격 0으로 맞닿아 있어 조금만 커져도 옆 카드에 가린다 - 올라오거나
            // 선택된 동안에는 앞으로 끌어온다.
            if (IsSelected || hovered) cardRoot.SetAsLastSibling();

            if (moveRoutine != null) StopCoroutine(moveRoutine);
            if (!isActiveAndEnabled) { ApplyTargetsImmediately(); return; }
            moveRoutine = StartCoroutine(AnimateTo(duration));
        }

        void ApplyTargetsImmediately()
        {
            cardRoot.anchoredPosition = TargetPosition();
            cardRoot.localScale = Vector3.one * TargetScale();
            if (background != null) background.color = TargetTint();
            if (!IsSelected && !hovered) cardRoot.SetSiblingIndex(collapsedSiblingIndex);
        }

        IEnumerator AnimateTo(float duration)
        {
            Vector2 targetPosition = TargetPosition();
            float targetScale = TargetScale();
            Color targetTint = TargetTint();

            Vector2 startPosition = cardRoot.anchoredPosition;
            float startScale = cardRoot.localScale.x;
            Color startTint = background != null ? background.color : Color.white;

            float t = 0f;
            while (t < 1f)
            {
                t += duration > 0f ? Time.unscaledDeltaTime / duration : 1f;
                float e = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
                cardRoot.anchoredPosition = Vector2.Lerp(startPosition, targetPosition, e);
                cardRoot.localScale = Vector3.one * Mathf.Lerp(startScale, targetScale, e);
                if (background != null) background.color = Color.Lerp(startTint, targetTint, e);
                yield return null;
            }

            cardRoot.anchoredPosition = targetPosition;
            cardRoot.localScale = Vector3.one * targetScale;
            if (background != null) background.color = targetTint;
            moveRoutine = null;

            // 내려간 뒤에는 원래 sibling 순서로 복귀한다 - LayoutGroup을 쓰지 않으므로 X 위치에는 영향이 없다.
            if (!IsSelected && !hovered) cardRoot.SetSiblingIndex(collapsedSiblingIndex);
        }
    }
}
