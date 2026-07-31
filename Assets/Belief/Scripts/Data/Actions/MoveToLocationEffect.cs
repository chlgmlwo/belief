using UnityEngine;
using Belief.Domain;
using Belief.Events;
using Belief.Systems.Movement;

namespace Belief.Data
{
    [CreateAssetMenu(fileName = "Effect_MoveToLocation", menuName = "Belief/Actions/Move To Location Effect")]
    public class MoveToLocationEffect : NpcActionEffect
    {
        public LocationData destination;

        public override void Apply(NpcState actor, ActionEffectContext context)
        {
            if (destination == null) return;

            var from = actor.CurrentLocation;
            NpcMovementService.MoveTo(actor, destination, context.Locations);
            context.EventBus.Publish(new NpcRelocatedEvent(actor.Data, from, destination));
        }
    }
}
