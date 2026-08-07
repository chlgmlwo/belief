using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using Belief.Data;

namespace Belief.Presentation.World
{
    /// <summary>
    /// body(placeholder 단색 사각형)는 실제 인물 사진 자산이 없어 그대로 유지한다 - 대신 그 위에
    /// frame/pin(프레임/압정) 장식만 얹는다(PlayHudSkin이 실제로 채워지면 Bind에서 적용).
    /// 이동/대사는 최소 연출(Coroutine 보간)만 제공하며 게임 상태는 건드리지 않는다 -
    /// 상태는 이미 확정된 뒤 이벤트로 재생만 한다.
    /// </summary>
    public class NpcActorView : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField] SpriteRenderer body;
        [SerializeField] TextMesh label;
        [SerializeField] GameObject dialogueRoot;
        [SerializeField] TextMeshPro dialogueLabel;
        [SerializeField] SpriteRenderer dialogueBackground;
        [SerializeField] SpriteRenderer frame;
        [SerializeField] SpriteRenderer pin;
        [SerializeField] SpriteRenderer nameTag;

        // 예전엔 0.35초라 위치 보간이 거의 순간이동처럼 보였다 - 실제로 "걸어서 이동하는" 느낌을
        // 주려면 걷기 사이클이 최소 한 바퀴 이상 돌 만큼 시간이 필요하다(사용자 지시로 확대,
        // 1.0초도 빠르다는 피드백으로 2.5초까지 재조정).
        const float MoveDuration = 2.5f;
        const float DialogueDuration = 2.5f;
        /// <summary>말풍선 프리팹 수치(글자 크기/폭/머리 위 간격)를 맞춰 둔 기준 카메라 크기 - Zone1 기준.
        /// 다른 줌의 스테이지에서는 FitDialogueToCamera가 이 값과의 비율만큼 되곱해 보정한다.</summary>
        const float DialogueReferenceOrthoSize = 5f;

        /// <summary>말풍선을 화면 가장자리에서 이만큼 띄운다. HUD는 ScreenSpaceOverlay 캔버스라
        /// 정렬 순서로는 월드 오브젝트를 그 위로 올릴 수 없다(Overlay는 order와 무관하게 항상 위에
        /// 그려진다) - 그래서 "앞으로 당기기"가 아니라 "가장자리에 들어가지 않게 막기"로 푼다.</summary>
        const float DialogueScreenMargin = 24f;

        /// <summary>프리팹이 정한 머리 위 위치 - 화면 밖으로 넘칠 때만 여기서 내리고, 다음 대사에서
        /// 다시 이 값으로 되돌린다.</summary>
        Vector3 dialogueBaseLocalPosition;
        const float HighlightDuration = 0.3f;
        const float SelectionTweenDuration = 0.18f;
        /// <summary>커서를 따라다니는 반응이라 선택 연출보다 짧아야 손이 끌리는 느낌이 안 난다.</summary>
        const float HoverTweenDuration = 0.14f;
        const float HoverScaleMultiplier = 1.1f;
        // ⚠️ 실측 결과 36장 전부가 사실상 거의 같은 자세의 미세한 노이즈성 변형이었다(행 1과 행 2를
        // 나란히 비교해도 자세 차이가 거의 없음) - "여러 걷기 사이클을 이어붙인 시트"가 아니라 그냥
        // 비슷한 자세를 반복 생성한 배치였다. 그래서 스프라이트 교체만으로는 결코 매끄러운 보행처럼
        // 안 보인다 - 실제 "부드러움"은 아래 WalkBob(연속 보간)이 담당하고, 스프라이트 교체는 아주
        // 느리게(보조 디테일로만) 돌린다.
        const float WalkFrameFps = 5f;
        const int WalkCycleFrameCount = 6;
        const float IdleBobAmplitude = 0.045f;
        const float IdleBobPeriod = 1.3f;
        // 걷는 동안의 "발걸음" 통통 튐 - 매 프레임(Time.deltaTime) 연속 보간이라 스프라이트 fps와
        // 무관하게 항상 부드럽다. Idle보다 살짝 크고 빠르게 잡아 "가만히 있음"과 구분되게 한다.
        const float WalkBobAmplitude = 0.06f;
        const float WalkBobPeriod = 0.28f;
        static readonly Color HighlightColor = new Color(0.95f, 0.85f, 0.30f);
        static readonly Color SelectionColor = new Color(0.95f, 0.75f, 0.25f);
        // 선택(SelectionColor)보다 확실히 옅다 - 호버는 "지금 이걸 가리키고 있다"는 안내일 뿐이라
        // 이미 대상으로 확정된 NPC보다 강해 보이면 안 된다.
        static readonly Color HoverColor = new Color(1f, 0.93f, 0.72f);
        static readonly Vector3 PinSelectedScaleMul = new Vector3(1.35f, 1.35f, 1f);
        Vector3 pinBaseScale = Vector3.one;
        Vector3 rootBaseScale = Vector3.one;

        public NpcData BoundData { get; private set; }
        public event Action<NpcData> Clicked;

        // OnMouseDown은 legacy Input Manager 기반이라 Active Input Handling이
        // "Input System Package (New)"로 설정된 이 프로젝트에서는 호출되지 않는다.
        // EventSystem + Physics2DRaycaster(WorldPresenter가 보장)를 통해 클릭을 받는다.
        public void OnPointerClick(PointerEventData eventData) => Clicked?.Invoke(BoundData);
        // 대상이 될 수 없을 때는 호버 반응을 하지 않는다 - 클릭으로 여는 조사 파일은 그대로 열린다.
        public void OnPointerEnter(PointerEventData eventData) { pointerInside = true; if (targetable) SetHovered(true); }

        public void OnPointerExit(PointerEventData eventData) { pointerInside = false; SetHovered(false); }

        /// <summary>지금 고른 카드로 이 사람을 대상으로 삼을 수 있는지 - WorldPresenter가 카드 선택이
        /// 바뀔 때마다 정해 준다(<see cref="LocationSiteView.SetTargetable"/>와 같은 규칙).</summary>
        public void SetTargetable(bool value)
        {
            if (targetable == value) return;
            targetable = value;
            SetHovered(targetable && pointerInside);
        }

        bool targetable = true;
        bool pointerInside;

        /// <summary>NPC가 커서 밑에 있는 채로 비활성화되면 OnPointerExit가 오지 않아 확대된 상태로
        /// 굳는다 - 다시 켜질 때 원래 크기로 보이도록 여기서 정리한다. 비활성화는 코루틴도 함께
        /// 끊어버리므로, 등록해 둔 playback을 여기서 풀지 않으면 PlaybackDirector가 영영 "재생 중"으로
        /// 남아 입력이 잠긴다.</summary>
        void OnDisable()
        {
            if (statePlayback != null)
            {
                PlaybackDirector.Instance?.Unregister(statePlayback);
                statePlayback = null;
            }
            stateRoutine = null;
            pointerInside = false;
            hovered = false;
            transform.localScale = rootBaseScale;
            if (body != null) body.color = RestingColor();
        }

        Coroutine moveRoutine;
        IPlayback movePlayback;
        bool moveSkipRequested;

        Coroutine walkCycleRoutine;
        Vector3 bodyBaseLocalScale = Vector3.one;

        Coroutine idleBobRoutine;
        Vector3 bodyBaseLocalPosition = Vector3.zero;

        Coroutine dialogueRoutine;
        IPlayback dialoguePlayback;
        bool dialogueSkipRequested;

        Coroutine highlightRoutine;
        IPlayback highlightPlayback;
        bool highlightSkipRequested;

        Coroutine stateRoutine;
        IPlayback statePlayback;
        bool stateSkipRequested;
        bool selected;
        bool hovered;

        Color baseColor;

        /// <summary>skin이 null이거나 필드가 비어 있으면 프레임/핑/대화창 아트를 조용히 생략하고
        /// 기존 placeholder 표시만 남긴다(하위 호환).</summary>
        public void Bind(NpcData data, PlayHudSkin skin)
        {
            BoundData = data;
            if (label != null) label.text = data.displayName;
            // baseColor는 실제 캐릭터 스프라이트가 들어오기 전 placeholder 단색 시절 값이었다 -
            // 이제 진짜 캐릭터 아트가 있으므로 흰색(원본 색 그대로)으로 되돌린다.
            baseColor = Color.white;
            if (body != null) body.color = baseColor;
            if (body != null)
            {
                bodyBaseLocalScale = body.transform.localScale;
                bodyBaseLocalPosition = body.transform.localPosition;
            }
            if (body != null && data.characterPhoto != null) body.sprite = data.characterPhoto;
            if (dialogueRoot != null)
            {
                dialogueBaseLocalPosition = dialogueRoot.transform.localPosition;
                dialogueRoot.SetActive(false);
            }
            if (pin != null) pinBaseScale = pin.transform.localScale;
            // WorldPresenter가 Bind 직전에 루트 스케일을 정해 주므로 여기서 잡으면 그 값이 기준이 된다 -
            // 호버 확대는 이 기준에 곱했다가 되돌리는 방식이라 캐릭터별 크기 보정을 건드리지 않는다.
            rootBaseScale = transform.localScale;
            FitColliderToBody();

            StartIdleBob();

            if (skin == null) return;
            if (frame != null) frame.sprite = skin.npcPhotoFrame;
            if (pin != null) pin.sprite = skin.pin;
            if (nameTag != null && data.displayName != null)
                nameTag.sprite = data.displayName.Length <= 3 ? skin.locationTag3 : skin.locationTag5;
            if (dialogueBackground != null && skin.npcDialogueBubble != null) dialogueBackground.sprite = skin.npcDialogueBubble;
        }

        /// <summary>프리팹의 BoxCollider2D는 placeholder 시절의 1x1 고정이었는데, 실제 캐릭터 사진은
        /// 1.05~1.40으로 제각각이라 판정면이 인물보다 최대 40% 작았다(세관원 기준 사진 면적의 51%).
        /// 클릭은 대충 가운데를 누르니 티가 안 났지만, 호버는 "가장자리에 올렸는데 반응이 없다"가
        /// 그대로 드러나므로 실제 스프라이트 크기에 맞춘다.</summary>
        void FitColliderToBody()
        {
            var box = GetComponent<BoxCollider2D>();
            if (box == null || body == null || body.sprite == null) return;
            // body의 로컬 스케일까지 곱해야 실제 화면상 크기가 된다(걷기 프레임 보정 전 기준 스케일).
            var size = body.sprite.bounds.size;
            box.size = new Vector2(size.x * bodyBaseLocalScale.x, size.y * bodyBaseLocalScale.y);
            box.offset = bodyBaseLocalPosition;
        }

        /// <summary>정지 상태에서도 완전히 멈춰 있지 않도록 몸을 위아래로 살짝 들썩이는 숨쉬기 연출 -
        /// 추가 스프라이트 없이 idle 사진 그대로 위치만 미세하게 흔든다. 걷는 동안에는 꺼야 하므로
        /// StartWalkCycle/StopWalkCycle이 서로 켜고 끈다.</summary>
        void StartIdleBob()
        {
            if (body == null) return;
            StopIdleBobVisualOnly();
            idleBobRoutine = StartCoroutine(IdleBobRoutine());
        }

        /// <summary>코루틴만 멈추고 body.transform.localPosition을 원위치로 되돌린다 - 걷기 시작
        /// 직전 호출용(걷기 자체는 body.transform.localScale만 건드리고 localPosition은 그대로
        /// 둬야 하므로, 들썩임이 남아있지 않게 미리 원위치로 정리한다).</summary>
        void StopIdleBobVisualOnly()
        {
            if (idleBobRoutine != null)
            {
                StopCoroutine(idleBobRoutine);
                idleBobRoutine = null;
            }
            if (body != null) body.transform.localPosition = bodyBaseLocalPosition;
        }

        IEnumerator IdleBobRoutine()
        {
            // 같은 프리팹을 여럿 쓰는 NPC들이 전부 같은 박자로 들썩이면 부자연스러워 보여서,
            // 시작 위상을 무작위로 어긋나게 한다.
            float t = UnityEngine.Random.Range(0f, IdleBobPeriod);
            while (true)
            {
                t += Time.deltaTime;
                float offset = Mathf.Sin(t / IdleBobPeriod * Mathf.PI * 2f) * IdleBobAmplitude;
                body.transform.localPosition = bodyBaseLocalPosition + new Vector3(0f, offset, 0f);
                yield return null;
            }
        }

        public void Highlight()
        {
            StopAndUnregister(highlightRoutine, highlightPlayback);
            highlightSkipRequested = false;
            highlightRoutine = StartCoroutine(HighlightRoutine());
        }

        IEnumerator HighlightRoutine()
        {
            highlightPlayback = new DelegatePlayback(() => highlightSkipRequested = true);
            PlaybackDirector.Instance?.Register(highlightPlayback);

            if (body != null) body.color = HighlightColor;

            float t = 0f;
            while (t < HighlightDuration && !highlightSkipRequested)
            {
                t += Time.deltaTime;
                yield return null;
            }

            // baseColor(흰색)가 아니라 지금 상태의 색으로 돌아가야 한다 - 선택되었거나 커서가 올라가
            // 있는 NPC에 플래시가 지나가면 그 강조가 통째로 풀려버렸다.
            if (body != null) body.color = RestingColor();
            PlaybackDirector.Instance?.Unregister(highlightPlayback);
            highlightPlayback = null;
            highlightRoutine = null;
        }

        /// <summary>정보 전달/확산의 일회성 플래시(Highlight)와 달리, 지금 전달 대상으로 선택되어 있는
        /// 동안 계속 유지되는 강조 표시 - TargetingController가 대상 선택/해제 시점에 호출한다.</summary>
        public void SetSelected(bool value)
        {
            if (selected == value) return;
            selected = value;
            StartStateTween(SelectionTweenDuration, gated: true);
        }

        /// <summary>커서가 이 NPC 위에 올라왔는지 - 확대와 색 강조를 동시에 건다.</summary>
        void SetHovered(bool value)
        {
            if (hovered == value) return;
            hovered = value;
            // 호버 트윈은 PlaybackDirector에 등록하지 않는다. 등록하면 재생 중으로 잡혀 입력이 잠기고,
            // 커서가 NPC를 스칠 때마다 손패 클릭이 씹힌다 - 카드 클릭 누락을 고친 것과 같은 함정이다.
            StartStateTween(HoverTweenDuration, gated: false);
        }

        /// <summary>지금 상태에서 body가 있어야 할 색. 선택이 호버를 이긴다 - 이미 전달 대상으로
        /// 확정된 NPC를 가리켰다고 해서 그 표시가 옅어지면 안 된다.</summary>
        Color RestingColor() => selected ? SelectionColor : hovered ? HoverColor : baseColor;

        /// <summary>선택과 호버가 각자 코루틴을 돌리면 둘 다 매 프레임 body.color에 써서 마지막에
        /// 실행된 쪽만 남는다(선택된 NPC를 가리켰다 떼면 선택 색이 풀리는 식). 그래서 상태가 어느
        /// 쪽으로 바뀌든 항상 이 하나의 트윈만 돌리고, 목표는 그때그때 RestingColor()로 다시 계산한다.</summary>
        void StartStateTween(float duration, bool gated)
        {
            StopAndUnregister(stateRoutine, statePlayback);
            statePlayback = null;
            stateSkipRequested = false;
            stateRoutine = StartCoroutine(StateTweenRoutine(duration, gated));
        }

        IEnumerator StateTweenRoutine(float duration, bool gated)
        {
            if (gated)
            {
                statePlayback = new DelegatePlayback(() => stateSkipRequested = true);
                PlaybackDirector.Instance?.Register(statePlayback);
            }

            Color from = body != null ? body.color : Color.white;
            Color to = RestingColor();
            Vector3 pinFrom = pin != null ? pin.transform.localScale : Vector3.one;
            Vector3 pinTo = selected ? Vector3.Scale(pinBaseScale, PinSelectedScaleMul) : pinBaseScale;
            // 확대는 루트에 건다 - body의 localScale은 걷기 프레임 크기 보정이 매 프레임 덮어쓰므로
            // 거기에 얹으면 이동 중에 호버 확대가 사라진다. 루트를 키우면 프레임/이름표까지 함께
            // 커져서 "NPC 하나가 통째로 다가오는" 모양이 된다.
            // 2D라 z는 건드리지 않는다 - Vector3 통째로 곱하면 z까지 1.1이 되어 정렬/보간에 쓸데없는
            // 값이 섞인다.
            Vector3 scaleFrom = transform.localScale;
            float mul = hovered ? HoverScaleMultiplier : 1f;
            Vector3 scaleTo = new Vector3(rootBaseScale.x * mul, rootBaseScale.y * mul, rootBaseScale.z);

            float t = 0f;
            while (t < duration && !stateSkipRequested)
            {
                t += Time.deltaTime;
                float e = Mathf.SmoothStep(0f, 1f, t / duration);
                if (body != null) body.color = Color.Lerp(from, to, e);
                if (pin != null) pin.transform.localScale = Vector3.Lerp(pinFrom, pinTo, e);
                transform.localScale = Vector3.Lerp(scaleFrom, scaleTo, e);
                yield return null;
            }
            if (body != null) body.color = to;
            if (pin != null) pin.transform.localScale = pinTo;
            transform.localScale = scaleTo;

            if (statePlayback != null)
            {
                PlaybackDirector.Instance?.Unregister(statePlayback);
                statePlayback = null;
            }
            stateRoutine = null;
        }

        public void SetWorldPosition(Vector2 position)
        {
            transform.position = new Vector3(position.x, position.y, transform.position.z);
        }

        public void AnimateTo(Vector2 target)
        {
            StopAndUnregister(moveRoutine, movePlayback);
            moveSkipRequested = false;
            moveRoutine = StartCoroutine(MoveRoutine(target));
        }

        IEnumerator MoveRoutine(Vector2 target)
        {
            movePlayback = new DelegatePlayback(() => moveSkipRequested = true);
            PlaybackDirector.Instance?.Register(movePlayback);

            StartWalkCycle();

            Vector2 start = transform.position;
            float t = 0f;
            while (t < 1f && !moveSkipRequested)
            {
                t += Time.deltaTime / MoveDuration;
                float e = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
                SetWorldPosition(Vector2.Lerp(start, target, e));
                yield return null;
            }
            SetWorldPosition(target);

            StopWalkCycle();

            PlaybackDirector.Instance?.Unregister(movePlayback);
            movePlayback = null;
            moveRoutine = null;
        }

        /// <summary>NpcData.walkFrames가 비어 있으면 조용히 생략하고 idle 사진을 그대로 유지한다
        /// (하위 호환 - 아직 걷기 프레임이 없는 캐릭터도 에러 없이 동작). 이동이 끝나기 전에(즉
        /// StopWalkCycle이 실행되기 전에) AnimateTo가 다시 호출되면 MoveRoutine이 StopCoroutine으로
        /// 즉시 끊긴다 - 이때 그 MoveRoutine이 시작해 둔 walkCycleRoutine은 "고아 코루틴"이 되어
        /// StopWalkCycle의 정리 코드(idle 사진/스케일 복귀)를 영원히 못 받고 계속 돌아버린다(실측:
        /// 걷기 프레임이 도착 후에도 계속 보이던 버그의 원인). 그래서 새 걷기를 시작하기 직전에
        /// 이전 walkCycleRoutine을 먼저 확실히 끊어야 한다.</summary>
        void StartWalkCycle()
        {
            if (body == null || BoundData == null || BoundData.walkFrames == null || BoundData.walkFrames.Length == 0) return;
            StopWalkCycleCoroutineOnly();
            StopIdleBobVisualOnly();
            walkCycleRoutine = StartCoroutine(WalkCycleRoutine());
        }

        void StopWalkCycleCoroutineOnly()
        {
            if (walkCycleRoutine != null)
            {
                StopCoroutine(walkCycleRoutine);
                walkCycleRoutine = null;
            }
        }

        void StopWalkCycle()
        {
            StopWalkCycleCoroutineOnly();
            if (body != null && BoundData != null)
            {
                body.sprite = BoundData.characterPhoto;
                body.transform.localScale = bodyBaseLocalScale;
            }
            StartIdleBob();
        }

        /// <summary>실제로 "부드러워 보이는" 움직임은 이 루틴이 만드는 게 아니다 - 걷기 프레임 교체는
        /// 소스 아트 자체가 프레임 간 뚜렷한 자세 변화가 없어(위 상수 주석 참고) 몇 프레임을 쓰든
        /// 스프라이트 교체만으로는 매끈해 보일 수 없다. 대신 매 프레임(Time.deltaTime 누적, 스프라이트
        /// 교체 주기와 무관) 연속 보간되는 "발걸음 통통 튐"(WalkBob)을 body의 localPosition.y에
        /// 얹어서 실제 체감 부드러움을 낸다 - 스프라이트 교체는 보조 디테일로만 낮은 fps로 돈다.
        /// 걷기 프레임은 원본 idle 사진(characterPhoto)과 크롭 크기가 서로 달라, 프레임이 바뀔 때마다
        /// idle 사진의 세로 크기에 맞춰 균일 스케일을 다시 계산한다(LocationSiteView의
        /// FitLabelToNameTag와 같은 방식). 시트 전체(최대 36장)가 아니라 앞쪽 WalkCycleFrameCount장만
        /// 순환한다(사용자 확인: 전부 안 써도 됨).</summary>
        IEnumerator WalkCycleRoutine()
        {
            var frames = BoundData.walkFrames;
            int count = Mathf.Min(WalkCycleFrameCount, frames.Length);
            float idleHeight = BoundData.characterPhoto != null ? BoundData.characterPhoto.bounds.size.y : 0f;
            int frameIndex = 0;
            float frameTimer = 0f;
            float frameInterval = 1f / WalkFrameFps;
            float bobTimer = 0f;

            ApplyWalkFrame(frames[0], idleHeight);

            while (true)
            {
                frameTimer += Time.deltaTime;
                bobTimer += Time.deltaTime;

                if (frameTimer >= frameInterval)
                {
                    frameTimer -= frameInterval;
                    frameIndex = (frameIndex + 1) % count;
                    ApplyWalkFrame(frames[frameIndex], idleHeight);
                }

                // Abs(Sin)은 항상 0 이상이라 "발이 땅을 딛고 튀어오르는" 느낌의 통통 튐이 된다
                // (Idle의 -~+ 오가는 숨쉬기 사인파와 의도적으로 다른 모양).
                float bob = Mathf.Abs(Mathf.Sin(bobTimer / WalkBobPeriod * Mathf.PI)) * WalkBobAmplitude;
                body.transform.localPosition = bodyBaseLocalPosition + new Vector3(0f, bob, 0f);

                yield return null;
            }
        }

        void ApplyWalkFrame(Sprite frame, float idleHeight)
        {
            body.sprite = frame;
            if (idleHeight > 0f && frame.bounds.size.y > 0f)
                body.transform.localScale = bodyBaseLocalScale * (idleHeight / frame.bounds.size.y);
        }

        public void ShowDialogue(string text)
        {
            if (dialogueRoot == null || dialogueLabel == null || string.IsNullOrEmpty(text)) return;

            StopAndUnregister(dialogueRoutine, dialoguePlayback);
            dialogueLabel.text = text;
            FitDialogueToCamera();
            // 지난 대사에서 내렸던 만큼을 먼저 되돌린 뒤 이번 위치를 다시 잰다.
            dialogueRoot.transform.localPosition = dialogueBaseLocalPosition;
            dialogueRoot.SetActive(true);
            ClampDialogueToScreen();
            dialogueSkipRequested = false;
            dialogueRoutine = StartCoroutine(DialogueRoutine());
        }

        /// <summary>말풍선은 월드 스페이스라 카메라 줌에 그대로 끌려다닌다 - 스테이지마다 orthographicSize가
        /// 5(Zone1) ~ 14(Metropolis)로 2.8배 차이가 나서, 한쪽에서 읽기 좋게 맞추면 반대쪽에서는 글자가
        /// 화면을 덮거나 반대로 못 읽을 만큼 작아진다(실측: 고정 크기일 때 같은 글자가 Zone1 62px /
        /// Metropolis 22px). 그래서 프리팹 수치는 기준 줌(DialogueReferenceOrthoSize)에서 한 번만 맞추고,
        /// 실제 표시 직전에 현재 줌 비율만큼 되곱해 <b>화면상 크기를 어느 스테이지에서나 동일</b>하게 만든다.
        /// 루트가 캐릭터 크기 보정 스케일을 갖고 있으므로(lossyScale) 그만큼은 나눠서 상쇄한다.</summary>
        void FitDialogueToCamera()
        {
            var cam = Camera.main;
            float ortho = cam != null && cam.orthographic ? cam.orthographicSize : DialogueReferenceOrthoSize;
            float zoom = ortho / DialogueReferenceOrthoSize;
            float parentScale = Mathf.Abs(transform.lossyScale.y);
            if (parentScale < 0.0001f) parentScale = 1f;
            dialogueRoot.transform.localScale = Vector3.one * (zoom / parentScale);
        }

        /// <summary>말풍선이 화면 위로 잘리지 않게 넘치는 만큼만 아래로 내린다.
        ///
        /// 지도 위쪽에 서 있는 NPC는 머리 위 말풍선이 화면 밖으로 나간다 - 대도시에서 17명 중
        /// 2명(경비대장·하급 경비병)이 위쪽 20px가량 잘려 있었다. 카메라 줌이 큰 스테이지일수록
        /// 말풍선을 그만큼 키워 보정하므로(FitDialogueToCamera) 더 쉽게 넘친다.
        ///
        /// 화면 안이면 아무것도 하지 않는다 - 평소 위치를 바꾸지 않기 위해서다.</summary>
        void ClampDialogueToScreen()
        {
            var cam = Camera.main;
            if (cam == null || dialogueBackground == null || !cam.orthographic) return;

            float top = cam.WorldToScreenPoint(new Vector3(0f, dialogueBackground.bounds.max.y, 0f)).y;
            float limit = Screen.height - DialogueScreenMargin;
            if (top <= limit) return;

            float worldPerPixel = cam.orthographicSize * 2f / Screen.height;
            float parentScale = Mathf.Abs(transform.lossyScale.y);
            if (parentScale < 0.0001f) parentScale = 1f;

            var p = dialogueRoot.transform.localPosition;
            p.y -= (top - limit) * worldPerPixel / parentScale;
            dialogueRoot.transform.localPosition = p;
        }

        /// <summary>동시에 대사는 최대 1개만 표시한다는 규칙을 지키기 위해 WorldPresenter가 새 대사를
        /// 띄우기 직전, 이전에 말하던 NPC의 말풍선을 페이드 없이 즉시 정리할 때 호출한다.</summary>
        public void HideDialogueImmediately()
        {
            if (dialogueRoutine == null) return;
            StopAndUnregister(dialogueRoutine, dialoguePlayback);
            dialogueRoutine = null;
            if (dialogueRoot != null) dialogueRoot.SetActive(false);
        }

        IEnumerator DialogueRoutine()
        {
            dialoguePlayback = new DelegatePlayback(() => dialogueSkipRequested = true);
            PlaybackDirector.Instance?.Register(dialoguePlayback);

            float t = 0f;
            while (t < DialogueDuration && !dialogueSkipRequested)
            {
                t += Time.deltaTime;
                yield return null;
            }

            dialogueRoot.SetActive(false);
            PlaybackDirector.Instance?.Unregister(dialoguePlayback);
            dialoguePlayback = null;
            dialogueRoutine = null;
        }

        /// <summary>StopCoroutine은 코루틴을 중간에서 끊어버려 정리 코드(Unregister)를 실행하지 못하게 한다 -
        /// 그래서 끊기 전에 등록된 playback을 직접 해제해야 PlaybackDirector에 누수가 남지 않는다.</summary>
        void StopAndUnregister(Coroutine routine, IPlayback playback)
        {
            if (routine == null) return;
            StopCoroutine(routine);
            if (playback != null) PlaybackDirector.Instance?.Unregister(playback);
        }
    }
}
