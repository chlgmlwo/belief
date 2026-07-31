using UnityEngine;

namespace Belief.Data
{
    /// <summary>watchedNpcs 중 지금 watchedLocation에 있는 인원수를 반환한다 - "장소를 떠남"을 세는
    /// NpcsLeaveLocationCondition과 반대로 "장소에 모여 있음"을 센다.</summary>
    [CreateAssetMenu(fileName = "Condition_NpcsGatherAtLocation", menuName = "Belief/Missions/Npcs Gather At Location Condition")]
    public class NpcsGatherAtLocationCondition : MissionConditionData
    {
        public LocationData watchedLocation;
        public NpcData[] watchedNpcs;

        public override int GetCurrentProgress(MissionEvaluationContext context)
        {
            if (watchedNpcs == null) return 0;

            int count = 0;
            foreach (var npcData in watchedNpcs)
            {
                if (context.Npcs.TryGetValue(npcData, out var state) && state.CurrentLocation == watchedLocation)
                    count++;
            }
            return count;
        }
    }
}
