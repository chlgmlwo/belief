using UnityEngine;

namespace Belief.Data
{
    /// <summary>RelationshipEvaluator 전용 튜닝. 새 Evaluator를 추가해도 이 파일은 무관하다.</summary>
    [CreateAssetMenu(fileName = "RelationshipTuning", menuName = "Belief/Config/Relationship Tuning", order = 11)]
    public class RelationshipTuningData : ScriptableObject
    {
        [Header("관계 강도 구간 (절대값 기준)")]
        [Range(0f, 1f)] public float weakThreshold = 0.3f;
        [Range(0f, 1f)] public float moderateThreshold = 0.7f;

        [Header("구간별 보정 크기")]
        [Range(0f, 1f)] public float weakModifier = 0.02f;
        [Range(0f, 1f)] public float moderateModifier = 0.12f;
        [Range(0f, 1f)] public float strongModifier = 0.30f;
    }
}
