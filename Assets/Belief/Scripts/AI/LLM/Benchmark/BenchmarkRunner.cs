using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Belief.AI;
using Belief.Data;
using Belief.Domain;
using Belief.Systems;

namespace Belief.AI.LLM.Benchmark
{
    /// <summary>
    /// GameInstaller 전체(Mission/Turn/Location/WorldPresenter 등)를 조립하지 않는 경량 실행기.
    /// PromptBuilder(BenchmarkPromptBuilder 경유) / ILlmTransport / ResponseParser / BenchmarkLogger /
    /// NpcThinkContext 생성만 조립한다. NPC 1명 + 정보 카드 1장을 Inspector에서 지정하고
    /// 컴포넌트 우클릭 메뉴(Run Benchmark)로 반복 호출을 실행하는 최소 형태다 - 아직 UI는 없다.
    /// </summary>
    public class BenchmarkRunner : MonoBehaviour
    {
        const int MaxDialogueLength = 200;

        [Header("Scenario Input")]
        [SerializeField] MajorNpcData npcData;
        [SerializeField] InformationCardData card;
        [SerializeField] LocationData currentLocation;
        [SerializeField] BeliefState assumedCurrentBelief = BeliefState.Unknown;
        [SerializeField] MemoryTuningData memoryTuning;
        [SerializeField] int currentTurn = 1;
        [SerializeField] string scenarioId = "manual";
        [SerializeField, Min(1)] int repeatCount = 1;

        [Header("Provider")]
        [SerializeField] bool useFakeTransport = true;
        [SerializeField] FakeTransportMode fakeTransportMode = FakeTransportMode.AlwaysSuccess;
        [SerializeField] LlmProviderConfig providerConfig;
        [SerializeField] bool requestReasonAndConfidence = true;

        public bool IsRunning { get; private set; }
        public string StatusText { get; private set; } = "대기 중";

        public event Action<BenchmarkResult> OnResultRecorded;
        public event Action<string> OnStatusChanged;

        readonly BenchmarkLogger logger = new BenchmarkLogger();
        public BenchmarkLogger Logger => logger;

        bool cancelRequested;
        CancellationTokenSource activeCts;

        [ContextMenu("Run Benchmark")]
        public void RunBenchmark()
        {
            if (IsRunning)
            {
                Debug.LogWarning("[BenchmarkRunner] 이미 실행 중입니다 - 중복 요청을 막기 위해 무시합니다.");
                return;
            }

            if (npcData == null || card == null)
            {
                Debug.LogError("[BenchmarkRunner] npcData/card를 먼저 설정하세요.");
                return;
            }

            if (npcData.availableActions == null || npcData.availableActions.Length == 0)
            {
                Debug.LogError("[BenchmarkRunner] npcData.availableActions가 비어 있어 호출할 수 없습니다.");
                return;
            }

            RunBenchmarkAsync();
        }

        [ContextMenu("Cancel Benchmark")]
        public void CancelBenchmark()
        {
            cancelRequested = true;
            activeCts?.Cancel();
        }

        [ContextMenu("Export Results (JSON + CSV)")]
        public void ExportResults()
        {
            string runId = logger.Results.Count > 0 ? logger.Results[logger.Results.Count - 1].benchmarkRunId : "empty";
            string modelId = logger.Results.Count > 0 ? logger.Results[logger.Results.Count - 1].modelId : "unknown";
            string dateStamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");

            string jsonPath = logger.ExportJson(runId, modelId, dateStamp);
            string csvPath = logger.ExportCsv(runId, modelId, dateStamp);
            Debug.Log($"[BenchmarkRunner] Export 완료\nJSON: {jsonPath}\nCSV: {csvPath}");
        }

        async void RunBenchmarkAsync()
        {
            IsRunning = true;
            cancelRequested = false;
            SetStatus("실행 중...");

            string runId = Guid.NewGuid().ToString("N").Substring(0, 8);
            ILlmTransport transport = ResolveTransport();

            for (int i = 0; i < repeatCount; i++)
            {
                if (cancelRequested)
                {
                    SetStatus($"취소됨 ({i}/{repeatCount})");
                    break;
                }

                await RunSingleCallAsync(transport, runId, i);
                SetStatus($"{i + 1}/{repeatCount} 완료");
            }

            IsRunning = false;
            if (!cancelRequested) SetStatus($"완료 ({repeatCount}건)");
        }

        async Task RunSingleCallAsync(ILlmTransport transport, string runId, int index)
        {
            var npcState = new NpcState(npcData);
            var locationState = new LocationState(currentLocation != null ? currentLocation : npcData.homeLocation);

            WorkingMemory workingMemory = WorkingMemory.Empty;
            if (memoryTuning != null)
            {
                var memorySelector = new MemorySelector();
                var memoryContext = new MemorySelectionContext(card, locationState, currentTurn);
                workingMemory = memorySelector.Select(npcState, memoryContext, memoryTuning);
            }

            var context = new NpcThinkContext(
                npcState, card, assumedCurrentBelief, workingMemory,
                locationState, npcData.availableActions, currentTurn);

            string prompt = BenchmarkPromptBuilder.Build(context, requestReasonAndConfidence);

            var result = new BenchmarkResult
            {
                benchmarkRunId = runId,
                scenarioId = string.IsNullOrEmpty(scenarioId) ? "manual" : scenarioId,
                provider = useFakeTransport ? "Fake" : (providerConfig != null ? providerConfig.provider.ToString() : "Unknown"),
                modelId = useFakeTransport ? "fake-transport" : (providerConfig != null ? providerConfig.modelId : "unknown"),
                npcId = npcData.npcId,
                informationCardId = card.cardId,
                rawPrompt = prompt,
            };

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            result.requestTimestamp = DateTime.UtcNow.ToString("o");

            int timeoutSeconds = providerConfig != null ? providerConfig.timeoutSeconds : 30;

            try
            {
                string raw = await InvokeTransportAsync(transport, prompt, timeoutSeconds);
                stopwatch.Stop();
                result.responseTimestamp = DateTime.UtcNow.ToString("o");
                result.latencyMs = stopwatch.ElapsedMilliseconds;
                result.rawResponse = raw;

                var parsed = ResponseParser.ParseForBenchmark(raw, npcData.availableActions, MaxDialogueLength);
                result.parseSuccess = parsed.IsValid;

                if (parsed.IsValid)
                {
                    result.parsedAction = parsed.ParsedAction;
                    result.parsedDialogue = parsed.ParsedDialogue;
                    result.parsedReason = parsed.ParsedReason;
                    result.parsedConfidence = parsed.ParsedConfidence;
                    result.parsedConfidenceAvailable = parsed.ConfidenceAvailable;
                }
                else
                {
                    result.errorType = "ParseError";
                    result.errorMessage = parsed.FailureReason;
                }

                if (transport is ITokenUsageReporting reporting &&
                    reporting.TryGetLastUsage(out int inTok, out int outTok, out int totTok))
                {
                    result.inputTokens = inTok;
                    result.outputTokens = outTok;
                    result.totalTokens = totTok;
                    result.tokensAvailable = true;
                }
                else
                {
                    result.tokensAvailable = false;
                }
            }
            catch (LlmTransportException ex)
            {
                stopwatch.Stop();
                result.responseTimestamp = DateTime.UtcNow.ToString("o");
                result.latencyMs = stopwatch.ElapsedMilliseconds;
                result.rawResponse = ex.RawResponseBody;
                result.parseSuccess = false;
                result.errorType = ex.WasCanceled ? "Canceled" : "TransportError";
                result.errorMessage = ex.Message;
                result.tokensAvailable = false;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                result.responseTimestamp = DateTime.UtcNow.ToString("o");
                result.latencyMs = stopwatch.ElapsedMilliseconds;
                result.parseSuccess = false;
                result.errorType = "UnexpectedError";
                result.errorMessage = ex.Message;
                result.tokensAvailable = false;
            }

            logger.Record(result);
            OnResultRecorded?.Invoke(result);
        }

        async Task<string> InvokeTransportAsync(ILlmTransport transport, string prompt, int timeoutSeconds)
        {
            activeCts = new CancellationTokenSource();

            Task<string> callTask = transport is ICancellableLlmTransport cancellable
                ? cancellable.SendAsync(prompt, activeCts.Token)
                : transport.SendAsync(prompt);

            Task timeoutTask = Task.Delay(TimeSpan.FromSeconds(Mathf.Max(1, timeoutSeconds)));
            Task completed = await Task.WhenAny(callTask, timeoutTask);

            if (completed == timeoutTask)
            {
                activeCts.Cancel();
                throw new LlmTransportException("BenchmarkRunner 측 Timeout으로 호출을 취소했습니다.", wasCanceled: true);
            }

            return await callTask;
        }

        ILlmTransport ResolveTransport()
        {
            if (useFakeTransport || providerConfig == null)
                return new FakeTransport(fakeTransportMode);

            if (!ApiKeyProvider.TryGetApiKey(providerConfig.provider.ToString(), out string apiKey))
            {
                Debug.LogWarning("[BenchmarkRunner] API 키를 찾을 수 없어 FakeTransport로 대체합니다 " +
                    "(환경 변수 BELIEF_LLM_API_KEY_* 또는 Belief/AI/Set LLM API Key... 확인).");
                return new FakeTransport(FakeTransportMode.AlwaysSuccess);
            }

            return providerConfig.provider switch
            {
                LlmProviderType.OpenAi => new OpenAiTransport(providerConfig, apiKey),
                _ => new FakeTransport(fakeTransportMode)
            };
        }

        void SetStatus(string status)
        {
            StatusText = status;
            OnStatusChanged?.Invoke(status);
        }
    }
}
