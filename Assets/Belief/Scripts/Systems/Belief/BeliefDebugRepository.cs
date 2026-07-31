using System.Collections.Generic;
using Belief.Data;
using Belief.Domain;

namespace Belief.Systems
{
    /// <summary>Debug Overlay가 읽는 쪽. 쓰기 메서드가 없다 - 실제 기록은 BeliefDebugRepository(구현체)만 한다.</summary>
    public interface IBeliefDebugRepository
    {
        bool TryGetLastEvaluation(NpcState npc, InformationCardData card, out BeliefEvaluationResult result);
    }

    /// <summary>
    /// NpcState/UI에 디버그 전용 데이터를 심지 않기 위한 별도 저장소. BeliefSystem만 Record를 호출한다.
    /// Debug Overlay 등 읽기 전용 소비자는 IBeliefDebugRepository로만 접근한다.
    /// </summary>
    public class BeliefDebugRepository : IBeliefDebugRepository
    {
        readonly Dictionary<(NpcState npc, InformationCardData card), BeliefEvaluationResult> lastEvaluations =
            new Dictionary<(NpcState, InformationCardData), BeliefEvaluationResult>();

        public void Record(NpcState npc, InformationCardData card, BeliefEvaluationResult result) =>
            lastEvaluations[(npc, card)] = result;

        public bool TryGetLastEvaluation(NpcState npc, InformationCardData card, out BeliefEvaluationResult result) =>
            lastEvaluations.TryGetValue((npc, card), out result);
    }
}
