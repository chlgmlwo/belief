using System.Collections.Generic;
using Belief.Data.NPC;

namespace Belief.Domain.NPC
{
    /// <summary>게임 중 실제로 변경되는 유일한 NPC 상태 객체. Profile은 읽기 전용 원본을 그대로
    /// 참조하고(아무도 쓰지 않으므로 복사 불필요), RuntimeInitial에서 온 나머지 컬렉션은 전부
    /// 깊은 복사로 생성해 원본 DTO를 오염시키지 않는다. 이름은 기획 스펙이 지정한 그대로 NpcState를
    /// 쓴다 - Belief.Domain(네임스페이스 다름)의 기존 NpcState(카드 기반 Belief 시스템의 NPC 상태)와는
    /// 별개의 타입이다. 이 JSON 기반 NPC 파이프라인은 기존 ScriptableObject 기반 Zone NPC 시스템과
    /// 아직 연결되어 있지 않다.</summary>
    public class NpcState
    {
        public string NpcId;
        public NpcProfileDto Profile;
        public NpcRuntimeStatus RuntimeStatus;
        public NpcGoalState CurrentGoal;
        public NpcMoveState CurrentMove;
        public List<BeliefState> BeliefStates = new List<BeliefState>();
        public WorkingMemory WorkingMemory = new WorkingMemory();
        public List<RelationshipState> RelationshipStates = new List<RelationshipState>();
    }
}
