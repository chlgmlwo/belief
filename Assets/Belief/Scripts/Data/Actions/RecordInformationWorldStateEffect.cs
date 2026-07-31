using UnityEngine;
using Belief.Domain;

namespace Belief.Data
{
    /// <summary>조사(Verify)·감시(Watch)처럼 정보를 퍼뜨리지 않는 행동이 남기는 결과 상태.
    /// AddActiveRumorEffect(실제 확산)와 의미가 다르므로 별도 리스트(InvestigationStates)에 쓴다.
    /// 카드/NPC는 컨텍스트에서 읽고, resultType만 에셋에 저작한다.</summary>
    [CreateAssetMenu(fileName = "Effect_RecordInformationWorldState", menuName = "Belief/Actions/Record Information World State Effect")]
    public class RecordInformationWorldStateEffect : NpcActionEffect
    {
        public InformationResultType resultType;

        [Tooltip("비워두면 행동 주체(actor)의 현재 위치를 사용한다.")]
        public LocationData overrideLocation;

        public override void Apply(NpcState actor, ActionEffectContext context)
        {
            if (context.JudgedCard == null) return;

            var location = overrideLocation != null ? overrideLocation : actor.CurrentLocation;
            if (location == null || !context.Locations.TryGetValue(location, out var locState)) return;

            var information = context.JudgedCard.information;
            var categoryId = information != null ? information.categoryId : null;

            foreach (var s in locState.InvestigationStates)
            {
                if (s.Information == information && s.Actor == actor.Data && s.ResultType == resultType)
                {
                    s.Refresh(context.CurrentTurn);
                    return;
                }
            }
            locState.InvestigationStates.Add(new InformationWorldState(information, categoryId, location, actor.Data, resultType, context.CurrentTurn));
        }
    }
}
