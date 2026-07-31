using UnityEngine;

namespace Belief.Data
{
    [CreateAssetMenu(fileName = "Condition_NpcsLeaveLocation", menuName = "Belief/Missions/Npcs Leave Location Condition")]
    public class NpcsLeaveLocationCondition : MissionConditionData
    {
        public LocationData watchedLocation;
        public NpcData[] watchedNpcs;

        public override int GetCurrentProgress(MissionEvaluationContext context)
        {
            if (watchedNpcs == null) return 0;

            int count = 0;
            foreach (var npcData in watchedNpcs)
            {
                if (context.Npcs.TryGetValue(npcData, out var state) && state.CurrentLocation != watchedLocation)
                    count++;
            }
            return count;
        }
    }
}
