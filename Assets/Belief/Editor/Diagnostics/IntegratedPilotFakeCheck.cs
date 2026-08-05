using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Belief.AI;
using Belief.AI.LLM;
using Belief.Core;
using Belief.Data;
using Belief.Domain;
using UnityEditor;
using UnityEngine;

namespace Belief.EditorTools.Diagnostics
{
    /// <summary>
    /// 파일럿 실행 구조(일회성 opt-in + 호출 예산)의 <b>결정적 검증</b>.
    /// <b>실제 API를 호출하지 않는다</b> - 응답을 직접 지정하는 가짜 Transport만 쓰고,
    /// 세계는 <see cref="JudgmentApplicationCheck.Build"/>가 만드는 자체 리그를 재사용한다.
    ///
    /// 여기서 확인하려는 명제는 세 가지다:
    /// <list type="number">
    /// <item>opt-in 토큰은 <b>정확히 한 번</b>만 소비된다 - 두 번째 Awake는 파일럿을 켤 수 없다.</item>
    /// <item>Transport 호출 수는 <see cref="IntegratedLlmPilotSession.MaxCalls"/>를 <b>절대</b>
    ///   넘지 않고, 초과 요청은 RuleBased <b>전체</b> 폴백이 된다(필드 혼합 0).</item>
    /// <item>세션이 닫히면(중단·Play 종료) 이후 요청은 Transport에 닿지 못한다.</item>
    /// </list>
    ///
    /// 이 검증은 SessionState 토큰을 실제로 쓰므로, 어떻게 끝나든 finally에서 토큰을 지운다 -
    /// 검증 때문에 다음 Play가 파일럿으로 켜지는 일은 없다.
    /// </summary>
    public static class IntegratedPilotFakeCheck
    {
        [MenuItem("BELIEF/Diagnostics/Verify Integrated Pilot (Fake Transport)", priority = 104)]
        public static async void Run()
        {
            if (!UnityEngine.Application.isPlaying)
            {
                EditorUtility.DisplayDialog("Integrated Pilot Check",
                    "Play Mode에서 실행해야 합니다.\n\nTimeout이 코루틴 기반이라 정지 상태에서는 판단을 끝까지 돌릴 수 없습니다.", "확인");
                return;
            }
            UnityEngine.Application.runInBackground = true;
            Debug.LogWarning("=== 파일럿 실행 구조 검증 (실제 API 호출 0회) ===\n" + await Execute());
        }

        public static async Task<string> Execute()
        {
            var sb = new StringBuilder();
            int pass = 0, total = 0;
            void Check(string label, bool ok, string detail = null)
            { total++; if (ok) pass++; sb.AppendLine($"  {(ok ? "PASS" : "*** FAIL ***")} {label}{(detail != null ? "  → " + detail : "")}"); }

            // 검증 시작 전에 남아 있을지 모르는 상태를 먼저 지운다 - 앞선 실행의 잔재 위에서
            // 판정하면 통과/실패가 실행 순서에 따라 달라진다.
            IntegratedLlmPilotSession.Clear();
            IntegratedLlmPilotSession.Disarm();

            // 도구의 4턴 상한이 StageData를 건드리지 않는다는 것을 값으로 확인하기 위한 기준.
            int installerMaxTurns = StageMaxTurns();

            try
            {
                // ── A. opt-in 일회성 ─────────────────────────────────────────────
                sb.AppendLine("### A. opt-in 일회성 소비");
                {
                    Check("무장 전 IsArmed=false", !IntegratedLlmPilotSession.IsArmed);
                    Check("무장 전 IsActive=false", !IntegratedLlmPilotSession.IsActive);

                    IntegratedLlmPilotSession.Arm("check-A",
                        providerConfigPath: IntegratedPilotRunner.DefaultProviderConfigPath);
                    Check("Arm 후 IsArmed=true", IntegratedLlmPilotSession.IsArmed);
                    Check("Arm만으로는 세션이 열리지 않음", !IntegratedLlmPilotSession.IsActive);

                    bool first = IntegratedLlmPilotSession.TryConsumeOptIn(out string id1, out bool log1, out var cfg1);
                    Check("1차 소비 성공", first && id1 == "check-A", id1);
                    Check("프롬프트 원문 로깅 기본 off", !log1);
                    Check("설정 자산이 토큰으로 전달됨 (씬 수정 없이)", cfg1 != null,
                        cfg1 != null ? cfg1.name : IntegratedPilotRunner.DefaultProviderConfigPath + " 없음");
                    Check("소비 즉시 토큰 소멸", !IntegratedLlmPilotSession.IsArmed);
                    Check("세션 활성화", IntegratedLlmPilotSession.IsActive
                                     && IntegratedLlmPilotSession.ActiveSessionId == "check-A");
                    Check("호출 카운터 0에서 시작", IntegratedLlmPilotSession.CallsUsed == 0);

                    bool second = IntegratedLlmPilotSession.TryConsumeOptIn(out string id2, out _, out var cfg2);
                    Check("2차 소비 실패 (씬 전환·재시작에서 다시 켜지지 않음)",
                        !second && id2 == null && cfg2 == null);
                }

                // ── B. 호출 예산 상한 ────────────────────────────────────────────
                sb.AppendLine();
                sb.AppendLine("### B. 호출 예산 (세션 A 계속)");
                {
                    var budget = new IntegratedLlmPilotCallBudget("check-A");

                    bool allOk = true; string firstDeny = null;
                    for (int i = 0; i < IntegratedLlmPilotSession.MaxCalls; i++)
                        if (!budget.TryConsume(out string why)) { allOk = false; firstDeny = $"{i + 1}번째에서 {why}"; break; }

                    Check($"{IntegratedLlmPilotSession.MaxCalls}회까지 허용", allOk, firstDeny);
                    Check("CallsUsed = 상한", IntegratedLlmPilotSession.CallsUsed == IntegratedLlmPilotSession.MaxCalls,
                        IntegratedLlmPilotSession.CallsUsed.ToString());
                    Check("잔여 0", IntegratedLlmPilotSession.CallsRemaining == 0);

                    bool over = budget.TryConsume(out string overWhy);
                    Check($"{IntegratedLlmPilotSession.MaxCalls + 1}번째 거부", !over);
                    Check("거부 사유 = PilotCallLimitExceeded",
                        overWhy == IntegratedLlmPilotSession.CallLimitExceededReason, overWhy);
                    Check("거부는 사용량을 늘리지 않음",
                        IntegratedLlmPilotSession.CallsUsed == IntegratedLlmPilotSession.MaxCalls);
                    Check("거부 카운터 기록", IntegratedLlmPilotSession.CallsDenied == 1,
                        IntegratedLlmPilotSession.CallsDenied + "회");
                }

                // ── C. 세션 종료 후 차단 ─────────────────────────────────────────
                sb.AppendLine();
                sb.AppendLine("### C. 세션 종료 후 차단");
                {
                    IntegratedLlmPilotSession.End(IntegratedLlmPilotSession.SessionEndedReason);
                    Check("종료 후 IsActive=false", !IntegratedLlmPilotSession.IsActive);
                    Check("종료 사유 기록",
                        IntegratedLlmPilotSession.EndReason == IntegratedLlmPilotSession.SessionEndedReason,
                        IntegratedLlmPilotSession.EndReason);

                    var stale = new IntegratedLlmPilotCallBudget("check-A");
                    bool allowed = stale.TryConsume(out string why);
                    Check("끝난 세션의 예산은 거부", !allowed);
                    Check("거부 사유 = PilotSessionEnded",
                        why == IntegratedLlmPilotSession.SessionEndedReason, why);

                    // 새 세션에서 옛 세션 ID로 만든 예산은 쓸 수 없다 - Play가 바뀌면 예산도 바뀐다.
                    IntegratedLlmPilotSession.BeginSession("check-C");
                    Check("새 세션은 카운터가 0", IntegratedLlmPilotSession.CallsUsed == 0);
                    Check("옛 세션 ID 예산은 새 세션에서도 거부", !stale.TryConsume(out _));
                    IntegratedLlmPilotSession.End("CheckDone");
                }

                // ── D. Thinker 통합: 상한 초과 시 Transport 호출 0 ───────────────
                sb.AppendLine();
                sb.AppendLine("### D. Thinker 통합 (가짜 Transport)");
                var rig = JudgmentApplicationCheck.Build();
                IntegratedJudgmentOutcome overLimitOutcome = default;
                RuleBaselineFields baseline = default;
                {
                    IntegratedLlmPilotSession.BeginSession("check-D");
                    string actionId = rig.Data.availableActions.First(a => a.intent == NpcActionIntent.Verify).actionId;
                    var transport = new JudgmentApplicationCheck.ScriptedTransport
                    { Response = JudgmentApplicationCheck.Json(actionId, "stay", "Plausible") };

                    var thinker = new IntegratedLlmThinker(
                        transport, new RuleBasedUnifiedThinker(rig.BeliefSys, new RuleBasedMajorThinker()),
                        2000, new IntegratedLlmPilotCallBudget("check-D"));

                    var id = rig.Application.CreateIdentity(rig.Npc, rig.Card, 2, "req-budget");

                    int llmResults = 0;
                    for (int i = 0; i < IntegratedLlmPilotSession.MaxCalls; i++)
                    {
                        var o = await thinker.DecideAsync(rig.Ctx, id, null);
                        if (o.Source == JudgmentResultSource.IntegratedLlm) llmResults++;
                    }

                    Check($"{IntegratedLlmPilotSession.MaxCalls}건 모두 LLM 결과", llmResults == IntegratedLlmPilotSession.MaxCalls,
                        llmResults + "건");
                    Check("Transport 호출 = 상한", transport.SendCount == IntegratedLlmPilotSession.MaxCalls,
                        transport.SendCount + "회");

                    overLimitOutcome = await thinker.DecideAsync(rig.Ctx, id, null);

                    Check("상한 초과 요청은 Transport를 부르지 않음",
                        transport.SendCount == IntegratedLlmPilotSession.MaxCalls, transport.SendCount + "회");
                    Check("상한 초과 = RuleBased 전체 폴백",
                        overLimitOutcome.Source == JudgmentResultSource.RuleBasedFallback);
                    Check("FallbackReason = PilotCallLimitExceeded",
                        overLimitOutcome.FallbackReason == IntegratedLlmPilotSession.CallLimitExceededReason,
                        overLimitOutcome.FallbackReason);
                    Check("폴백에도 판단은 존재(판단 누락 없음)", overLimitOutcome.HasJudgment);
                }

                // ── E. 필드 혼합 0 - 폴백 6필드가 RuleOnly와 완전히 같은가 ───────
                sb.AppendLine();
                sb.AppendLine("### E. 필드 혼합 0");
                {
                    var rule = await new RuleBasedUnifiedThinker(rig.BeliefSys, new RuleBasedMajorThinker())
                        .DecideAsync(rig.Ctx, null);
                    baseline = new RuleBaselineFields(rule.Judgment);
                    var j = overLimitOutcome.Judgment;

                    Check("Interpretation 동일", j.Interpretation == baseline.Interpretation);
                    Check("Belief 동일", j.Belief == baseline.BeliefState, $"{j.Belief} vs {baseline.BeliefState}");
                    Check("Goal 동일", j.Goal == baseline.Goal, $"{j.Goal} vs {baseline.Goal}");
                    Check("Action 동일", j.Action == baseline.Action,
                        $"{(j.Action != null ? j.Action.actionId : "-")} vs {(baseline.Action != null ? baseline.Action.actionId : "-")}");
                    Check("Destination 동일", j.Destination == baseline.Destination);
                    Check("Dialogue 동일", j.Dialogue == baseline.Dialogue);
                    Check("Source 표시가 RuleBasedFallback", j.Summary.Source == "RuleBasedFallback", j.Summary.Source);

                    // LLM이 보낸 값이 하나라도 섞이지 않았는지 - 가짜 응답의 고정 문자열로 확인한다.
                    Check("LLM 응답 문자열 미유입 (Goal)", j.Goal != "새 목표", j.Goal);
                    Check("LLM 응답 문자열 미유입 (Dialogue)", j.Dialogue != "대사");
                }

                // ── F. 실제 적용까지 RuleOnly와 동일 ─────────────────────────────
                sb.AppendLine();
                sb.AppendLine("### F. 상한 초과 결과의 실제 적용");
                {
                    var applied = rig.Application.Apply(overLimitOutcome, rig.Ctx, 2, null);
                    Check("적용됨", applied.Applied, applied.FailureReason);
                    Check("source=RuleBasedFallback", applied.ResultSource == JudgmentResultSource.RuleBasedFallback);
                    Check("월드 Belief = RuleOnly 결과", rig.Npc.GetBelief(rig.Card) == baseline.BeliefState,
                        $"{rig.Npc.GetBelief(rig.Card)} vs {baseline.BeliefState}");
                    Check("월드 Action = RuleOnly 결과", rig.Npc.CurrentAction == baseline.Action);
                    Check("월드 Goal = RuleOnly 결과", rig.Npc.CurrentGoal == baseline.Goal);
                    Check("이동 예약 1건(중복 아님)", rig.Reservations.Count == 1, rig.Reservations.Count + "건");

                    var again = rig.Application.Apply(overLimitOutcome, rig.Ctx, 2, null);
                    Check("같은 판단 재적용 차단", again.DuplicateBlocked && !again.Applied);

                    // 다음 절은 "예산이 아예 없는" 상태를 보므로 카운터까지 지우고 넘어간다.
                    IntegratedLlmPilotSession.Clear();
                }

                // ── G. 예산 없음 = 기존 동작 무회귀 ──────────────────────────────
                sb.AppendLine();
                sb.AppendLine("### G. 무회귀 (예산 미지정)");
                {
                    var rig2 = JudgmentApplicationCheck.Build();
                    var transport = new JudgmentApplicationCheck.ScriptedTransport
                    { Response = JudgmentApplicationCheck.Json(rig2.Data.availableActions[0].actionId, "stay") };
                    var thinker = new IntegratedLlmThinker(
                        transport, new RuleBasedUnifiedThinker(rig2.BeliefSys, new RuleBasedMajorThinker()), 2000);

                    int n = IntegratedLlmPilotSession.MaxCalls + 1;
                    int llmResults = 0;
                    var id = rig2.Application.CreateIdentity(rig2.Npc, rig2.Card, 2, "req-nobudget");
                    for (int i = 0; i < n; i++)
                        if ((await thinker.DecideAsync(rig2.Ctx, id, null)).Source == JudgmentResultSource.IntegratedLlm)
                            llmResults++;

                    Check($"예산 없으면 {n}건 전부 LLM 결과", llmResults == n, llmResults + "건");
                    Check("Transport 호출 제한 없음", transport.SendCount == n, transport.SendCount + "회");
                    Check("세션 카운터는 건드리지 않음", IntegratedLlmPilotSession.CallsUsed == 0,
                        IntegratedLlmPilotSession.CallsUsed.ToString());
                }

                // ── H. 스테이지 정책 ─────────────────────────────────────────────
                sb.AppendLine();
                sb.AppendLine("### H. 스테이지 정책");
                {
                    Check("STAGE_01 허용", IntegratedLlmPilotPolicy.IsAllowed("STAGE_01", out _));
                    foreach (string other in new[] { "STAGE_02", "STAGE_03", "STAGE_04" })
                        Check($"{other} 거부", !IntegratedLlmPilotPolicy.IsAllowed(other, out _));
                    Check("StageData 없음 거부", !IntegratedLlmPilotPolicy.IsAllowed(null, out _));
                }

                // ── I. 씬 무변경 ─────────────────────────────────────────────────
                sb.AppendLine();
                sb.AppendLine("### I. 씬 무변경");
                {
                    var installer = UnityEngine.Object.FindFirstObjectByType<GameInstaller>();
                    if (installer == null) Check("GameInstaller 존재", false);
                    else
                    {
                        var so = new SerializedObject(installer);
                        var mode = (ThinkerMode)so.FindProperty("thinkerMode").enumValueIndex;
                        Check("씬 thinkerMode가 RuleOnly 그대로", mode == ThinkerMode.RuleOnly, mode.ToString());
                        Check("씬 shadowMode 꺼짐", !so.FindProperty("shadowMode").boolValue);
                        Check("이 검증이 파일럿을 켜지 않음", installer.JudgmentApplication == null);
                    }
                }

                // ── J. 실행 도구 사전 점검 ───────────────────────────────────────
                sb.AppendLine();
                sb.AppendLine("### J. 파일럿 사전 점검 (무장하지 않음)");
                {
                    bool ok = IntegratedPilotRunner.Preflight(out string blocker, out string plan);
                    Check("사전 점검 통과", ok, ok ? null : blocker.Replace("\n", " / "));
                    if (ok)
                    {
                        Check("권장 표본에 High/Medium/Low 모두 포함",
                            plan.Contains("High") && plan.Contains("Medium") && plan.Contains("Low"));
                        Check("Verify 가능 NPC 표기", plan.Contains("Verify :") && !plan.Contains("Verify : 없음"));
                        Check("관계 보유 NPC 표기", plan.Contains("관계   :") && !plan.Contains("관계   : 없음"));
                        sb.AppendLine(plan.TrimEnd());
                    }
                    Check("사전 점검이 무장하지 않음", !IntegratedLlmPilotSession.IsArmed);
                }

                // ── K. 턴 상한 자동 중단 ─────────────────────────────────────────
                // 실제 턴 진행은 게임이 하지만, "상한에 닿아 세션이 닫힌 뒤의 세계"는 여기서
                // 그대로 재현할 수 있다 - 그 상태에서 카드를 더 밀어 넣어도 Transport가 0이어야 한다.
                sb.AppendLine();
                sb.AppendLine("### K. 턴 상한 자동 중단 (호출 예산과 독립)");
                {
                    var rig3 = JudgmentApplicationCheck.Build();
                    IntegratedLlmPilotSession.BeginSession("check-K");
                    var transport = new JudgmentApplicationCheck.ScriptedTransport
                    { Response = JudgmentApplicationCheck.Json(rig3.Data.availableActions[0].actionId, "stay") };
                    var thinker = new IntegratedLlmThinker(
                        transport, new RuleBasedUnifiedThinker(rig3.BeliefSys, new RuleBasedMajorThinker()),
                        2000, new IntegratedLlmPilotCallBudget("check-K"), false,
                        IntegratedLlmPilotSession.Coverage);
                    var id = rig3.Application.CreateIdentity(rig3.Npc, rig3.Card, 2, "req-turn");

                    // 4턴 안에서는 평소대로 LLM을 탄다 - 예산은 아직 3회밖에 쓰지 않았다.
                    for (int i = 0; i < 3; i++) await thinker.DecideAsync(rig3.Ctx, id, null);
                    Check("상한 전에는 정상 호출", transport.SendCount == 3, transport.SendCount + "회");
                    int usedBefore = IntegratedLlmPilotSession.CallsUsed;

                    // 턴 상한 도달 - 러너가 하는 일과 같다.
                    IntegratedLlmPilotSession.End(IntegratedLlmPilotSession.TurnLimitReason);

                    var after = await thinker.DecideAsync(rig3.Ctx, id, null);
                    Check("상한 후 새 Transport 호출 0", transport.SendCount == 3, transport.SendCount + "회");
                    Check("상한 후 판단은 RuleBased 전체 폴백",
                        after.Source == JudgmentResultSource.RuleBasedFallback);
                    Check("FallbackReason = PilotTurnLimitReached",
                        after.FallbackReason == IntegratedLlmPilotSession.TurnLimitReason, after.FallbackReason);
                    Check("호출 예산은 남아 있었음 (턴 상한과 독립)",
                        usedBefore < IntegratedLlmPilotSession.MaxCalls,
                        $"{usedBefore}/{IntegratedLlmPilotSession.MaxCalls}");
                    Check("상한 후 예산 사용량 증가 0",
                        IntegratedLlmPilotSession.CallsUsed == usedBefore,
                        IntegratedLlmPilotSession.CallsUsed.ToString());

                    // 상한 후 새 카드를 더 밀어 넣어도 마찬가지다("새 카드 전달 0"의 결정적 형태).
                    var extra = await thinker.DecideAsync(rig3.Ctx,
                        rig3.Application.CreateIdentity(rig3.Npc, rig3.Card, 2, "req-turn-2"), null);
                    Check("상한 후 추가 카드도 Transport 0", transport.SendCount == 3, transport.SendCount + "회");
                    Check("상한 후 추가 카드도 폴백",
                        extra.Source == JudgmentResultSource.RuleBasedFallback);

                    // ── 카드 등급 Coverage 집계 ─────────────────────────────────
                    var cov = IntegratedLlmPilotSession.Coverage;
                    Check("Coverage 표본 기록됨", cov != null && cov.Count == 5, cov?.Count + "건");
                    Check("Coverage가 카드·NPC를 기록",
                        cov != null && cov.Count > 0
                        && cov.Samples[0].CardId == rig3.Card.cardId
                        && cov.Samples[0].NpcId == rig3.Data.npcId,
                        cov != null && cov.Count > 0 ? $"{cov.Samples[0].CardId}→{cov.Samples[0].NpcId}" : null);
                    Check("Coverage가 등급을 카드 credibility로 분류",
                        cov != null && cov.Count > 0
                        && cov.Samples[0].Tier == IntegratedLlmPilotCoverage.TierOf(rig3.Card.information.baseCredibility),
                        cov != null && cov.Count > 0 ? $"{cov.Samples[0].Tier} @ {cov.Samples[0].Credibility:F2}" : null);
                    Check("Coverage가 직접/재확산을 구분 (이 리그는 직접 전달)",
                        cov != null && cov.DirectCount == cov.Count && cov.RespreadCount == 0,
                        cov != null ? $"직접 {cov.DirectCount} / 재확산 {cov.RespreadCount}" : null);
                    Check("한 등급만 나온 표본은 CoverageIncomplete (실패 아님)",
                        cov != null && !cov.CoverageComplete,
                        cov != null ? $"High {cov.HasHigh} / Medium {cov.HasMedium} / Low {cov.HasLow}" : null);

                    // 등급 3종을 모두 본 표본은 완전으로 집계되는지 - 같은 집계기로 확인한다.
                    var full = new IntegratedLlmPilotCoverage();
                    Check("세 등급 경계값 분류", IntegratedLlmPilotCoverage.TierOf(0.70f) == "High"
                                          && IntegratedLlmPilotCoverage.TierOf(0.55f) == "Medium"
                                          && IntegratedLlmPilotCoverage.TierOf(0.30f) == "Low");
                    Check("빈 표본은 미완전", !full.CoverageComplete);

                    Check("StageData.maxTurns 무변경 (도구 상한과 별개)",
                        installerMaxTurns == StageMaxTurns(), $"{installerMaxTurns}턴");

                    IntegratedLlmPilotSession.Clear();
                    IntegratedPilotRunner.ResetMonitorForCheck();
                    Check("정리 후 표본 없음", IntegratedLlmPilotSession.Coverage == null);
                    Check("정리 후 예산 초기화", IntegratedLlmPilotSession.CallsUsed == 0
                                          && IntegratedLlmPilotSession.CallsDenied == 0);
                    Check("정리 후 턴 감시 잔존 0", !IntegratedPilotRunner.IsMonitoring);
                }

                // ── L. 살아 있는 턴 감시가 실제 TurnSystem에 붙는가 ──────────────
                // K는 "상한에 닿은 뒤의 세계"를 재현했고, 여기서는 그 앞단 - 감시가 실제로 돌아가는
                // 게임의 누적 턴을 붙잡는지 - 를 확인한다. 턴을 인위적으로 밀지 않는다(게임 무개입).
                sb.AppendLine();
                sb.AppendLine("### L. 턴 감시 부착 (실제 TurnSystem 읽기)");
                {
                    var installer = UnityEngine.Object.FindFirstObjectByType<GameInstaller>();
                    if (installer == null || installer.Turns == null) Check("GameInstaller/TurnSystem 존재", false);
                    else
                    {
                        int liveStageTurn = installer.Turns.StageTurn;
                        IntegratedLlmPilotSession.BeginSession("check-L");

                        // 에디터 idle이 몇 번 돌 시간을 준다 - 감시는 EditorApplication.update에서만 움직인다.
                        for (int i = 0; i < 20 && !IntegratedPilotRunner.IsMonitoring; i++) await Task.Delay(50);

                        Check("감시가 세션에 붙음", IntegratedPilotRunner.IsMonitoring);
                        Check("시작 턴 = 실제 누적 턴",
                            IntegratedPilotRunner.MonitorStartStageTurn == liveStageTurn,
                            $"{IntegratedPilotRunner.MonitorStartStageTurn} vs {liveStageTurn}");
                        Check("아직 상한 미도달 (게임 개입 없음)", !IntegratedPilotRunner.TurnLimitReached);
                        Check("감시는 이벤트 버스를 구독하지 않음 - 세션만 닫으면 게임 쪽 잔존 0",
                            IntegratedLlmPilotSession.IsActive);

                        IntegratedLlmPilotSession.End(IntegratedLlmPilotSession.TurnLimitReason);
                        IntegratedLlmPilotSession.Clear();
                        IntegratedPilotRunner.ResetMonitorForCheck();
                        Check("정리 후 감시 잔존 0", !IntegratedPilotRunner.IsMonitoring);
                        Check("StageData.maxTurns 여전히 무변경", StageMaxTurns() == installerMaxTurns,
                            StageMaxTurns() + "턴");
                    }
                }
            }
            finally
            {
                // 어떻게 끝나든 토큰·세션·카운터를 남기지 않는다 - 검증 때문에 다음 Play에서
                // 요금이 나가거나, 가짜 호출 수가 실제 파일럿 보고에 섞이는 일은 없어야 한다.
                IntegratedLlmPilotSession.Clear();
                IntegratedLlmPilotSession.Disarm();
            }

            Check("검증 종료 후 무장 없음", !IntegratedLlmPilotSession.IsArmed);
            Check("검증 종료 후 세션 없음", !IntegratedLlmPilotSession.IsActive);
            Check("검증 종료 후 카운터 0 (실제 파일럿 보고에 섞이지 않음)",
                IntegratedLlmPilotSession.CallsUsed == 0 && IntegratedLlmPilotSession.CallsDenied == 0);

            sb.AppendLine();
            sb.AppendLine($"합계 {pass}/{total} PASS   (실제 API 호출 0회)");
            return sb.ToString();
        }

        /// <summary>지금 열려 있는 씬 StageData의 턴 상한. 도구의 4턴 상한이 이 값을 건드리지
        /// 않는다는 것을 검증 전후로 비교하기 위한 것이다(없으면 -1).</summary>
        static int StageMaxTurns()
        {
            var installer = UnityEngine.Object.FindFirstObjectByType<GameInstaller>();
            return installer != null && installer.StageAsset != null ? installer.StageAsset.maxTurns : -1;
        }

        /// <summary>규칙 기반 기준값 6필드를 떼어 보관한다 - 비교 대상이 무엇인지 코드에서
        /// 바로 읽히도록 하기 위한 값 홀더일 뿐, 판단 로직은 없다.
        /// 필드 이름을 BeliefState로 둔 것은 루트 네임스페이스 Belief와의 충돌을 피하기 위해서다.</summary>
        readonly struct RuleBaselineFields
        {
            public readonly string Interpretation;
            public readonly BeliefState BeliefState;
            public readonly string Goal;
            public readonly NpcActionData Action;
            public readonly LocationData Destination;
            public readonly string Dialogue;

            public RuleBaselineFields(NpcJudgment j)
            {
                Interpretation = j.Interpretation; BeliefState = j.Belief; Goal = j.Goal;
                Action = j.Action; Destination = j.Destination; Dialogue = j.Dialogue;
            }
        }
    }
}
