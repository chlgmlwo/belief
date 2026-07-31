using System.Collections.Generic;
using Belief.Data;
using Belief.Domain;
using UnityEngine;

namespace Belief.Systems
{
    /// <summary>
    /// 오케스트레이터. 1) BeliefContext 생성 2) 각 Evaluator 호출 3) 결과 합산
    /// 4) 최종 Belief 적용. 판단 공식은 전혀 갖지 않는다 - 전부 IBeliefEvaluator 구현체에 있다.
    /// </summary>
    public class BeliefSystem
    {
        readonly IReadOnlyList<IBeliefEvaluator> evaluators;
        readonly BeliefTuningData tuning;
        readonly BeliefDebugRepository debugRepository;
        readonly LocationMechanicsSettings locationMechanics;

        public BeliefSystem(
            IReadOnlyList<IBeliefEvaluator> evaluators, BeliefTuningData tuning, BeliefDebugRepository debugRepository,
            LocationMechanicsSettings locationMechanics)
        {
            this.evaluators = evaluators;
            this.tuning = tuning;
            this.debugRepository = debugRepository;
            this.locationMechanics = locationMechanics;
        }

        public BeliefEvaluationResult Evaluate(
            NpcState npc, InformationCardData card, LocationState where,
            WorkingMemory workingMemory, int currentTurn)
        {
            // Location Mechanics V1 - 카드 원본 baseCredibility는 절대 건드리지 않고, 이 판단
            // 시점(=where, 항상 판단이 실제로 벌어진 장소)에서만 유효한 신뢰도를 별도로 계산한다.
            float baseCredibility = card.information.baseCredibility;
            float locationDelta = 0f;
            bool sensitiveMatched = false;
            float sensitiveBonus = 0f;
            if (locationMechanics != null && where != null && where.Data != null)
            {
                locationDelta = locationMechanics.GetCredibilityDelta(where.Data.credibilityModifier);
                sensitiveMatched = locationMechanics.IsSensitiveTypeMatch(card.information.informationType, where.Data.sensitiveInformationType);
                if (sensitiveMatched) sensitiveBonus = locationMechanics.sensitiveTypeMatchBonus;
            }
            float effectiveCredibility = Mathf.Clamp01(baseCredibility + locationDelta + sensitiveBonus);

            var context = new BeliefContext(npc, card, where, workingMemory, currentTurn, effectiveCredibility);

            float coreScore = 0f;
            float exceptionalScoreRaw = 0f;
            float credibilityContribution = 0f;
            var breakdown = new List<BeliefContribution>(evaluators.Count);

            foreach (var evaluator in evaluators)
            {
                var contribution = evaluator.Evaluate(context);
                breakdown.Add(contribution);

                if (contribution.IsExceptional) exceptionalScoreRaw += contribution.ScoreDelta;
                else coreScore += contribution.ScoreDelta;

                if (contribution.Type == BeliefContributionType.Credibility)
                    credibilityContribution = contribution.ScoreDelta;
            }

            float cappedExceptional = Mathf.Clamp(exceptionalScoreRaw, -tuning.maxExceptionalModifier, tuning.maxExceptionalModifier);
            float finalScore = Mathf.Clamp01(coreScore + cappedExceptional);

            var finalBelief = ScoreToBeliefState(finalScore);
            var baseOnlyBelief = ScoreToBeliefState(Mathf.Clamp01(coreScore));

            var result = new BeliefEvaluationResult(
                baseJudgmentScore: coreScore,
                rawExceptionalModifier: exceptionalScoreRaw,
                cappedExceptionalModifier: cappedExceptional,
                finalScore: finalScore,
                finalBelief: finalBelief,
                wasReversedByException: finalBelief != baseOnlyBelief,
                usedWorkingMemory: workingMemory,
                breakdown: breakdown,
                card: card,
                declaredSource: card.source,
                targetNpc: npc.Data,
                baseCredibility: baseCredibility,
                locationCredibilityDelta: locationDelta,
                sensitiveTypeMatched: sensitiveMatched,
                sensitiveTypeBonus: sensitiveBonus,
                effectiveCredibility: effectiveCredibility,
                credibilityEvaluatorContribution: credibilityContribution);

            debugRepository.Record(npc, card, result);
            return result;
        }

        public void Apply(NpcState npc, InformationCardData card, BeliefEvaluationResult result)
            => npc.SetBelief(card, result.FinalBelief);

        BeliefState ScoreToBeliefState(float score) =>
            score >= tuning.trustedThreshold ? BeliefState.Trusted :
            score >= tuning.plausibleThreshold ? BeliefState.Plausible :
            score >= tuning.needsVerificationThreshold ? BeliefState.NeedsVerification :
            score >= tuning.doubtfulThreshold ? BeliefState.Doubtful :
            BeliefState.Denied;
    }
}
