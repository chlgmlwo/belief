using System.Collections.Generic;
using Belief.Data;
using Belief.Domain;

namespace Belief.Systems
{
    public readonly struct BeliefEvaluationResult
    {
        public readonly float BaseJudgmentScore;
        public readonly float RawExceptionalModifier;
        public readonly float CappedExceptionalModifier;
        public readonly float FinalScore;
        public readonly BeliefState FinalBelief;
        public readonly bool WasReversedByException;
        public readonly WorkingMemory UsedWorkingMemory;
        public readonly IReadOnlyList<BeliefContribution> Breakdown;

        /// <summary>이 평가가 대상으로 삼은 정보 카드. Information/DeclaredSource는 여기서 파생된다
        /// (card.information / card.source) - 별도 문자열 필드로 복제하지 않는다.</summary>
        public readonly InformationCardData Card;

        /// <summary>카드가 스스로 주장하는 출처. SourceEvaluator 기여분과 대응. card.source와 동일하지만
        /// 평가 시점 값을 그대로 고정해 로그/디버그에서 바로 꺼내 쓸 수 있게 별도 보관한다.</summary>
        public readonly InfoSourceData DeclaredSource;

        /// <summary>이 평가의 대상이 된 NPC(믿음이 갱신되는 주체).</summary>
        public readonly NpcData TargetNpc;

        // ---- Location Mechanics V1 - 카드 원본 baseCredibility는 그대로 두고, 이 판단 시점에
        // 계산된 유효 신뢰도의 각 구성 요소를 관찰/디버그용으로 그대로 보존한다. ----
        public readonly float BaseCredibility;
        public readonly float LocationCredibilityDelta;
        public readonly bool SensitiveTypeMatched;
        public readonly float SensitiveTypeBonus;
        public readonly float EffectiveCredibility;
        public readonly float CredibilityEvaluatorContribution;

        public BeliefEvaluationResult(
            float baseJudgmentScore,
            float rawExceptionalModifier,
            float cappedExceptionalModifier,
            float finalScore,
            BeliefState finalBelief,
            bool wasReversedByException,
            WorkingMemory usedWorkingMemory,
            IReadOnlyList<BeliefContribution> breakdown,
            InformationCardData card,
            InfoSourceData declaredSource,
            NpcData targetNpc,
            float baseCredibility,
            float locationCredibilityDelta,
            bool sensitiveTypeMatched,
            float sensitiveTypeBonus,
            float effectiveCredibility,
            float credibilityEvaluatorContribution)
        {
            BaseJudgmentScore = baseJudgmentScore;
            RawExceptionalModifier = rawExceptionalModifier;
            CappedExceptionalModifier = cappedExceptionalModifier;
            FinalScore = finalScore;
            FinalBelief = finalBelief;
            WasReversedByException = wasReversedByException;
            UsedWorkingMemory = usedWorkingMemory;
            Breakdown = breakdown;
            Card = card;
            DeclaredSource = declaredSource;
            TargetNpc = targetNpc;
            BaseCredibility = baseCredibility;
            LocationCredibilityDelta = locationCredibilityDelta;
            SensitiveTypeMatched = sensitiveTypeMatched;
            SensitiveTypeBonus = sensitiveTypeBonus;
            EffectiveCredibility = effectiveCredibility;
            CredibilityEvaluatorContribution = credibilityEvaluatorContribution;
        }
    }
}
