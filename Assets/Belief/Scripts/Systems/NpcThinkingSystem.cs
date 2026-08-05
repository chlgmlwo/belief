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
    public class NpcThinkingSystem
    {
        readonly MemorySelector memorySelector;
        readonly BeliefSystem beliefSystem;
        readonly IMajorNpcThinker thinker;
        readonly ActionResolutionSystem actionResolution;
        readonly MemoryTuningData memoryTuning;
        readonly IGameEventBus eventBus;

        /// <summary>Shadow Mode가 꺼져 있으면 null. 관찰 전용이라 이 값이 null이든 아니든
        /// 아래 판단 로직은 한 줄도 달라지지 않는다.</summary>
        readonly ShadowJudgmentSystem shadow;

        public NpcThinkingSystem(
            MemorySelector memorySelector, BeliefSystem beliefSystem, IMajorNpcThinker thinker,
            ActionResolutionSystem actionResolution, MemoryTuningData memoryTuning, IGameEventBus eventBus,
            ShadowJudgmentSystem shadow = null)
        {
            this.memorySelector = memorySelector;
            this.beliefSystem = beliefSystem;
            this.thinker = thinker;
            this.actionResolution = actionResolution;
            this.memoryTuning = memoryTuning;
            this.eventBus = eventBus;
            this.shadow = shadow;
        }

        /// <summary>비동기(LLM 요청이 Timeout까지 여러 프레임에 걸쳐 대기할 수 있다) - Unity 메인
        /// 스레드를 절대 동기적으로 막지 않는다(GetAwaiter().GetResult()/.Result/.Wait() 없음).
        /// 이 메서드 안에서 await는 정확히 한 번(thinker.DecideAsync)뿐이라, 이 호출이 완료되기 전에
        /// 같은 NPC에 대해 이 메서드가 다시 시작될 일은 없다(InfoDeliverySystem이 한 카드 노출 안에서
        /// NPC별로 순차 await하므로 재진입 없음) - 중복 ActionResolution 걱정이 구조적으로 없다.
        /// 반환하는 BeliefState는 InfoDeliverySystem.Judge가 Major/Minor 공통으로 재확산 조건
        /// (TryReSpread)을 판정하는 데 쓰인다 - 이 메서드 자체는 재확산에 전혀 관여하지 않는다.</summary>
        /// <param name="propagator">이 정보를 실제로 전달한 NPC(재확산 경로에서만 존재). 플레이어가
        /// 정보원을 통해 직접 전달한 경우는 항상 null이며, 그때는 관계 근거를 만들지 않는다.</param>
        public async Task<BeliefState> HandleExposureAsync(
            NpcState npc, InformationCardData card, LocationState where, int currentTurn,
            SpreadMechanicsSnapshot? spreadInfo = null, NpcState propagator = null)
        {
            if (!(npc.Data is MajorNpcData major)) return BeliefState.Unknown;

            // 판단 전 상태 - 관찰용이 아니라 실제 게임 로직(이동 판단 대상 선별)이 읽는 값이라
            // #if로 감싸지 않는다. 예전에는 이 두 줄이 에디터 전용 + 리스너 있을 때만 실행됐는데,
            // 그러면 빌드에서는 "Belief가 바뀌었는지"를 알 방법이 아예 없다. 비용은 딕셔너리 조회
            // 1회 + 참조 복사 1회뿐이다.
            var beliefBefore = npc.GetBelief(card);
            string goalBefore = npc.CurrentGoal;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // 관찰 전용 - 실제 판단에 쓰이는 어떤 값도 여기서 계산하지 않는다(전부 아래 실제 판단
            // 코드가 만든 결과를 그대로 옮겨 담기만 한다). trace는 이 호출의 지역 변수이므로(예전의
            // static CurrentBuilder와 달리) 비동기 대기 중에도 다른 NPC의 판단과 섞일 수 없다.
            NpcDecisionTraceBuilder trace = null;
            if (NpcDecisionTraceHub.HasListeners)
            {
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

            // 이번 턴 이동 판단에서 LLM을 쓸 NPC를 고르는 유일한 근거. "정보를 받았다"가 아니라
            // "받은 결과 판단이 실제로 달라졌다"만 대상으로 삼는다 - 같은 값으로 재확정된 경우는
            // 새로 판단할 것이 없으므로 호출하지 않는다(토큰이 그대로 비용이다).
            if (beliefResult.FinalBelief != beliefBefore) npc.MarkBeliefChanged();

            var candidates = major.availableActions ?? Array.Empty<NpcActionData>();

            // 관계를 판단 근거로 쓸 수 있는 대상을 "이번 문맥에 실제로 등장하는 인물"로 한정하기 위한
            // 입력. 지금 이 장소에 함께 있는 인물 목록을 이 시점에 복사해 둔다 - 판단 중 재확산으로
            // 장소 인원이 바뀌어도 이 판단이 본 세계는 고정된다.
            var presentNpcs = new List<NpcState>();
            if (where != null)
                foreach (var other in where.PresentNpcs)
                    if (other != null && other != npc) presentNpcs.Add(other);

            var thinkContext = new NpcThinkContext(
                npc, card, beliefResult.FinalBelief, workingMemory, where, candidates, currentTurn,
                presentNpcs, propagator);

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

            // Effect가 목표를 바꿀 수 있으므로 Apply 이후에 본다. 지금은 SetGoal을 호출하는 시스템이
            // 하나도 없어 절대 true가 되지 않는다 - 구조만 미리 열어 두는 것이고, 이번 병렬화의
            // 선별 성능은 오직 위의 Belief 변화로만 판단해야 한다.
            if (npc.CurrentGoal != goalBefore) npc.MarkGoalChanged();

            if (thinkResult.Dialogue != null)
                eventBus.Publish(new NpcSpokeEvent(npc.Data, thinkResult.Dialogue));

            eventBus.Publish(new CardJudgedEvent(npc.Data, card, beliefResult.FinalBelief, currentTurn));

            // ── Shadow Mode(관찰 전용) ────────────────────────────────────────────────
            // 실제 판단이 전부 끝나고 월드에 적용된 뒤에 발사한다. 컨텍스트에 담는 값은 이 메서드
            // 앞부분에서 이미 캡처해 둔 "판단 직전" 값들이라, 여기서 만들어도 그때의 세계를 그대로
            // 본다. await하지 않으므로 턴 진행은 Shadow 응답을 기다리지 않는다.
            if (shadow != null)
            {
                string ruleDialogue = thinkResult.Dialogue == null ? null
                    : (thinkResult.Dialogue.IsGenerated ? thinkResult.Dialogue.GeneratedText
                        : (thinkResult.Dialogue.PredefinedLine != null ? thinkResult.Dialogue.PredefinedLine.text : null));

                var shadowContext = new NpcJudgmentContext(
                    npc, card, where, currentTurn, beliefBefore, goalBefore, workingMemory,
                    candidates, major.movementCandidates, presentNpcs, propagator);

                shadow.Observe(shadowContext, beliefResult.FinalBelief, thinkResult.ChosenAction,
                    npc.CurrentGoal, ruleDialogue);
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (trace != null)
            {
                trace.WithBeliefEvaluation(beliefResult);
                trace.WithBeliefChange(beliefBefore, beliefResult.FinalBelief);
                trace.WithActionDecision(beliefResult.FinalBelief, candidates, thinkResult.ChosenAction);
                trace.WithDialogueDecision(beliefResult.FinalBelief, major.beliefDialogues, thinkResult.Dialogue);
                trace.WithGoal(goalBefore, npc.CurrentGoal);

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
