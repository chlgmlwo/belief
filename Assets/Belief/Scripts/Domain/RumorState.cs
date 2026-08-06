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

        /// <summary>이 소문이 마지막으로 생성·갱신된 시점의 WorldChangeClock 값. ActivatedTurn은
        /// 미션마다 1로 리셋되는 미션 로컬 턴이라 미션 경계를 넘는 비교에 쓸 수 없어서 따로 둔다.</summary>
        public long LastChangedStamp { get; private set; }

        public RumorState(InformationData information, InformationCardData sourceCard, LocationData location,
            NpcData propagatedBy, int activatedTurn)
        {
            Information = information;
            SourceCard = sourceCard;
            Location = location;
            PropagatedBy = propagatedBy;
            ActivatedTurn = activatedTurn;
            IsActive = true;
            LastChangedStamp = WorldChangeClock.Next();
        }

        public void Refresh(int turn)
        {
            IsActive = true;
            ActivatedTurn = turn;
            LastChangedStamp = WorldChangeClock.Next();
        }

        /// <summary>스냅샷 보관·복원용 사본. RumorState는 class라 리스트만 새로 만들면 원본과 같은
        /// 인스턴스를 공유해서, 이후 Refresh가 스냅샷 내용까지 함께 바꿔 버린다.</summary>
        public RumorState Clone()
        {
            var copy = new RumorState(Information, SourceCard, Location, PropagatedBy, ActivatedTurn);
            copy.IsActive = IsActive;
            copy.LastChangedStamp = LastChangedStamp; // 복제는 세계의 변화가 아니므로 원본 값을 그대로 물려준다.
            return copy;
        }
    }
}
