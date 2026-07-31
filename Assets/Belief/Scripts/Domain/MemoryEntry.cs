namespace Belief.Domain
{
    /// <summary>
    /// Long Memory의 한 항목. Data 레이어 ScriptableObject를 직접 참조하지 않고
    /// ID 문자열만 들고 있어, 런타임 Data 변경/교체의 영향을 받지 않는다.
    /// 필요 시 BeliefSystem/MemorySelector가 Repository를 통해 ID로 조회한다.
    /// </summary>
    public class MemoryEntry
    {
        public string Description { get; }
        public int TurnRecorded { get; }
        public float Importance { get; }

        /// <summary>+1이면 믿음을 밀어올리는 방향, -1이면 끌어내리는 방향, 0이면 중립. 생성 시점에 카테고리에서 확정해서 담는다.</summary>
        public float Valence { get; }

        public string RelatedNpcId { get; }
        public string RelatedLocationId { get; }
        public string RelatedSourceId { get; }
        public string RelatedInformationCategoryId { get; }

        /// <summary>비어있지 않으면 핵심 기억(배신/생명의 은인 등) 후보임을 나타낸다.</summary>
        public string MemoryCategoryId { get; }

        public bool IsCore => !string.IsNullOrEmpty(MemoryCategoryId);

        public MemoryEntry(
            string description,
            int turnRecorded,
            float importance,
            string relatedNpcId = null,
            string relatedLocationId = null,
            string relatedSourceId = null,
            string relatedInformationCategoryId = null,
            string memoryCategoryId = null,
            float valence = 0f)
        {
            Description = description;
            TurnRecorded = turnRecorded;
            Importance = importance;
            RelatedNpcId = relatedNpcId;
            RelatedLocationId = relatedLocationId;
            RelatedSourceId = relatedSourceId;
            RelatedInformationCategoryId = relatedInformationCategoryId;
            MemoryCategoryId = memoryCategoryId;
            Valence = valence;
        }
    }
}
