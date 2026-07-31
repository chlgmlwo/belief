using UnityEngine;

namespace Belief.Data
{
    public abstract class NpcData : ScriptableObject
    {
        [Header("Identity")]
        public string npcId;
        public string displayName;
        public LocationData homeLocation;

        [Header("Profile (조사 파일 표시용, Frozen - 읽기 전용 원문)")]
        public string gender;
        public string job;
        public string affiliation;
        [TextArea(1, 3)] public string gameplayRoleSummary;
        [TextArea(1, 2)] public string[] aiNotes;

        [Header("Personality (BeliefSystem 전용, 플레이어 비공개)")]
        [Range(0f, 1f)] public float trustBias = 0.5f;
        [Range(0f, 1f)] public float skepticism = 0.5f;

        public abstract NpcRank Rank { get; }

        /// <summary>Frozen AI Profile에 정의된 장기 목표(있는 경우). NpcState가 타입 분기 없이
        /// 공통으로 읽을 수 있도록 base에 가상 프로퍼티로 둔다 - 목표가 없는 NPC 유형은 null을 반환한다.</summary>
        public virtual string InitialGoal => null;
    }
}
