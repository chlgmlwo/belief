using UnityEngine;
using Belief.Domain;

namespace Belief.Data
{
    /// <summary>여러 NpcActionEffect를 순서대로 실행한다. NpcActionData.effect는 단일 참조라,
    /// "기존 로그 이펙트는 유지하고 새 월드 상태 이펙트를 추가"하는 경우에 이 컨테이너로 감싼다.</summary>
    [CreateAssetMenu(fileName = "Effect_Composite", menuName = "Belief/Actions/Composite Effect")]
    public class CompositeEffect : NpcActionEffect
    {
        public NpcActionEffect[] effects;

        public override void Apply(NpcState actor, ActionEffectContext context)
        {
            if (effects == null) return;
            foreach (var effect in effects)
                effect?.Apply(actor, context);
        }
    }
}
