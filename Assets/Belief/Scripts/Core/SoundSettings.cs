using System;
using UnityEngine;

namespace Belief.Core
{
    public enum SoundChannel { Bgm, Sfx }

    /// <summary>배경음/효과음 크기를 담아 두는 유일한 자리. 값은 PlayerPrefs에 저장돼 다음 실행에도
    /// 남는다(WebGL에서는 IndexedDB에 들어간다).
    ///
    /// 배경음은 <see cref="BgmController"/>가 이 값을 직접 읽고 <see cref="Changed"/>를 구독해
    /// 즉시 따라온다. 효과음은 아직 붙지 않았다 - 붙일 때는 각 AudioSource에
    /// <see cref="SoundChannelVolume"/>을 달아 두면 이 값이 자동으로 반영된다.</summary>
    public static class SoundSettings
    {
        const string BgmKey = "belief.sound.bgm";
        const string SfxKey = "belief.sound.sfx";
        const float DefaultVolume = 0.7f;

        static float bgm = -1f, sfx = -1f;

        /// <summary>값이 바뀔 때마다 발생 - 재생 중인 소리가 즉시 따라오게 하기 위한 통로.</summary>
        public static event Action Changed;

        public static float Bgm
        {
            get { Load(); return bgm; }
            set { Load(); Set(ref bgm, BgmKey, value); }
        }

        public static float Sfx
        {
            get { Load(); return sfx; }
            set { Load(); Set(ref sfx, SfxKey, value); }
        }

        public static float VolumeOf(SoundChannel channel) => channel == SoundChannel.Bgm ? Bgm : Sfx;

        static void Load()
        {
            if (bgm >= 0f) return;
            bgm = Mathf.Clamp01(PlayerPrefs.GetFloat(BgmKey, DefaultVolume));
            sfx = Mathf.Clamp01(PlayerPrefs.GetFloat(SfxKey, DefaultVolume));
        }

        static void Set(ref float field, string key, float value)
        {
            value = Mathf.Clamp01(value);
            if (Mathf.Approximately(field, value)) return;
            field = value;
            PlayerPrefs.SetFloat(key, value);
            // WebGL은 Save()를 불러야 실제로 기록된다(오토세이브에서 겪은 것과 같은 이유).
            PlayerPrefs.Save();
            Changed?.Invoke();
        }
    }

    /// <summary>AudioSource 옆에 붙여 두면 그 소리가 해당 채널 크기를 따라간다 - 소리를 넣을 때
    /// 볼륨 배선을 각자 다시 짜지 않게 하려고 미리 둔다.</summary>
    [RequireComponent(typeof(AudioSource))]
    public class SoundChannelVolume : MonoBehaviour
    {
        [SerializeField] SoundChannel channel = SoundChannel.Sfx;

        AudioSource source;
        float baseVolume = 1f;

        void Awake()
        {
            source = GetComponent<AudioSource>();
            baseVolume = source.volume;   // 클립별로 정해 둔 상대 크기는 그대로 존중한다
            Apply();
        }

        void OnEnable() { SoundSettings.Changed += Apply; Apply(); }
        void OnDisable() { SoundSettings.Changed -= Apply; }

        void Apply()
        {
            if (source != null) source.volume = baseVolume * SoundSettings.VolumeOf(channel);
        }
    }
}
