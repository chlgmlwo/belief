using System.Collections.Generic;
using Belief.Data;
using Belief.Domain;
using Belief.Events;
using UnityEngine;

namespace Belief.Systems
{
    public readonly struct MinorExposureOutcome
    {
        public readonly BeliefState Belief;

        public MinorExposureOutcome(BeliefState belief)
        {
            Belief = belief;
        }
    }

    /// <summary>
    /// Minor NPC 전용 고정 규칙 행동 - 일반 시민 계층. 장기 목표/복잡한 관계망/심층 추론 없이
    /// 소문을 믿고, 간단히 이동하고, 정보를 전달(재확산)하는 역할만 한다. LLM 교체 대상이 아니다.
    /// BeliefSystem은 Major와 동일하게 재사용한다.
    /// </summary>
    public class MinorNpcBehaviorSystem
    {
        readonly BeliefSystem beliefSystem;
        readonly ActionResolutionSystem actionResolution;
        readonly IGameEventBus eventBus;

        public MinorNpcBehaviorSystem(BeliefSystem beliefSystem, ActionResolutionSystem actionResolution, IGameEventBus eventBus)
        {
            this.beliefSystem = beliefSystem;
            this.actionResolution = actionResolution;
            this.eventBus = eventBus;
        }

        public MinorExposureOutcome HandleExposure(NpcState npc, InformationCardData card, LocationState where, int currentTurn)
        {
            var beliefResult = beliefSystem.Evaluate(npc, card, where, WorkingMemory.Empty, currentTurn);
            beliefSystem.Apply(npc, card, beliefResult);

            eventBus.Publish(new CardJudgedEvent(npc.Data, card, beliefResult.FinalBelief, currentTurn));

            return new MinorExposureOutcome(beliefResult.FinalBelief);
        }

        public void MoveMinorNpcs(IEnumerable<NpcState> allNpcs)
        {
            foreach (var npc in new List<NpcState>(allNpcs))
            {
                if (npc.Data.Rank != NpcRank.Minor) continue;

                var current = npc.CurrentLocation;
                if (current == null || current.connectedLocations == null || current.connectedLocations.Length == 0) continue;

                float moveChance = Mathf.Min(0.15f + 0.15f * npc.ConvictionCount, 0.9f);
                if (Random.value > moveChance) continue;

                var dest = current.connectedLocations[Random.Range(0, current.connectedLocations.Length)];
                actionResolution.MoveNpc(npc, dest);
            }
        }
    }
}
