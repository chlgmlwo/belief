using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Belief.Core
{
    /// <summary>배경음을 트는 유일한 자리. 씬을 넘어도 살아남는 영속 오브젝트라
    /// (ProgressionController와 같은 방식) 같은 곡이 이어지는 구간에서는 음악이 끊기지 않는다 -
    /// 1구역에서 2구역으로 넘어가도 같은 곡이면 그대로 흐른다.
    ///
    /// <b>씬 이름만으로는 곡이 정해지지 않는다.</b> 구역 씬은 브리핑이 먼저 화면을 덮고 있고 그동안은
    /// 타이틀 곡이 흘러야 하며, 4구역은 마지막에 엔딩 화면으로 다시 타이틀 곡으로 돌아간다. 그래서
    /// 씬 로드는 "일단 타이틀 곡"만 정하고, 실제 전환 시점(브리핑을 닫을 때 / 엔딩이 뜰 때)에서
    /// <see cref="Request"/>를 불러 준다.
    ///
    /// 곡이 바뀔 때는 잘라 붙이지 않고 짧게 페이드한다. 그리고 이 페이드는 unscaledDeltaTime을 쓴다 -
    /// 일시정지 메뉴가 timeScale을 0으로 두는 동안에도 음악은 흘러야 하기 때문이다.</summary>
    [RequireComponent(typeof(AudioSource))]
    public class BgmController : MonoBehaviour
    {
        public static BgmController Instance { get; private set; }

        const float FadeOutDuration = 0.35f;
        const float FadeInDuration = 0.6f;

        AudioSource source;
        BgmLibrary library;
        BgmTrack current = BgmTrack.None;
        Coroutine switching;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Bootstrap()
        {
            if (Instance != null) return;
            var go = new GameObject("BgmController");
            DontDestroyOnLoad(go);
            go.AddComponent<BgmController>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            library = Resources.Load<BgmLibrary>("BgmLibrary");
            if (library == null)
                Debug.LogWarning("[BGM] Resources/BgmLibrary를 찾지 못해 배경음이 재생되지 않는다.");

            source = GetComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;             // 배경음이므로 항상 이어 돈다
            source.spatialBlend = 0f;       // 2D - 카메라 위치와 무관하게 같은 크기로 들린다
            ApplyVolume();

            // 소리 크기의 주인을 하나로 둔다 - SoundChannelVolume을 함께 붙이면 그쪽과 페이드가
            // 번갈아 source.volume을 덮어써서, 곡이 바뀔 때마다 설정값이 튄다.
            EnsureListener();

            SoundSettings.Changed += ApplyVolume;
            SceneManager.sceneLoaded += OnSceneLoaded;

            // 첫 씬은 sceneLoaded가 이미 지나갔을 수 있다(Bootstrap이 BeforeSceneLoad에서 도므로
            // 타이밍이 Unity 버전/설정에 따라 갈린다) - 여기서 한 번 더 걸어 두면 어느 쪽이든 켜진다.
            // 같은 곡이면 Play가 조용히 넘어가므로 중복 호출은 해가 없다.
            Play(BgmTrack.TitleAndBriefing);
        }

        void OnDestroy()
        {
            if (Instance != this) return;
            SoundSettings.Changed -= ApplyVolume;
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        /// <summary>0~1. 곡을 바꿀 때만 움직이고, 실제 볼륨은 여기에 설정값을 곱한 값이다.</summary>
        float fade = 1f;

        void ApplyVolume()
        {
            if (source != null) source.volume = SoundSettings.Bgm * fade;
        }

        /// <summary>씬이 바뀌면 일단 타이틀 곡이다 - 구역 씬도 브리핑이 먼저 뜨기 때문이다.
        /// 플레이 곡으로 넘어가는 것은 브리핑을 닫는 쪽에서 알려 준다.</summary>
        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            EnsureListener();
            Play(BgmTrack.TitleAndBriefing);
        }

        AudioListener ownListener;

        /// <summary>이 프로젝트의 씬에는 <b>AudioListener가 하나도 없다</b>(소리가 없던 시절에 카메라
        /// 프리팹에서 빠진 채로 굳었다). 리스너가 없으면 아무리 재생해도 들리지 않으므로 여기서
        /// 하나를 책임진다 - 영속 오브젝트라 모든 씬에서 유효하다.
        ///
        /// 나중에 어느 씬의 카메라에 리스너가 다시 붙으면 둘이 되어 Unity가 경고를 뱉으므로,
        /// 씬이 바뀔 때마다 "남의 리스너가 있으면 내 것을 끈다"로 정리한다.</summary>
        void EnsureListener()
        {
            bool othersExist = false;
            foreach (var l in FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (l != ownListener) { othersExist = true; break; }

            if (othersExist)
            {
                if (ownListener != null) ownListener.enabled = false;
                return;
            }

            if (ownListener == null) ownListener = gameObject.AddComponent<AudioListener>();
            ownListener.enabled = true;
        }

        /// <summary>지금 씬에서 "플레이 중"에 흘러야 할 곡. 구역 이름으로 정한다.</summary>
        public static BgmTrack StageTrackForCurrentScene()
        {
            string name = SceneManager.GetActiveScene().name;
            return name == "Metropolis" ? BgmTrack.Stage4 : BgmTrack.Stage123;
        }

        /// <summary>컨트롤러가 아직 없거나(에디터에서 스크립트만 돌리는 경우) 곡이 같으면 조용히 넘어간다.</summary>
        public static void Request(BgmTrack track) => Instance?.Play(track);

        public void Play(BgmTrack track)
        {
            if (track == current) return;   // 같은 곡이면 처음부터 다시 틀지 않는다
            var clip = library != null ? library.Get(track) : null;
            if (clip == null) return;       // 곡이 비어 있으면 지금 흐르던 것을 그대로 둔다

            current = track;
            if (switching != null) StopCoroutine(switching);
            switching = StartCoroutine(SwitchRoutine(clip));
        }

        IEnumerator SwitchRoutine(AudioClip clip)
        {
            if (source.isPlaying)
            {
                float from = fade;
                float t = 0f;
                while (t < FadeOutDuration)
                {
                    t += Time.unscaledDeltaTime;
                    fade = Mathf.Lerp(from, 0f, t / FadeOutDuration);
                    ApplyVolume();
                    yield return null;
                }
            }

            // 프리로드를 꺼 두었으므로 클립이 아직 메모리에 없다 - 이 상태로 Play를 부르면 아무 소리도
            // 나지 않는다(오류도 안 난다). 올라올 때까지 기다렸다 튼다.
            if (clip.loadState != AudioDataLoadState.Loaded)
            {
                clip.LoadAudioData();
                float waited = 0f;
                while (clip.loadState == AudioDataLoadState.Loading && waited < 10f)
                {
                    waited += Time.unscaledDeltaTime;
                    yield return null;
                }
                if (clip.loadState != AudioDataLoadState.Loaded)
                {
                    Debug.LogWarning($"[BGM] '{clip.name}' 로드 실패({clip.loadState}) - 재생을 건너뛴다.");
                    switching = null;
                    yield break;
                }
            }

            source.clip = clip;
            fade = 0f;
            ApplyVolume();
            source.Play();

            float t2 = 0f;
            while (t2 < FadeInDuration)
            {
                t2 += Time.unscaledDeltaTime;
                fade = Mathf.Lerp(0f, 1f, t2 / FadeInDuration);
                ApplyVolume();
                yield return null;
            }
            fade = 1f;
            ApplyVolume();

            switching = null;
        }
    }
}
