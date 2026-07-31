using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using Belief.Data;

namespace Belief.Presentation.World
{
    /// <summary>
    /// Placeholder 시각 표현(단색 사각형 + 라벨). 나중에 Sprite/Animator만 교체하면
    /// 그대로 동작하도록 코드에서 Sprite를 직접 참조하지 않는다.
    /// 이동/대사는 최소 연출(Coroutine 보간)만 제공하며 게임 상태는 건드리지 않는다 -
    /// 상태는 이미 확정된 뒤 이벤트로 재생만 한다.
    /// </summary>
    public class NpcActorView : MonoBehaviour, IPointerClickHandler
    {
        [SerializeField] SpriteRenderer body;
        [SerializeField] TextMesh label;
        [SerializeField] GameObject dialogueRoot;
        [SerializeField] TextMeshPro dialogueLabel;

        const float MoveDuration = 0.35f;
        const float DialogueDuration = 2.5f;
        const float HighlightDuration = 0.3f;
        const float SelectionTweenDuration = 0.18f;
        static readonly Color HighlightColor = new Color(0.95f, 0.85f, 0.30f);
        static readonly Color SelectionColor = new Color(0.95f, 0.75f, 0.25f);

        public NpcData BoundData { get; private set; }
        public event Action<NpcData> Clicked;

        // OnMouseDown은 legacy Input Manager 기반이라 Active Input Handling이
        // "Input System Package (New)"로 설정된 이 프로젝트에서는 호출되지 않는다.
        // EventSystem + Physics2DRaycaster(WorldPresenter가 보장)를 통해 클릭을 받는다.
        public void OnPointerClick(PointerEventData eventData) => Clicked?.Invoke(BoundData);

        Coroutine moveRoutine;
        IPlayback movePlayback;
        bool moveSkipRequested;

        Coroutine dialogueRoutine;
        IPlayback dialoguePlayback;
        bool dialogueSkipRequested;

        Coroutine highlightRoutine;
        IPlayback highlightPlayback;
        bool highlightSkipRequested;

        Coroutine selectionRoutine;
        IPlayback selectionPlayback;
        bool selectionSkipRequested;
        bool selected;

        Color baseColor;

        public void Bind(NpcData data)
        {
            BoundData = data;
            if (label != null) label.text = data.displayName;
            baseColor = data.Rank == NpcRank.Major ? new Color(0.30f, 0.85f, 0.55f) : new Color(0.45f, 0.60f, 0.80f);
            if (body != null) body.color = baseColor;
            if (dialogueRoot != null) dialogueRoot.SetActive(false);
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

            if (body != null) body.color = baseColor;
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

            StopAndUnregister(selectionRoutine, selectionPlayback);
            selectionSkipRequested = false;
            selectionRoutine = StartCoroutine(SelectionTweenRoutine(value));
        }

        IEnumerator SelectionTweenRoutine(bool toSelected)
        {
            selectionPlayback = new DelegatePlayback(() => selectionSkipRequested = true);
            PlaybackDirector.Instance?.Register(selectionPlayback);

            Color from = body != null ? body.color : Color.white;
            Color to = toSelected ? SelectionColor : baseColor;

            float t = 0f;
            while (t < SelectionTweenDuration && !selectionSkipRequested)
            {
                t += Time.deltaTime;
                if (body != null) body.color = Color.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / SelectionTweenDuration));
                yield return null;
            }
            if (body != null) body.color = to;

            PlaybackDirector.Instance?.Unregister(selectionPlayback);
            selectionPlayback = null;
            selectionRoutine = null;
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

            PlaybackDirector.Instance?.Unregister(movePlayback);
            movePlayback = null;
            moveRoutine = null;
        }

        public void ShowDialogue(string text)
        {
            if (dialogueRoot == null || dialogueLabel == null || string.IsNullOrEmpty(text)) return;

            StopAndUnregister(dialogueRoutine, dialoguePlayback);
            dialogueLabel.text = text;
            dialogueRoot.SetActive(true);
            dialogueSkipRequested = false;
            dialogueRoutine = StartCoroutine(DialogueRoutine());
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
