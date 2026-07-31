using UnityEngine;

namespace Belief.Data
{
    public enum MissionClearMode { Any, All }

    [CreateAssetMenu(fileName = "Mission_", menuName = "Belief/Mission", order = 9)]
    public class MissionData : ScriptableObject
    {
        public string missionId;
        public string displayTitle;
        [TextArea(2, 4)] public string objectiveText;

        /// <summary>이 미션이 활성화될 때 적용할 최대 턴 수. 0 이하면 지정하지 않은 것으로 보고
        /// 씬(구역)의 기존 기본값을 그대로 재사용한다 - 모든 미션에 값을 채울 필요는 없다.</summary>
        public int turnLimit = 0;

        public MissionClearMode clearMode = MissionClearMode.All;
        public MissionConditionData[] successConditions;

        /// <summary>비어 있으면 이 미션 고유의 실패 조건은 없다(씬 레벨 GameInstaller.instantFailCondition과는
        /// 별개 - 그쪽은 항상 검사되고, 이쪽은 이 미션이 현재 목표일 때만 추가로 검사된다).</summary>
        public MissionConditionData[] failureConditions;

        public string nextMissionId;
        public string nextMissionTitle;

        /// <summary>다음 미션 예고 UI에서 이 미션의 실제 제목 대신 잠금 표시를 보여줄지 여부.</summary>
        public bool isHiddenUntilUnlocked = true;

        /// <summary>성공 조건 집계 - clearMode에 따라 Any(하나 이상 충족) 또는 All(전부 충족)일 때만
        /// SuccessTarget을 반환한다. MissionSystem/ProgressionController가 이 메서드 하나만 호출하도록 해
        /// 집계 규칙이 두 곳에서 따로 구현되며 갈라지는 것을 막는다.</summary>
        public int GetSuccessProgress(MissionEvaluationContext context)
        {
            if (successConditions == null || successConditions.Length == 0) return 0;

            if (clearMode == MissionClearMode.Any)
            {
                foreach (var c in successConditions)
                    if (c != null && c.GetCurrentProgress(context) >= c.TargetCount)
                        return SuccessTarget;
                return 0;
            }

            foreach (var c in successConditions)
                if (c == null || c.GetCurrentProgress(context) < c.TargetCount)
                    return 0;
            return SuccessTarget;
        }

        public int SuccessTarget => 1;

        /// <summary>failureConditions 중 하나라도 충족되면 true(OR 판정) - 비어 있으면 항상 false.</summary>
        public bool IsAnyFailureConditionMet(MissionEvaluationContext context)
        {
            if (failureConditions == null) return false;
            foreach (var c in failureConditions)
                if (c != null && c.GetCurrentProgress(context) >= c.TargetCount)
                    return true;
            return false;
        }
    }
}
