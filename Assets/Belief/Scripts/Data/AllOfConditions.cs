using UnityEngine;

namespace Belief.Data
{
    /// <summary>서브 조건 전부를 만족해야 자기 TargetCount를 만족한 것으로 취급한다 - AnyOfConditions(OR)의
    /// AND 대응물. MissionData.successConditions/condition의 단일 참조 구조를 바꾸지 않고 "동시에 두 곳에서"
    /// 같은 다중 조건 묶음을 표현한다.</summary>
    [CreateAssetMenu(fileName = "Condition_AllOf", menuName = "Belief/Missions/All Of Conditions")]
    public class AllOfConditions : MissionConditionData
    {
        public MissionConditionData[] subConditions;

        public override int GetCurrentProgress(MissionEvaluationContext context)
        {
            if (subConditions == null || subConditions.Length == 0) return 0;
            foreach (var sub in subConditions)
            {
                if (sub == null || sub.GetCurrentProgress(context) < sub.TargetCount) return 0;
            }
            return TargetCount;
        }
    }
}
