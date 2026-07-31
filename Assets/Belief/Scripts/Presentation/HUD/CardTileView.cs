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

        const float CollapsedHeight = 92f;

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
            if (categoryText != null) categoryText.text = $"{categoryId} · {kind}";
            if (kindText != null) kindText.text = $"출처: {sourceName}";
            if (descriptionText != null) descriptionText.text = card.information != null ? card.information.description : "";
            if (tagsText != null)
            {
                var tags = card.information != null ? card.information.tags : null;
                tagsText.text = tags != null && tags.Length > 0 ? $"태그: {string.Join(", ", tags)}" : "";
            }

            // 배경에 카드 프레임 아트(정보카드 UI)가 물려 있으면 원래 색을 짙게 덮어씌우지 않는다 -
            // 대신 옅은 강조색만 얹어 선택 상태를 표시한다(placeholder 단색 배경일 때만 기존처럼
            // 완전히 채운 색으로 표시).
            bool hasArt = background != null && background.sprite != null;
            if (background != null)
                background.color = hasArt
                    ? (selected ? new Color(1f, 1f, 0.75f, 1f) : Color.white)
                    : (selected ? SelectedColor : NormalColor);

            // 선택 시 밝은 배경 위에 흰 글자는 대비가 낮아진다 - 검은 글자로 바꿔 가독성을 유지한다.
            var textColor = hasArt ? Color.black : (selected ? Color.black : Color.white);
            if (titleText != null) titleText.color = textColor;
            if (kindText != null) kindText.color = hasArt ? new Color(0.15f, 0.12f, 0.08f) : (selected ? Color.black : new Color(0.72f, 0.78f, 0.74f));
        }

        /// <summary>접힘/펼침/사용중/제거중 상태를 전환한다. 펼침 상태는 손패 전체에서 한 장만
        /// 유지되어야 하므로(동시에 여러 장 펼침 불가) 이 호출 자체는 강제하지 않고, 호출부
        /// (InformationCardHand를 조율하는 HudPresenter)가 다른 카드를 먼저 접은 뒤 호출해야 한다.</summary>
        public void SetHandState(CardHandState state)
        {
            HandState = state;
            bool expanded = state == CardHandState.Expanded;

            // ExpandedDetail(설명/태그 미리보기)은 항상 꺼둔다 - 아래 CardInfo 패널이 선택된 카드의
            // 제목/설명/출처를 이미 그대로 보여주고 있어 완전히 중복인데, 손패 줄 바로 위로 펼쳐지며
            // 월드 뷰(장소/NPC)를 가리는 문제가 있었다. 배경색(Bind의 SelectedColor)만으로 선택 상태를
            // 충분히 구분한다.
            if (expandedDetailRoot != null) expandedDetailRoot.SetActive(false);
            if (layoutElement != null) layoutElement.preferredHeight = CollapsedHeight;

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
