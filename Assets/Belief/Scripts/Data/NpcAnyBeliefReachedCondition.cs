using UnityEngine;

namespace Belief.Data
{
    /// <summary>NpcBeliefReachedCondition은 특정 카드 한 장에 대한 belief만 검사해 그 카드 하나에
    /// 묶인다(카테고리 하나에 묶는 것보다도 더 좁음). 이 조건은 "어떤 카드(어떤 카테고리)로
    /// 도달했든 그 NPC가 실제로 내린 판단 결과"만 본다 - 지금까지 이 NPC가 받은 모든 카드의 belief를
    /// 훑어 하나라도 임계값을 만족하면 충족된다. atOrBelow=true(기본)면 "그 단계 이하로 낮아짐"
    /// (의심/거부 방향), false면 "그 단계 이상으로 올라감"(신뢰 방향). Unknown(아직 판단한 적 없음)은
    /// 어느 방향으로도 미충족으로 취급한다.</summary>
    [CreateAssetMenu(fileName = "Condition_NpcAnyBeliefReached", menuName = "Belief/Missions/Npc Any Belief Reached Condition")]
    public class NpcAnyBeliefReachedCondition : MissionConditionData
    {
        public NpcData targetNpc;
        public BeliefState thresholdState;
        public bool atOrBelow = true;

        public override int GetCurrentProgress(MissionEvaluationContext context)
        {
            if (targetNpc == null) return 0;
            if (!context.Npcs.TryGetValue(targetNpc, out var state)) return 0;

            int thresholdRank = BeliefRank.Of(thresholdState);
            foreach (var kvp in state.Beliefs)
            {
                if (kvp.Value == BeliefState.Unknown) continue;
                int currentRank = BeliefRank.Of(kvp.Value);
                bool satisfied = atOrBelow ? currentRank <= thresholdRank : currentRank >= thresholdRank;
                if (satisfied) return TargetCount;
            }
            return 0;
        }
    }
}
