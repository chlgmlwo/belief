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

        /// <summary>
        /// <b>이 효과는 순전히 관찰용이라 실패해도 뒤 효과를 막아서는 안 된다.</b>
        ///
        /// CompositeEffect는 배열 순서대로 실행하는데, 여러 행동에서 이 로그 효과가 첫 번째에 있고
        /// 그 뒤에 조사 기록·기억 생성 같은 <b>도메인 핵심 효과</b>가 온다. GameEventBus는 멀티캐스트
        /// 델리게이트라 WorldEventOccurred 구독자가 예외를 던지면 그것이 여기까지 전파되어
        /// 뒤 효과들이 통째로 실행되지 않는다(예: Verify가 기억을 남기지 못한다).
        ///
        /// 그래서 발행만 감싸 막는다 - 조용히 삼키지 않고 Console에 남긴다.
        /// </summary>
        public override void Apply(NpcState actor, ActionEffectContext context)
        {
            string resolved = !string.IsNullOrEmpty(message) && actor.Data != null
                ? message.Replace("{name}", actor.Data.displayName)
                : message;

            try { context.EventBus.Publish(new WorldEventOccurred(resolved)); }
            catch (System.Exception ex)
            {
                Debug.LogError($"[LogWorldEventEffect] 월드 로그 발행 중 예외가 발생했습니다(뒤 효과는 계속 실행됩니다): {ex}");
            }
        }
    }
}
