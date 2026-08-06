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

        float animationDuration = 0.25f;
        float selectedScale = 1f;
        int collapsedSiblingIndex;
        bool hovered;
        Image background;

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

        public void SetSelected(bool selected)
        {
            if (IsSelected == selected) return;
            IsSelected = selected;
            StartStateTween(animationDuration);
        }

        /// <summary>지금 상태에서 카드가 있어야 할 자리. 호버 상승은 선택 상승 위에 더해진다 -
        /// 선택된 카드를 가리켰을 때도 반응이 없으면 "이건 못 누르나" 싶어진다.</summary>
        Vector2 TargetPosition() =>
            (IsSelected ? ExpandedPosition : CollapsedPosition) + new Vector2(0f, hovered ? HoverRaise : 0f);

        float TargetScale() => (IsSelected ? selectedScale : 1f) * (hovered ? HoverScaleMultiplier : 1f);

        Color TargetTint() => hovered ? HoverTint : Color.white;

        /// <summary>선택과 호버가 각자 코루틴을 돌리면 둘 다 매 프레임 같은 위치/크기에 써서 나중에
        /// 시작된 쪽만 남는다(선택된 카드를 가리켰다 떼면 카드가 내려앉는 식). 그래서 어느 쪽이
        /// 바뀌든 항상 이 하나의 트윈만 돌리고, 목표는 그때그때 다시 계산한다.</summary>
        void StartStateTween(float duration)
        {
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
