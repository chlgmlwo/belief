using System;
using System.Collections.Generic;
using Belief.AI;
using Belief.AI.LLM;
using Belief.Data;
using Belief.Domain;
using Belief.Events;
using UnityEngine;

namespace Belief.Systems
{
    /// <summary>
    /// 통합 판단 하나를 적용한 결과. 적용되지 않았다면 <b>왜</b> 그런지, 적용됐다면 어디까지
    /// 진행됐는지를 그대로 드러낸다 - 부분 진행을 숨기지 않는 것이 목적이다.
    /// </summary>
    public readonly struct JudgmentApplicationResult
    {
        public readonly bool Applied;
        public readonly string ApplicationKey;
        public readonly JudgmentResultSource ResultSource;

        public readonly bool BeliefApplied;
        public readonly bool GoalApplied;
        public readonly bool ActionApplied;
        public readonly bool DestinationReserved;
        public readonly DestinationReservationType ReservationType;
        public readonly bool DialoguePublished;

        public readonly bool DuplicateBlocked;
        public readonly bool StaleRequestDiscarded;
        public readonly string FailureReason;
        public readonly Exception Exception;

        /// <summary>적용이 확정된 최종 Belief. 적용되지 않았으면 Unknown이며, 호출자는 기존 값을 유지한다.</summary>
        public readonly BeliefState FinalBelief;

        public JudgmentApplicationResult(bool applied, string key, JudgmentResultSource source,
            bool belief, bool goal, bool action, bool destinationReserved, DestinationReservationType reservationType,
            bool dialogue, bool duplicate, bool stale, string failureReason, Exception exception, BeliefState finalBelief)
        {
            Applied = applied; ApplicationKey = key; ResultSource = source;
            BeliefApplied = belief; GoalApplied = goal; ActionApplied = action;
            DestinationReserved = destinationReserved; ReservationType = reservationType;
            DialoguePublished = dialogue; DuplicateBlocked = duplicate; StaleRequestDiscarded = stale;
            FailureReason = failureReason; Exception = exception; FinalBelief = finalBelief;
        }

        public static JudgmentApplicationResult NotApplied(string key, JudgmentResultSource source,
            string reason, bool duplicate = false, bool stale = false) =>
            new JudgmentApplicationResult(false, key, source, false, false, false, false,
                DestinationReservationType.None, false, duplicate, stale, reason, null, BeliefState.Unknown);
    }

    /// <summary>
    /// 확정된 통합 판단을 <b>실제 월드에 적용하는 유일한 지점</b>.
    ///
    /// 이 클래스가 하지 않는 것: LLM 호출, JSON 파싱, fallback 생성, NPC 직접 이동,
    /// 미션 판정, 정보 재확산, Shadow 비교. 그 책임들은 각각 다른 곳에 있고, 여기로 끌어오면
    /// "적용" 단계가 다시 실패 가능한 단계가 된다.
    ///
    /// <b>원자성의 의미</b>: 월드를 되돌리는 롤백을 만들지 않는다. 대신 적용을 시작하기 전에
    /// 최신성·중복·화이트리스트를 전부 확인해 실패 가능성을 없앤다. 적용 블록에는 비동기 호출도
    /// 외부 I/O도 없고, 남은 위험은 이벤트 구독자 예외뿐인데 그것은 핵심 상태를 모두 반영한
    /// <b>뒤</b>에 발행하므로 되돌릴 것이 생기지 않는다.
    /// </summary>
    public class JudgmentApplicationSystem
    {
        readonly ActionResolutionSystem actionResolution;
        readonly DestinationReservation reservations;
        readonly IGameEventBus eventBus;
        readonly Func<string> stageIdProvider;
        readonly Func<string> missionIdProvider;

        readonly HashSet<string> appliedKeys = new HashSet<string>();

        /// <summary>미션 시도가 바뀔 때마다 증가. 지난 시도의 판단이 새 시도에 적용되는 것을 막는다.</summary>
        int missionAttemptId;
        public int MissionAttemptId => missionAttemptId;

        public JudgmentApplicationSystem(
            ActionResolutionSystem actionResolution, DestinationReservation reservations,
            IGameEventBus eventBus, Func<string> stageIdProvider, Func<string> missionIdProvider)
        {
            this.actionResolution = actionResolution;
            this.reservations = reservations;
            this.eventBus = eventBus;
            this.stageIdProvider = stageIdProvider;
            this.missionIdProvider = missionIdProvider;

            eventBus.Subscribe<TurnStartedEvent>(e =>
            {
                if (e.Turn != 1) return;
                missionAttemptId++;
                appliedKeys.Clear();   // 새 시도 = 적용 이력도 새로 시작
            });
        }

        /// <summary>이번 판단에 붙일 신원. 요청을 만들기 <b>전</b>에 발급해 두고, 응답이 돌아온 뒤
        /// 그때의 세계와 대조한다.</summary>
        public JudgmentRequestIdentity CreateIdentity(NpcState npc, InformationCardData card, int turn, string requestId) =>
            new JudgmentRequestIdentity(
                stageIdProvider != null ? stageIdProvider() : "",
                missionIdProvider != null ? missionIdProvider() : "",
                missionAttemptId, turn,
                npc?.Data != null ? npc.Data.npcId : null,
                card != null ? card.cardId : null,
                requestId);

        public JudgmentApplicationResult Apply(
            IntegratedJudgmentOutcome outcome, NpcJudgmentContext ctx, int currentTurn, object trace)
        {
            if (!outcome.HasJudgment)
                return JudgmentApplicationResult.NotApplied(null, outcome.Source, outcome.FallbackReason ?? "NoJudgment");

            var j = outcome.Judgment;
            var id = j.Identity;
            string key = id.ApplicationKey;

            // ── 게이트 ① 최신성 ──────────────────────────────────────────────────
            // 실패해도 fallback을 새로 만들지 않는다 - 이미 지난 시도/턴의 판단이라 되살릴 이유가 없다.
            string stale = CheckStale(id, ctx, currentTurn);
            if (stale != null)
                return JudgmentApplicationResult.NotApplied(key, outcome.Source, stale, stale: true);

            // ── 게이트 ② 중복 ────────────────────────────────────────────────────
            if (appliedKeys.Contains(key))
                return JudgmentApplicationResult.NotApplied(key, outcome.Source, "DuplicateApplication", duplicate: true);

            // ── 게이트 ③ 화이트리스트 재확인 ─────────────────────────────────────
            string whitelist = CheckWhitelist(j, ctx);
            if (whitelist != null)
                return JudgmentApplicationResult.NotApplied(key, outcome.Source, whitelist);

            // ══ 여기부터 적용. 실패 가능한 외부 호출 없음 ═══════════════════════
            bool beliefApplied = false, goalApplied = false, actionApplied = false;
            bool reserved = false, dialoguePublished = false;
            var reservationType = DestinationReservationType.None;
            Exception applyException = null;

            try
            {
                // ③ Belief - 통합 판단 모드에서는 판단 결과가 곧 믿음이다. BeliefSystem은 점수를
                //    계산하는 역할이고, 여기서 적용하는 값은 이미 확정된 enum이므로 상태 쓰기만 한다.
                ctx.Npc.SetBelief(ctx.Card, j.Belief);
                beliefApplied = true;

                // ④ Goal
                ctx.Npc.SetGoal(j.Goal);
                goalApplied = true;

                // ⑤ Action - 기존 단일 관문을 그대로 쓴다. 기억·조사기록은 이 안에서 발생한다.
                actionResolution.Apply(j.Action, ctx.Npc, ctx.Card, ctx.Where, currentTurn);
                actionApplied = true;

                // ⑥ Destination 예약 - 즉시 이동시키지 않는다. stay도 명시적으로 예약해야
                //    이동 단계에서 규칙 기반이 끼어들지 않는다.
                if (reservations != null)
                {
                    if (reservations.TryReserve(ctx.Npc, j.Destination, key, missionAttemptId, currentTurn, out string reserveFailure))
                    {
                        reserved = true;
                        reservationType = j.Destination == null
                            ? DestinationReservationType.Stay : DestinationReservationType.MoveTo;
                    }
                    else
                    {
                        Debug.LogError($"[JudgmentApplication] 이동 예약 실패 ({key}): {reserveFailure}. "
                                     + "Belief/Goal/Action은 이미 적용됐고, 이번 턴 이동만 기존 경로를 탄다.");
                    }
                }

                // ⑦ 적용 완료 키 - 여기까지 왔으면 핵심 상태는 전부 반영됐다.
                appliedKeys.Add(key);
            }
            catch (Exception ex)
            {
                // 롤백하지 않는다. 어디까지 반영됐는지를 숨기지 않고 그대로 드러낸다 -
                // 조용히 성공으로 처리하면 다음 턴에 원인 없는 상태 불일치로 나타난다.
                applyException = ex;
                Debug.LogError($"[JudgmentApplication] 적용 중 예외 ({key}) - "
                             + $"Belief={beliefApplied} Goal={goalApplied} Action={actionApplied} 예약={reserved}\n{ex}");
            }

            // ── 관찰용 이벤트 - 핵심 상태 반영 뒤에 발행한다 ─────────────────────
            // 구독자 예외가 위에서 확정된 상태를 되돌리지 못한다.
            if (j.Dialogue != null && j.Dialogue.Length > 0)
            {
                try
                {
                    eventBus.Publish(new NpcSpokeEvent(ctx.Npc.Data, new DialogueContent(j.Dialogue)));
                    dialoguePublished = true;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[JudgmentApplication] Dialogue 발행 중 예외({key}) - 핵심 상태는 유지됩니다:\n{ex}");
                }
            }

            try { eventBus.Publish(new CardJudgedEvent(ctx.Npc.Data, ctx.Card, j.Belief, currentTurn)); }
            catch (Exception ex)
            {
                Debug.LogError($"[JudgmentApplication] CardJudgedEvent 발행 중 예외({key}) - 핵심 상태는 유지됩니다:\n{ex}");
            }

            return new JudgmentApplicationResult(
                applied: beliefApplied && goalApplied && actionApplied,
                key, outcome.Source, beliefApplied, goalApplied, actionApplied,
                reserved, reservationType, dialoguePublished,
                duplicate: false, stale: false,
                failureReason: applyException != null ? "ApplyException" : null,
                exception: applyException,
                finalBelief: beliefApplied ? j.Belief : BeliefState.Unknown);
        }

        string CheckStale(JudgmentRequestIdentity id, NpcJudgmentContext ctx, int currentTurn)
        {
            if (id.MissionAttemptId != missionAttemptId) return "StaleAttempt";
            if (id.Turn != currentTurn) return "StaleTurn";

            string stage = stageIdProvider != null ? stageIdProvider() : "";
            if (!string.Equals(id.StageId ?? "", stage ?? "")) return "StaleStage";

            string mission = missionIdProvider != null ? missionIdProvider() : "";
            if (!string.Equals(id.MissionId ?? "", mission ?? "")) return "StaleMission";

            if (ctx.Npc?.Data == null || id.NpcId != ctx.Npc.Data.npcId) return "NpcMismatch";
            if (ctx.Card == null || id.CardId != ctx.Card.cardId) return "CardMismatch";
            if (string.IsNullOrEmpty(id.RequestId)) return "MissingRequestId";
            return null;
        }

        static string CheckWhitelist(ValidatedNpcJudgment j, NpcJudgmentContext ctx)
        {
            if (j.Action == null) return "MissingAction";
            if (ctx.ActionCandidates == null || !Has(ctx.ActionCandidates, j.Action)) return "ActionNotInCandidates";
            if (j.Destination != null)
            {
                if (ctx.MoveCandidates == null || !Has(ctx.MoveCandidates, j.Destination)) return "DestinationNotInCandidates";
                if (ctx.Where != null && j.Destination == ctx.Where.Data) return "DestinationNotNormalized";
            }
            return null;
        }

        static bool Has<T>(IReadOnlyList<T> list, T item) where T : class
        {
            for (int i = 0; i < list.Count; i++) if (ReferenceEquals(list[i], item)) return true;
            return false;
        }
    }
}
