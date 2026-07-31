using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Belief.AI;
using Belief.Data;
using Belief.Domain;
using Belief.Events;
using UnityEngine;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using Belief.Debugging;
#endif

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
    /// Minor NPC 판단 - 일반 시민 계층. Belief 평가는 Major와 동일한 BeliefSystem을 재사용하고,
    /// Intent/Action/Effect도 공용 IMajorNpcThinker(RuleBased 또는 LLM/Fallback, GameInstaller가 만든
    /// 것과 동일한 인스턴스)를 그대로 재사용한다 - Minor 전용 Intent 매핑이나 Thinker를 새로 만들지
    /// 않는다("Major" 명칭은 역사적인 것일 뿐, RuleBasedMajorThinker.ChooseAction/LlmMajorThinker의
    /// PromptBuilder 모두 MajorNpcData 캐스팅 없이 이미 NPC 계급 무관하게 동작한다).
    /// 이동만은 여전히 이 클래스 자체의 확률적 배회(MoveMinorNpcs)로 남아있다 - MajorNpcMovementSystem에는
    /// 포함되지 않고, 이번 변경도 그 경로를 건드리지 않는다.
    /// </summary>
    public class MinorNpcBehaviorSystem
    {
        readonly BeliefSystem beliefSystem;
        readonly ActionResolutionSystem actionResolution;
        readonly IMajorNpcThinker thinker;
        readonly IGameEventBus eventBus;

        public MinorNpcBehaviorSystem(
            BeliefSystem beliefSystem, ActionResolutionSystem actionResolution, IMajorNpcThinker thinker, IGameEventBus eventBus)
        {
            this.beliefSystem = beliefSystem;
            this.actionResolution = actionResolution;
            this.thinker = thinker;
            this.eventBus = eventBus;
        }

        /// <summary>비동기인 이유는 MajorNpcThinkingSystem.HandleExposureAsync와 동일 - 공용
        /// thinker.DecideAsync가 LLM Timeout까지 대기할 수 있어서다(RuleOnly 모드에서는
        /// Task.FromResult로 즉시 완료되어 실질적 대기가 없다). 이 메서드 안에서 await는 정확히 한 번
        /// (thinker.DecideAsync)뿐이고, 유일한 호출자(InfoDeliverySystem.Judge)가 NPC별로 순차 await하므로
        /// 재진입이나 중복 ActionResolution 위험이 구조적으로 없다.</summary>
        public async Task<MinorExposureOutcome> HandleExposureAsync(
            NpcState npc, InformationCardData card, LocationState where, int currentTurn)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // 관찰 전용 - MajorNpcThinkingSystem.HandleExposureAsync와 동일한 패턴. trace는 지역 변수라
            // 비동기 대기 중에도 다른 NPC의 판단과 섞이지 않는다.
            NpcDecisionTraceBuilder trace = null;
            BeliefState beliefBeforeForTrace = BeliefState.Unknown;
            string goalBeforeForTrace = null;
            if (NpcDecisionTraceHub.HasListeners)
            {
                beliefBeforeForTrace = npc.GetBelief(card);
                goalBeforeForTrace = npc.CurrentGoal;
                trace = new NpcDecisionTraceBuilder(
                    "InformationJudgment", npc,
                    NpcDecisionTraceContext.StageId, NpcDecisionTraceContext.StageTurn,
                    NpcDecisionTraceContext.MissionId, NpcDecisionTraceContext.MissionTurn,
                    NpcDecisionTraceContext.ThinkerMode);
                trace.WithReceivedInformation(card);
                trace.WithStateBefore(npc, card);
                if (where != null && where.Data != null)
                    trace.Record.LocSensitiveInfoType = where.Data.sensitiveInformationType.ToString();
            }
#endif

            var beliefResult = beliefSystem.Evaluate(npc, card, where, WorkingMemory.Empty, currentTurn);
            beliefSystem.Apply(npc, card, beliefResult);

            var minor = npc.Data as MinorNpcData;
            IReadOnlyList<NpcActionData> candidates = minor != null && minor.availableActions != null
                ? minor.availableActions : System.Array.Empty<NpcActionData>();
            var thinkContext = new NpcThinkContext(
                npc, card, beliefResult.FinalBelief, WorkingMemory.Empty, where, candidates, currentTurn);

            object traceParam = null;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            traceParam = trace;
#endif
            var thinkResult = await thinker.DecideAsync(thinkContext, traceParam);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            int memoryCountBeforeApply = npc.LongMemory.Count;
#endif

            if (thinkResult.ChosenAction != null)
                actionResolution.Apply(thinkResult.ChosenAction, npc, card, where, currentTurn);

            eventBus.Publish(new CardJudgedEvent(npc.Data, card, beliefResult.FinalBelief, currentTurn));

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (trace != null)
            {
                trace.WithBeliefEvaluation(beliefResult);
                trace.WithBeliefChange(beliefBeforeForTrace, beliefResult.FinalBelief);
                trace.WithActionDecision(beliefResult.FinalBelief, candidates, thinkResult.ChosenAction);
                trace.WithGoal(goalBeforeForTrace, npc.CurrentGoal);

                // ActivatedTurn == currentTurn으로 생성/갱신(Refresh) 둘 다 잡는다 - MajorNpcThinkingSystem과 동일 규칙.
                var investigationMatch = where != null
                    ? where.InvestigationStates.FirstOrDefault(s =>
                        s.Information == card.information && s.Actor == npc.Data && s.ActivatedTurn == currentTurn)
                    : null;
                bool memoryAdded = npc.LongMemory.Count != memoryCountBeforeApply;
                trace.WithEffectResult(
                    thinkResult.ChosenAction, investigationMatch != null,
                    investigationMatch != null ? investigationMatch.ResultType.ToString() : null, memoryAdded);

                string positionLabel = npc.CurrentLocation != null ? npc.CurrentLocation.locationId : null;
                trace.WithFinalResolution(
                    finalIntent: NpcDecisionTraceBuilder.IntentMapping(beliefResult.FinalBelief).ToString(),
                    finalActionId: thinkResult.ChosenAction != null ? thinkResult.ChosenAction.actionId : null,
                    finalDialogue: null,
                    finalGoal: npc.CurrentGoal,
                    finalMoveDestinationId: null,
                    actionResolutionCount: thinkResult.ChosenAction != null ? 1 : 0,
                    positionBefore: positionLabel,
                    positionAfter: positionLabel);
                trace.Publish();
            }
#endif

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
