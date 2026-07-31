using UnityEngine;
using Belief.Domain;
using Belief.Events;

namespace Belief.Data
{
    [CreateAssetMenu(fileName = "Effect_LogWorldEvent", menuName = "Belief/Actions/Log World Event Effect")]
    public class LogWorldEventEffect : NpcActionEffect
    {
        [Tooltip("{name} 토큰이 있으면 actor.Data.displayName으로 치환된다 - NPC마다 별도 에셋을 만들지 않고도 공용(Generic) 메시지를 실제 이름으로 표시하기 위함.")]
        public string message;

        public override void Apply(NpcState actor, ActionEffectContext context)
        {
            string resolved = !string.IsNullOrEmpty(message) && actor.Data != null
                ? message.Replace("{name}", actor.Data.displayName)
                : message;
            context.EventBus.Publish(new WorldEventOccurred(resolved));
        }
    }
}
