using Belief.Data;

namespace Belief.Domain
{
    /// <summary>RumorState(실제 확산)와 구별되는, NPC 자신의 조사/감시 행동이 남기는 결과 상태.
    /// Verify(조사)·Watch(감시)처럼 정보를 퍼뜨리지 않는 행동의 결과를 표현한다.</summary>
    public enum InformationResultType
    {
        Investigating,
        Monitoring,
        Propagating
    }

    public class InformationWorldState
    {
        public readonly InformationData Information;
        public readonly string CategoryId;
        public readonly LocationData Location;
        public readonly NpcData Actor;
        public readonly InformationResultType ResultType;
        public int ActivatedTurn { get; private set; }
        public bool IsActive { get; private set; }

        public InformationWorldState(InformationData information, string categoryId, LocationData location,
            NpcData actor, InformationResultType resultType, int activatedTurn)
        {
            Information = information;
            CategoryId = categoryId;
            Location = location;
            Actor = actor;
            ResultType = resultType;
            ActivatedTurn = activatedTurn;
            IsActive = true;
        }

        public void Refresh(int turn)
        {
            IsActive = true;
            ActivatedTurn = turn;
        }
    }
}
