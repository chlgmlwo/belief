using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using Belief.Data;
using Belief.Domain;

namespace Belief.Presentation.World
{
    public class LocationSiteView : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        // background는 이제 카드 전체가 아니라 사진 프레임(frame) 안쪽에 들어가는 "사진" 자리다 -
        // 실제 건물 사진 자산이 없으므로 중립 placeholder 색만 채우고, 상태별 색 전환은 그대로
        // 이 필드 하나만 건드린다(코드 변경 없이 재사용).
        [SerializeField] SpriteRenderer background;
        [SerializeField] TextMesh label;

        [SerializeField] SpriteRenderer frame;
        [SerializeField] SpriteRenderer nameTag;
        [SerializeField] SpriteRenderer pin;

        /// <summary>정보 전달(접선) 지점에만 붙는 태그 - 평소엔 꺼져 있고 WorldPresenter가 전달
        /// 지점으로 만든 카드에서만 켠다. 이 태그가 붙은 카드를 클릭하면 전달 확정이 된다.
        /// 문구("접선")는 아트(접선 UI.png)에 이미 인쇄돼 있으므로 별도 텍스트를 얹지 않는다.</summary>
        [Header("Contact Tag (정보 전달 지점에만 표시)")]
        [SerializeField] SpriteRenderer contactTag;

        public event Action<LocationData> Clicked;
        /// <summary>장소 정보 패널(LocationInfoPaper)을 여닫는 용도 - 커서가 이 장소 카드 위에
        /// 들어오면/나가면 발생한다(사용자 지시로 클릭 대신 호버 트리거로 변경).</summary>
        public event Action<LocationData> HoverEnter;
        public event Action<LocationData> HoverExit;
        /// <summary>연결선(WorldPresenter.DrawLocationConnections)이 카드 중심이 아니라 압정 위치에
        /// 붙도록 노출한다.</summary>
        public Transform PinTransform => pin != null ? pin.transform : transform;

        // OnMouseDown은 legacy Input Manager 기반이라 Active Input Handling이
        // "Input System Package (New)"로 설정된 이 프로젝트에서는 호출되지 않는다.
        // EventSystem + Physics2DRaycaster(WorldPresenter가 보장)를 통해 클릭을 받는다.
        public void OnPointerClick(PointerEventData eventData) => Clicked?.Invoke(BoundData);
        public void OnPointerEnter(PointerEventData eventData) => HoverEnter?.Invoke(BoundData);
        public void OnPointerExit(PointerEventData eventData) => HoverExit?.Invoke(BoundData);

        // NormalColor는 실제 사진 자산이 들어오기 전 placeholder 단색 시절 값이었다 - 이제 진짜
        // 사진이 있으므로 흰색(원본 색 그대로)으로 되돌린다. Alert/Locked/Highlight/Selection은
        // 상태 강조용 의도된 틴트라 그대로 유지한다.
        static readonly Color NormalColor = Color.white;
        static readonly Color AlertColor = new Color(0.80f, 0.62f, 0.38f);
        static readonly Color LockedColor = new Color(0.70f, 0.40f, 0.36f);
        static readonly Color HighlightColor = new Color(0.45f, 0.85f, 0.62f);
        static readonly Color SelectionColor = new Color(0.98f, 0.80f, 0.35f);

        const float HighlightDuration = 0.3f;
        const float SelectionTweenDuration = 0.18f;

        // 폰트 크기는 장소마다 달라지면 안 되고 전부 동일해야 한다(사용자 지시) - 그래서 "이 장소의
        // 이름"이 아니라 "게임 전체에서 가장 긴 장소 이름 하나"를 기준으로 "이 정도 크기면 가장 긴
        // 이름도 리본 밖으로 안 나간다"는 스케일을 딱 한 번만 계산해서 모든 장소에 똑같이 적용한다.
        // ⚠️ 이 이름보다 더 긴 장소 이름이 나중에 추가되면 이 상수도 같이 갱신해야 한다(2026-08-04
        // 기준 4개 스테이지 전체 실측 결과 최장 8자, Stage_04 "알현실 앞 광장").
        const string WorstCaseReferenceName = "알현실 앞 광장";
        // 리본 폭의 몇 %까지 최장 이름이 채우게 할지 - 100%면 리본 끝에 딱 붙어 답답해 보여서 여백을 남긴다.
        const float NameTagFitWidthRatio = 0.85f;
        const float LabelMinScaleMultiplier = 0.5f;
        const float LabelMaxScaleMultiplier = 2.5f;

        // 모든 LocationSiteView 인스턴스가 공유하는 값 - 최초 1회만 계산하고 이후로는 재사용해
        // 어떤 장소든 완전히 동일한 폰트 크기를 쓰게 보장한다.
        static float? cachedUniformFitScaleMultiplier;

        public LocationData BoundData { get; private set; }

        LocationSiteState currentState = LocationSiteState.Normal;
        Coroutine highlightRoutine;
        IPlayback highlightPlayback;
        bool highlightSkipRequested;

        Coroutine selectionRoutine;
        IPlayback selectionPlayback;
        bool selectionSkipRequested;
        bool selected;

        Vector3 labelBaseLocalScale;
        bool labelBaseScaleCaptured;

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

            FitLabelToNameTag(skin);
        }

        // ------------------------------------------------------------ 정보 전달(접선) 태그

        /// <summary>전달 가능할 때의 태그 색과, 아직 대상이 정해지지 않아 눌러도 소용없을 때의 흐린 색.
        /// 태그를 아예 껐다 켜면 "전달 지점이 사라졌다"처럼 보이므로 항상 두되 진하기만 바꾼다.</summary>
        static readonly Color ContactReadyColor = Color.white;
        static readonly Color ContactIdleColor = new Color(1f, 1f, 1f, 0.75f);
        static readonly Color ContactFlashColor = new Color(1f, 0.92f, 0.55f);
        const float ContactFlashDuration = 0.3f;

        public bool IsContactPoint { get; private set; }

        Coroutine contactFlashRoutine;

        public void BindContactTag(Sprite sprite)
        {
            IsContactPoint = true;
            if (contactTag != null)
            {
                contactTag.gameObject.SetActive(true);
                if (sprite != null) contactTag.sprite = sprite;
            }
            SetContactReady(false);
        }

        public void SetContactReady(bool ready)
        {
            if (!IsContactPoint || contactTag == null) return;
            contactTag.color = ready ? ContactReadyColor : ContactIdleColor;
        }

        /// <summary>튜토리얼이 "여기를 누르면 전달된다"를 알리려고 태그를 한 번 번쩍이게 한다 -
        /// 예전엔 하단 패널의 전달 버튼(Image)을 깜빡였는데, 버튼이 이 태그로 옮겨왔다.</summary>
        public void FlashContactTag()
        {
            if (!IsContactPoint || contactTag == null) return;
            if (contactFlashRoutine != null) StopCoroutine(contactFlashRoutine);
            contactFlashRoutine = StartCoroutine(ContactFlashRoutine());
        }

        IEnumerator ContactFlashRoutine()
        {
            Color baseColor = contactTag.color;
            var flash = new Color(ContactFlashColor.r, ContactFlashColor.g, ContactFlashColor.b, baseColor.a);
            float t = 0f;
            while (t < ContactFlashDuration)
            {
                t += Time.deltaTime;
                contactTag.color = Color.Lerp(flash, baseColor, t / ContactFlashDuration);
                yield return null;
            }
            contactTag.color = baseColor;
            contactFlashRoutine = null;
        }

        /// <summary>모든 장소가 이름 길이와 무관하게 완전히 동일한 폰트 크기를 쓰도록, 실제 이 장소의
        /// 이름이 아니라 게임 전체 최장 이름(WorstCaseReferenceName) 기준으로 계산한 단일 스케일을
        /// 정적 캐시에서 가져와(없으면 최초 1회 계산) 그대로 적용한다.</summary>
        void FitLabelToNameTag(PlayHudSkin skin)
        {
            if (label == null || nameTag == null) return;
            if (!labelBaseScaleCaptured)
            {
                labelBaseLocalScale = label.transform.localScale;
                labelBaseScaleCaptured = true;
            }
            label.transform.localScale = labelBaseLocalScale;

            if (!cachedUniformFitScaleMultiplier.HasValue)
            {
                float? computed = ComputeUniformFitScaleMultiplier(skin);
                if (computed.HasValue) cachedUniformFitScaleMultiplier = computed;
            }

            label.transform.localScale = labelBaseLocalScale * (cachedUniformFitScaleMultiplier ?? 1f);
        }

        /// <summary>label.transform.localScale이 labelBaseLocalScale인 상태에서 호출되어야 한다 -
        /// 최장 이름 문자열을 임시로 렌더링해 그 폭을 측정한 뒤 원래 텍스트로 되돌린다(계산 목적으로만
        /// 잠깐 바꿔치기, 화면엔 노출 안 됨 - 매 프레임이 아니라 이 계산이 끝나는 한 프레임 내에서
        /// 즉시 원복되므로 깜빡임 없음).</summary>
        float? ComputeUniformFitScaleMultiplier(PlayHudSkin skin)
        {
            if (skin == null || skin.locationTag5 == null || label == null) return null;

            string originalText = label.text;
            label.text = WorstCaseReferenceName;
            var meshRenderer = label.GetComponent<MeshRenderer>();
            float worstCaseWidth = meshRenderer != null ? meshRenderer.bounds.size.x : 0f;
            label.text = originalText;

            if (worstCaseWidth <= 0.0001f) return null;

            float tag5WorldWidth = skin.locationTag5.bounds.size.x * nameTag.transform.lossyScale.x;
            float targetWidth = tag5WorldWidth * NameTagFitWidthRatio;
            return Mathf.Clamp(targetWidth / worstCaseWidth, LabelMinScaleMultiplier, LabelMaxScaleMultiplier);
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
