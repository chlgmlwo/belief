using UnityEngine;

namespace Belief.Data
{
    /// <summary>서브 조건 중 하나라도 자기 TargetCount를 만족하면 완료로 취급한다 - "clearLogic ANY"를
    /// MissionData.condition의 단일 참조 구조를 바꾸지 않고 표현한다.</summary>
    [CreateAssetMenu(fileName = "Condition_AnyOf", menuName = "Belief/Missions/Any Of Conditions")]
    public class AnyOfConditions : MissionConditionData
    {
        public MissionConditionData[] subConditions;

        public override int GetCurrentProgress(MissionEvaluationContext context)
        {
            if (subConditions == null) return 0;
            foreach (var sub in subConditions)
            {
                if (sub == null) continue;
                if (sub.GetCurrentProgress(context) >= sub.TargetCount) return TargetCount;
            }
            return 0;
        }
    }
}
