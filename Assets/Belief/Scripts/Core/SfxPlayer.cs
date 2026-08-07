using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Belief.Core
{
    /// <summary>효과음을 내는 유일한 자리. 배경음과 같은 방식의 영속 오브젝트라 씬이 바뀌어도
    /// 부르는 쪽은 <see cref="Play"/> 한 줄이면 된다.
    ///
    /// <b>겹쳐 나는 소리를 눌러 준다.</b> 확산은 NPC 여러 명이 한꺼번에 판단하고 움직이므로
    /// 같은 소리가 같은 프레임에 몇 번씩 겹쳐 울린다 - 같은 종류가 짧은 간격 안에 다시 오면
    /// 무시해서 소리가 뭉치지 않게 한다.</summary>
    public class SfxPlayer : MonoBehaviour
    {
        public static SfxPlayer Instance { get; private set; }

        /// <summary>같은 종류가 이 시간 안에 또 오면 무시한다.</summary>
        const float RetriggerGuard = 0.07f;

        /// <summary>동시에 울릴 수 있는 소리 수. PlayOneShot을 쓰지 않는 이유는 시작 지점과 길이를
        /// 소리마다 다르게 잘라야 하는데, 그건 AudioSource 하나를 통째로 잡아야만 되기 때문이다.</summary>
        const int VoiceCount = 6;

        AudioSource[] voices;
        SfxLibrary library;
        readonly Dictionary<Sfx, float> lastPlayed = new Dictionary<Sfx, float>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Bootstrap()
        {
            if (Instance != null) return;
            var go = new GameObject("SfxPlayer");
            DontDestroyOnLoad(go);
            go.AddComponent<SfxPlayer>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            library = Resources.Load<SfxLibrary>("SfxLibrary");
            if (library == null)
                Debug.LogWarning("[SFX] Resources/SfxLibrary를 찾지 못해 효과음이 나지 않는다.");

            voices = new AudioSource[VoiceCount];
            for (int i = 0; i < VoiceCount; i++)
            {
                var v = gameObject.AddComponent<AudioSource>();
                v.playOnAwake = false;
                v.loop = false;
                v.spatialBlend = 0f;
                voices[i] = v;
            }

            SceneManager.sceneLoaded += OnSceneLoaded;
            HookButtons();
        }

        void OnDestroy()
        {
            if (Instance == this) SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode) => HookButtons();

        public static void Play(Sfx kind) => Instance?.PlayInternal(kind);

        void PlayInternal(Sfx kind)
        {
            if (library == null || voices == null) return;
            var entry = library.Find(kind);
            if (entry == null) return;

            // 일시정지 중에도 눌리는 버튼이 있으므로 시간은 unscaled로 잰다.
            float now = Time.unscaledTime;
            if (lastPlayed.TryGetValue(kind, out float last) && now - last < RetriggerGuard) return;
            lastPlayed[kind] = now;

            var voice = TakeVoice();
            if (voice == null) return;

            voice.clip = entry.clip;
            voice.volume = entry.volume * SoundSettings.Sfx;
            voice.time = Mathf.Clamp(entry.startTime, 0f, Mathf.Max(0f, entry.clip.length - 0.05f));
            voice.Play();

            float length = entry.maxLength > 0f
                ? Mathf.Min(entry.maxLength, entry.clip.length - voice.time)
                : entry.clip.length - voice.time;
            StartCoroutine(StopAfter(voice, length));
        }

        /// <summary>비어 있는 소리 자리를 고른다. 전부 울리고 있으면 가장 오래된 것을 뺏는다 -
        /// 새 소리가 나지 않는 것보다 낫다.</summary>
        AudioSource TakeVoice()
        {
            foreach (var v in voices)
                if (v != null && !v.isPlaying) return v;

            AudioSource oldest = null;
            float best = -1f;
            foreach (var v in voices)
                if (v != null && v.time > best) { best = v.time; oldest = v; }
            return oldest;
        }

        /// <summary>정해진 길이만큼만 울리고 짧게 줄여 끈다 - 뚝 끊으면 "딱" 하는 잡음이 난다.</summary>
        System.Collections.IEnumerator StopAfter(AudioSource voice, float length)
        {
            const float FadeOut = 0.06f;
            float wait = Mathf.Max(0f, length - FadeOut);
            float t = 0f;
            while (t < wait) { t += Time.unscaledDeltaTime; yield return null; }

            float from = voice.volume;
            t = 0f;
            while (t < FadeOut && voice.isPlaying)
            {
                t += Time.unscaledDeltaTime;
                voice.volume = Mathf.Lerp(from, 0f, t / FadeOut);
                yield return null;
            }
            voice.Stop();
        }

        // ------------------------------------------------------------ 버튼 클릭음

        /// <summary>씬 안의 모든 버튼에 클릭음을 건다. 버튼마다 손으로 다는 대신 한 곳에서 훑는 이유는,
        /// 버튼이 HUD·메인 메뉴·일시정지·결과창에 흩어져 있어 한 군데라도 빠뜨리면 "어떤 버튼은
        /// 소리가 나고 어떤 버튼은 안 나는" 상태가 되기 때문이다.
        ///
        /// 이미 건 버튼에는 표식을 남겨 두 번 걸리지 않게 한다(씬을 다시 로드하면 버튼도 새것이라
        /// 표식이 함께 사라진다).</summary>
        public static void HookButtons()
        {
            foreach (var b in FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Hook(b);
        }

        /// <summary>런타임에 만든 버튼처럼 씬 훑기에서 놓치는 것들을 위해 열어 둔다.</summary>
        public static void Hook(Button button)
        {
            if (button == null || button.GetComponent<SfxClickMarker>() != null) return;
            button.gameObject.AddComponent<SfxClickMarker>();
            // 판정을 누르는 순간에 하는 이유는 순서 때문이다. 씬에 놓인 버튼은 훑기보다 Awake가
            // 먼저지만 런타임에 만든 버튼은 반대라, 걸 때 검사하면 이미 걸린 것을 되돌리지 못한다.
            button.onClick.AddListener(() =>
            {
                if (button != null && button.GetComponent<SfxClickMute>() == null) Play(Sfx.Click);
            });
        }

        /// <summary>이 버튼에는 공용 클릭음을 내지 않는다 - 자기 소리를 따로 가진 버튼(카드 등)이
        /// 두 소리를 겹쳐 내지 않게 한다.</summary>
        public static void Mute(Button button)
        {
            if (button != null && button.GetComponent<SfxClickMute>() == null)
                button.gameObject.AddComponent<SfxClickMute>();
        }
    }

    /// <summary>클릭음을 이미 건 버튼이라는 표식. 리스너는 중복 등록을 스스로 막지 못한다.</summary>
    [DisallowMultipleComponent]
    public class SfxClickMarker : MonoBehaviour { }

    /// <summary>공용 클릭음을 내지 않는 버튼이라는 표식.</summary>
    [DisallowMultipleComponent]
    public class SfxClickMute : MonoBehaviour { }
}
