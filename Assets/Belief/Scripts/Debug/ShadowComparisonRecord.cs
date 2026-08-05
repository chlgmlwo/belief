using System;

namespace Belief.Debugging
{
    /// <summary>Shadow 요청 하나의 최종 처리 상태.</summary>
    public enum ShadowOutcome
    {
        /// <summary>응답이 도착하고 검증까지 통과했다 - 비교 통계에 들어가는 유일한 값.</summary>
        Compared,
        /// <summary>Timeout/Transport 오류/파싱 실패/후보 밖 응답 등으로 결과를 얻지 못했다.</summary>
        Failed,
        /// <summary>동시 실행 상한과 대기 상한을 모두 넘겨 <b>발사 자체를 하지 않았다</b>(토큰 0).</summary>
        SkippedConcurrencyLimit,
        /// <summary>응답은 왔지만 그 사이 미션 재시작/씬 전환/게임오버로 세션이 바뀌었다.
        /// 새 시도의 기록에 섞이면 안 되므로 폐기하고 정상 통계에서도 제외한다.</summary>
        StaleSession,
    }

    /// <summary>
    /// 규칙 기반 실제 판단과 Shadow LLM 통합 판단을 나란히 담는 관찰 기록.
    /// <b>차이는 오류가 아니다</b> - 규칙 기반 Belief는 수치가 만든 값이고 LLM은 같은 정보를
    /// 자연어로 받으므로, 둘이 다르다고 해서 어느 쪽이 틀린 것이 아니다. Goal 차이도 마찬가지로
    /// 관찰값이며 실패로 집계하지 않는다.
    /// </summary>
    [Serializable]
    public class ShadowComparisonRecord
    {
        // ── 세션 식별 (늦게 도착한 응답이 어느 시도의 것인지 가린다) ──────────────
        public string RequestId;
        public string StageId;
        public string MissionId;
        public int MissionAttemptId;
        public int Turn;
        public string NpcId;
        public string CardId;

        // ── 입력 (판단 직전 스냅샷) ────────────────────────────────────────────
        public string SourceId;
        public float CardCredibility;
        public float SourceTrust;
        public string BeliefBefore;
        public string GoalBefore;
        public string[] ProfileTags = Array.Empty<string>();
        public string[] UsableRelationships = Array.Empty<string>();   // "npcId|label|strength"
        public string PropagatorNpcId;
        public string[] PresentNpcIds = Array.Empty<string>();
        public int WorkingMemoryCount;

        // ── 규칙 기반 실제 결과 (월드에 적용된 값) ─────────────────────────────
        public string RuleBelief;
        public string RuleActionId;
        public string RuleGoal;
        public string RuleDialogue;

        // ── Shadow LLM 결과 (월드에 적용되지 않음) ─────────────────────────────
        public string LlmInterpretation;
        public string LlmBelief;
        public string LlmGoal;
        public string LlmActionId;
        public string LlmDestinationId;   // null/"" 이면 stay
        public string LlmDialogue;
        public string LlmPrimaryReason;
        public string LlmProfileInfluence;
        public string LlmRelationshipInfluence;

        // ── 차이 (관찰값 - 실패 아님) ──────────────────────────────────────────
        public bool BeliefDiffers;
        public int BeliefStepDelta;       // +1이면 LLM이 한 단계 더 믿음
        public bool ActionDiffers;
        public bool GoalDiffers;

        // ── 진단 ──────────────────────────────────────────────────────────────
        public ShadowOutcome Outcome;
        public string FailureReason;
        public double LatencyMs;
        public string PromptText;         // shadowPromptLogging이 켜졌을 때만 채워진다
        public string RawResponse;
    }
}
