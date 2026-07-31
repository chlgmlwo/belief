using UnityEngine;
using Belief.Domain;

namespace Belief.Data
{
    /// <summary>특정 NPC의 조사/감시 행동이 특정 장소에 남긴 InformationWorldState를 검사한다.
    /// requiredCategoryId가 null이면 카테고리 무관, 채우면 그 카테고리의 정보로 유발된 것만 인정한다.</summary>
    [CreateAssetMenu(fileName = "Condition_InformationWorldState", menuName = "Belief/Missions/Information World State Condition")]
    public class InformationWorldStateCondition : MissionConditionData
    {
        public LocationData targetLocation;
        public NpcData requiredActor;
        public InformationResultType requiredResultType;
        public string requiredCategoryId;

        public override int GetCurrentProgress(MissionEvaluationContext context)
        {
            if (targetLocation == null || !context.Locations.TryGetValue(targetLocation, out var loc)) return 0;

            foreach (var s in loc.InvestigationStates)
            {
                if (!s.IsActive) continue;
                if (s.Actor != requiredActor) continue;
                if (s.ResultType != requiredResultType) continue;
                if (requiredCategoryId != null && s.CategoryId != requiredCategoryId) continue;
                return TargetCount;
            }
            return 0;
        }
    }
}
