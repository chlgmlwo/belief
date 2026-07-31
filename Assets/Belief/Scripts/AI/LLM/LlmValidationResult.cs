using Belief.Data;

namespace Belief.AI.LLM
{
    /// <summary>ResponseParser의 결과. 실패해도 예외를 던지지 않고 이 값으로만 알린다.</summary>
    public readonly struct LlmValidationResult
    {
        public readonly bool IsValid;
        public readonly string FailureReason;
        public readonly NpcActionData ChosenAction;
        public readonly string Dialogue;

        LlmValidationResult(bool isValid, string failureReason, NpcActionData chosenAction, string dialogue)
        {
            IsValid = isValid;
            FailureReason = failureReason;
            ChosenAction = chosenAction;
            Dialogue = dialogue;
        }

        public static LlmValidationResult Success(NpcActionData chosenAction, string dialogue) =>
            new LlmValidationResult(true, null, chosenAction, dialogue);

        public static LlmValidationResult Failure(string reason) =>
            new LlmValidationResult(false, reason, null, null);
    }

    /// <summary>ResponseParser.ParseMove의 결과. Destination==null이면서 IsValid==true인 경우는
    /// LLM이 명시적으로 "stay"를 반환한 정상 응답이다 - IsValid==false(파싱/검증 실패)와는 다르다.</summary>
    public readonly struct LlmMoveValidationResult
    {
        public readonly bool IsValid;
        public readonly string FailureReason;
        public readonly LocationData Destination;

        LlmMoveValidationResult(bool isValid, string failureReason, LocationData destination)
        {
            IsValid = isValid;
            FailureReason = failureReason;
            Destination = destination;
        }

        public static LlmMoveValidationResult Success(LocationData destination) =>
            new LlmMoveValidationResult(true, null, destination);

        public static LlmMoveValidationResult Failure(string reason) =>
            new LlmMoveValidationResult(false, reason, null);
    }
}
