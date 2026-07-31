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
        /// 그 값들로부터 모든 장소의 PresentNpcs를 다시 구성한다(중복 소유 상태 방지).</summary>
        public readonly struct LocationStateSnapshot
        {
            public readonly List<RumorState> ActiveRumors;
            public readonly List<InformationWorldState> InvestigationStates;
            public readonly LocationSiteState SiteState;

            public LocationStateSnapshot(List<RumorState> activeRumors, List<InformationWorldState> investigationStates, LocationSiteState siteState)
            {
                ActiveRumors = new List<RumorState>(activeRumors);
                InvestigationStates = new List<InformationWorldState>(investigationStates);
                SiteState = siteState;
            }
        }

        public LocationStateSnapshot CaptureSnapshot() => new LocationStateSnapshot(ActiveRumors, InvestigationStates, SiteState);

        public void RestoreSnapshot(LocationStateSnapshot snapshot)
        {
            ActiveRumors.Clear();
            ActiveRumors.AddRange(snapshot.ActiveRumors);

            InvestigationStates.Clear();
            InvestigationStates.AddRange(snapshot.InvestigationStates);

            SiteState = snapshot.SiteState;
        }
    }
}
