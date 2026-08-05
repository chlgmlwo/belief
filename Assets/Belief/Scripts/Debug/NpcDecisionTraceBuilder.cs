#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using Belief.AI;
using Belief.Data;
using Belief.Domain;
using Belief.Systems;

namespace Belief.Debugging
{
    /// <summary>하나의 판단(정보 판단 또는 턴 이동)에 대한 관찰 기록을 단계별로 채워 나가는 헬퍼.
    /// 호출부(NpcThinkingSystem/RuleBasedMajorThinker/LlmMajorThinker/NpcMovementSystem 등)가
    /// 이미 계산한 값만 그대로 옮겨 담는다 - 여기서 새로 계산하거나 추론하지 않는다.
    /// 이 클래스는 게임 판단에 어떤 영향도 주지 않는다(Publish 결과를 아무도 읽지 않아도 게임은
    /// 동일하게 동작한다).</summary>
    public class NpcDecisionTraceBuilder
    {
        readonly NpcDecisionTraceRecord record = new NpcDecisionTraceRecord();
        readonly DateTime startedAtUtc;

        public NpcDecisionTraceRecord Record => record;

        public NpcDecisionTraceBuilder(
            string decisionType, NpcState npc, string stageId, int stageTurn,
            string missionId, int missionTurn, string thinkerMode)
        {
            startedAtUtc = DateTime.UtcNow;

            record.DecisionId = Guid.NewGuid().ToString("N");
            record.DecisionType = decisionType;
            record.StageId = stageId;
            record.StageTurn = stageTurn;
            record.MissionId = missionId;
            record.MissionTurn = missionTurn;
            record.ThinkerMode = thinkerMode;
            record.StartTimestampUtc = startedAtUtc.ToString("O");

            if (npc?.Data != null)
            {
                record.NpcId = npc.Data.npcId;
                record.NpcDisplayName = npc.Data.displayName;
            }
            if (npc?.CurrentLocation != null)
            {
                record.CurrentLocationId = npc.CurrentLocation.locationId;
                record.CurrentLocationDisplayName = npc.CurrentLocation.displayName;
            }
        }

        public NpcDecisionTraceBuilder WithReceivedInformation(InformationCardData card)
        {
            if (card == null)
            {
                record.HasReceivedInformation = false;
                return this;
            }

            record.HasReceivedInformation = true;
            record.CardId = card.cardId;
            record.CardTitle = card.information != null ? card.information.title : null;
            record.CardCategoryId = card.information != null ? card.information.categoryId : null;
            record.CardDescription = card.information != null ? card.information.description : null;
            record.CardSourceId = card.source != null ? card.source.sourceId : null;
            record.CardSourceDisplayName = card.source != null ? card.source.displayName : null;
            record.CardDeliveryType = card.cardType.ToString();
            record.CardTargetType = card.TargetType.ToString();
            record.CardTags = card.information != null && card.information.tags != null
                ? (string[])card.information.tags.Clone() : Array.Empty<string>();
            return this;
        }

        /// <summary>Location Mechanics V1(§3, §4) - 재확산으로 도달한 경우에만 snapshot이 존재한다.
        /// 이미 InfoDeliverySystem이 계산해 둔 값을 그대로 옮겨 담을 뿐, 여기서 다시 계산하지 않는다.</summary>
        public NpcDecisionTraceBuilder WithSpreadMechanics(SpreadMechanicsSnapshot? snapshot)
        {
            record.ReachedViaRespread = snapshot.HasValue;
            if (!snapshot.HasValue) return this;

            var s = snapshot.Value;
            record.LocBaseSpreadPower = s.BaseSpreadPower;
            record.LocSourceSpreadSpeed = s.SourceSpreadSpeed.ToString();
            record.LocSpreadMultiplier = s.SpreadMultiplier;
            record.LocEffectiveSpreadPower = s.EffectiveSpreadPower;
            record.LocTargetNpcDensity = s.TargetNpcDensity.ToString();
            record.LocCandidateNpcCount = s.CandidateNpcCount;
            record.LocDensityTargetLimit = s.DensityTargetLimit;
            record.LocSelectedSecondaryRecipientCount = s.SelectedSecondaryRecipientCount;
            record.LocExcludedRecipientCount = s.ExcludedRecipientCount;
            return this;
        }

        /// <summary>판단 대상 카드가 있으면 그 카드에 대한 Belief를, 없으면(턴 이동 판단) 가장 최근
        /// 수신 정보의 Belief를 "판단 전 상태"로 표시한다 - HudPresenter의 조사 패널과 동일한 규칙.</summary>
        public NpcDecisionTraceBuilder WithStateBefore(NpcState npc, InformationCardData beliefReferenceCard)
        {
            if (npc == null) return this;

            record.GoalBefore = npc.CurrentGoal;

            var beliefState = beliefReferenceCard != null ? npc.GetBelief(beliefReferenceCard) : BeliefState.Unknown;
            record.BeliefStageBeforeRaw = beliefState.ToString();
            record.BeliefStageBeforeLabel = BeliefLabel(beliefState);

            foreach (var kvp in npc.Beliefs)
                record.BeliefsBefore.Add(new BeliefEntrySnapshot { CardId = kvp.Key.cardId, BeliefStateRaw = kvp.Value.ToString() });

            var (conviction, doubt) = BeliefRatios(npc);
            record.ConvictionRatio = conviction;
            record.DoubtRatio = doubt;

            if (beliefReferenceCard != null)
                record.IntentBefore = IntentMapping(beliefState).ToString();

            if (npc.Data is MajorNpcData major && major.relationships != null)
            {
                foreach (var rel in major.relationships)
                {
                    if (rel.other == null) continue;
                    record.Relationships.Add(new RelationshipSnapshot
                    {
                        TargetNpcId = rel.other.npcId,
                        TargetDisplayName = rel.other.displayName,
                        RelationshipTypeLabel = rel.relationshipTypeLabel
                    });
                }
            }

            return this;
        }

        public NpcDecisionTraceBuilder WithBeliefEvaluation(BeliefEvaluationResult result)
        {
            foreach (var c in result.Breakdown)
            {
                // Credibility만 예외적으로 실제 입력값(Location Mechanics V1이 계산한 EffectiveCredibility)을
                // 그대로 보여준다 - 다른 6개 Evaluator는 여전히 별도 근거를 반환하지 않으므로 지어내지 않는다.
                string inputSummary = c.Type == BeliefContributionType.Credibility
                    ? $"EffectiveCredibility={result.EffectiveCredibility:F3} (base={result.BaseCredibility:F3}, locationDelta={result.LocationCredibilityDelta:F3}, sensitiveBonus={result.SensitiveTypeBonus:F3})"
                    : "(데이터 없음 - Evaluator가 별도 근거를 반환하지 않음)";

                record.Evaluators.Add(new EvaluatorEntry
                {
                    EvaluatorName = c.Type.ToString(),
                    ScoreDelta = c.ScoreDelta,
                    IsExceptional = c.IsExceptional,
                    InputSummary = inputSummary
                });
            }
            record.BaseJudgmentScore = result.BaseJudgmentScore;
            record.RawExceptionalModifier = result.RawExceptionalModifier;
            record.CappedExceptionalModifier = result.CappedExceptionalModifier;
            record.FinalScore = result.FinalScore;
            record.WasReversedByException = result.WasReversedByException;

            record.LocBaseCredibility = result.BaseCredibility;
            record.LocCredibilityDelta = result.LocationCredibilityDelta;
            record.LocCardInformationType = result.Card != null && result.Card.information != null
                ? result.Card.information.informationType.ToString() : null;
            record.LocSensitiveTypeMatched = result.SensitiveTypeMatched;
            record.LocSensitiveTypeBonus = result.SensitiveTypeBonus;
            record.LocEffectiveCredibility = result.EffectiveCredibility;
            record.LocCredibilityEvaluatorContribution = result.CredibilityEvaluatorContribution;
            return this;
        }

        public NpcDecisionTraceBuilder WithBeliefChange(BeliefState before, BeliefState after)
        {
            record.BeliefBeforeLabel = BeliefLabel(before);
            record.BeliefAfterLabel = BeliefLabel(after);
            record.BeliefAfterRaw = after.ToString();
            record.BeliefChanged = before != after;
            return this;
        }

        /// <summary>RuleBasedMajorThinker.ChooseAction과 정확히 같은 매핑을 판단부와 별도로 다시
        /// 계산하지만, 순수 함수(BeliefState -> Intent)라 실제 선택 결과와 어긋날 위험이 없다 -
        /// 실제 선택(selected)은 항상 thinker.Decide()가 반환한 값을 그대로 받는다.</summary>
        public NpcDecisionTraceBuilder WithActionDecision(BeliefState currentBelief, IReadOnlyList<NpcActionData> candidates, NpcActionData selected)
        {
            record.ActionInputBeliefStage = currentBelief.ToString();
            var intent = IntentMapping(currentBelief);
            record.ActionIntentMapping = intent.ToString();

            if (candidates != null)
            {
                foreach (var c in candidates)
                {
                    if (c == null) continue;
                    record.ActionCandidates.Add(new ActionCandidateEntry
                    {
                        ActionId = c.actionId,
                        DisplayLabel = c.displayLabel,
                        Intent = c.intent.ToString(),
                        IntentMatches = c.intent == intent,
                        IsSelected = c == selected
                    });
                }
            }
            record.SelectedActionId = selected != null ? selected.actionId : null;
            record.SelectedActionLabel = selected != null ? selected.displayLabel : null;
            return this;
        }

        public NpcDecisionTraceBuilder WithDialogueDecision(BeliefState currentBelief, DialogueLineData[] candidates, DialogueContent chosen)
        {
            record.DialogueContextTag = BeliefToContextTag(currentBelief);

            if (candidates != null)
            {
                foreach (var d in candidates)
                {
                    if (d == null) continue;
                    bool isSelected = chosen != null && !chosen.IsGenerated && chosen.PredefinedLine == d;
                    record.DialogueCandidates.Add(new DialogueCandidateEntry { ContextTag = d.contextTag, Text = d.text, IsSelected = isSelected });
                    if (isSelected) record.SelectedDialogueIndex = record.DialogueCandidates.Count - 1;
                }
            }

            if (chosen != null)
            {
                record.DialogueIsGenerated = chosen.IsGenerated;
                record.SelectedDialogueText = chosen.IsGenerated ? chosen.GeneratedText : (chosen.PredefinedLine != null ? chosen.PredefinedLine.text : null);
            }
            return this;
        }

        public NpcDecisionTraceBuilder WithGoal(string before, string after)
        {
            record.GoalTextBefore = before;
            record.GoalTextAfter = after;
            record.GoalChanged = before != after;
            record.GoalContentParsed = false;
            return this;
        }

        public NpcDecisionTraceBuilder WithGoalPresenceUsedInMove(bool used)
        {
            record.GoalPresenceUsed = used;
            record.GoalContentParsed = false;
            return this;
        }

        /// <summary>RuleBasedMajorThinker.DecideMove가 이미 계산한 후보별 점수를 그대로 옮겨 담는다 -
        /// 여기서 점수를 다시 계산하지 않는다(가중치가 바뀌어도 이 메서드는 손댈 필요가 없다).</summary>
        public NpcDecisionTraceBuilder WithMoveScoring(
            LocationData currentLocation, IReadOnlyList<LocationData> candidates,
            LocationData[] preferred, LocationData[] avoided,
            float convictionRatio, float doubtRatio,
            List<MoveCandidateScoreEntry> scoredEntries, LocationData selected, bool isStay, bool hadTie)
        {
            record.MoveCurrentLocationId = currentLocation != null ? currentLocation.locationId : null;
            if (candidates != null)
                foreach (var c in candidates)
                    if (c != null) record.MovementCandidateIds.Add(c.locationId);

            if (preferred != null) foreach (var p in preferred) if (p != null) record.PreferredLocationIds.Add(p.locationId);
            if (avoided != null) foreach (var a in avoided) if (a != null) record.AvoidedLocationIds.Add(a.locationId);

            record.MoveConvictionRatio = convictionRatio;
            record.MoveDoubtRatio = doubtRatio;
            record.MoveCandidateScores = scoredEntries;
            record.MoveBestScore = scoredEntries.Count > 0 ? scoredEntries.Max(e => e.FinalScore) : 0f;
            record.MoveHadTie = hadTie;
            record.MoveIsStay = isStay;
            record.SelectedDestinationId = selected != null ? selected.locationId : null;

            if (candidates != null)
            {
                foreach (var c in candidates)
                {
                    if (c == currentLocation)
                        record.ExcludedMoveCandidates.Add(new ExcludedCandidateEntry { LocationId = c.locationId, Reason = "현재 위치" });
                }
            }
            return this;
        }

        public NpcDecisionTraceBuilder WithLlmAttempt(string promptText, DateTime requestStartUtc, int timeoutMs)
        {
            record.UsedLlm = true;
            record.PromptText = promptText;
            record.LlmRequestStartUtc = requestStartUtc.ToString("O");
            record.TimeoutMs = timeoutMs;
            return this;
        }

        public NpcDecisionTraceBuilder WithLlmResult(
            bool responseSucceeded, string rawResponse, double responseTimeMs,
            bool parseSucceeded, bool destinationOrActionValid,
            bool fallbackOccurred, string fallbackReason, string usedResultSource)
        {
            record.LlmResponseSucceeded = responseSucceeded;
            record.RawResponseText = rawResponse;
            record.LlmResponseTimeMs = responseTimeMs;
            record.LlmParseSucceeded = parseSucceeded;
            record.LlmDestinationValid = destinationOrActionValid;
            record.FallbackOccurred = fallbackOccurred;
            record.FallbackReason = fallbackReason ?? "";
            record.UsedResultSource = usedResultSource;
            record.ResolvedByLlm = usedResultSource == "LLM";
            record.ResolvedByFallback = usedResultSource != null && usedResultSource.Contains("Fallback");
            return this;
        }

        /// <summary>검증을 통과한 판단 근거 3필드를 그대로 옮겨 담는다 - 여기서 해석하거나 보정하지
        /// 않는다. LlmMajorThinker만 호출하며, RuleBased 폴백으로 확정된 판단에는 붙지 않는다
        /// (그 경우 세 값이 모두 빈 문자열로 남아 "근거 없음"과 구분된다).</summary>
        public NpcDecisionTraceBuilder WithJudgmentGrounds(string primaryReason, string profileInfluence, string relationshipInfluence)
        {
            record.GroundsPrimaryReason = primaryReason ?? "";
            record.GroundsProfileInfluence = profileInfluence ?? "";
            record.GroundsRelationshipInfluence = relationshipInfluence ?? "";
            return this;
        }

        /// <summary>Action Effect가 실제로 실행된 뒤 남긴 흔적을 관찰만 한다 - 여기서 InvestigationState나
        /// Memory를 만들지 않는다(호출부가 ActionResolutionSystem.Apply 이후의 실제 상태를 읽어 그대로 전달).</summary>
        public NpcDecisionTraceBuilder WithEffectResult(
            NpcActionData appliedAction, bool investigationStateChanged, string investigationResultType, bool memoryAdded)
        {
            record.AppliedEffectType = DescribeEffect(appliedAction != null ? appliedAction.effect : null);
            record.InvestigationStateChanged = investigationStateChanged;
            record.InvestigationStateResultType = investigationResultType;
            record.MemoryAdded = memoryAdded;
            return this;
        }

        static string DescribeEffect(NpcActionEffect effect)
        {
            if (effect == null) return "None";
            if (effect is CompositeEffect composite && composite.effects != null)
                return "CompositeEffect[" + string.Join(",", composite.effects.Where(e => e != null).Select(e => e.GetType().Name)) + "]";
            return effect.GetType().Name;
        }

        public NpcDecisionTraceBuilder WithFinalResolution(
            string finalIntent, string finalActionId, string finalDialogue, string finalGoal, string finalMoveDestinationId,
            int actionResolutionCount, string positionBefore, string positionAfter)
        {
            record.FinalIntent = finalIntent;
            record.FinalActionId = finalActionId;
            record.FinalDialogueText = finalDialogue;
            record.FinalGoal = finalGoal;
            record.FinalMoveDestinationId = finalMoveDestinationId;
            record.ActionResolutionCount = actionResolutionCount;
            record.PositionBeforeMove = positionBefore;
            record.PositionAfterMove = positionAfter;
            return this;
        }

        public NpcDecisionTraceBuilder WithError(string message)
        {
            record.HasError = true;
            record.ErrorMessage = message;
            return this;
        }

        public void Publish()
        {
            var endedAtUtc = DateTime.UtcNow;
            record.EndTimestampUtc = endedAtUtc.ToString("O");
            record.ProcessingTimeMs = (endedAtUtc - startedAtUtc).TotalMilliseconds;
            NpcDecisionTraceHub.Publish(record);
        }

        public static string BeliefLabel(BeliefState state) => state switch
        {
            BeliefState.Trusted => "신뢰함",
            BeliefState.Plausible => "가능성이 있다고 판단함",
            BeliefState.NeedsVerification => "확인이 필요하다고 판단함",
            BeliefState.Doubtful => "의심함",
            BeliefState.Denied => "부정함",
            _ => "판단 전(정보 없음)"
        };

        public static string BeliefToContextTag(BeliefState state) => state switch
        {
            BeliefState.Trusted => "Trust",
            BeliefState.Plausible => "Possible",
            BeliefState.NeedsVerification => "NeedVerification",
            BeliefState.Doubtful => "Doubt",
            BeliefState.Denied => "Reject",
            _ => null
        };

        /// <summary>RuleBasedMajorThinker.ChooseAction의 매핑과 정확히 동일한 순수 함수 - 실제 판단
        /// 코드를 바꾸지 않고 표시용으로만 재사용한다.</summary>
        public static NpcActionIntent IntentMapping(BeliefState state) => state switch
        {
            BeliefState.Trusted => NpcActionIntent.Comply,
            BeliefState.Plausible => NpcActionIntent.Escalate,
            BeliefState.Doubtful => NpcActionIntent.Verify,
            BeliefState.NeedsVerification => NpcActionIntent.Verify,
            BeliefState.Denied => NpcActionIntent.Ignore,
            _ => NpcActionIntent.Wait
        };

        public static (float convictionRatio, float doubtRatio) BeliefRatios(NpcState npc)
        {
            int total = npc.Beliefs.Count;
            if (total == 0) return (0f, 0f);

            int conviction = 0, doubt = 0;
            foreach (var kvp in npc.Beliefs)
            {
                if (kvp.Value == BeliefState.Plausible || kvp.Value == BeliefState.Trusted) conviction++;
                else if (kvp.Value == BeliefState.Doubtful || kvp.Value == BeliefState.Denied) doubt++;
            }
            return ((float)conviction / total, (float)doubt / total);
        }
    }
}
#endif
