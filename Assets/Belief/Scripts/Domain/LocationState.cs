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
    }
}
