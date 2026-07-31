using System.Collections.Generic;
using Belief.Data;
using Belief.Domain;
using Belief.Events;

namespace Belief.Systems
{
    /// <summary>스테이지 승리(GameOverEvent(true)) 확정 이후에만 실행되는 순수 결과 연출 -
    /// ProgressionController/MissionSystem의 판정에는 전혀 관여하지 않고 그 결과만 관찰한다.
    /// 최종 스테이지 승리 분기(ReevaluateCurrentStage)는 GameOverEvent(true)를 발행하기 전에 이미
    /// TurnEndedEvent 구독을 해제한다 - 이 시스템이 그 직후 Belief를 바꿔도 다시 판정 루프를
    /// 유발할 수 없으므로, "정체 발각으로 인한 즉시실패"와 이 결과 연출은 서로 절대 충돌하지 않는다.</summary>
    public class StageFinalResultSystem
    {
        public StageFinalResultSystem(StageFinalResultData data, IReadOnlyDictionary<NpcData, NpcState> npcs, IGameEventBus eventBus)
        {
            if (data == null || data.targetNpc == null) return;

            eventBus.Subscribe<GameOverEvent>(e =>
            {
                if (!e.Won) return;
                if (!npcs.TryGetValue(data.targetNpc, out var npc)) return;
                if (data.denialCards == null) return;

                foreach (var card in data.denialCards)
                    if (card != null) npc.SetBelief(card, BeliefState.Denied);
            });
        }
    }
}
