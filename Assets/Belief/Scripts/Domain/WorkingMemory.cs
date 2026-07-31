using System;
using System.Collections.Generic;

namespace Belief.Domain
{
    /// <summary>
    /// MemorySelector만 생성한다. BeliefSystem/IMajorNpcThinker는 읽기만 하며,
    /// 어느 시스템도 이 값을 수정하지 않는다. LongMemory 전체가 아니라
    /// 이번 판단에 관련 있는 최대 소수 항목만 담는다(슬롯 고정 없음).
    /// </summary>
    public class WorkingMemory
    {
        public IReadOnlyList<MemoryEntry> Entries { get; }
        public bool IsEmpty => Entries.Count == 0;

        public WorkingMemory(IReadOnlyList<MemoryEntry> entries)
        {
            Entries = entries;
        }

        public static WorkingMemory Empty { get; } = new WorkingMemory(Array.Empty<MemoryEntry>());
    }
}
