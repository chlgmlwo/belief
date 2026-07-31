using UnityEngine;
using Belief.Domain;

namespace Belief.Data
{
    /// <summary>특정 장소의 SiteState(Normal/Alert/Locked)가 원하는 값인지 검사한다.</summary>
    [CreateAssetMenu(fileName = "Condition_LocationState", menuName = "Belief/Missions/Location State Condition")]
    public class LocationStateCondition : MissionConditionData
    {
        public LocationData targetLocation;
        public LocationSiteState requiredState;

        public override int GetCurrentProgress(MissionEvaluationContext context) =>
            targetLocation != null &&
            context.Locations.TryGetValue(targetLocation, out var loc) &&
            loc.SiteState == requiredState
                ? TargetCount : 0;
    }
}
