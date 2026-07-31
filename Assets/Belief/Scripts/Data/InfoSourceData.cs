using UnityEngine;

namespace Belief.Data
{
    [CreateAssetMenu(fileName = "Source_", menuName = "Belief/Info Source", order = 3)]
    public class InfoSourceData : ScriptableObject
    {
        [Header("Identity")]
        public string sourceId;
        public string displayName;

        [Header("Judgement Input")]
        [Tooltip("BeliefSystem의 기본판단축에 더해지는 출처 자체의 신뢰도.")]
        [Range(0f, 1f)] public float baseTrustModifier = 0.5f;
    }
}
