using System;
using System.Threading;
using System.Threading.Tasks;
using Belief.AI;
using Belief.Systems;
using UnityEngine;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using Belief.Debugging;
#endif

namespace Belief.AI.LLM
{
    /// <summary>
    /// 실제 LLM 서비스가 무엇이든 이 클래스는 전혀 모른다 - ILlmTransport 뒤에 완전히 숨겨져 있다.
    /// 흐름: PromptBuilder -> ILlmTransport(Timeout 경합) -> ResponseParser -> Validation -> Action 선택.
    /// Timeout/Transport 예외/빈 응답/JSON 파싱 실패/후보 밖 응답/취소 중 하나라도 발생하면 그 판단
    /// 1회만 RuleBasedMajorThinker로 즉시 대체한다 - 무상태(circuit breaker 없음)라 다음 NPC부터는
    /// 다시 LLM을 시도한다.
    ///
    /// 동기 블로킹 없음(중요): 이 클래스는 GetAwaiter().GetResult()/.Result/.Wait()를 어디서도 쓰지
    /// 않는다. Transport 요청은 BenchmarkRunner.InvokeTransportAsync와 동일한 Task.WhenAny(요청,
    /// Task.Delay(timeoutMs)) 경합 패턴으로 기다린다 - Unity 메인 스레드는 매 프레임 그대로 진행되며,
    /// Timeout이 지나면 남은 요청을 기다리지 않고 즉시 RuleBased 결과로 넘어간다.
    ///
    /// 불변식(반드시 유지): ThinkerFactory는 RuleOnly 모드에서 이 클래스를 아예 생성하지 않는다 -
    /// Transport 호출 자체가 발생하지 않는다.
    /// </summary>
    public class LlmMajorThinker : IMajorNpcThinker
    {
        const int MaxDialogueLength = 200;

        /// <summary>Timeout의 유일한 기본값. 실제 적용값은 생성자 인자 하나(timeoutMs)로만 관리되고,
        /// 그 값은 GameInstaller 인스펙터(llmTimeoutMs) -> ThinkerFactory.Create -> 여기로만 전달된다 -
        /// 다른 곳에서 별도의 Timeout 상수를 만들지 않는다.</summary>
        public const int DefaultTimeoutMs = 3000;

        readonly ILlmTransport transport;
        readonly RuleBasedMajorThinker fallback;
        readonly PromptRepository promptRepository;
        readonly int timeoutMs;

        public LlmMajorThinker(ILlmTransport transport, RuleBasedMajorThinker fallback, PromptRepository promptRepository, int timeoutMs = DefaultTimeoutMs)
        {
            this.transport = transport;
            this.fallback = fallback;
            this.promptRepository = promptRepository;
            this.timeoutMs = timeoutMs > 0 ? timeoutMs : DefaultTimeoutMs;
        }

        public async Task<NpcThinkResult> DecideAsync(NpcThinkContext context, object trace)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var traceBuilder = trace as NpcDecisionTraceBuilder;
#endif
            string prompt;
            try
            {
                prompt = PromptBuilder.Build(context);
            }
            catch (Exception)
            {
                var failure = LlmValidationResult.Failure("Prompt 생성 예외 발생");
                promptRepository.Record(context.Npc, null, null, failure, usedFallback: true);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                traceBuilder?.WithLlmAttempt(null, DateTime.UtcNow, timeoutMs);
                traceBuilder?.WithLlmResult(false, null, 0, false, false, true, "TransportException", "RuleBased(Fallback)");
#endif
                return await fallback.DecideAsync(context, trace);
            }

            var requestStart = DateTime.UtcNow;
            var outcome = await SendWithTimeoutAsync(prompt, requestStart);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            traceBuilder?.WithLlmAttempt(prompt, requestStart, timeoutMs);
#endif

            if (outcome.FailureReason == "Timeout")
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                ObserveLateResponse(outcome.PendingTask, traceBuilder, context.Npc.Data.npcId);
                traceBuilder?.WithLlmResult(false, null, outcome.ElapsedMs, false, false, true, "Timeout", "RuleBased(Fallback)");
#endif
                return await fallback.DecideAsync(context, trace);
            }

            if (outcome.FailureReason != null)
            {
                var failure = LlmValidationResult.Failure(outcome.FailureReason);
                promptRepository.Record(context.Npc, prompt, outcome.Raw, failure, usedFallback: true);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                traceBuilder?.WithLlmResult(outcome.FailureReason == "EmptyResponse", outcome.Raw, outcome.ElapsedMs,
                    false, false, true, outcome.FailureReason, "RuleBased(Fallback)");
#endif
                return await fallback.DecideAsync(context, trace);
            }

            var validation = ResponseParser.Parse(outcome.Raw, context.CandidateActions, MaxDialogueLength);

            if (!validation.IsValid)
            {
                promptRepository.Record(context.Npc, prompt, outcome.Raw, validation, usedFallback: true);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                traceBuilder?.WithLlmResult(true, outcome.Raw, outcome.ElapsedMs, false, false, true,
                    ClassifyDecideFailure(outcome.Raw, validation.FailureReason), "RuleBased(Fallback)");
#endif
                return await fallback.DecideAsync(context, trace);
            }

            promptRepository.Record(context.Npc, prompt, outcome.Raw, validation, usedFallback: false);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            traceBuilder?.WithLlmResult(true, outcome.Raw, outcome.ElapsedMs, true, true, false, null, "LLM");
#endif

            var dialogue = new DialogueContent(validation.Dialogue);
            return new NpcThinkResult(validation.ChosenAction, dialogue);
        }

        /// <summary>이동 판단도 DecideAsync와 동일한 원칙을 따른다: LLM 성공 시 LLM이 반환한 Move만
        /// 쓰고, Prompt 조립 실패/Timeout/Transport 예외/파싱 실패/후보 밖 응답/취소면 그 판단 1회만
        /// RuleBased로 대체한다 - 같은 턴에 둘 다 적용되는 경우는 없다(항상 둘 중 하나의 결과만 반환됨).</summary>
        public async Task<NpcMoveResult> DecideMoveAsync(NpcMoveContext context, object trace)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var traceBuilder = trace as NpcDecisionTraceBuilder;
#endif
            string prompt;
            try
            {
                prompt = PromptBuilder.BuildMove(context);
            }
            catch (Exception)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                traceBuilder?.WithLlmAttempt(null, DateTime.UtcNow, timeoutMs);
                traceBuilder?.WithLlmResult(false, null, 0, false, false, true, "TransportException", "RuleBased(Fallback)");
#endif
                return await fallback.DecideMoveAsync(context, trace);
            }

            var requestStart = DateTime.UtcNow;
            var outcome = await SendWithTimeoutAsync(prompt, requestStart);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            traceBuilder?.WithLlmAttempt(prompt, requestStart, timeoutMs);
#endif

            if (outcome.FailureReason == "Timeout")
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                ObserveLateResponse(outcome.PendingTask, traceBuilder, context.Npc.Data.npcId);
                traceBuilder?.WithLlmResult(false, null, outcome.ElapsedMs, false, false, true, "Timeout", "RuleBased(Fallback)");
#endif
                return await fallback.DecideMoveAsync(context, trace);
            }

            if (outcome.FailureReason != null)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                traceBuilder?.WithLlmResult(outcome.FailureReason == "EmptyResponse", outcome.Raw, outcome.ElapsedMs,
                    false, false, true, outcome.FailureReason, "RuleBased(Fallback)");
#endif
                return await fallback.DecideMoveAsync(context, trace);
            }

            var validation = ResponseParser.ParseMove(outcome.Raw, context.Candidates);
            if (!validation.IsValid)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                traceBuilder?.WithLlmResult(true, outcome.Raw, outcome.ElapsedMs, false, false, true,
                    ClassifyMoveFailure(outcome.Raw, validation.FailureReason), "RuleBased(Fallback)");
#endif
                return await fallback.DecideMoveAsync(context, trace);
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            traceBuilder?.WithLlmResult(true, outcome.Raw, outcome.ElapsedMs, true, true, false, null, "LLM");
#endif
            return new NpcMoveResult(validation.Destination);
        }

        readonly struct TransportOutcome
        {
            public readonly string Raw;
            public readonly string FailureReason; // null이면 성공
            public readonly double ElapsedMs;
            public readonly Task<string> PendingTask; // FailureReason == "Timeout"일 때만 채워짐(늦은 응답 관찰용)

            TransportOutcome(string raw, string failureReason, double elapsedMs, Task<string> pendingTask)
            {
                Raw = raw;
                FailureReason = failureReason;
                ElapsedMs = elapsedMs;
                PendingTask = pendingTask;
            }

            public static TransportOutcome Success(string raw, double elapsedMs) => new TransportOutcome(raw, null, elapsedMs, null);
            public static TransportOutcome Failed(string reason, double elapsedMs) => new TransportOutcome(null, reason, elapsedMs, null);
            public static TransportOutcome TimedOut(double elapsedMs, Task<string> pendingTask) => new TransportOutcome(null, "Timeout", elapsedMs, pendingTask);
        }

        /// <summary>필수 구현 1(동기 블로킹 제거)+2(단일 Timeout)의 핵심. BenchmarkRunner.
        /// InvokeTransportAsync와 동일한 Task.WhenAny 경합 패턴 - GetAwaiter().GetResult()/.Result/
        /// .Wait()를 어디서도 쓰지 않는다. Timeout이면 남은 요청(pendingTask)에 취소만 요청하고
        /// 더 기다리지 않은 채 즉시 반환한다(Unity 메인 스레드가 절대 멈추지 않음 - 강제 테스트 B).</summary>
        async Task<TransportOutcome> SendWithTimeoutAsync(string prompt, DateTime requestStart)
        {
            var cts = new CancellationTokenSource();
            Task<string> requestTask;
            try
            {
                requestTask = transport is ICancellableLlmTransport cancellable
                    ? cancellable.SendAsync(prompt, cts.Token)
                    : transport.SendAsync(prompt);
            }
            catch (Exception)
            {
                return TransportOutcome.Failed("TransportException", 0);
            }

            var timeoutTask = Task.Delay(timeoutMs);
            var completed = await Task.WhenAny(requestTask, timeoutTask);
            double elapsed = (DateTime.UtcNow - requestStart).TotalMilliseconds;

            if (completed == timeoutTask)
            {
                cts.Cancel(); // Transport가 취소를 지원하면(ICancellableLlmTransport) 실제 요청도 끊는다.
                return TransportOutcome.TimedOut(elapsed, requestTask);
            }

            try
            {
                string raw = await requestTask;
                if (string.IsNullOrWhiteSpace(raw)) return TransportOutcome.Failed("EmptyResponse", elapsed);
                return TransportOutcome.Success(raw, elapsed);
            }
            catch (LlmTransportException ex)
            {
                return TransportOutcome.Failed(ex.WasCanceled ? "Cancelled" : "TransportException", elapsed);
            }
            catch (Exception)
            {
                return TransportOutcome.Failed("TransportException", elapsed);
            }
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>필수 구현 5(늦은 응답 폐기)의 구현. Timeout으로 이미 RuleBased 결과를 확정한
        /// 뒤에도 원래 요청은 백그라운드에서 계속 진행 중일 수 있다 - 여기서는 그 결과를 "관찰"만
        /// 하고 절대 적용하지 않는다(이 메서드는 ActionResolutionSystem/이동을 호출할 방법 자체가
        /// 없다 - 구조적으로 중복 적용이 불가능). traceBuilder는 이 판단 하나에 대해 지역 변수로
        /// capture된 참조라, 이 콜백이 나중에 실행돼도 그 사이 다른 NPC의 판단이 만든 다른 레코드를
        /// 건드릴 수 없다(필수 구현 8 - static 공유 없음).</summary>
        static async void ObserveLateResponse(Task<string> pendingTask, NpcDecisionTraceBuilder traceBuilder, string npcId)
        {
            if (pendingTask == null) return;
            try
            {
                await pendingTask;
                Debug.LogWarning($"[LlmMajorThinker] Timeout 이후 늦게 도착한 응답을 폐기했습니다 (npc={npcId}).");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[LlmMajorThinker] Timeout 이후 늦게 도착한 요청이 실패로 종료됐습니다 (npc={npcId}): {ex.Message}");
            }
            finally
            {
                if (traceBuilder != null) traceBuilder.Record.LateResponseDiscarded = true;
            }
        }
#endif

        /// <summary>ResponseParser.Parse/ParseMove의 한국어 자유 텍스트 실패 사유를, 관찰 창이
        /// 요구하는 영문 분류 라벨로만 매핑한다 - 실제 검증 로직(ResponseParser)은 전혀 바꾸지 않는다.</summary>
        static string ClassifyDecideFailure(string raw, string failureReason)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "EmptyResponse";
            if (failureReason != null && failureReason.Contains("JSON 파싱 실패")) return "JsonParseFailure";
            if (failureReason != null && failureReason.Contains("CandidateActions에 없는")) return "InvalidDestination";
            return "JsonParseFailure";
        }

        static string ClassifyMoveFailure(string raw, string failureReason)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "EmptyResponse";
            if (failureReason != null && failureReason.Contains("이동 후보에 없는")) return "InvalidDestination";
            if (failureReason != null && failureReason.Contains("JSON 파싱 실패")) return "JsonParseFailure";
            return "InvalidDestination";
        }
    }
}
