using Belief.Data;

namespace Belief.Domain
{
    /// <summary>실제로 확산된 정보가 한 장소에 남기는 결과 상태. 카드가 아니라 InformationData
    /// 단위로 식별해, 같은 정보를 나르는 다른 카드(다른 출처)도 동일한 소문으로 인정한다.
    /// PropagatedBy가 null이면 정보원(플레이어 SPREAD)을 통한 확산, NpcData가 있으면
    /// 그 NPC 자신의 확산 행동(Escalate 등)으로 생성된 것이다.</summary>
    public class RumorState
    {
        public readonly InformationData Information;
        public readonly InformationCardData SourceCard;
        public readonly LocationData Location;
        public readonly NpcData PropagatedBy;
        public int ActivatedTurn { get; private set; }
        public bool IsActive { get; private set; }

        public RumorState(InformationData information, InformationCardData sourceCard, LocationData location,
            NpcData propagatedBy, int activatedTurn)
        {
            Information = information;
            SourceCard = sourceCard;
            Location = location;
            PropagatedBy = propagatedBy;
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
