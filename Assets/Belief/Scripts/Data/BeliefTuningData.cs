using UnityEngine;

namespace Belief.Data
{
    /// <summary>BeliefSystem 자신이 쓰는 값만 담는다 (예외축 상한, 점수-BeliefState 임계값).</summary>
    [CreateAssetMenu(fileName = "BeliefTuning", menuName = "Belief/Config/Belief Tuning", order = 10)]
    public class BeliefTuningData : ScriptableObject
    {
        [Tooltip("관계+기억 보정 합산의 최대 절대값.")]
        [Range(0f, 1f)] public float maxExceptionalModifier = 0.35f;

        [Header("점수 -> BeliefState 임계값 (내림차순)")]
        [Range(0f, 1f)] public float trustedThreshold = 0.75f;
        [Range(0f, 1f)] public float plausibleThreshold = 0.55f;
        [Range(0f, 1f)] public float needsVerificationThreshold = 0.35f;
        [Range(0f, 1f)] public float doubtfulThreshold = 0.15f;
    }
}
