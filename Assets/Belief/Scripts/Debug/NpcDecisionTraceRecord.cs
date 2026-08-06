using System;
using System.Collections.Generic;

namespace Belief.Debugging
{
    /// <summary>NPC 판단 관찰 기록 하나. 게임 판단에 관여하지 않는 순수 관측 데이터 - 이 클래스의
    /// 어떤 필드도 실제 게임 로직이 읽지 않는다(쓰기 전용, Hub -> Store 방향으로만 흐른다).
    /// JsonUtility로 그대로 직렬화 가능하도록 전부 public 필드 + [Serializable]로 구성한다.</summary>
    [Serializable]
    public class NpcDecisionTraceRecord
    {
        // ---- 식별 (section 2) ----
        public string DecisionId;
        public string StageId;
        public int StageTurn;
        public string MissionId;
        public int MissionTurn;
        public string NpcId;
        public string NpcDisplayName;
        public string ThinkerMode; // "RuleOnly" / "FakeLlm"
        public string DecisionType; // "InformationJudgment" / "TurnMove"
        public string StartTimestampUtc; // ISO 8601 문자열(직렬화 단순화를 위해 DateTime 대신 문자열로 보관)
        public string EndTimestampUtc;
        public double ProcessingTimeMs;
        public bool HasError;
        public string ErrorMessage;

        // ---- A. 기본 메타데이터 ----
        public string CurrentLocationId;
        public string CurrentLocationDisplayName;

        // ---- B. 수신한 정보 ----
        public bool HasReceivedInformation;
        public string CardId;
        public string CardTitle;
        public string CardCategoryId;
        public string CardDescription;
        public string CardSourceId;
        public string CardSourceDisplayName;
        public string CardDeliveryType; // Spread / Deliver
        public string CardTargetType;   // Place / Npc
        public string[] CardTags;

        // ---- B-부속. Location Mechanics V1 - 재확산으로 도달한 경우에만 채워짐(§3, §4) ----
        public bool ReachedViaRespread;
        public float LocBaseSpreadPower;
        public string LocSourceSpreadSpeed;
        public float LocSpreadMultiplier;
        public float LocEffectiveSpreadPower;
        public string LocTargetNpcDensity;
        public int LocCandidateNpcCount;
        public int LocDensityTargetLimit;
        public int LocSelectedSecondaryRecipientCount;
        public int LocExcludedRecipientCount;

        // ---- C. 판단 전 NPC 상태 ----
        public string GoalBefore;
        public string BeliefStageBeforeLabel; // 한글 표시명(신뢰함 등) - 카드 기준 GetBelief 결과
        public string BeliefStageBeforeRaw;   // BeliefState enum 이름
        public List<BeliefEntrySnapshot> BeliefsBefore = new List<BeliefEntrySnapshot>();
        public float ConvictionRatio;
        public float DoubtRatio;
        public string IntentBefore; // 계산 가능한 경우만(F 섹션과 동일한 매핑, 값 변경 없음)
        public List<RelationshipSnapshot> Relationships = new List<RelationshipSnapshot>();

        // ---- D. Belief 평가 과정 (BeliefEvaluationResult.Breakdown 그대로) ----
        public List<EvaluatorEntry> Evaluators = new List<EvaluatorEntry>();
        public float BaseJudgmentScore;
        public float RawExceptionalModifier;
        public float CappedExceptionalModifier;
        public float FinalScore;

        // ---- D-부속. Location Mechanics V1 - CredibilityEvaluator 입력값 분해(§6) ----
        public float LocBaseCredibility;
        public float LocCredibilityDelta;
        public string LocCardInformationType;   // 카드의 InformationType (없으면 Unspecified)
        public string LocSensitiveInfoType;      // 장소의 sensitiveInformationType
        public bool LocSensitiveTypeMatched;
        public float LocSensitiveTypeBonus;
        public float LocEffectiveCredibility;
        public float LocCredibilityEvaluatorContribution;

        // ---- E. 믿음 변화 ----
        public string BeliefBeforeLabel;
        public string BeliefAfterLabel;
        public string BeliefAfterRaw;
        public bool BeliefChanged;
        public bool WasReversedByException;

        // ---- F. 행동 판단 ----
        public string ActionInputBeliefStage;
        public string ActionIntentMapping;
        public List<ActionCandidateEntry> ActionCandidates = new List<ActionCandidateEntry>();
        public string SelectedActionId;
        public string SelectedActionLabel;

        // ---- G. 대사 선택 ----
        public string DialogueContextTag;
        public List<DialogueCandidateEntry> DialogueCandidates = new List<DialogueCandidateEntry>();
        public string SelectedDialogueText;
        public bool DialogueIsGenerated;
        public int SelectedDialogueIndex = -1;

        // ---- H. Goal ----
        public string GoalTextBefore;
        public string GoalTextAfter;
        public bool GoalChanged;
        public bool GoalContentParsed; // 항상 false로 명시 - 텍스트 내용은 어디서도 파싱하지 않음
        public bool GoalPresenceUsed;  // 이동 판단에서 존재 여부만 사용했는지

        // ---- I. 이동 판단 ----
        public string MoveCurrentLocationId;
        public List<string> MovementCandidateIds = new List<string>();
        public List<string> PreferredLocationIds = new List<string>();
        public List<string> AvoidedLocationIds = new List<string>();
        public List<MoveCandidateScoreEntry> MoveCandidateScores = new List<MoveCandidateScoreEntry>();
        public List<ExcludedCandidateEntry> ExcludedMoveCandidates = new List<ExcludedCandidateEntry>();
        public float MoveConvictionRatio;
        public float MoveUnverifiedRatio;
        public float MoveDoubtRatio;
        public float MoveBestScore;
        public float MoveStayScore; // 현재 위치의 매력도 - MoveBestScore가 이 값을 초과해야 이동한다
        public bool MoveHadTie;
        public string SelectedDestinationId;
        public bool MoveIsStay;
        public string MovementRulesStatus = "데이터 존재 / 현재 RuleBased 코드에서는 미사용";

        // ---- J. LLM 및 폴백 상태 ----
        public bool UsedLlm;
        public int TimeoutMs; // 이번 요청에 적용된 Timeout 설정값(단일 설정 위치: LlmMajorThinker 생성자 인자)
        public string LlmRequestStartUtc;
        public double LlmResponseTimeMs;
        public bool LlmResponseSucceeded;
        public bool LlmParseSucceeded;
        public bool LlmDestinationValid;
        public bool FallbackOccurred;
        public string FallbackReason; // Timeout/TransportException/EmptyResponse/JsonParseFailure/InvalidDestination/Cancelled/""
        public string UsedResultSource; // "LLM" / "RuleBased(Fallback)" / "N/A"

        // ── 판단 근거(1단계) - 검증을 통과한 값만 들어온다(지어낸 인물/태그는 여기 도달 전에 무효 처리) ──
        // 행동 판단과 이동 판단은 서로 다른 호출이므로, 같은 턴 같은 NPC라도 이 값들이 다를 수 있다.
        // ── IntegratedLlm 실제 적용 전용 ─────────────────────────────────────────
        // ShadowComparisonRecord와 섞지 않는다 - 그쪽은 "규칙 vs LLM 비교"용이라 적용 개념이 없다.
        public string Mode;                      // RuleOnly / Shadow / IntegratedLlm / IntegratedLlm-Fallback
        public string RequestId;
        public int MissionAttemptId;
        public string ApplicationKey;
        public string FinalInterpretation;
        public int InputTokens;
        public int OutputTokens;
        /// <summary>단가가 코드에 없으므로 비용은 계산하지 않는다 - 외부 설정이 생기기 전까지 true.</summary>
        public bool CostUnavailable = true;
        public string EstimatedCost;
        public string[] SoftWarnings = System.Array.Empty<string>();
        public bool DuplicateApplicationBlocked;
        public bool StaleRequestDiscarded;
        public string DestinationReservationType; // None / Stay / MoveTo

        public string GroundsPrimaryReason;      // profile/relationship/belief/goal/source/situation/location
        public string GroundsProfileInfluence;   // 이 NPC의 5개 성향 태그 중 하나, 없으면 ""
        public string GroundsRelationshipInfluence; // 이번 문맥에 등장하는 관계 NPC의 npcId, 없으면 ""

        public bool ResolvedByLlm;      // 최종적으로 LLM 결과가 확정되었는지
        public bool ResolvedByFallback; // 최종적으로 RuleBased 폴백 결과가 확정되었는지
        public bool LateResponseDiscarded; // Timeout 이후 도착한 응답을 폐기했는지 - 이 판단이 Publish된 뒤에
                                            // 실제로 늦은 응답이 도착하면 이 필드만 나중에 true로 갱신될 수 있다
                                            // (레코드는 참조 타입이라 살아있는 Window에는 즉시 반영되지만,
                                            // 이 시점 이전에 이미 Export된 JSONL 스냅샷에는 반영되지 않는다).
        public string PromptText;
        public string RawResponseText;

        // ---- K. 최종 실행 결과 ----
        public string FinalIntent;
        public string FinalActionId;
        public string FinalDialogueText;
        public string FinalGoal;
        public string FinalMoveDestinationId;
        public int ActionResolutionCount;
        public string PositionBeforeMove;
        public string PositionAfterMove;
        public string MissionImpactNote; // 참고용 서술(판정 로직 자체는 건드리지 않음) - 비어 있을 수 있음

        // ---- K-부속. 실행된 Action Effect가 실제로 어떤 상태를 남겼는지(관찰만, 상태를 만들지 않음) ----
        public string AppliedEffectType; // 예: "CompositeEffect[LogWorldEventEffect,RecordInformationWorldStateEffect]", Effect가 없으면 "None"
        public bool InvestigationStateChanged; // 이번 판단으로 InvestigationState가 생성/갱신됐는지(ActivatedTurn==이번 턴 기준)
        public string InvestigationStateResultType; // 생성/갱신된 경우의 InformationResultType, 아니면 null
        public bool MemoryAdded; // 이번 판단으로 LongMemory 항목이 추가됐는지
    }

    [Serializable]
    public class BeliefEntrySnapshot
    {
        public string CardId;
        public string BeliefStateRaw;
    }

    [Serializable]
    public class RelationshipSnapshot
    {
        public string TargetNpcId;
        public string TargetDisplayName;
        public string RelationshipTypeLabel;
    }

    [Serializable]
    public class EvaluatorEntry
    {
        public string EvaluatorName; // BeliefContributionType.ToString()
        public float ScoreDelta;
        public bool IsExceptional;
        public string InputSummary; // 데이터에 없으면 "(데이터 없음 - Evaluator가 별도 근거를 반환하지 않음)"
    }

    [Serializable]
    public class ActionCandidateEntry
    {
        public string ActionId;
        public string DisplayLabel;
        public string Intent;
        public bool IntentMatches; // context.CurrentBelief로부터의 매핑 Intent와 일치하는지(공개 필드만 사용)
        public bool IsSelected;
    }

    [Serializable]
    public class DialogueCandidateEntry
    {
        public string ContextTag;
        public string Text;
        public bool IsSelected;
    }

    [Serializable]
    public class MoveCandidateScoreEntry
    {
        public string LocationId;
        public string LocationDisplayName;
        public float GoalTerm;
        public float BeliefTerm; // conviction/unverified/doubt 세 항의 합(모든 후보에 균등)
        public float PreferenceTerm; // Preferred(+)/Avoided(-)/Neutral(0)
        public float DoubtOverrideTerm; // 기피 후보에만 적용되는 항
        public float FinalScore;
        public bool IsSelected;
    }

    [Serializable]
    public class ExcludedCandidateEntry
    {
        public string LocationId;
        public string Reason; // "현재 위치" 등
    }
}
