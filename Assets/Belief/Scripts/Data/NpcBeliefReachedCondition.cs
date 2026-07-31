using UnityEngine;

namespace Belief.Data
{
    /// <summary>특정 NPC의 특정 카드에 대한 BeliefState가 임계값에 도달했는지 검사한다.
    /// atOrBelow=true면 "그 단계 이하로 낮아짐"(예: 의심 방향), false면 "그 단계 이상으로 올라감"
    /// (예: 신뢰 방향). Unknown(아직 노출된 적 없음)은 어느 방향으로도 미충족으로 취급한다.</summary>
    [CreateAssetMenu(fileName = "Condition_NpcBeliefReached", menuName = "Belief/Missions/Npc Belief Reached Condition")]
    public class NpcBeliefReachedCondition : MissionConditionData
    {
        public NpcData targetNpc;
        public InformationCardData referenceCard;
        public BeliefState thresholdState;
        public bool atOrBelow = true;

        public override int GetCurrentProgress(MissionEvaluationContext context)
        {
            if (targetNpc == null || referenceCard == null) return 0;
            if (!context.Npcs.TryGetValue(targetNpc, out var state)) return 0;

            var current = state.GetBelief(referenceCard);
            if (current == BeliefState.Unknown) return 0;

            int currentRank = BeliefRank.Of(current);
            int thresholdRank = BeliefRank.Of(thresholdState);
            bool satisfied = atOrBelow ? currentRank <= thresholdRank : currentRank >= thresholdRank;
            return satisfied ? TargetCount : 0;
        }
    }
}
