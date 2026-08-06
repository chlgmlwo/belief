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

        /// <summary>이 기록이 마지막으로 생성·갱신된 시점의 WorldChangeClock 값. ActivatedTurn은
        /// 미션마다 1로 리셋되는 미션 로컬 턴이라 미션 경계를 넘는 비교에 쓸 수 없어서 따로 둔다.</summary>
        public long LastChangedStamp { get; private set; }

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
            LastChangedStamp = WorldChangeClock.Next();
        }

        public void Refresh(int turn)
        {
            IsActive = true;
            ActivatedTurn = turn;
            LastChangedStamp = WorldChangeClock.Next();
        }

        /// <summary>오토세이브에서 되살릴 때만 쓰는 생성 경로 - <see cref="RumorState.Restore"/>와 같은 이유다.
        /// IsActive/LastChangedStamp가 비공개 setter라 저장본의 값을 바깥에서 넣을 방법이 없어서 둔다.
        /// 새 기록을 만드는 데는 쓰지 말 것.</summary>
        public static InformationWorldState Restore(InformationData information, string categoryId,
            LocationData location, NpcData actor, InformationResultType resultType, int activatedTurn,
            bool isActive, long lastChangedStamp)
        {
            var s = new InformationWorldState(information, categoryId, location, actor, resultType, activatedTurn);
            s.IsActive = isActive;
            s.LastChangedStamp = lastChangedStamp;
            return s;
        }

        /// <summary>스냅샷 보관·복원용 사본. class라 리스트만 새로 만들면 원본과 같은 인스턴스를
        /// 공유해서, 이후 Refresh가 스냅샷 내용까지 함께 바꿔 버린다.</summary>
        public InformationWorldState Clone()
        {
            var copy = new InformationWorldState(Information, CategoryId, Location, Actor, ResultType, ActivatedTurn);
            copy.IsActive = IsActive;
            copy.LastChangedStamp = LastChangedStamp; // 복제는 세계의 변화가 아니므로 원본 값을 그대로 물려준다.
            return copy;
        }
    }
}
