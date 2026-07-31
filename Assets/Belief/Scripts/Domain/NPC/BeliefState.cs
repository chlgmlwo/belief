using System.Collections.Generic;

namespace Belief.Domain.NPC
{
    /// <summary>NpcRuntimeDto.beliefStates 원소 하나의 런타임(가변) 사본. 이름은 기획 스펙이
    /// 지정한 그대로 BeliefState를 쓴다 - Belief.Domain(네임스페이스 다름)의 기존 BeliefState
    /// enum과는 별개의 타입이다(이 JSON 기반 NPC 시스템은 다른 값 체계(Trust/Possible/
    /// NeedVerification/Doubt/Reject)를 쓰는 별도 파이프라인이라 공용화하지 않았다).</summary>
    public class BeliefState
    {
        public string InformationId;
        public string CurrentLevel;
        public string PreviousLevel;
        public string SourceId;
        public List<string> EvidenceIds = new List<string>();
        public int LastUpdatedTurn;
        public bool IsInitialBelief;
    }
}
