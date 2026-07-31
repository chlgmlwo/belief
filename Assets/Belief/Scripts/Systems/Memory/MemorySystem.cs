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

        /// <summary>미션 시도 시작 시점의 반복 거짓말 스트릭 카운터 스냅샷(RestartCurrentMission 복원용).
        /// 이 카운터는 NpcState에 속하지 않는 MemorySystem 전용 상태라 별도로 캡처/복원해야 한다 -
        /// 그렇지 않으면 실패한 시도에서 쌓인 스트릭이 재시도에 그대로 이어져, NpcState.LongMemory는
        /// 되돌아갔는데도 "반복 거짓말" 기억이 실제보다 더 적은 카드 만에 조기 발생할 수 있다.</summary>
        public readonly struct StreakSnapshot
        {
            public readonly Dictionary<(string npcId, string sourceId), int> Streaks;

            public StreakSnapshot(Dictionary<(string npcId, string sourceId), int> streaks)
            {
                Streaks = new Dictionary<(string, string), int>(streaks);
            }
        }

        public StreakSnapshot CaptureSnapshot() => new StreakSnapshot(falseInfoStreak);

        public void RestoreSnapshot(StreakSnapshot snapshot)
        {
            falseInfoStreak.Clear();
            foreach (var kv in snapshot.Streaks) falseInfoStreak[kv.Key] = kv.Value;
        }
    }
}
