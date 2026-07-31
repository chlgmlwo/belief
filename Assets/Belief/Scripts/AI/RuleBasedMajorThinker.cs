using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Belief.Data;
using Belief.Debugging;
using Belief.Systems;
using UnityEngine;

namespace Belief.AI
{
    /// <summary>
    /// 결정론적 규칙 기반 구현. Mock이 아니라 개발/테스트 및 LLM 장애 시 비상 폴백으로
    /// 실제 출시 빌드에 포함되는 정식 컴포넌트. LLM과 동일 수준의 복잡성은 필요 없고
    /// 행동 선택 + 최소 대사 + 이동 판단 + 게임 진행 보장만 하면 된다. 실제로 기다릴 것이 전혀
    /// 없으므로(순수 CPU 계산) Task.FromResult로 즉시 완료된 Task를 반환한다 - 블로킹 위험 없음.
    /// </summary>
    public class RuleBasedMajorThinker : IMajorNpcThinker
    {
        public Task<NpcThinkResult> DecideAsync(NpcThinkContext context, object trace)
        {
            var chosen = ChooseAction(context);
            var dialogue = new DialogueContent(ChooseDialogue(context));
            return Task.FromResult(new NpcThinkResult(chosen, dialogue));
        }

        // 아래 수치는 전부 잠정값이다(Frozen 아님) - 사용자가 실측 결과를 보고 다시 조정할 수 있다.
        // 정확한 계산식은 DecideMove/ScoreCandidate 안에 그대로 노출돼 있다(별도 설정 파일 없음).
        //
        // 밸런스 조정(NPC 이동): Goal 존재만으로 거의 매 턴 이동이 발동하던 문제를 완화한다 -
        // GoalPresenceTerm을 0.5 -> 0.1로 낮추고, Stay/이동 분기 임계값을 0(양수면 이동)에서
        // 0.3(그 이상이어야 이동)으로 올렸다. 그 결과 "Goal만 있고 Belief 변화가 없는" 평상시에는
        // 0.1 < 0.3이라 Stay가 기본이 되고, 정보 수신으로 convictionRatio/doubtRatio가 커지거나
        // 후보가 선호 장소일 때만 임계값을 넘어 실제 이동으로 이어진다. ConvictionTermPerRatio/
        // WaverTermPerRatio/PreferredTerm/AvoidedTerm은 기존 값과 역할을 그대로 유지한다.
        const float GoalPresenceTerm = 0.1f;          // CurrentGoal이 있을 때만 더해지는 고정항(2. Goal 관련성) - 0.5에서 하향
        const float PreferredTerm = 0.5f;             // locationPreference.preferred (4. 보조 가중치)
        const float AvoidedTerm = -0.5f;              // locationPreference.avoided (4. 보조 가중치)
        const float ConvictionTermPerRatio = 0.3f;    // 모든 후보에 고르게 더해짐 - conviction 비율 1일 때 +0.3
        const float WaverTermPerRatio = 1.5f;         // 기피 후보에만 더해짐 - doubt 비율 1일 때 +1.5 (기피(-0.5)를
                                                       // 상쇄하고도 남아 선호(+0.5~+0.8) 후보를 역전할 수 있는 크기)
        const float MoveThreshold = 0.3f;             // 최고 점수가 이 값을 "초과"해야 이동(그 이하면 Stay) -
                                                       // 기존 0(양수면 이동)에서 상향, Goal 단독으로는 못 넘도록 함

        /// <summary>이동 판단 전체 점수식(§8 순서 그대로): 1) movementCandidates·현재위치 제외로
        /// 유효 후보 산출 -> 2) Goal 존재 여부(텍스트 내용은 파싱하지 않음, 존재만 반영) ->
        /// 3) Belief 분포(같은 카드별 Beliefs를 conviction/doubt 비율로 집계 - 카드 하나짜리 Intent
        /// 개념을 여러 카드에 걸쳐 근사) -> 4) locationPreference 보조항 -> 5~6) 최고 점수 후보 선택 ->
        /// 7) 동점만 무작위 tie-break -> 8) 최고 점수가 MoveThreshold(0.3) 이하이면 Stay.
        /// score(loc) = GoalPresenceTerm(있으면) + ConvictionTermPerRatio*convictionRatio(전체)
        ///            + PreferredTerm 또는 AvoidedTerm(해당 시) + (avoided일 때만) WaverTermPerRatio*doubtRatio</summary>
        public Task<NpcMoveResult> DecideMoveAsync(NpcMoveContext context, object trace)
        {
            return Task.FromResult(DecideMoveCore(context, trace));
        }

        NpcMoveResult DecideMoveCore(NpcMoveContext context, object trace)
        {
            if (!(context.Npc.Data is MajorNpcData major)) return new NpcMoveResult(null);
            if (context.Candidates == null || context.Candidates.Count == 0) return new NpcMoveResult(null);

            var (convictionRatio, doubtRatio) = BeliefRatios(context.Npc);
            bool hasGoal = !string.IsNullOrEmpty(context.Npc.CurrentGoal);

            LocationData best = null;
            float bestScore = float.NegativeInfinity;
            var ties = new List<LocationData>();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // 관찰 전용 - 아래 실제 판단 루프가 이미 계산하는 breakdown을 그대로 옮겨 담을 뿐,
            // 여기서 점수를 다시 계산하지 않는다. 호출자가 trace를 넘기지 않았다면(=리스너 없음)
            // 아예 만들지 않는다(오버헤드 없음).
            var traceEntries = trace != null
                ? new List<(LocationData loc, CandidateScoreBreakdown breakdown)>() : null;
#endif

            foreach (var loc in context.Candidates)
            {
                if (loc == null || loc == context.CurrentLocation) continue; // 5. 유효성 검사(현재 위치 제외)

                var breakdown = ScoreCandidate(major, loc, hasGoal, convictionRatio, doubtRatio);
                float score = breakdown.FinalScore;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                traceEntries?.Add((loc, breakdown));
#endif

                if (score > bestScore)
                {
                    bestScore = score;
                    ties.Clear();
                    ties.Add(loc);
                }
                else if (score == bestScore)
                {
                    ties.Add(loc);
                }
            }

            bool hadTie = ties.Count > 1;
            if (ties.Count > 0) best = ties[Random.Range(0, ties.Count)]; // 7. 동점일 때만 제한적 무작위

            // 8. 최고 점수가 MoveThreshold를 넘는 유효한 목적지가 없으면 Stay.
            bool isStay = !(bestScore > MoveThreshold && best != null);
            var result = isStay ? new NpcMoveResult(null) : new NpcMoveResult(best);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (traceEntries != null)
                WriteMoveTrace(trace, context, major, convictionRatio, doubtRatio, hasGoal, traceEntries, best, isStay, hadTie);
#endif

            return result;
        }

        readonly struct CandidateScoreBreakdown
        {
            public readonly float GoalTerm;
            public readonly float ConvictionTerm;
            public readonly float PreferenceTerm;
            public readonly float DoubtOverrideTerm;
            public float FinalScore => GoalTerm + ConvictionTerm + PreferenceTerm + DoubtOverrideTerm;

            public CandidateScoreBreakdown(float goalTerm, float convictionTerm, float preferenceTerm, float doubtOverrideTerm)
            {
                GoalTerm = goalTerm;
                ConvictionTerm = convictionTerm;
                PreferenceTerm = preferenceTerm;
                DoubtOverrideTerm = doubtOverrideTerm;
            }
        }

        static CandidateScoreBreakdown ScoreCandidate(MajorNpcData major, LocationData loc, bool hasGoal, float convictionRatio, float doubtRatio)
        {
            float goalTerm = hasGoal ? GoalPresenceTerm : 0f; // 2. Goal 관련성(존재 여부만 - 텍스트 내용 파싱 금지 규칙 준수)
            float convictionTerm = ConvictionTermPerRatio * convictionRatio; // 3. Belief(확신 방향) - 모든 후보에 균등 적용

            bool isPreferred = major.preferredLocations != null && major.preferredLocations.Contains(loc);
            bool isAvoided = !isPreferred && major.avoidedLocations != null && major.avoidedLocations.Contains(loc);

            float preferenceTerm = 0f;
            float doubtOverrideTerm = 0f;
            if (isPreferred)
            {
                preferenceTerm = PreferredTerm; // 4. locationPreference 보조항
            }
            else if (isAvoided)
            {
                preferenceTerm = AvoidedTerm;
                doubtOverrideTerm = WaverTermPerRatio * doubtRatio; // 3. Belief(회의 방향) - 기피 장소에 한해 신뢰 붕괴를 반영
            }

            return new CandidateScoreBreakdown(goalTerm, convictionTerm, preferenceTerm, doubtOverrideTerm);
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>호출자(MajorNpcMovementSystem, 또는 LLM 실패 시 LlmMajorThinker)가 만들어 넘긴
        /// 레코드에 이동 후보 점수(I절)만 채운다 - 레코드를 새로 만들거나 Publish하지 않는다.
        /// 최종 결과/Publish는 항상 호출자 쪽 책임이다(판단 1건 = 레코드 1개를 보장하기 위함).</summary>
        static void WriteMoveTrace(
            object traceObj, NpcMoveContext context, MajorNpcData major, float convictionRatio, float doubtRatio, bool hasGoal,
            List<(LocationData loc, CandidateScoreBreakdown breakdown)> rawEntries,
            LocationData best, bool isStay, bool hadTie)
        {
            var trace = traceObj as NpcDecisionTraceBuilder;
            if (trace == null) return;

            trace.WithReceivedInformation(null);
            trace.WithStateBefore(context.Npc, null);
            trace.WithGoalPresenceUsedInMove(hasGoal);

            var entries = new List<MoveCandidateScoreEntry>();
            foreach (var (loc, b) in rawEntries)
            {
                entries.Add(new MoveCandidateScoreEntry
                {
                    LocationId = loc.locationId,
                    LocationDisplayName = loc.displayName,
                    GoalTerm = b.GoalTerm,
                    ConvictionTerm = b.ConvictionTerm,
                    PreferenceTerm = b.PreferenceTerm,
                    DoubtOverrideTerm = b.DoubtOverrideTerm,
                    FinalScore = b.FinalScore,
                    IsSelected = !isStay && loc == best
                });
            }

            trace.WithMoveScoring(
                context.CurrentLocation, context.Candidates, major.preferredLocations, major.avoidedLocations,
                convictionRatio, doubtRatio, entries, isStay ? null : best, isStay, hadTie);
        }
#endif

        /// <summary>NpcState.Beliefs(카드별 BeliefState)를 conviction(Plausible/Trusted)과
        /// doubt(Doubtful/Denied) 비율로 집계한다. ChooseAction의 단일-카드 Intent 개념을 여러
        /// 카드에 걸친 이동 판단에 맞게 근사한 것 - NpcState/BeliefSystem 자체는 건드리지 않는다.</summary>
        static (float convictionRatio, float doubtRatio) BeliefRatios(Belief.Domain.NpcState npc)
        {
            int total = npc.Beliefs.Count;
            if (total == 0) return (0f, 0f);

            int conviction = 0, doubt = 0;
            foreach (var kvp in npc.Beliefs)
            {
                if (kvp.Value == BeliefState.Plausible || kvp.Value == BeliefState.Trusted) conviction++;
                else if (kvp.Value == BeliefState.Doubtful || kvp.Value == BeliefState.Denied) doubt++;
            }

            return ((float)conviction / total, (float)doubt / total);
        }

        static DialogueLineData ChooseDialogue(NpcThinkContext context)
        {
            if (!(context.Npc.Data is MajorNpcData major) || major.beliefDialogues == null) return null;

            var tag = context.CurrentBelief switch
            {
                BeliefState.Trusted => "Trust",
                BeliefState.Plausible => "Possible",
                BeliefState.NeedsVerification => "NeedVerification",
                BeliefState.Doubtful => "Doubt",
                BeliefState.Denied => "Reject",
                _ => null
            };
            if (tag == null) return null;

            return major.beliefDialogues.FirstOrDefault(d => d != null && d.contextTag == tag);
        }

        static NpcActionData ChooseAction(NpcThinkContext context)
        {
            if (context.CandidateActions == null || context.CandidateActions.Count == 0) return null;

            var preferredIntent = context.CurrentBelief switch
            {
                BeliefState.Trusted => NpcActionIntent.Comply,
                BeliefState.Plausible => NpcActionIntent.Escalate,
                BeliefState.Doubtful => NpcActionIntent.Verify,
                BeliefState.NeedsVerification => NpcActionIntent.Verify,
                BeliefState.Denied => NpcActionIntent.Ignore,
                _ => NpcActionIntent.Wait
            };

            return context.CandidateActions.FirstOrDefault(a => a.intent == preferredIntent)
                ?? context.CandidateActions[0];
        }
    }
}
