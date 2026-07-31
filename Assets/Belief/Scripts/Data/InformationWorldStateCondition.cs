using UnityEngine;
using Belief.Domain;

namespace Belief.Data
{
    /// <summary>특정 NPC의 조사/감시 행동이 특정 장소에 남긴 InformationWorldState를 검사한다.
    /// requiredCategoryId가 비어있으면(null 또는 "") 카테고리 무관, 채우면 그 카테고리의 정보로 유발된
    /// 것만 인정한다. Unity의 문자열 필드는 에셋 저장 시 null을 진짜 null로 보존하지 못하고 항상 ""로
    /// 직렬화하므로 null만 체크하면 카테고리 무관 설정이 저장 후 죽은 코드가 된다 - 반드시 IsNullOrEmpty로 검사한다.</summary>
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
                if (!string.IsNullOrEmpty(requiredCategoryId) && s.CategoryId != requiredCategoryId) continue;
                return TargetCount;
            }
            return 0;
        }
    }
}
