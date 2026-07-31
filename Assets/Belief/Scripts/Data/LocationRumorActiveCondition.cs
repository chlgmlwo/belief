using UnityEngine;

namespace Belief.Data
{
    /// <summary>특정 장소에 특정 정보가 "실제로 확산된 상태"(RumorState)로 남아있는지 검사한다.
    /// requiredInformation/requiredCategoryId 중 하나만 채운다(정확히 이 정보만 인정할지, 이
    /// 카테고리에 속한 정보라면 무엇이든 인정할지). requiredPropagator가 null이면 정보원(플레이어
    /// SPREAD) 확산도 인정하고, NPC를 지정하면 그 NPC 자신이 확산시킨 경우만 인정한다.</summary>
    [CreateAssetMenu(fileName = "Condition_LocationRumorActive", menuName = "Belief/Missions/Location Rumor Active Condition")]
    public class LocationRumorActiveCondition : MissionConditionData
    {
        public LocationData targetLocation;
        public InformationData requiredInformation;
        public string requiredCategoryId;
        public NpcData requiredPropagator;

        public override int GetCurrentProgress(MissionEvaluationContext context)
        {
            if (targetLocation == null || !context.Locations.TryGetValue(targetLocation, out var loc)) return 0;

            foreach (var r in loc.ActiveRumors)
            {
                if (!r.IsActive) continue;
                if (r.PropagatedBy != requiredPropagator) continue;
                if (requiredInformation != null && r.Information != requiredInformation) continue;
                // Unity는 string 필드의 "미설정"을 저장 시 null이 아니라 항상 빈 문자열로 직렬화한다 -
                // null 비교만으로는 저장 후 와일드카드(카테고리 무관) 경로에 절대 도달할 수 없었다.
                if (!string.IsNullOrEmpty(requiredCategoryId) &&
                    (r.Information == null || r.Information.categoryId != requiredCategoryId)) continue;
                return TargetCount;
            }
            return 0;
        }
    }
}
