using System.Collections.Generic;

namespace Belief.Domain.NPC
{
    /// <summary>NpcRuntimeDto.workingMemory의 런타임(가변) 사본. 이름은 기획 스펙이 지정한 그대로
    /// WorkingMemory를 쓴다 - Belief.Systems의 기존 WorkingMemory(카드 판단용 기억 선택 결과)와는
    /// 별개의 타입이다.</summary>
    public class WorkingMemory
    {
        public List<string> KnownInformationIds = new List<string>();
        public List<string> RecentInformationIds = new List<string>();
        public List<string> RecentEventIds = new List<string>();
        public List<string> ObservedNpcIds = new List<string>();
    }
}
