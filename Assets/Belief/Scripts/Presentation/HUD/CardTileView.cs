using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Belief.Data;
using Belief.Presentation;

namespace Belief.Presentation.HUD
{
    /// <summary>손패의 접힘/펼침 상태. Collapsed는 카테고리+이름만 보이는 기본 상태, Expanded는
    /// 전체 정보가 펼쳐진 상태(동시에 한 장만 가능), Using은 전달 확정 처리 중(입력 잠금),
    /// Removed는 사용 완료되어 손패에서 빠지는 중이다.</summary>
    public enum CardHandState
    {
        Collapsed,
        Expanded,
        Using,
        Removed
    }

    public class CardTileView : MonoBehaviour
    {
        [SerializeField] Image background;
        [SerializeField] TMP_Text titleText;
        [SerializeField] TMP_Text kindText;
        [SerializeField] Button button;

        [Header("Expand/Collapse (신규 - section 8/9)")]
        [SerializeField] TMP_Text categoryText;
        [SerializeField] GameObject expandedDetailRoot;
        [SerializeField] TMP_Text descriptionText;
        [SerializeField] TMP_Text tagsText;
        [SerializeField] LayoutElement layoutElement;

        // 배치가이드 _ 기본 실측(1920x1080): 카드 폭 약 380px, 높이 약 190px, 카드 사이 간격 약 70px.
        // HorizontalLayoutGroup은 childControlWidth/Height=false라 카드 크기에 관여하지 않으므로
        // (레이아웃 그룹은 간격 정렬만, 크기는 ApplySlot이 RectTransform에 직접 대입) 여기서 폭/높이/
        // 선택 시 상승/확대값을 명시적으로 관리한다.
        const float CollapsedWidth = 380f;
        const float CollapsedHeight = 190f;
        // 배치가이드 _ 기본 실측: 카드 사이 여백이 76px(카드 폭 384 / 시작점 간격 460)이다.
        const float CardSpacing = 76f;
        const float SelectedRaise = 28f;
        const float ExpandedScale = 1.05f;
        const float SelectAnimDuration = 0.18f;

        Coroutine slotRoutine;
        bool slotInitialized;

        /// <summary>손패 N장 중 index번째 카드의 위치/크기/선택 상승을 직접 계산해 적용한다 -
        /// HorizontalLayoutGroup의 단순 균등 정렬에 맡기지 않고 카드별 좌표를 명시적으로 관리한다
        /// (가이드: 선택 카드는 중앙 위로 상승, 전체 폭에 분산 배치). 좌우 재배치(x)는 즉시 반영하고
        /// 선택 상승(y/scale)만 부드럽게 보간한다 - 손패가 늘거나 줄 때 카드가 미끄러지지 않게.</summary>
        public void ApplySlot(int index, int count, bool selected)
        {
            var rt = (RectTransform)transform;
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(CollapsedWidth, CollapsedHeight);

            float totalWidth = count * CollapsedWidth + Mathf.Max(0, count - 1) * CardSpacing;
            float startX = -totalWidth * 0.5f + CollapsedWidth * 0.5f;
            float x = startX + index * (CollapsedWidth + CardSpacing);

            float targetY = selected ? SelectedRaise : 0f;
            float targetScale = selected ? ExpandedScale : 1f;

            if (slotRoutine != null) { StopCoroutine(slotRoutine); slotRoutine = null; }

            // 최초 배치이거나 비활성 상태면 연출 없이 즉시 확정한다(코루틴이 돌 수 없는 상황).
            if (!slotInitialized || !isActiveAndEnabled)
            {
                slotInitialized = true;
                rt.anchoredPosition = new Vector2(x, targetY);
                rt.localScale = Vector3.one * targetScale;
                return;
            }

            slotRoutine = StartCoroutine(AnimateSlot(rt, x, targetY, targetScale));
        }

        IEnumerator AnimateSlot(RectTransform rt, float x, float targetY, float targetScale)
        {
            float startY = rt.anchoredPosition.y;
            float startScale = rt.localScale.x;
            rt.anchoredPosition = new Vector2(x, startY);

            float t = 0f;
            while (t < 1f)
            {
                t += Time.unscaledDeltaTime / SelectAnimDuration;
                float e = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
                rt.anchoredPosition = new Vector2(x, Mathf.Lerp(startY, targetY, e));
                rt.localScale = Vector3.one * Mathf.Lerp(startScale, targetScale, e);
                yield return null;
            }

            rt.anchoredPosition = new Vector2(x, targetY);
            rt.localScale = Vector3.one * targetScale;
            slotRoutine = null;
        }

        static readonly Color NormalColor = new Color(0.10f, 0.15f, 0.13f);
        static readonly Color SelectedColor = new Color(0.30f, 0.85f, 0.55f);
        static readonly Color HighlightColor = new Color(0.95f, 0.85f, 0.30f);
        const float AppearDuration = 0.2f;
        const float HighlightDuration = 0.3f;

        public InformationCardData BoundCard { get; private set; }
        public CardHandState HandState { get; private set; } = CardHandState.Collapsed;
        public event Action<InformationCardData> Clicked;

        CanvasGroup canvasGroup;
        Coroutine appearRoutine;
        IPlayback appearPlayback;
        bool appearSkipRequested;

        Coroutine highlightRoutine;
        IPlayback highlightPlayback;
        bool highlightSkipRequested;
        bool isSelected;

        void Awake()
        {
            canvasGroup = GetComponent<CanvasGroup>();
            if (canvasGroup == null) canvasGroup = gameObject.AddComponent<CanvasGroup>();
            if (button != null) button.onClick.AddListener(() => Clicked?.Invoke(BoundCard));
        }

        public void Bind(InformationCardData card, bool selected)
        {
            BoundCard = card;
            isSelected = selected;
            string kind = card.cardType == InfoCardType.Spread ? "SPREAD" : "DELIVER";
            string categoryId = card.information != null ? card.information.categoryId : "?";
            string sourceName = card.source != null ? card.source.displayName : "?";

            if (titleText != null) titleText.text = card.information != null ? card.information.title : "?";
            // 카드 아트의 슬롯 폭에 맞춘다 - 좁은 CODE 칸에는 카테고리만, 넓은 타입/타겟 칸에
            // 종류와 출처를 함께 넣는다. 둘을 CODE 칸에 몰면 카드 오른쪽 경계를 넘어간다.
            if (categoryText != null) categoryText.text = categoryId;
            if (kindText != null) kindText.text = $"{kind} / {sourceName}";
            if (descriptionText != null) descriptionText.text = card.information != null ? card.information.description : "";
            if (tagsText != null)
            {
                var tags = card.information != null ? card.information.tags : null;
                tagsText.text = tags != null && tags.Length > 0 ? $"태그: {string.Join(", ", tags)}" : "";
            }

            // 카드 프레임 아트(정보카드 UI)가 물려 있으면 아트를 그대로 살린다 - 배치가이드의 손패도
            // 선택 카드에 색을 입히지 않고 위로 올려서(ApplySlot의 SelectedRaise) 구분한다.
            // placeholder 단색 배경일 때만 기존처럼 색으로 선택 상태를 표시한다.
            bool hasArt = background != null && background.sprite != null;
            if (background != null)
                background.color = hasArt
                    ? Color.white
                    : (selected ? SelectedColor : NormalColor);

            // 아트가 있으면 목업에서 확정한 색을 쓴다(제목은 헤더 위 밝은 글자, 타입/타겟은 어두운 글자).
            if (titleText != null)
                titleText.color = hasArt ? new Color(0.96f, 0.95f, 0.93f) : (selected ? Color.black : Color.white);
            if (kindText != null)
                kindText.color = hasArt ? new Color(0.16f, 0.13f, 0.11f) : (selected ? Color.black : new Color(0.72f, 0.78f, 0.74f));
        }

        /// <summary>접힘/펼침/사용중/제거중 상태를 전환한다. 펼침 상태는 손패 전체에서 한 장만
        /// 유지되어야 하므로(동시에 여러 장 펼침 불가) 이 호출 자체는 강제하지 않고, 호출부
        /// (InformationCardHand를 조율하는 HudPresenter)가 다른 카드를 먼저 접은 뒤 호출해야 한다.</summary>
        public void SetHandState(CardHandState state)
        {
            HandState = state;

            // ExpandedDetail(설명/태그)은 카드 아트 안에 그대로 얹혀 있으므로 항상 켜둔다 - 배치가이드의
            // 손패도 카드 위에 설명/태그가 인쇄된 형태다. 접힌 카드는 화면 아래로 잘려 헤더만 보이고,
            // 선택해서 위로 올라올 때 자연스럽게 전체 내용이 드러난다(ApplySlot의 SelectedRaise).
            if (expandedDetailRoot != null && !expandedDetailRoot.activeSelf)
                expandedDetailRoot.SetActive(true);
            // 크기/위치/상승은 이제 ApplySlot이 RectTransform에 직접 대입한다(레이아웃 그룹이
            // childControlWidth/Height=false라 LayoutElement.preferred*는 어차피 무시됐다).

            if (button != null) button.interactable = state != CardHandState.Using && state != CardHandState.Removed;
        }

        public void PlayAppear()
        {
            CancelRunningRoutine();
            appearSkipRequested = false;
            appearRoutine = StartCoroutine(AppearRoutine());
        }

        /// <summary>더 이상 보유하지 않게 된 카드가 자리에서 서서히 사라지는 연출. onComplete는
        /// 연출이 끝난(또는 스킵된) 직후 정확히 한 번 호출된다 - 호출부가 그 안에서 Destroy한다.</summary>
        public void PlayDisappear(Action onComplete)
        {
            CancelRunningRoutine();
            appearSkipRequested = false;
            appearRoutine = StartCoroutine(DisappearRoutine(onComplete));
        }

        void CancelRunningRoutine()
        {
            // 등장/퇴장 연출도 localScale을 건드리므로 선택 상승 보간과 겹치면 서로 값을 덮어쓴다.
            if (slotRoutine != null) { StopCoroutine(slotRoutine); slotRoutine = null; }

            if (appearRoutine == null) return;
            StopCoroutine(appearRoutine);
            if (appearPlayback != null) PlaybackDirector.Instance?.Unregister(appearPlayback);
        }

        /// <summary>LocationSiteView/NpcActorView의 Highlight()와 같은 일회성 플래시 - 튜토리얼이
        /// "지금 이 카드를 눌러 보세요"를 강조할 때 반복 호출해 은은하게 깜빡이는 효과를 낸다.</summary>
        public void Highlight()
        {
            if (highlightRoutine != null)
            {
                StopCoroutine(highlightRoutine);
                if (highlightPlayback != null) PlaybackDirector.Instance?.Unregister(highlightPlayback);
            }
            highlightSkipRequested = false;
            highlightRoutine = StartCoroutine(HighlightRoutine());
        }

        IEnumerator HighlightRoutine()
        {
            highlightPlayback = new DelegatePlayback(() => highlightSkipRequested = true);
            PlaybackDirector.Instance?.Register(highlightPlayback);

            if (background != null) background.color = HighlightColor;

            float t = 0f;
            while (t < HighlightDuration && !highlightSkipRequested)
            {
                t += Time.deltaTime;
                yield return null;
            }

            if (background != null)
            {
                bool hasArt = background.sprite != null;
                background.color = hasArt
                    ? (isSelected ? new Color(1f, 1f, 0.75f, 1f) : Color.white)
                    : (isSelected ? SelectedColor : NormalColor);
            }

            PlaybackDirector.Instance?.Unregister(highlightPlayback);
            highlightPlayback = null;
            highlightRoutine = null;
        }

        IEnumerator AppearRoutine()
        {
            appearPlayback = new DelegatePlayback(() => appearSkipRequested = true);
            PlaybackDirector.Instance?.Register(appearPlayback);

            canvasGroup.alpha = 0f;
            transform.localScale = Vector3.one * 0.7f;

            float t = 0f;
            while (t < 1f && !appearSkipRequested)
            {
                t += Time.deltaTime / AppearDuration;
                float e = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
                canvasGroup.alpha = e;
                transform.localScale = Vector3.one * Mathf.Lerp(0.7f, 1f, e);
                yield return null;
            }

            canvasGroup.alpha = 1f;
            transform.localScale = Vector3.one;

            PlaybackDirector.Instance?.Unregister(appearPlayback);
            appearPlayback = null;
            appearRoutine = null;
        }

        IEnumerator DisappearRoutine(Action onComplete)
        {
            appearPlayback = new DelegatePlayback(() => appearSkipRequested = true);
            PlaybackDirector.Instance?.Register(appearPlayback);

            float startAlpha = canvasGroup.alpha;
            Vector3 startScale = transform.localScale;

            float t = 0f;
            while (t < 1f && !appearSkipRequested)
            {
                t += Time.deltaTime / AppearDuration;
                float e = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
                canvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, e);
                transform.localScale = Vector3.Lerp(startScale, Vector3.one * 0.7f, e);
                yield return null;
            }

            canvasGroup.alpha = 0f;

            PlaybackDirector.Instance?.Unregister(appearPlayback);
            appearPlayback = null;
            appearRoutine = null;
            onComplete?.Invoke();
        }
    }
}
