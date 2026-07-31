using System.Collections.Generic;
using Belief.Data;
using Belief.Domain;
using Belief.Events;

namespace Belief.Systems
{
    /// <summary>
    /// LongMemory의 유일한 쓰기 지점. 명시적 생성(MemoryWorthyEventOccurred 구독)과
    /// 패턴 자동 감지(CardJudgedEvent 누적) 두 경로를 모두 지원한다. LLM은 이 클래스에
    /// 전혀 접근하지 않는다.
    /// </summary>
    public class MemorySystem
    {
        readonly Dictionary<NpcData, NpcState> npcLookup;
        readonly MemoryCategoryData repeatedLiesCategory;
        readonly int repeatedLieThreshold;

        readonly Dictionary<(string npcId, string sourceId), int> falseInfoStreak =
            new Dictionary<(string, string), int>();

        public MemorySystem(
            IGameEventBus bus,
            Dictionary<NpcData, NpcState> npcLookup,
            MemoryCategoryData repeatedLiesCategory,
            int repeatedLieThreshold = 3)
        {
            this.npcLookup = npcLookup;
            this.repeatedLiesCategory = repeatedLiesCategory;
            this.repeatedLieThreshold = repeatedLieThreshold;

            bus.Subscribe<MemoryWorthyEventOccurred>(OnMemoryWorthyEvent);
            bus.Subscribe<CardJudgedEvent>(OnCardJudged);
        }

        void OnMemoryWorthyEvent(MemoryWorthyEventOccurred e)
        {
            if (npcLookup.TryGetValue(e.Target, out var state))
                state.RecordMemory(e.Entry);
        }

        void OnCardJudged(CardJudgedEvent e)
        {
            if (e.Card.source == null || e.Card.information.isActuallyTrue || repeatedLiesCategory == null) return;

            var key = (e.Npc.npcId, e.Card.source.sourceId);
            falseInfoStreak.TryGetValue(key, out var streak);
            streak++;
            falseInfoStreak[key] = streak;

            if (streak != repeatedLieThreshold) return;
            if (!npcLookup.TryGetValue(e.Npc, out var state)) return;

            var entry = new MemoryEntry(
                description: $"{e.Card.source.displayName}에서 반복적으로 거짓 정보가 나옴",
                turnRecorded: e.Turn,
                importance: 0.8f,
                relatedSourceId: e.Card.source.sourceId,
                memoryCategoryId: repeatedLiesCategory.memoryCategoryId,
                valence: repeatedLiesCategory.valence);

            state.RecordMemory(entry);
        }
    }
}
