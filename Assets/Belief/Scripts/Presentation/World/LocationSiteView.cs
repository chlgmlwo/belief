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
        // background는 이제 카드 전체가 아니라 사진 프레임(frame) 안쪽에 들어가는 "사진" 자리다 -
        // 실제 건물 사진 자산이 없으므로 중립 placeholder 색만 채우고, 상태별 색 전환은 그대로
        // 이 필드 하나만 건드린다(코드 변경 없이 재사용).
        [SerializeField] SpriteRenderer background;
        [SerializeField] TextMesh label;

        [SerializeField] SpriteRenderer frame;
        [SerializeField] SpriteRenderer nameTag;
        [SerializeField] SpriteRenderer pin;

        public event Action<LocationData> Clicked;
        /// <summary>연결선(WorldPresenter.DrawLocationConnections)이 카드 중심이 아니라 압정 위치에
        /// 붙도록 노출한다.</summary>
        public Transform PinTransform => pin != null ? pin.transform : transform;

        // OnMouseDown은 legacy Input Manager 기반이라 Active Input Handling이
        // "Input System Package (New)"로 설정된 이 프로젝트에서는 호출되지 않는다.
        // EventSystem + Physics2DRaycaster(WorldPresenter가 보장)를 통해 클릭을 받는다.
        public void OnPointerClick(PointerEventData eventData) => Clicked?.Invoke(BoundData);

        // 중립 parchment 톤 기준 - 검은/어두운 박스로 보이지 않도록 전부 밝은 톤으로 낮췄다.
        static readonly Color NormalColor = new Color(0.66f, 0.63f, 0.58f);
        static readonly Color AlertColor = new Color(0.80f, 0.62f, 0.38f);
        static readonly Color LockedColor = new Color(0.70f, 0.40f, 0.36f);
        static readonly Color HighlightColor = new Color(0.45f, 0.85f, 0.62f);
        static readonly Color SelectionColor = new Color(0.98f, 0.80f, 0.35f);

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
        /// 호출자 책임이며 이 뷰는 좌표의 출처를 모른다(순수 표시). skin이 null이거나 필드가 비어
        /// 있으면 프레임/태그/핑을 조용히 생략하고 기존 placeholder 표시만 남긴다(하위 호환).</summary>
        public void Bind(LocationData data, Vector2 position, PlayHudSkin skin)
        {
            BoundData = data;
            if (label != null) label.text = data.displayName;
            transform.position = new Vector3(position.x, position.y, transform.position.z);
            SetSiteState(LocationSiteState.Normal);

            if (background != null && data.locationPhoto != null) background.sprite = data.locationPhoto;

            if (frame != null && skin != null) frame.sprite = skin.locationImageFrame;
            if (pin != null && skin != null) pin.sprite = skin.pin;
            if (nameTag != null && skin != null)
                nameTag.sprite = data.displayName != null && data.displayName.Length <= 3
                    ? skin.locationTag3
                    : skin.locationTag5;
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
