using System.Collections.Generic;
using Belief.Data;
using Belief.Domain;

namespace Belief.Systems.Movement
{
    /// <summary>
    /// 순수 상태 이동 로직(행동 로직이라 Systems 소속 - Domain은 상태 객체만 둔다).
    /// 이벤트 발행은 호출자 책임. MoveToLocationEffect와 ActionResolutionSystem.MoveNpc가
    /// 공유해서 이동 로직 중복을 없앤다.
    /// </summary>
    public static class NpcMovementService
    {
        public static void MoveTo(NpcState actor, LocationData destination, IReadOnlyDictionary<LocationData, LocationState> locations)
        {
            var from = actor.CurrentLocation;
            if (from != null && locations.TryGetValue(from, out var fromState))
                fromState.PresentNpcs.Remove(actor);

            actor.CurrentLocation = destination;

            if (locations.TryGetValue(destination, out var toState))
                toState.PresentNpcs.Add(actor);
        }
    }
}
