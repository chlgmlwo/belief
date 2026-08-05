using System;
using UnityEngine;

namespace Belief.Data
{
    [Serializable]
    public struct RelationshipEntry
    {
        public NpcData other;
        public RelationshipTypeData type;
        [Range(-1f, 1f)] public float strength;

        [Header("조사 파일 표시용 (Frozen 원문, 정성적 텍스트만 - 수치 아님)")]
        public string relationshipTypeLabel;
        [TextArea(1, 2)] public string relationshipDescription;
    }

    [CreateAssetMenu(fileName = "Npc_Major_", menuName = "Belief/NPC/Major", order = 2)]
    public class MajorNpcData : NpcData
    {
        [Header("Major NPC")]
        [TextArea(2, 4)] public string goal;
        [Range(0f, 1f)] public float loyalty = 0.5f;

        [Header("Relationships (BeliefSystem 전용, 플레이어 비공개)")]
        public RelationshipEntry[] relationships;

        [Header("Candidate Actions (IMajorNpcThinker가 이 중에서만 선택)")]
        public NpcActionData[] availableActions;

        [Header("Belief Dialogues (RuleBasedMajorThinker가 CurrentBelief에 맞는 contextTag를 찾아 사용)")]
        public DialogueLineData[] beliefDialogues;

        [Header("Movement (RuleBasedMajorThinker/LLM Thinker가 매 턴 종료 시 이 중 하나로 이동할지 판단한다)")]
        public LocationData[] movementCandidates;

        [Header("Location Preference (Frozen locationPreference - 이동 후보 필터링/가중치 전용)")]
        public LocationData[] preferredLocations;
        public LocationData[] avoidedLocations;

        public override string InitialGoal => goal;

        /// <summary>관계 항목이 아예 없는 경우와 강도 0인 관계를 구분하기 위해 bool로 존재 여부를 반환한다.</summary>
        public bool TryGetRelationshipStrength(NpcData other, out float strength)
        {
            if (relationships != null)
            {
                foreach (var entry in relationships)
                {
                    if (entry.other == other)
                    {
                        strength = entry.strength;
                        return true;
                    }
                }
            }
            strength = 0f;
            return false;
        }
    }
}
