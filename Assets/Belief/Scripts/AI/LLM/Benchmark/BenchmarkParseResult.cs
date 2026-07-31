namespace Belief.AI.LLM.Benchmark
{
    /// <summary>ResponseParser.ParseForBenchmark의 결과. LlmValidationResult와 같은 원칙으로
    /// 예외를 던지지 않고 값으로만 성공/실패를 알린다.</summary>
    public readonly struct BenchmarkParseResult
    {
        public readonly bool IsValid;
        public readonly string FailureReason;
        public readonly string ParsedAction;
        public readonly string ParsedDialogue;
        public readonly string ParsedReason;
        public readonly float ParsedConfidence;
        public readonly bool ConfidenceAvailable;

        BenchmarkParseResult(bool isValid, string failureReason, string parsedAction, string parsedDialogue,
            string parsedReason, float parsedConfidence, bool confidenceAvailable)
        {
            IsValid = isValid;
            FailureReason = failureReason;
            ParsedAction = parsedAction;
            ParsedDialogue = parsedDialogue;
            ParsedReason = parsedReason;
            ParsedConfidence = parsedConfidence;
            ConfidenceAvailable = confidenceAvailable;
        }

        public static BenchmarkParseResult Success(string action, string dialogue, string reason, float confidence, bool confidenceAvailable) =>
            new BenchmarkParseResult(true, null, action, dialogue, reason, confidence, confidenceAvailable);

        public static BenchmarkParseResult Failure(string reason) =>
            new BenchmarkParseResult(false, reason, null, null, null, 0f, false);
    }
}
