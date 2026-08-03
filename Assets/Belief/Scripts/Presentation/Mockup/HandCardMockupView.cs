using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Belief.Presentation.Mockup
{
    /// <summary>UI_PlayHudMockup 전용 손패 카드 뷰. 카드 전달/장소/NPC 선택 등 실제 기능은
    /// 다루지 않고, 클릭 시 위/아래로 부드럽게 움직이는 선택 상승 애니메이션만 담당한다.</summary>
    public class HandCardMockupView : MonoBehaviour
    {
        [SerializeField] RectTransform cardRoot;
        [SerializeField] Button clickButton;

        public event Action<HandCardMockupView> Clicked;

        public bool IsSelected { get; private set; }
        public Vector2 CollapsedPosition { get; private set; }
        public Vector2 ExpandedPosition { get; private set; }

        float animationDuration = 0.25f;
        float selectedScale = 1f;
        int collapsedSiblingIndex;

        Coroutine moveRoutine;

        void Awake()
        {
            if (cardRoot == null) cardRoot = (RectTransform)transform;
            if (clickButton == null) clickButton = GetComponentInChildren<Button>(true);

            // 씬에 이미 배치된 위치를 그대로 기본(Collapsed) 위치로 캡처한다 - 임의의 값으로 덮어쓰지 않는다.
            CollapsedPosition = cardRoot.anchoredPosition;
            ExpandedPosition = CollapsedPosition;
            collapsedSiblingIndex = cardRoot.GetSiblingIndex();

            if (clickButton != null) clickButton.onClick.AddListener(HandleClicked);
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

            if (selected) cardRoot.SetAsLastSibling();

            if (moveRoutine != null) StopCoroutine(moveRoutine);
            moveRoutine = StartCoroutine(AnimateTo(selected ? ExpandedPosition : CollapsedPosition,
                selected ? selectedScale : 1f, selected));
        }

        IEnumerator AnimateTo(Vector2 targetPosition, float targetScale, bool selected)
        {
            Vector2 startPosition = cardRoot.anchoredPosition;
            float startScale = cardRoot.localScale.x;

            float t = 0f;
            while (t < 1f)
            {
                t += animationDuration > 0f ? Time.unscaledDeltaTime / animationDuration : 1f;
                float e = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
                cardRoot.anchoredPosition = Vector2.Lerp(startPosition, targetPosition, e);
                cardRoot.localScale = Vector3.one * Mathf.Lerp(startScale, targetScale, e);
                yield return null;
            }

            cardRoot.anchoredPosition = targetPosition;
            cardRoot.localScale = Vector3.one * targetScale;
            moveRoutine = null;

            // 내려간 뒤에는 원래 sibling 순서로 복귀한다 - LayoutGroup을 쓰지 않으므로 X 위치에는 영향이 없다.
            if (!selected) cardRoot.SetSiblingIndex(collapsedSiblingIndex);
        }
    }
}
