using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Belief.AI;
using Belief.AI.LLM;
using Belief.Data;
using Belief.Debugging;
using Belief.Domain;
using Belief.Events;
using UnityEngine;

namespace Belief.Systems
{
    /// <summary>
    /// Shadow Mode - 실제 게임 판단이 끝난 뒤, 같은 스냅샷으로 LLM 통합 판단을 <b>따로</b> 돌려
    /// 규칙 기반 결과와 비교만 한다.
    ///
    /// <b>월드를 바꿀 수단이 구조적으로 없다.</b> 이 클래스는 ActionResolutionSystem,
    /// BeliefSystem, NpcState의 어떤 변경 API(SetBelief/SetGoal/SetCurrentAction/RecordMemory)도
    /// 참조하지 않는다 - 실수로 적용하는 코드를 쓰는 것 자체가 불가능하다. NpcState는 프로필과
    /// 관계처럼 변하지 않는 정의를 읽기 위해서만 컨텍스트에 담겨 있다.
    ///
    /// Frozen 규칙(재정의): "RuleOnly에서 Transport를 호출하지 않는다"는 <b>게임 판단 경로</b>에
    /// 적용된다. Shadow는 명시적으로 켜야만 동작하는 별도의 개발·관찰 경로이며, 그 응답·실패·
    /// timeout은 게임 판단·월드·미션에 어떤 영향도 주지 않는다.
    /// </summary>
    public class ShadowJudgmentSystem
    {
        public const int MaxConcurrent = 4;
        public const int MaxQueued = 8;

        readonly ILlmTransport transport;
        readonly int timeoutMs;
        readonly bool logPrompts;
        readonly Func<string> stageIdProvider;
        readonly Func<string> missionIdProvider;

        /// <summary>미션 시도가 바뀔 때마다 증가한다. 응답이 도착했을 때 이 값이 발사 시점과
        /// 다르면 그 응답은 지난 시도의 것이므로 StaleSession으로 폐기한다.</summary>
        int attemptId;

        /// <summary>게임오버 이후에는 새 요청을 만들지 않고, 도착한 응답도 정상 통계에서 제외한다.</summary>
        bool sessionClosed;

        /// <summary>비활성화되면 다시 켜지기 전까지 어떤 요청도 발사하지 않는다.</summary>
        bool enabled;

        int inFlight;
        readonly Queue<Func<Task>> pending = new Queue<Func<Task>>();
        int requestCounter;

        public ShadowJudgmentSystem(
            ILlmTransport transport, int timeoutMs, bool logPrompts, IGameEventBus eventBus,
            Func<string> stageIdProvider, Func<string> missionIdProvider)
        {
            this.transport = transport;
            this.timeoutMs = timeoutMs > 0 ? timeoutMs : LlmMajorThinker.DefaultTimeoutMs;
            this.logPrompts = logPrompts;
            this.stageIdProvider = stageIdProvider;
            this.missionIdProvider = missionIdProvider;
            this.enabled = transport != null;

            // 턴 1로 시작하는 모든 지점(StartGame/ResetForNewMission/RestartMissionAttempt)이
            // 새 시도다 - TurnSystem을 고치지 않고 이벤트만으로 세션 경계를 잡는다.
            eventBus.Subscribe<TurnStartedEvent>(e =>
            {
                if (e.Turn != 1) return;
                attemptId++;
                sessionClosed = false;
            });
            eventBus.Subscribe<GameOverEvent>(_ => sessionClosed = true);
        }

        /// <summary>씬 전환 등으로 더 이상 관찰하지 않을 때. 이후 새 요청은 발사되지 않는다.</summary>
        public void Disable() => enabled = false;

        /// <summary>
        /// 실제 판단이 끝난 뒤 호출한다. <b>절대 await하지 않는다</b> - 턴 진행이 Shadow 응답을
        /// 기다리면 게임 타이밍이 관찰 때문에 바뀐다.
        /// </summary>
        public void Observe(NpcJudgmentContext ctx, BeliefState ruleBelief, NpcActionData ruleAction,
            string ruleGoal, string ruleDialogue)
        {
            if (!enabled || sessionClosed || transport == null) return;
            if (ctx.Npc == null || ctx.Card == null) return;

            var record = BuildRecord(ctx, ruleBelief, ruleAction, ruleGoal, ruleDialogue);

            if (inFlight >= MaxConcurrent && pending.Count >= MaxQueued)
            {
                // 발사하지 않는다 - 토큰을 쓰지 않고 "건너뛰었다"는 사실만 남긴다.
                record.Outcome = ShadowOutcome.SkippedConcurrencyLimit;
                ShadowComparisonHub.Publish(record);
                return;
            }

            int firedAttempt = attemptId;
            Func<Task> job = () => RunOne(ctx, record, firedAttempt);

            if (inFlight < MaxConcurrent) StartJob(job);
            else pending.Enqueue(job);
        }

        void StartJob(Func<Task> job)
        {
            inFlight++;
            // async void가 아니라 Task를 만들어 예외를 여기서 전부 삼킨다 - Shadow의 어떤 실패도
            // 게임 쪽 async 흐름으로 새어 나가면 안 된다.
            _ = RunGuarded(job);
        }

        async Task RunGuarded(Func<Task> job)
        {
            try { await job(); }
            catch (Exception ex) { Debug.LogWarning($"[Shadow] 관찰 작업이 예외로 끝났습니다(게임에는 영향 없음): {ex.Message}"); }
            finally
            {
                inFlight--;
                if (pending.Count > 0 && inFlight < MaxConcurrent && enabled)
                    StartJob(pending.Dequeue());
            }
        }

        async Task RunOne(NpcJudgmentContext ctx, ShadowComparisonRecord record, int firedAttempt)
        {
            string prompt;
            try { prompt = UnifiedPromptBuilder.Build(ctx); }
            catch (Exception)
            {
                record.Outcome = ShadowOutcome.Failed;
                record.FailureReason = "PromptBuildFailure";
                ShadowComparisonHub.Publish(record);
                return;
            }

            record.PromptChars = prompt.Length;
            if (logPrompts) record.PromptText = prompt;

            var start = DateTime.UtcNow;
            string raw = null;
            string failure = null;

            var cts = new CancellationTokenSource();
            Task<string> request;
            try
            {
                request = transport is ICancellableLlmTransport c ? c.SendAsync(prompt, cts.Token) : transport.SendAsync(prompt);
            }
            catch (Exception)
            {
                record.Outcome = ShadowOutcome.Failed;
                record.FailureReason = "TransportException";
                record.LatencyMs = (DateTime.UtcNow - start).TotalMilliseconds;
                ShadowComparisonHub.Publish(record);
                return;
            }

            // 게임 판단과 동일한 Timeout 경합 패턴(필수 구현 2 - 단일 Timeout 설정 재사용).
            // 재시도는 하지 않는다.
            var timeout = CoroutineRunner.DelayAsync(timeoutMs);
            var done = await Task.WhenAny(request, timeout);
            record.LatencyMs = (DateTime.UtcNow - start).TotalMilliseconds;

            if (done == timeout)
            {
                cts.Cancel();
                failure = "Timeout";
            }
            else
            {
                try
                {
                    raw = await request;
                    if (string.IsNullOrWhiteSpace(raw)) failure = "EmptyResponse";
                    else
                    {
                        record.ResponseChars = raw.Length;
                        // 응답 직후에 읽는다 - Transport가 방금 이 호출의 usage를 채워 뒀다.
                        if (transport is ITokenUsageReporting reporter
                            && reporter.TryGetLastUsage(out int inTok, out int outTok, out _))
                        {
                            record.TokensAvailable = true;
                            record.InputTokens = inTok;
                            record.OutputTokens = outTok;
                        }
                    }
                }
                catch (LlmTransportException ex) { failure = ex.WasCanceled ? "Cancelled" : "TransportException"; }
                catch (Exception) { failure = "TransportException"; }
            }

            // 응답이 오는 사이 미션이 재시작됐거나 게임이 끝났으면 지난 시도의 결과다.
            if (firedAttempt != attemptId || sessionClosed)
            {
                record.Outcome = ShadowOutcome.StaleSession;
                record.FailureReason = failure;
                ShadowComparisonHub.Publish(record);
                return;
            }

            if (failure != null)
            {
                record.Outcome = ShadowOutcome.Failed;
                record.FailureReason = failure;
                ShadowComparisonHub.Publish(record);
                return;
            }

            if (logPrompts) record.RawResponse = raw;

            var validation = UnifiedResponseParser.Parse(raw, ctx);

            // 목적지 계측은 검증 성공·실패와 무관하게 남긴다 - 무효 처리된 응답의 원본도
            // "왜 이동하지 않았는가"를 가리는 데 필요하다.
            record.RawLlmDestinationId = validation.RawDestinationId;
            record.DestinationNormalizationReason = validation.DestinationReason;

            if (!validation.IsValid)
            {
                record.Outcome = ShadowOutcome.Failed;
                record.FailureReason = validation.FailureReason;
                ShadowComparisonHub.Publish(record);
                return;
            }

            FillLlmResult(record, validation.Judgment);
            record.Outcome = ShadowOutcome.Compared;
            ShadowComparisonHub.Publish(record);
        }

        ShadowComparisonRecord BuildRecord(NpcJudgmentContext ctx, BeliefState ruleBelief,
            NpcActionData ruleAction, string ruleGoal, string ruleDialogue)
        {
            var tags = JudgmentGroundsValidator.ProfileTagsOf(ctx.Npc.Data);
            var usable = JudgmentGroundsValidator.UsableRelationships(ctx.Npc, ctx.PresentNpcs, ctx.Propagator);

            var relStrings = new List<string>(usable.Count);
            foreach (var r in usable)
                relStrings.Add($"{r.other.npcId}|{r.relationshipTypeLabel}|{r.strength:F2}");

            var presentIds = new List<string>();
            if (ctx.PresentNpcs != null)
                foreach (var n in ctx.PresentNpcs)
                    if (n != null && n != ctx.Npc && n.Data != null) presentIds.Add(n.Data.npcId);

            var info = ctx.Card.information;
            return new ShadowComparisonRecord
            {
                RequestId = "shadow-" + (++requestCounter),
                StageId = stageIdProvider != null ? stageIdProvider() : "",
                MissionId = missionIdProvider != null ? missionIdProvider() : "",
                MissionAttemptId = attemptId,
                Turn = ctx.Turn,
                NpcId = ctx.Npc.Data.npcId,
                CardId = ctx.Card.cardId,

                SourceId = ctx.Card.source != null ? ctx.Card.source.sourceId : null,
                CardCredibility = info != null ? info.baseCredibility : 0f,
                SourceTrust = ctx.Card.source != null ? ctx.Card.source.baseTrustModifier : 0f,
                BeliefBefore = ctx.BeliefBefore.ToString(),
                GoalBefore = ctx.GoalBefore,
                ProfileTags = tags.ToArray(),
                UsableRelationships = relStrings.ToArray(),
                PropagatorNpcId = ctx.Propagator != null ? ctx.Propagator.Data.npcId : null,
                PresentNpcIds = presentIds.ToArray(),
                WorkingMemoryCount = ctx.Memory != null && !ctx.Memory.IsEmpty ? ctx.Memory.Entries.Count : 0,

                RuleBelief = ruleBelief.ToString(),
                RuleActionId = ruleAction != null ? ruleAction.actionId : null,
                RuleGoal = ruleGoal,
                RuleDialogue = ruleDialogue,
            };
        }

        static void FillLlmResult(ShadowComparisonRecord r, NpcJudgment j)
        {
            r.LlmInterpretation = j.Interpretation;
            r.LlmBelief = j.Belief.ToString();
            r.LlmGoal = j.Goal;
            r.LlmActionId = j.Action != null ? j.Action.actionId : null;
            r.LlmDestinationId = j.Destination != null ? j.Destination.locationId : "stay";
            r.NormalizedLlmDestinationId = r.LlmDestinationId;
            r.LlmDialogue = j.Dialogue;
            r.LlmPrimaryReason = j.Grounds.PrimaryReason;
            r.LlmProfileInfluence = j.Grounds.ProfileInfluence ?? "none";
            r.LlmRelationshipInfluence = j.Grounds.RelationshipInfluence ?? "none";

            r.BeliefDiffers = r.RuleBelief != r.LlmBelief;
            r.BeliefStepDelta = Step(j.Belief) - StepFromName(r.RuleBelief);
            r.ActionDiffers = r.RuleActionId != r.LlmActionId;
            // Goal 차이는 오류가 아니라 관찰값이다 - 규칙 기반은 Goal을 아예 바꾸지 않으므로
            // 거의 항상 다르게 나온다. 실패로 집계하지 않는다.
            r.GoalDiffers = (r.RuleGoal ?? "") != (r.LlmGoal ?? "");
        }

        /// <summary>믿음 단계를 정수로 - 차이의 방향과 크기를 보기 위한 관찰용 척도다.</summary>
        static int Step(BeliefState s) => s switch
        {
            BeliefState.Denied => 0,
            BeliefState.Doubtful => 1,
            BeliefState.NeedsVerification => 2,
            BeliefState.Plausible => 3,
            BeliefState.Trusted => 4,
            _ => 2
        };

        static int StepFromName(string name) =>
            Enum.TryParse<BeliefState>(name, out var s) ? Step(s) : 2;
    }
}
