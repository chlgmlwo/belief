using System.Collections.Generic;
using Belief.Data;

namespace Belief.Domain
{
    /// <summary>이산적 장소 상태. 게이지가 아니라 명시적 단계 전이로만 표현한다.</summary>
    public enum LocationSiteState
    {
        Normal,
        Alert,
        Locked
    }

    public class LocationState
    {
        public LocationData Data { get; }
        public List<NpcState> PresentNpcs { get; } = new List<NpcState>();
        public List<RumorState> ActiveRumors { get; } = new List<RumorState>();
        public List<InformationWorldState> InvestigationStates { get; } = new List<InformationWorldState>();

        /// <summary>쓰기는 SetLocationStateEffect(ActionResolutionSystem 경유) 전용.</summary>
        public LocationSiteState SiteState { get; set; } = LocationSiteState.Normal;

        public LocationState(LocationData data)
        {
            Data = data;
        }

        /// <summary>미션 시도 시작 시점의 가변 상태 스냅샷(RestartCurrentMission 복원용). PresentNpcs는
        /// 포함하지 않는다 - NpcState.CurrentLocation이 유일한 출처이고, 복원 후 TurnSystem이
        /// 그 값들로부터 모든 장소의 PresentNpcs를 다시 구성한다(중복 소유 상태 방지).
        ///
        /// RumorState/InformationWorldState는 <b>class</b>라 리스트만 새로 만드는 얕은 복사로는
        /// 스냅샷이 원본과 같은 인스턴스를 가리킨다 - 시도 중에 Refresh()가 기존 레코드를 변형하면
        /// 스냅샷 내용도 같이 바뀌어 복원해도 그 변형이 되돌아가지 않았다. 그래서 담을 때도 꺼낼 때도
        /// Clone()으로 끊어 준다.</summary>
        public readonly struct LocationStateSnapshot
        {
            public readonly List<RumorState> ActiveRumors;
            public readonly List<InformationWorldState> InvestigationStates;
            public readonly LocationSiteState SiteState;

            public LocationStateSnapshot(List<RumorState> activeRumors, List<InformationWorldState> investigationStates, LocationSiteState siteState)
            {
                ActiveRumors = new List<RumorState>(activeRumors.Count);
                foreach (var r in activeRumors) ActiveRumors.Add(r.Clone());

                InvestigationStates = new List<InformationWorldState>(investigationStates.Count);
                foreach (var s in investigationStates) InvestigationStates.Add(s.Clone());

                SiteState = siteState;
            }
        }

        public LocationStateSnapshot CaptureSnapshot() => new LocationStateSnapshot(ActiveRumors, InvestigationStates, SiteState);

        public void RestoreSnapshot(LocationStateSnapshot snapshot)
        {
            ActiveRumors.Clear();
            foreach (var r in snapshot.ActiveRumors) ActiveRumors.Add(r.Clone());

            InvestigationStates.Clear();
            foreach (var s in snapshot.InvestigationStates) InvestigationStates.Add(s.Clone());

            SiteState = snapshot.SiteState;
        }
    }
}
