using UnityEngine;

namespace Belief.Data
{
    /// <summary>특정 NPC가 지금 특정 장소에 있는지 검사한다("장소를 떠남"을 보는
    /// NpcsLeaveLocationCondition과 반대로 "장소에 도착함"을 본다).</summary>
    [CreateAssetMenu(fileName = "Condition_NpcAtLocation", menuName = "Belief/Missions/Npc At Location Condition")]
    public class NpcAtLocationCondition : MissionConditionData
    {
        public NpcData targetNpc;
        public LocationData targetLocation;

        public override int GetCurrentProgress(MissionEvaluationContext context)
        {
            if (targetNpc == null || targetLocation == null) return 0;
            if (!context.Npcs.TryGetValue(targetNpc, out var state)) return 0;

            return state.CurrentLocation == targetLocation ? TargetCount : 0;
        }
    }
}
