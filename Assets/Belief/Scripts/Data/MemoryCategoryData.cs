using UnityEngine;

namespace Belief.Data
{
    /// <summary>핵심 기억 종류(배신/생명의 은인/반복된 거짓말/중요한 약속/가족 관련 사건 등).</summary>
    [CreateAssetMenu(fileName = "MemoryCategory_", menuName = "Belief/Memory Category", order = 5)]
    public class MemoryCategoryData : ScriptableObject
    {
        public string memoryCategoryId;
        public string displayName;

        [Tooltip("이 기억이 믿음을 밀어올리는 방향인지(+1, 생명의 은인 등) 끌어내리는 방향인지(-1, 배신 등).")]
        [Range(-1f, 1f)] public float valence = -1f;
    }
}
