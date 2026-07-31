using Belief.Domain;
using Belief.Events;
using UnityEngine;

namespace Belief.Data
{
    /// <summary>명시적 생성 경로(생명의 은인/중요한 약속/스토리 이벤트 등). MemorySystem에 통보만 하고
    /// LongMemory에 직접 쓰지 않는다 - 쓰기는 MemorySystem 전용.</summary>
    [CreateAssetMenu(fileName = "Effect_RecordMemory", menuName = "Belief/Actions/Record Memory Effect")]
    public class RecordMemoryEffect : NpcActionEffect
    {
        public string description;
        [Range(0f, 1f)] public float importance = 0.7f;
        public MemoryCategoryData category;
        public NpcData relatedNpc;
        public LocationData relatedLocation;
        public InfoSourceData relatedSource;

        public override void Apply(NpcState actor, ActionEffectContext context)
        {
            var entry = new MemoryEntry(
                description: description,
                turnRecorded: context.CurrentTurn,
                importance: importance,
                relatedNpcId: relatedNpc != null ? relatedNpc.npcId : null,
                relatedLocationId: relatedLocation != null ? relatedLocation.locationId : null,
                relatedSourceId: relatedSource != null ? relatedSource.sourceId : null,
                memoryCategoryId: category != null ? category.memoryCategoryId : null,
                valence: category != null ? category.valence : 0f);

            context.EventBus.Publish(new MemoryWorthyEventOccurred(actor.Data, entry));
        }
    }
}
