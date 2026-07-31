using UnityEngine;
using Belief.Domain;
using Belief.Events;

namespace Belief.Data
{
    [CreateAssetMenu(fileName = "Effect_SetLocationState", menuName = "Belief/Actions/Set Location State Effect")]
    public class SetLocationStateEffect : NpcActionEffect
    {
        public LocationData targetLocation;
        public LocationSiteState newState;

        public override void Apply(NpcState actor, ActionEffectContext context)
        {
            if (targetLocation == null || !context.Locations.TryGetValue(targetLocation, out var state)) return;

            state.SiteState = newState;
            context.EventBus.Publish(new LocationStateChangedEvent(targetLocation, newState));
        }
    }
}
