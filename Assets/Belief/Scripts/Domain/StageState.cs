using System.Collections.Generic;
using Belief.Data;

namespace Belief.Domain
{
    /// <summary>
    /// 한 스테이지의 런타임 상태 묶음. StageData(정적)로부터 만들어진 Location/Npc 런타임 상태와
    /// 이 스테이지의 미션 목록, 턴 상태를 한 곳에서 참조할 수 있게 모아둔다. 이 클래스 자체는 참조를
    /// 모아두는 컨테이너일 뿐이며, 각 하위 상태(LocationState/NpcState/MissionState/TurnState)의
    /// 쓰기는 지금까지와 동일하게 각자의 소유 시스템(예: BeliefSystem, MissionSystem)이 전담한다.
    /// </summary>
    public class StageState
    {
        public StageData Data { get; }
        public IReadOnlyDictionary<LocationData, LocationState> Locations { get; }
        public IReadOnlyDictionary<NpcData, NpcState> Npcs { get; }
        public IReadOnlyList<MissionState> Missions { get; }
        public TurnState Turn { get; }

        public StageState(
            StageData data,
            IReadOnlyDictionary<LocationData, LocationState> locations,
            IReadOnlyDictionary<NpcData, NpcState> npcs,
            IReadOnlyList<MissionState> missions,
            TurnState turn)
        {
            Data = data;
            Locations = locations;
            Npcs = npcs;
            Missions = missions;
            Turn = turn;
        }
    }
}
