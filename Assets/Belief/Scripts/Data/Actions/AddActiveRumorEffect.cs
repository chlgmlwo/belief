using UnityEngine;
using Belief.Domain;

namespace Belief.Data
{
    /// <summary>실제로 정보를 확산시키는 행동(Escalate 등) 전용. 지금 판단 중이던 카드/현재 위치를
    /// 컨텍스트에서 읽어 그 장소에 RumorState를 남긴다 - 카드/NPC를 에셋에 하드코딩하지 않는다.
    /// 조사(Verify)·감시(Watch)처럼 실제로 퍼뜨리지 않는 행동에는 쓰지 않는다
    /// (그 경우는 RecordInformationWorldStateEffect를 쓴다).</summary>
    [CreateAssetMenu(fileName = "Effect_AddActiveRumor", menuName = "Belief/Actions/Add Active Rumor Effect")]
    public class AddActiveRumorEffect : NpcActionEffect
    {
        [Tooltip("비워두면 행동 주체(actor)의 현재 위치를 사용한다.")]
        public LocationData overrideLocation;

        public override void Apply(NpcState actor, ActionEffectContext context)
        {
            if (context.JudgedCard == null) return;

            var location = overrideLocation != null ? overrideLocation : actor.CurrentLocation;
            if (location == null || !context.Locations.TryGetValue(location, out var locState)) return;

            var information = context.JudgedCard.information;
            foreach (var r in locState.ActiveRumors)
            {
                if (r.Information == information && r.PropagatedBy == actor.Data)
                {
                    r.Refresh(context.CurrentTurn);
                    return;
                }
            }
            locState.ActiveRumors.Add(new RumorState(information, context.JudgedCard, location, actor.Data, context.CurrentTurn));
        }
    }
}
