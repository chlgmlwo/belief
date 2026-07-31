using System.Collections.Generic;
using Belief.Data;
using Belief.Domain;
using Belief.Events;
using Belief.Systems.Movement;

namespace Belief.Systems
{
    /// <summary>
    /// 상태를 바꾸는 지점을 두 갈래로 한정한다: 1) 저작된 NpcActionData의 Effect 실행(Apply),
    /// 2) 특정 Action 에셋에 묶이지 않는 규칙 기반 이동(MoveNpc, 예: Minor NPC 배회).
    /// 둘 다 이 클래스를 거쳐야 하고, Belief/Memory는 건드리지 않는다.
    /// </summary>
    public class ActionResolutionSystem
    {
        readonly IReadOnlyDictionary<LocationData, LocationState> locations;
        readonly IGameEventBus eventBus;

        public ActionResolutionSystem(IReadOnlyDictionary<LocationData, LocationState> locations, IGameEventBus eventBus)
        {
            this.locations = locations;
            this.eventBus = eventBus;
        }

        public void Apply(NpcActionData action, NpcState actor, InformationCardData judgedCard, LocationState currentLocation, int currentTurn)
        {
            if (action == null) return;

            actor.SetCurrentAction(action);
            if (action.effect == null) return;

            var context = new ActionEffectContext(locations, eventBus, currentTurn, judgedCard, currentLocation);
            action.effect.Apply(actor, context);
        }

        public void MoveNpc(NpcState actor, LocationData destination)
        {
            if (destination == null) return;

            var from = actor.CurrentLocation;
            NpcMovementService.MoveTo(actor, destination, locations);
            eventBus.Publish(new NpcRelocatedEvent(actor.Data, from, destination));
        }
    }
}
