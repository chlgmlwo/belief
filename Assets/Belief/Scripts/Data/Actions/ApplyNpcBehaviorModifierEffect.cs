using UnityEngine;
using Belief.Domain;
using Belief.Events;

namespace Belief.Data
{
    [CreateAssetMenu(fileName = "Effect_ApplyNpcBehaviorModifier", menuName = "Belief/Actions/Apply Npc Behavior Modifier Effect")]
    public class ApplyNpcBehaviorModifierEffect : NpcActionEffect
    {
        public string behaviorModifierId;

        public override void Apply(NpcState actor, ActionEffectContext context)
        {
            actor.SetBehaviorModifier(behaviorModifierId);
            context.EventBus.Publish(new NpcBehaviorModifierChangedEvent(actor.Data, behaviorModifierId));
        }
    }
}
