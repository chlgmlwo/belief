using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Belief.Presentation
{
    /// <summary>연출 하나(이동 보간, 대사 표시 등)를 나타낸다. 즉시완료/스킵 시 최종 상태로 강제 점프한다.</summary>
    public interface IPlayback
    {
        void CompleteImmediately();
    }

    /// <summary>델리게이트 하나로 IPlayback을 구현 - 자잘한 연출마다 클래스를 새로 만들지 않기 위함.</summary>
    public class DelegatePlayback : IPlayback
    {
        readonly Action onCompleteImmediately;
        public DelegatePlayback(Action onCompleteImmediately) => this.onCompleteImmediately = onCompleteImmediately;
        public void CompleteImmediately() => onCompleteImmediately?.Invoke();
    }

    /// <summary>
    /// 현재 재생 중인 연출을 추적하고 스킵을 지원하는 Presentation 전용 유틸리티.
    /// 게임 상태는 절대 건드리지 않는다 - 등록/해제와 스킵 신호 전달만 한다.
    /// </summary>
    public class PlaybackDirector : MonoBehaviour
    {
        public static PlaybackDirector Instance { get; private set; }

        readonly List<IPlayback> active = new List<IPlayback>();
        public bool IsPlaying => active.Count > 0;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void Register(IPlayback playback) => active.Add(playback);
        public void Unregister(IPlayback playback) => active.Remove(playback);

        public void SkipAll()
        {
            foreach (var p in new List<IPlayback>(active))
                p.CompleteImmediately();
        }

        void Update()
        {
            if (!IsPlaying) return;

            bool skipPressed =
                (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame) ||
                (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame);

            if (skipPressed) SkipAll();
        }
    }
}
