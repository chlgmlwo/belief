using System;

namespace Belief.AI.LLM.Benchmark
{
    /// <summary>
    /// 호출 1건의 기록. 타임스탬프는 DateTime이 아니라 ISO-8601 문자열로 저장한다
    /// (JsonUtility가 DateTime을 그대로 다루지 못하고, CSV에도 그대로 쓸 수 있어야 하므로).
    /// 토큰 정보가 없으면 0 + tokensAvailable=false로 명확히 구분한다.
    /// </summary>
    [Serializable]
    public class BenchmarkResult
    {
        public string benchmarkRunId;
        public string scenarioId;
        public string provider;
        public string modelId;
        public string npcId;
        public string informationCardId;

        public string requestTimestamp;
        public string responseTimestamp;
        public long latencyMs;

        public string rawPrompt;
        public string rawResponse;

        public bool parseSuccess;
        public string parsedAction;
        public string parsedDialogue;
        public string parsedReason;
        public float parsedConfidence;
        public bool parsedConfidenceAvailable;

        public int inputTokens;
        public int outputTokens;
        public int totalTokens;
        public bool tokensAvailable;

        public string errorType;
        public string errorMessage;
    }
}
