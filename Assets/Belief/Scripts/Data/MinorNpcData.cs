using UnityEngine;

namespace Belief.Data
{
    [CreateAssetMenu(fileName = "Npc_Minor_", menuName = "Belief/NPC/Minor", order = 2)]
    public class MinorNpcData : NpcData
    {
        public override NpcRank Rank => NpcRank.Minor;

        /// <summary>공용 IMajorNpcThinker(RuleBased 또는 LLM/Fallback)가 이 중에서만 Intent에 맞는
        /// Action을 선택한다 - MajorNpcData.availableActions와 완전히 같은 형태(호환 목적, base로
        /// 승격하지 않음). Goal/Relationships 없이도 판단 가능한 공용(Generic) Action만 배선한다.</summary>
        [Header("Candidate Actions (공통 Thinker가 이 중에서만 선택)")]
        public NpcActionData[] availableActions;
    }
}
