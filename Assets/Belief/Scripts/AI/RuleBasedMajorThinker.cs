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
        // 밸런스 조정 2차(2026-08-06, 전 구역 LLM 플레이 실측 858건 기준). 이전 구조는 고정 임계값
        // 0.3과 PreferredTerm 0.5 때문에 이동이 확률이 아니라 켜짐/꺼짐 스위치였다 - 선호 장소가
        // 2곳이면 0.1+0.5=0.6으로 매 턴 왕복(집사 93.8%, 하급경비 81.3%), 1곳(자기 집)이거나
        // 0곳이면 최고점이 0.1이라 영구 정착(공작부인·기사단장·하녀·영주 전부 이동 0회)했다.
        // 그 결과 NpcsLeaveLocation/NpcAtLocation/NpcsGather 계열 미션 조건이 성립 불가가 됐다.
        //
        // 두 가지를 바꾼다.
        // 1) NeedsVerification 데드존 제거. BeliefRatios가 Plausible/Trusted만 conviction,
        //    Doubtful/Denied만 doubt로 세는 바람에 NeedsVerification은 양쪽 다 0점이었는데,
        //    실측 판단의 78%가 바로 그 등급이다. 즉 NPC 판단의 대부분이 이동 점수에 아무 영향을
        //    주지 못했고, 비율 분모만 키워 "정보를 줄수록 덜 움직이는" 역인센티브까지 있었다.
        //    unverifiedRatio를 독립 항으로 승격해 "확인이 필요하다"가 실제 이동 동기가 되게 한다.
        // 2) 고정 임계값을 현재 위치의 매력도(StayScore)로 대체. 후보에서 현재 위치를 제외하는
        //    구조 탓에 "지금 있는 곳이 내 선호 장소"라는 사실이 점수에 전혀 반영되지 않았다.
        //    이제 선호 장소에 있으면 머무는 쪽이 0.25만큼 유리해져 평상시 왕복이 사라지고,
        //    반대로 기피 장소에 있으면 떠나는 쪽이 유리해진다.
        const float GoalPresenceTerm = 0.1f;          // CurrentGoal이 있을 때만 더해지는 고정항(2. Goal 관련성)
        const float PreferredTerm = 0.25f;            // locationPreference.preferred - 0.5에서 하향(단독으로는 못 움직이게)
        const float AvoidedTerm = -0.3f;              // locationPreference.avoided - -0.5에서 완화
        const float ConvictionTermPerRatio = 0.6f;    // 모든 후보에 고르게 더해짐 - conviction 비율 1일 때 +0.6
        const float UnverifiedTermPerRatio = 0.5f;    // 신규 - NeedsVerification 비율 1일 때 +0.5(확인하러 나선다)
        const float DoubtTermPerRatio = 0.4f;         // 신규 - doubt 비율 1일 때 모든 후보에 +0.4(불신도 이동 동기)
        const float WaverTermPerRatio = 0.5f;         // 기피 후보에만 <b>추가로</b> 더해짐 - 1.5에서 대폭 하향.
                                                       // 1.5였을 때는 기피(-0.5)를 뒤집고 선호(+0.6)까지 역전해
                                                       // "의심하면 기피 장소가 최우선 목적지가 되는" 결과였다.
        const float StayInertia = 0.3f;               // 현재 위치의 기본 매력도 - 후보 최고점이 StayScore를
                                                       // "초과"해야 이동한다(기존 MoveThreshold를 관성으로 재해석)

        /// <summary>이동 판단 전체 점수식(§8 순서 그대로): 1) movementCandidates·현재위치 제외로
        /// 유효 후보 산출 -> 2) Goal 존재 여부(텍스트 내용은 파싱하지 않음, 존재만 반영) ->
        /// 3) Belief 분포(같은 카드별 Beliefs를 conviction/unverified/doubt 비율로 집계 - 카드 하나짜리
        /// Intent 개념을 여러 카드에 걸쳐 근사) -> 4) locationPreference 보조항 -> 5~6) 최고 점수 후보
        /// 선택 -> 7) 동점만 무작위 tie-break -> 8) 최고 점수가 <b>현재 위치의 StayScore 이하</b>이면 Stay.
        /// score(loc)  = GoalPresenceTerm(있으면) + ConvictionTermPerRatio*convictionRatio
        ///             + UnverifiedTermPerRatio*unverifiedRatio + DoubtTermPerRatio*doubtRatio
        ///             + PreferredTerm 또는 AvoidedTerm(해당 시) + (avoided일 때만) WaverTermPerRatio*doubtRatio
        /// stayScore   = StayInertia + (현재 위치가 선호면 PreferredTerm, 기피면 AvoidedTerm)
        /// 세 belief 항은 모든 후보에 균등하게 들어가므로 <b>어디로</b> 갈지는 바꾸지 않고
        /// <b>움직일지 말지</b>만 결정한다 - 방향은 순수하게 locationPreference가 정한다.</summary>
        public Task<NpcMoveResult> DecideMoveAsync(NpcMoveContext context, object trace)
        {
            return Task.FromResult(DecideMoveCore(context, trace));
        }

        NpcMoveResult DecideMoveCore(NpcMoveContext context, object trace)
        {
            if (!(context.Npc.Data is MajorNpcData major)) return new NpcMoveResult(null);

            var (convictionRatio, unverifiedRatio, doubtRatio) = BeliefRatios(context.Npc);
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

            // 후보가 아예 없어도 여기를 그냥 통과시킨다 - 예전에는 이 경우 조기 return이라
            // trace가 한 번도 채워지지 않아 MoveIsStay가 기본값 false로 남았고, 실제로는 머물렀는데
            // 기록상 "이동"으로 잡혀 이동률 집계가 부풀려졌다(실측 265건 중 100건이 이 허위 이동).
            foreach (var loc in context.Candidates ?? (IReadOnlyList<LocationData>)System.Array.Empty<LocationData>())
            {
                if (loc == null || loc == context.CurrentLocation) continue; // 5. 유효성 검사(현재 위치 제외)

                var breakdown = ScoreCandidate(major, loc, hasGoal, convictionRatio, unverifiedRatio, doubtRatio);
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

            // 8. 현재 위치의 매력도(StayScore)를 넘는 후보가 없으면 Stay.
            float stayScore = StayScore(major, context.CurrentLocation);
            bool isStay = !(bestScore > stayScore && best != null);
            var result = isStay ? new NpcMoveResult(null) : new NpcMoveResult(best);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (traceEntries != null)
                WriteMoveTrace(trace, context, major, convictionRatio, unverifiedRatio, doubtRatio, stayScore,
                    hasGoal, traceEntries, best, isStay, hadTie);
#endif

            return result;
        }

        readonly struct CandidateScoreBreakdown
        {
            public readonly float GoalTerm;
            /// <summary>conviction/unverified/doubt 세 항의 합. 예전에는 conviction 하나뿐이라
            /// ConvictionTerm이었다.</summary>
            public readonly float BeliefTerm;
            public readonly float PreferenceTerm;
            public readonly float DoubtOverrideTerm;
            public float FinalScore => GoalTerm + BeliefTerm + PreferenceTerm + DoubtOverrideTerm;

            public CandidateScoreBreakdown(float goalTerm, float beliefTerm, float preferenceTerm, float doubtOverrideTerm)
            {
                GoalTerm = goalTerm;
                BeliefTerm = beliefTerm;
                PreferenceTerm = preferenceTerm;
                DoubtOverrideTerm = doubtOverrideTerm;
            }
        }

        static CandidateScoreBreakdown ScoreCandidate(MajorNpcData major, LocationData loc, bool hasGoal,
            float convictionRatio, float unverifiedRatio, float doubtRatio)
        {
            float goalTerm = hasGoal ? GoalPresenceTerm : 0f; // 2. Goal 관련성(존재 여부만 - 텍스트 내용 파싱 금지 규칙 준수)

            // 3. Belief 분포 - 세 등급 전부가 이동 동기가 된다. 모든 후보에 균등 적용되므로
            //    목적지 선택에는 영향이 없고, StayScore와의 비교(=움직일지 말지)에만 작용한다.
            float beliefTerm = ConvictionTermPerRatio * convictionRatio
                             + UnverifiedTermPerRatio * unverifiedRatio
                             + DoubtTermPerRatio * doubtRatio;

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
                doubtOverrideTerm = WaverTermPerRatio * doubtRatio; // 기피 장소에 한해 신뢰 붕괴가 회피를 완화한다
            }

            return new CandidateScoreBreakdown(goalTerm, beliefTerm, preferenceTerm, doubtOverrideTerm);
        }

        /// <summary>현재 위치에 머무는 쪽의 점수. 후보 점수와 같은 축에서 비교하기 위해 belief 항은
        /// 넣지 않는다(belief 항은 모든 후보에 균등하게 들어가므로 여기 넣으면 서로 상쇄돼
        /// 데드존이 되살아난다). 현재 위치가 선호/기피인지만 반영한다.</summary>
        static float StayScore(MajorNpcData major, LocationData current)
        {
            float score = StayInertia;
            if (current == null) return score;

            if (major.preferredLocations != null && major.preferredLocations.Contains(current)) score += PreferredTerm;
            else if (major.avoidedLocations != null && major.avoidedLocations.Contains(current)) score += AvoidedTerm;
            return score;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>호출자(NpcMovementSystem, 또는 LLM 실패 시 LlmMajorThinker)가 만들어 넘긴
        /// 레코드에 이동 후보 점수(I절)만 채운다 - 레코드를 새로 만들거나 Publish하지 않는다.
        /// 최종 결과/Publish는 항상 호출자 쪽 책임이다(판단 1건 = 레코드 1개를 보장하기 위함).</summary>
        static void WriteMoveTrace(
            object traceObj, NpcMoveContext context, MajorNpcData major,
            float convictionRatio, float unverifiedRatio, float doubtRatio, float stayScore, bool hasGoal,
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
                    BeliefTerm = b.BeliefTerm,
                    PreferenceTerm = b.PreferenceTerm,
                    DoubtOverrideTerm = b.DoubtOverrideTerm,
                    FinalScore = b.FinalScore,
                    IsSelected = !isStay && loc == best
                });
            }

            trace.WithMoveScoring(
                context.CurrentLocation, context.Candidates, major.preferredLocations, major.avoidedLocations,
                convictionRatio, unverifiedRatio, doubtRatio, stayScore, entries, isStay ? null : best, isStay, hadTie);
        }
#endif

        /// <summary>Belief 분포를 세 갈래 비율로 집계한다. ChooseAction의 단일-카드 Intent 개념을
        /// 여러 카드에 걸친 이동 판단에 맞게 근사한 것 - NpcState/BeliefSystem 자체는 건드리지 않는다.
        /// NeedsVerification을 따로 세는 것이 핵심 -
        /// 예전에는 conviction(Plausible/Trusted)도 doubt(Doubtful/Denied)도 아니어서 0점 처리됐는데,
        /// LLM 판단 실측의 78%가 바로 그 등급이라 NPC 판단 대부분이 이동에 아무 영향을 못 줬다.
        /// Unknown은 "아직 판단한 적 없음"이므로 세 갈래 어디에도 넣지 않고 분모에만 남긴다.</summary>
        static (float convictionRatio, float unverifiedRatio, float doubtRatio) BeliefRatios(Belief.Domain.NpcState npc)
        {
            int total = npc.Beliefs.Count;
            if (total == 0) return (0f, 0f, 0f);

            int conviction = 0, unverified = 0, doubt = 0;
            foreach (var kvp in npc.Beliefs)
            {
                if (kvp.Value == BeliefState.Plausible || kvp.Value == BeliefState.Trusted) conviction++;
                else if (kvp.Value == BeliefState.NeedsVerification) unverified++;
                else if (kvp.Value == BeliefState.Doubtful || kvp.Value == BeliefState.Denied) doubt++;
            }

            return ((float)conviction / total, (float)unverified / total, (float)doubt / total);
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
