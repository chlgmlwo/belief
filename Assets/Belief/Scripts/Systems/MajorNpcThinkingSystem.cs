using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Belief.AI;
using Belief.Data;
using Belief.Debugging;
using Belief.Domain;
using Belief.Events;

namespace Belief.Systems
{
    /// <summary>
    /// Major NPC 판단 오케스트레이션: MemorySelector -> BeliefSystem -> IMajorNpcThinker ->
    /// ActionResolutionSystem 순서로 위임한다. 이 클래스 자체는 Belief를 계산하지 않고 읽기만 한다.
    /// </summary>
    public class MajorNpcThinkingSystem
    {
        readonly MemorySelector memorySelector;
        readonly BeliefSystem beliefSystem;
        readonly IMajorNpcThinker thinker;
        readonly ActionResolutionSystem actionResolution;
        readonly MemoryTuningData memoryTuning;
        readonly IGameEventBus eventBus;

        public MajorNpcThinkingSystem(
            MemorySelector memorySelector, BeliefSystem beliefSystem, IMajorNpcThinker thinker,
            ActionResolutionSystem actionResolution, MemoryTuningData memoryTuning, IGameEventBus eventBus)
        {
            this.memorySelector = memorySelector;
            this.beliefSystem = beliefSystem;
            this.thinker = thinker;
            this.actionResolution = actionResolution;
            this.memoryTuning = memoryTuning;
            this.eventBus = eventBus;
        }

        /// <summary>비동기(LLM 요청이 Timeout까지 여러 프레임에 걸쳐 대기할 수 있다) - Unity 메인
        /// 스레드를 절대 동기적으로 막지 않는다(GetAwaiter().GetResult()/.Result/.Wait() 없음).
        /// 이 메서드 안에서 await는 정확히 한 번(thinker.DecideAsync)뿐이라, 이 호출이 완료되기 전에
        /// 같은 NPC에 대해 이 메서드가 다시 시작될 일은 없다(InfoDeliverySystem이 한 카드 노출 안에서
        /// NPC별로 순차 await하므로 재진입 없음) - 중복 ActionResolution 걱정이 구조적으로 없다.
        /// 반환하는 BeliefState는 InfoDeliverySystem.Judge가 Major/Minor 공통으로 재확산 조건
        /// (TryReSpread)을 판정하는 데 쓰인다 - 이 메서드 자체는 재확산에 전혀 관여하지 않는다.</summary>
        public async Task<BeliefState> HandleExposureAsync(
            NpcState npc, InformationCardData card, LocationState where, int currentTurn,
            SpreadMechanicsSnapshot? spreadInfo = null)
        {
            if (!(npc.Data is MajorNpcData major)) return BeliefState.Unknown;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // 관찰 전용 - 실제 판단에 쓰이는 어떤 값도 여기서 계산하지 않는다(전부 아래 실제 판단
            // 코드가 만든 결과를 그대로 옮겨 담기만 한다). trace는 이 호출의 지역 변수이므로(예전의
            // static CurrentBuilder와 달리) 비동기 대기 중에도 다른 NPC의 판단과 섞일 수 없다.
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
                trace.WithSpreadMechanics(spreadInfo);
                trace.WithStateBefore(npc, card);
                if (where != null && where.Data != null)
                    trace.Record.LocSensitiveInfoType = where.Data.sensitiveInformationType.ToString();
            }
#endif

            var selectionContext = new MemorySelectionContext(card, where, currentTurn);
            var workingMemory = memorySelector.Select(npc, selectionContext, memoryTuning);

            var beliefResult = beliefSystem.Evaluate(npc, card, where, workingMemory, currentTurn);
            beliefSystem.Apply(npc, card, beliefResult);

            var candidates = major.availableActions ?? Array.Empty<NpcActionData>();
            var thinkContext = new NpcThinkContext(
                npc, card, beliefResult.FinalBelief, workingMemory, where, candidates, currentTurn);

            // trace를 메서드 인자로 명시적으로 전달한다(더 이상 static 공유 없음) - LlmMajorThinker는
            // LLM/Fallback 정보(J절)를 이 레코드에 직접 채우고, Publish는 항상 아래에서 이 메서드가 한다.
            object traceParam = null;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            traceParam = trace;
#endif
            var thinkResult = await thinker.DecideAsync(thinkContext, traceParam);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // Effect가 실제로 무엇을 남겼는지 관찰하기 위한 판단 전 스냅샷 - 여기서 아무 상태도 만들지 않는다.
            int memoryCountBeforeApply = npc.LongMemory.Count;
#endif

            if (thinkResult.ChosenAction != null)
                actionResolution.Apply(thinkResult.ChosenAction, npc, card, where, currentTurn);

            if (thinkResult.Dialogue != null)
                eventBus.Publish(new NpcSpokeEvent(npc.Data, thinkResult.Dialogue));

            eventBus.Publish(new CardJudgedEvent(npc.Data, card, beliefResult.FinalBelief, currentTurn));

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (trace != null)
            {
                trace.WithBeliefEvaluation(beliefResult);
                trace.WithBeliefChange(beliefBeforeForTrace, beliefResult.FinalBelief);
                trace.WithActionDecision(beliefResult.FinalBelief, candidates, thinkResult.ChosenAction);
                trace.WithDialogueDecision(beliefResult.FinalBelief, major.beliefDialogues, thinkResult.Dialogue);
                trace.WithGoal(goalBeforeForTrace, npc.CurrentGoal);

                // ActivatedTurn == currentTurn으로 생성/갱신(Refresh) 둘 다 잡는다 - Count 증감만으로는
                // Refresh(같은 항목 재사용, Count 불변)를 놓친다.
                var investigationMatch = where != null
                    ? where.InvestigationStates.FirstOrDefault(s =>
                        s.Information == card.information && s.Actor == npc.Data && s.ActivatedTurn == currentTurn)
                    : null;
                bool memoryAdded = npc.LongMemory.Count != memoryCountBeforeApply;
                trace.WithEffectResult(
                    thinkResult.ChosenAction, investigationMatch != null,
                    investigationMatch != null ? investigationMatch.ResultType.ToString() : null, memoryAdded);

                string finalDialogueText = thinkResult.Dialogue != null
                    ? (thinkResult.Dialogue.IsGenerated ? thinkResult.Dialogue.GeneratedText
                        : (thinkResult.Dialogue.PredefinedLine != null ? thinkResult.Dialogue.PredefinedLine.text : null))
                    : null;
                string positionLabel = npc.CurrentLocation != null ? npc.CurrentLocation.locationId : null;

                trace.WithFinalResolution(
                    finalIntent: NpcDecisionTraceBuilder.IntentMapping(beliefResult.FinalBelief).ToString(),
                    finalActionId: thinkResult.ChosenAction != null ? thinkResult.ChosenAction.actionId : null,
                    finalDialogue: finalDialogueText,
                    finalGoal: npc.CurrentGoal,
                    finalMoveDestinationId: null,
                    actionResolutionCount: thinkResult.ChosenAction != null ? 1 : 0,
                    positionBefore: positionLabel,
                    positionAfter: positionLabel);
                trace.Publish();
            }
#endif
            return beliefResult.FinalBelief;
        }
    }
}
