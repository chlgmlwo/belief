using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using Belief.Data;
using Belief.Domain;

namespace Belief.Presentation.World
{
    public class LocationSiteView : MonoBehaviour, IPointerClickHandler
    {
        [SerializeField] SpriteRenderer background;
        [SerializeField] TextMesh label;

        public event Action<LocationData> Clicked;

        // OnMouseDown은 legacy Input Manager 기반이라 Active Input Handling이
        // "Input System Package (New)"로 설정된 이 프로젝트에서는 호출되지 않는다.
        // EventSystem + Physics2DRaycaster(WorldPresenter가 보장)를 통해 클릭을 받는다.
        public void OnPointerClick(PointerEventData eventData) => Clicked?.Invoke(BoundData);

        static readonly Color NormalColor = new Color(0.10f, 0.15f, 0.13f);
        static readonly Color AlertColor = new Color(0.45f, 0.30f, 0.10f);
        static readonly Color LockedColor = new Color(0.40f, 0.10f, 0.10f);
        static readonly Color HighlightColor = new Color(0.30f, 0.85f, 0.55f);
        static readonly Color SelectionColor = new Color(0.95f, 0.75f, 0.25f);

        const float HighlightDuration = 0.3f;
        const float SelectionTweenDuration = 0.18f;

        public LocationData BoundData { get; private set; }

        LocationSiteState currentState = LocationSiteState.Normal;
        Coroutine highlightRoutine;
        IPlayback highlightPlayback;
        bool highlightSkipRequested;

        Coroutine selectionRoutine;
        IPlayback selectionPlayback;
        bool selectionSkipRequested;
        bool selected;

        /// <summary>position은 항상 호출자(WorldPresenter)가 넘긴다 - 스테이지별 수동 레이아웃
        /// (StageData.locationLayout)이 있으면 그 값, 없으면 LocationData.worldPosition의 해석은
        /// 호출자 책임이며 이 뷰는 좌표의 출처를 모른다(순수 표시).</summary>
        public void Bind(LocationData data, Vector2 position)
        {
            BoundData = data;
            if (label != null) label.text = data.displayName;
            transform.position = new Vector3(position.x, position.y, transform.position.z);
            SetSiteState(LocationSiteState.Normal);
        }

        public void SetSiteState(LocationSiteState state)
        {
            currentState = state;
            if (highlightRoutine == null && selectionRoutine == null && !selected) ApplyBaseColor();
        }

        /// <summary>정보 확산의 일회성 플래시(Highlight)와 달리, 지금 전달 대상으로 선택되어 있는 동안
        /// 계속 유지되는 강조 표시 - TargetingController가 대상 선택/해제 시점에 호출한다.</summary>
        public void SetSelected(bool value)
        {
            if (selected == value) return;
            selected = value;

            if (selectionRoutine != null)
            {
                StopCoroutine(selectionRoutine);
                if (selectionPlayback != null) PlaybackDirector.Instance?.Unregister(selectionPlayback);
            }
            selectionSkipRequested = false;
            selectionRoutine = StartCoroutine(SelectionTweenRoutine(value));
        }

        IEnumerator SelectionTweenRoutine(bool toSelected)
        {
            selectionPlayback = new DelegatePlayback(() => selectionSkipRequested = true);
            PlaybackDirector.Instance?.Register(selectionPlayback);

            Color from = background != null ? background.color : Color.white;
            Color to = toSelected ? SelectionColor : BaseColorForState();

            float t = 0f;
            while (t < SelectionTweenDuration && !selectionSkipRequested)
            {
                t += Time.deltaTime;
                if (background != null) background.color = Color.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / SelectionTweenDuration));
                yield return null;
            }
            if (background != null) background.color = to;

            PlaybackDirector.Instance?.Unregister(selectionPlayback);
            selectionPlayback = null;
            selectionRoutine = null;
        }

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

            ApplyBaseColor();
            PlaybackDirector.Instance?.Unregister(highlightPlayback);
            highlightPlayback = null;
            highlightRoutine = null;
        }

        void ApplyBaseColor()
        {
            if (background == null) return;
            background.color = BaseColorForState();
        }

        Color BaseColorForState() => currentState switch
        {
            LocationSiteState.Alert => AlertColor,
            LocationSiteState.Locked => LockedColor,
            _ => NormalColor
        };
    }
}
