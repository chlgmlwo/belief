using System.Collections.Generic;
using System.Linq;
using Belief.Data;
using Belief.Domain;

namespace Belief.Systems
{
    public readonly struct MemorySelectionContext
    {
        public readonly InformationCardData Card;
        public readonly LocationState CurrentLocation;
        public readonly int CurrentTurn;

        public MemorySelectionContext(InformationCardData card, LocationState currentLocation, int currentTurn)
        {
            Card = card;
            CurrentLocation = currentLocation;
            CurrentTurn = currentTurn;
        }
    }

    /// <summary>
    /// LongMemory를 검색해 이번 판단과 가장 관련 있는 기억을 최대 N개(고정 슬롯 없음) 골라
    /// WorkingMemory를 만든다. 이 클래스만 LongMemory를 읽고, 쓰지는 않는다.
    /// </summary>
    public class MemorySelector
    {
        public WorkingMemory Select(NpcState npc, MemorySelectionContext context, MemoryTuningData tuning)
        {
            if (npc.LongMemory.Count == 0) return WorkingMemory.Empty;

            string locationId = context.CurrentLocation?.Data.locationId;
            string sourceId = context.Card.source != null ? context.Card.source.sourceId : null;

            var scored = npc.LongMemory
                .Select(entry => (entry, score: Relevance(entry, locationId, sourceId, context.CurrentTurn, tuning)))
                .Where(t => t.score > 0f)
                .OrderByDescending(t => t.score)
                .Take(tuning.maxWorkingMemorySize)
                .Select(t => t.entry)
                .ToList();

            return scored.Count == 0 ? WorkingMemory.Empty : new WorkingMemory(scored);
        }

        static float Relevance(MemoryEntry entry, string locationId, string sourceId, int currentTurn, MemoryTuningData tuning)
        {
            bool matchesLocation = locationId != null && entry.RelatedLocationId == locationId;
            bool matchesSource = sourceId != null && entry.RelatedSourceId == sourceId;
            bool isRecent = (currentTurn - entry.TurnRecorded) <= tuning.recentWindowTurns;

            bool anySignal = matchesLocation || matchesSource || isRecent || entry.IsCore;
            if (!anySignal) return 0f;

            float score = 0f;
            if (matchesLocation) score += 1f;
            if (matchesSource) score += 1f;
            if (isRecent) score += 0.5f;
            if (entry.IsCore) score += 0.5f;
            score += entry.Importance * 0.5f;

            return score;
        }
    }
}
