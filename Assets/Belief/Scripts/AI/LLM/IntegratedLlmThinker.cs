using System;
using System.Threading;
using System.Threading.Tasks;
using Belief.AI;
using UnityEngine;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using Belief.Debugging;
#endif

namespace Belief.AI.LLM
{
    /// <summary>이 판단이 어느 경로에서 나왔는지. 섞인 결과는 존재하지 않는다.</summary>
    public enum JudgmentResultSource
    {
        IntegratedLlm,
        RuleBasedFallback,
    }

    /// <summary>
    /// 통합 판단 한 건의 최종 결과. <b>LLM 전체 성공이거나 RuleBased 전체 폴백이거나 둘 중 하나</b>이며,
    /// 두 경로의 필드를 섞은 결과는 이 타입으로 표현할 수 없다.
    /// </summary>
    public readonly struct IntegratedJudgmentOutcome
    {
        public readonly bool HasJudgment;
        public readonly ValidatedNpcJudgment Judgment;
        public readonly JudgmentResultSource Source;

        /// <summary>RuleBasedFallback일 때만 채워진다 - 왜 LLM 결과를 버렸는지.</summary>
        public readonly string FallbackReason;

        public readonly double LatencyMs;
        public readonly bool TokensAvailable;
        public readonly int InputTokens;
        public readonly int OutputTokens;
        public readonly string RawResponse;

        IntegratedJudgmentOutcome(bool hasJudgment, ValidatedNpcJudgment judgment, JudgmentResultSource source,
            string fallbackReason, double latencyMs, bool tokensAvailable, int inputTokens, int outputTokens, string rawResponse)
        {
            HasJudgment = hasJudgment; Judgment = judgment; Source = source; FallbackReason = fallbackReason;
            LatencyMs = latencyMs; TokensAvailable = tokensAvailable;
            InputTokens = inputTokens; OutputTokens = outputTokens; RawResponse = rawResponse;
        }

        public static IntegratedJudgmentOutcome FromLlm(ValidatedNpcJudgment j, double ms,
            bool tokensAvailable, int inTok, int outTok, string raw) =>
            new IntegratedJudgmentOutcome(true, j, JudgmentResultSource.IntegratedLlm, null, ms, tokensAvailable, inTok, outTok, raw);

        public static IntegratedJudgmentOutcome FromFallback(ValidatedNpcJudgment j, string reason, double ms, string raw) =>
            new IntegratedJudgmentOutcome(true, j, JudgmentResultSource.RuleBasedFallback, reason, ms, false, 0, 0, raw);

        /// <summary>폴백조차 만들 수 없었던 경우(NPC 유형 불일치 등). 호출자는 이 판단을 건너뛴다.</summary>
        public static IntegratedJudgmentOutcome None(string reason, double ms) =>
            new IntegratedJudgmentOutcome(false, default, JudgmentResultSource.RuleBasedFallback, reason, ms, false, 0, 0, null);
    }

    /// <summary>
    /// 통합 판단의 실제 요청 계층. <b>결과를 만들 뿐 월드에 적용하지 않는다</b> -
    /// ActionResolutionSystem·BeliefSystem·NpcState 변경 API를 전혀 참조하지 않는다.
    ///
    /// 반환은 언제나 둘 중 하나다:
    /// <list type="number">
    /// <item>LLM 응답이 <b>모든</b> 검증을 통과 → IntegratedLlm 결과 전체</item>
    /// <item>그 외 전부 → RuleBasedUnifiedThinker가 만든 <b>전체</b> 결과 (정확히 1회)</item>
    /// </list>
    /// 필드 단위로 섞지 않는다 - "LLM Belief + RuleBased Action" 같은 결과는 만들 수 없다.
    ///
    /// Frozen 규칙 준수: 동기 블로킹 없음(Task.WhenAny 경합), Timeout은 생성자 인자 하나로만 관리,
    /// 재시도 0회, 늦은 응답은 관찰만 하고 절대 적용 경로에 닿지 않는다.
    /// 기존 <see cref="IMajorNpcThinker"/> 계약은 건드리지 않는다 - 이 클래스는 그 인터페이스를
    /// 구현하지 않으며, RuleOnly/FakeLlm/Llm 세 모드는 지금까지와 동일하게 동작한다.
    /// </summary>
    public class IntegratedLlmThinker
    {
        readonly ILlmTransport transport;
        readonly IIntegratedNpcThinker fallback;
        readonly int timeoutMs;

        public IntegratedLlmThinker(ILlmTransport transport, IIntegratedNpcThinker fallback,
            int timeoutMs = LlmMajorThinker.DefaultTimeoutMs)
        {
            this.transport = transport;
            this.fallback = fallback;
            this.timeoutMs = timeoutMs > 0 ? timeoutMs : LlmMajorThinker.DefaultTimeoutMs;
        }

        public async Task<IntegratedJudgmentOutcome> DecideAsync(
            NpcJudgmentContext context, JudgmentRequestIdentity identity, object trace)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var traceBuilder = trace as NpcDecisionTraceBuilder;
#endif
            var start = DateTime.UtcNow;

            // ── 프롬프트 조립 ────────────────────────────────────────────────────
            string prompt;
            try { prompt = UnifiedPromptBuilder.Build(context); }
            catch (Exception)
            {
                return await FallbackAsync(context, identity, "PromptBuildFailure", Elapsed(start), null, trace);
            }

            // ── Transport 요청 (재시도 없음) ──────────────────────────────────────
            var cts = new CancellationTokenSource();
            Task<string> request;
            try
            {
                request = transport is ICancellableLlmTransport c
                    ? c.SendAsync(prompt, cts.Token)
                    : transport.SendAsync(prompt);
            }
            catch (Exception)
            {
                return await FallbackAsync(context, identity, "TransportException", Elapsed(start), null, trace);
            }

            // Task.Delay가 아니라 CoroutineRunner.DelayAsync - WebGL에서 Task.Delay는 깨어나지 않아
            // Timeout이 영영 걸리지 않는다(그러면 그 판단이 통째로 멈춘다).
            var timeoutTask = CoroutineRunner.DelayAsync(timeoutMs);
            var completed = await Task.WhenAny(request, timeoutTask);

            if (completed == timeoutTask)
            {
                cts.Cancel();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                ObserveLateResponse(request, traceBuilder, identity);
#endif
                return await FallbackAsync(context, identity, "Timeout", Elapsed(start), null, trace);
            }

            string raw;
            try
            {
                raw = await request;
                if (string.IsNullOrWhiteSpace(raw))
                    return await FallbackAsync(context, identity, "EmptyResponse", Elapsed(start), null, trace);
            }
            catch (LlmTransportException ex)
            {
                return await FallbackAsync(context, identity, ex.WasCanceled ? "Cancelled" : "TransportException", Elapsed(start), null, trace);
            }
            catch (Exception)
            {
                return await FallbackAsync(context, identity, "TransportException", Elapsed(start), null, trace);
            }

            bool tokensAvailable = false; int inTok = 0, outTok = 0;
            if (transport is ITokenUsageReporting reporter && reporter.TryGetLastUsage(out inTok, out outTok, out _))
                tokensAvailable = true;

            // ── 검증 ────────────────────────────────────────────────────────────
            var validation = UnifiedResponseParser.Parse(raw, context);
            if (!validation.IsValid)
                return await FallbackAsync(context, identity, validation.FailureReason, Elapsed(start), raw, trace);

            var j = validation.Judgment;
            var summary = new ValidationSummary("IntegratedLlm", CollectSoftWarnings(context, j));

            // 마지막 게이트 - 여기서 실패해도 부분 적용은 없다. 아직 아무것도 적용하지 않았기 때문이다.
            if (!ValidatedNpcJudgment.TryCreate(
                    j.Interpretation, j.Belief, j.Goal, j.Action, j.Destination, j.Dialogue, j.Grounds,
                    identity, summary, context, out var validated, out var gateFailure))
                return await FallbackAsync(context, identity, gateFailure, Elapsed(start), raw, trace);

            return IntegratedJudgmentOutcome.FromLlm(validated, Elapsed(start), tokensAvailable, inTok, outTok, raw);
        }

        /// <summary>
        /// LLM 결과를 <b>통째로 버리고</b> 규칙 기반 전체 판단을 1회 만든다.
        /// 이 메서드는 한 번의 DecideAsync에서 최대 한 번만 호출된다 - 모든 실패 경로가
        /// 여기서 곧바로 return하기 때문에 두 번 도달할 수 없다.
        /// </summary>
        async Task<IntegratedJudgmentOutcome> FallbackAsync(
            NpcJudgmentContext context, JudgmentRequestIdentity identity, string reason, double ms, string raw, object trace)
        {
            var v = await fallback.DecideAsync(context, trace);
            if (!v.IsValid)
                return IntegratedJudgmentOutcome.None($"{reason}+FallbackFailed:{v.FailureReason}", ms);

            var j = v.Judgment;
            var summary = new ValidationSummary("RuleBasedFallback", Array.Empty<string>());
            if (!ValidatedNpcJudgment.TryCreate(
                    j.Interpretation, j.Belief, j.Goal, j.Action, j.Destination, j.Dialogue, j.Grounds,
                    identity, summary, context, out var validated, out var gateFailure))
                return IntegratedJudgmentOutcome.None($"{reason}+FallbackGate:{gateFailure}", ms);

            return IntegratedJudgmentOutcome.FromFallback(validated, reason, ms, raw);
        }

        /// <summary>구조적으로 확정할 수 없어 로그로만 남기는 의미적 모순들.
        /// <b>여기서 판단을 무효화하지 않는다</b> - 표본이 쌓인 뒤에 승격 여부를 정한다.</summary>
        static string[] CollectSoftWarnings(NpcJudgmentContext ctx, NpcJudgment j)
        {
            var list = new System.Collections.Generic.List<string>();

            // Goal 안에 다른 이동 후보의 locationId가 문자열로 들어 있는데 stay를 골랐다면,
            // 문자열 대조만으로 확인 가능한 모순이다(자연어 판정이 아니다).
            if (j.Destination == null && !string.IsNullOrEmpty(j.Goal) && ctx.MoveCandidates != null)
                foreach (var l in ctx.MoveCandidates)
                    if (l != null && ctx.Where != null && l != ctx.Where.Data
                        && j.Goal.IndexOf(l.locationId, StringComparison.OrdinalIgnoreCase) >= 0)
                    { list.Add($"GoalMentionsOtherLocationButStayed:{l.locationId}"); break; }

            // 관계를 근거로 댔는데 대사에 그 인물이 전혀 등장하지 않는 경우 - 관찰용 신호.
            if (!string.IsNullOrEmpty(j.Grounds.RelationshipInfluence) && !string.IsNullOrEmpty(j.Dialogue))
            {
                bool mentioned = false;
                if (ctx.Npc.Data is Belief.Data.MajorNpcData major && major.relationships != null)
                    foreach (var rel in major.relationships)
                        if (rel.other != null && rel.other.npcId == j.Grounds.RelationshipInfluence
                            && !string.IsNullOrEmpty(rel.other.displayName)
                            && j.Dialogue.Contains(rel.other.displayName)) { mentioned = true; break; }
                if (!mentioned) list.Add("RelationshipGroundsNotReflectedInDialogue");
            }

            return list.ToArray();
        }

        static double Elapsed(DateTime start) => (DateTime.UtcNow - start).TotalMilliseconds;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>Timeout으로 폴백이 확정된 뒤에도 원래 요청은 계속 진행 중일 수 있다.
        /// 여기서는 그 결과를 <b>관찰만</b> 한다 - 이 메서드는 판단 결과를 반환할 방법도,
        /// 월드를 바꿀 방법도 없으므로 늦은 응답이 적용되는 것이 구조적으로 불가능하다.</summary>
        static async void ObserveLateResponse(Task<string> pending, NpcDecisionTraceBuilder traceBuilder, JudgmentRequestIdentity identity)
        {
            if (pending == null) return;
            try
            {
                await pending;
                Debug.LogWarning($"[IntegratedLlm] Timeout 이후 늦게 도착한 통합 판단 응답을 폐기했습니다 ({identity}).");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[IntegratedLlm] Timeout 이후 늦게 도착한 요청이 실패로 끝났습니다 ({identity}): {ex.Message}");
            }
            finally
            {
                if (traceBuilder != null) traceBuilder.Record.LateResponseDiscarded = true;
            }
        }
#endif
    }
}
