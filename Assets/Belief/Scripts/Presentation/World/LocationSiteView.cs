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

        public void OnPointerEnter(PointerEventData eventData)
        {
            // 대상이 될 수 없을 때는 확대 반응을 하지 않는다 - 다만 장소 정보 패널은 그대로 뜬다.
            // 확대는 "여기에 낼 수 있다"는 뜻이고 정보 패널은 그냥 조사이므로 서로 다른 이야기다.
            pointerInside = true;
            if (targetable) SetHovered(true);
            HoverEnter?.Invoke(BoundData);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            pointerInside = false;
            SetHovered(false);
            HoverExit?.Invoke(BoundData);
        }

        /// <summary>지금 고른 카드로 이 장소를 대상으로 삼을 수 있는지 - WorldPresenter가 카드 선택이
        /// 바뀔 때마다 정해 준다. 사람 대상 카드를 든 채로 장소에 커서를 올려도 카드가 반응하지 않아,
        /// "여긴 아니다"라는 경고 문구를 따로 띄울 필요가 없어진다.
        ///
        /// 커서를 올려 둔 채로 손패에서 카드를 바꾸는 일이 흔하므로, 막힐 때 확대를 되돌리는 것뿐
        /// 아니라 풀릴 때 그 자리에서 확대를 켜 주기까지 한다(안 그러면 한 번 나갔다 들어와야 반응한다).</summary>
        public void SetTargetable(bool value)
        {
            if (targetable == value) return;
            targetable = value;
            SetHovered(targetable && pointerInside);
        }

        bool targetable = true;
        bool pointerInside;

        /// <summary>커서 밑에 있는 채로 비활성화되면 OnPointerExit가 오지 않아 확대된 채로 굳는다 -
        /// 다시 켜질 때 원래 크기로 보이도록 정리한다.</summary>
        void OnDisable()
        {
            pointerInside = false;

            // 선택 강조가 켜진 채로 꺼지면 색을 되돌릴 코루틴이 함께 죽어 카드가 물든 채로 굳는다 -
            // 다시 켜져도 selected가 false라 되돌리는 트윈이 아예 안 돌아 영영 남는다. 여기서 끊는다.
            if (selectionRoutine != null)
            {
                StopCoroutine(selectionRoutine);
                if (selectionPlayback != null) PlaybackDirector.Instance?.Unregister(selectionPlayback);
                selectionRoutine = null;
                selectionPlayback = null;
            }
            selected = false;
            ApplyBaseColor();

            if (!hovered) return;
            hovered = false;
            if (hoverRoutine != null) { StopCoroutine(hoverRoutine); hoverRoutine = null; }
            transform.localScale = baseScale;
        }

        // NormalColor는 실제 사진 자산이 들어오기 전 placeholder 단색 시절 값이었다 - 이제 진짜
        // 사진이 있으므로 흰색(원본 색 그대로)으로 되돌린다.
        //
        // Alert/Locked 틴트는 BaseColorForState에서 쓰지 않는다(이유는 그쪽 주석 참고). 값은
        // 남겨 둔다 - 경계 상태를 다시 표시하기로 하면 색을 새로 고르는 대신 이 값이 기준이 된다.
        static readonly Color NormalColor = Color.white;
        static readonly Color AlertColor = new Color(0.80f, 0.62f, 0.38f);
        static readonly Color LockedColor = new Color(0.70f, 0.40f, 0.36f);
        static readonly Color HighlightColor = new Color(0.45f, 0.85f, 0.62f);
        static readonly Color SelectionColor = new Color(0.98f, 0.80f, 0.35f);

        const float HighlightDuration = 0.3f;
        const float SelectionTweenDuration = 0.18f;

        /// <summary>커서를 따라다니는 반응이라 선택 연출보다 짧아야 손이 끌리는 느낌이 안 난다.</summary>
        const float HoverTweenDuration = 0.14f;
        const float HoverScaleMultiplier = 1.08f;

        bool hovered;
        Coroutine hoverRoutine;
        /// <summary>WorldPresenter가 Bind 직전에 정해 주는 카드 기본 크기 - 호버 확대는 여기에
        /// 곱했다가 되돌린다.</summary>
        Vector3 baseScale = Vector3.one;

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
            baseScale = transform.localScale;
            SetSiteState(LocationSiteState.Normal);

            // 사진이 없으면 사진 자리를 아예 그리지 않는다. 예전에는 여기서 조용히 넘어갔는데,
            // 그러면 프리팹 기본값인 PlaceholderSquare가 남고 SetSiteState(Normal)이 그걸 흰색
            // (NormalColor)으로 칠해서 지도 위에 "빈 흰 카드"가 떴다(Stage_04의 저택가 - 아직 아트가
            // 없는 유일한 장소인데 M01 클리어 조건 지점이라 뺄 수도 없다). 압정과 이름표는 남겨서
            // "사진 없는 지도 표식"으로 읽히게 하고, 클릭 판정은 루트 콜라이더라 그대로 동작한다.
            if (background != null)
            {
                bool hasPhoto = data.locationPhoto != null;
                background.enabled = hasPhoto;
                if (hasPhoto) background.sprite = data.locationPhoto;
            }

            FitColliderToPhoto();

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

        /// <summary>접선 지점을 <b>"진행 완료" 버튼 하나</b>로만 표시한다 - 장소 사진/프레임/압정/이름표를
        /// 전부 끄고 태그만 남긴다. 여기는 게임 세계의 장소가 아니라 "턴을 확정한다"는 시스템 동작이
        /// 놓인 자리라, 다른 장소들과 똑같은 지도 표식으로 보이면 오히려 헷갈린다(사용자 지시).
        ///
        /// 크기는 <b>화면 픽셀 기준</b>으로 맞춘다. 카메라 orthographicSize가 스테이지마다 5~14로
        /// 달라서 월드 스케일을 고정하면 Zone1과 Metropolis에서 버튼 크기가 2.8배까지 차이 난다.
        /// 클릭 판정도 태그로 옮긴다 - 루트 콜라이더(카드 한 장 크기)를 남겨 두면 버튼 바깥의
        /// 빈 지도를 눌러도 턴이 확정돼 버린다.</summary>
        public void ShowAsActionButtonOnly(Sprite sprite, float targetScreenWidthPx, Camera cam)
        {
            IsContactPoint = true;

            if (background != null) background.enabled = false;
            if (frame != null) frame.enabled = false;
            if (nameTag != null) nameTag.enabled = false;
            if (pin != null) pin.enabled = false;
            if (label != null) label.gameObject.SetActive(false);

            // 카드 전체를 덮던 루트 콜라이더는 끈다(아래에서 태그 콜라이더만 남긴다).
            var rootCollider = GetComponent<Collider2D>();
            if (rootCollider != null) rootCollider.enabled = false;

            if (contactTag == null) return;
            contactTag.gameObject.SetActive(true);
            if (sprite != null) contactTag.sprite = sprite;
            contactTag.transform.localPosition = Vector3.zero;   // 카드 모서리가 아니라 지정된 자리 그대로

            var s = contactTag.sprite;
            if (s != null && cam != null && cam.orthographic && Screen.height > 0)
            {
                float spriteWorldWidth = s.rect.width / s.pixelsPerUnit;
                float pxPerWorldUnit = Screen.height / (2f * cam.orthographicSize);
                float wantWorldWidth = targetScreenWidthPx / pxPerWorldUnit;
                // 부모(카드 루트)에 이미 걸려 있는 스케일을 상쇄해 최종 화면 크기를 맞춘다.
                float parentScale = transform.lossyScale.x != 0f ? transform.lossyScale.x : 1f;
                float scale = wantWorldWidth / spriteWorldWidth / parentScale;
                contactTag.transform.localScale = new Vector3(scale, scale, 1f);
            }

            // 콜라이더를 스프라이트 크기에 맞춘다 - 스케일은 Transform이 처리하므로 원본 크기 그대로.
            var tagCollider = contactTag.GetComponent<BoxCollider2D>();
            if (tagCollider != null && s != null)
            {
                tagCollider.enabled = true;
                tagCollider.offset = Vector2.zero;
                tagCollider.size = new Vector2(s.rect.width / s.pixelsPerUnit, s.rect.height / s.pixelsPerUnit);
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

        /// <summary>프리팹의 BoxCollider2D는 placeholder 시절의 1x1 고정인데, 실제 장소 사진은
        /// 세로로 길다(여관 0.90x1.45). 그래서 판정면이 카드 위아래 1/3을 덮지 못하고 좌우로는
        /// 오히려 삐져나가 있었다 - 클릭은 대충 가운데를 누르니 티가 안 났지만, 호버는 "카드 위에
        /// 올렸는데 반응이 없다"가 그대로 드러난다.
        ///
        /// 사진이 없는 장소(Stage_04 저택가)는 압정과 이름표만 남는 표식이라, 사진에 맞추면 판정면이
        /// 사라진다 - 그 경우엔 프리팹의 1x1을 그대로 둔다.</summary>
        void FitColliderToPhoto()
        {
            var box = GetComponent<BoxCollider2D>();
            if (box == null || background == null || !background.enabled || background.sprite == null) return;

            var scale = background.transform.localScale;
            var size = background.sprite.bounds.size;
            box.size = new Vector2(size.x * scale.x, size.y * scale.y);
            box.offset = (Vector2)background.transform.localPosition
                         + new Vector2(background.sprite.bounds.center.x * scale.x, background.sprite.bounds.center.y * scale.y);
        }

        /// <summary>커서가 이 장소 카드 위에 올라왔는지 - 카드 전체를 부드럽게 확대한다.
        /// 색은 건드리지 않는다(선택/경보/잠금 틴트가 이미 background.color를 쓰고 있어, 여기서
        /// 같이 쓰면 서로 덮어써 선택 표시가 풀린다).</summary>
        void SetHovered(bool value)
        {
            if (hovered == value) return;
            hovered = value;

            if (hoverRoutine != null) StopCoroutine(hoverRoutine);
            if (!isActiveAndEnabled)
            {
                transform.localScale = value ? baseScale * HoverScaleMultiplier : baseScale;
                return;
            }
            hoverRoutine = StartCoroutine(HoverScaleRoutine());
        }

        /// <summary>PlaybackDirector에 등록하지 않는다 - 등록하면 "재생 중"으로 잡혀 입력이 잠기고,
        /// 커서가 지도 위를 지나갈 때마다 손패 클릭이 씹힌다.</summary>
        IEnumerator HoverScaleRoutine()
        {
            // 2D라 z는 그대로 둔다.
            float mul = hovered ? HoverScaleMultiplier : 1f;
            Vector3 from = transform.localScale;
            var to = new Vector3(baseScale.x * mul, baseScale.y * mul, baseScale.z);

            float t = 0f;
            while (t < HoverTweenDuration)
            {
                t += Time.deltaTime;
                transform.localScale = Vector3.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / HoverTweenDuration));
                yield return null;
            }
            transform.localScale = to;
            hoverRoutine = null;
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

        /// <summary>지금은 어떤 상태에서도 사진을 원래 색으로 둔다.
        ///
        /// 예전엔 Alert를 주황, Locked를 붉은색으로 카드 전체에 칠했다. 그런데 <b>카드 전체를
        /// 물들이는 건 호버·선택 강조와 똑같은 표현</b>이라, 경비 초소가 경계 상태로 바뀌면
        /// "커서를 올리지도 않았는데 하이라이팅이 켜진 채 안 꺼진다"로 읽혔다(사용자 리포트 2회).
        ///
        /// 실제로 안 꺼지는 게 맞다 - 상태를 Normal로 되돌리는 효과가 하나도 없어서, 한 번 경계로
        /// 바뀐 장소는 그 판이 끝날 때까지 물들어 있었다. 게다가 그 색이 무슨 뜻인지 알려 주는 곳도
        /// 없었다.
        ///
        /// 상태 자체는 그대로 살아 있다 - 믿음 계산(SituationEvaluator)과 미션 조건
        /// (LocationStateCondition)이 계속 읽는다. 없앤 것은 표시뿐이다. 경계 상태를 다시 보여 주려면
        /// 카드 색이 아니라 별도 표식(예: 이름표 옆 작은 아이콘)으로 해야 강조와 헷갈리지 않는다.</summary>
        Color BaseColorForState() => NormalColor;
    }
}
