using UnityEngine;

namespace Belief.Data
{
    /// <summary>MemorySelector/MemoryEvaluator 전용 튜닝.</summary>
    [CreateAssetMenu(fileName = "MemoryTuning", menuName = "Belief/Config/Memory Tuning", order = 12)]
    public class MemoryTuningData : ScriptableObject
    {
        [Tooltip("이 턴 수 이내에 기록된 기억을 '최근'으로 취급한다.")]
        public int recentWindowTurns = 3;

        [Tooltip("MemorySelector가 WorkingMemory에 담을 최대 기억 개수.")]
        public int maxWorkingMemorySize = 2;

        [Tooltip("기억 하나가 Belief 판단에 기여하는 최대 보정 크기.")]
        [Range(0f, 1f)] public float maxSingleMemoryModifier = 0.25f;
    }
}
