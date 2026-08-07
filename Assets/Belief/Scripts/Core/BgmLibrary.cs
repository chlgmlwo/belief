using UnityEngine;

namespace Belief.Core
{
    /// <summary>배경음이 세 갈래뿐이라 곡 목록도 세 칸이다.</summary>
    public enum BgmTrack
    {
        None,
        /// <summary>타이틀 화면, 구역 브리핑, 엔딩 - 게임을 "하고 있지 않은" 구간이 전부 이 곡이다.</summary>
        TitleAndBriefing,
        /// <summary>1~3구역 플레이 중.</summary>
        Stage123,
        /// <summary>4구역(대도시) 플레이 중.</summary>
        Stage4,
    }

    /// <summary>어느 곡을 쓸지만 담는 자료. <see cref="BgmController"/>가 Resources에서 이름으로
    /// 읽으므로 반드시 Resources 폴더 안에 `BgmLibrary` 이름으로 있어야 한다
    /// (ProgressionData와 같은 방식).</summary>
    [CreateAssetMenu(fileName = "BgmLibrary", menuName = "Belief/Audio/BGM Library")]
    public class BgmLibrary : ScriptableObject
    {
        public AudioClip titleAndBriefing;
        public AudioClip stage123;
        public AudioClip stage4;

        public AudioClip Get(BgmTrack track)
        {
            switch (track)
            {
                case BgmTrack.TitleAndBriefing: return titleAndBriefing;
                case BgmTrack.Stage123: return stage123;
                case BgmTrack.Stage4: return stage4;
                default: return null;
            }
        }
    }
}
