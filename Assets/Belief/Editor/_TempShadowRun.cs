// 임시 검증 하네스 - Shadow Mode 실제 LLM 계측용. 검증이 끝나면 삭제한다.
// 게임 데이터/미션 조건은 수정하지 않는다. 실제 GameInstaller의 시스템을 그대로 사용한다.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Belief.Core;
using Belief.Data;
using Belief.Debugging;
using Belief.Domain;
using UnityEditor;
using UnityEngine;

namespace Belief.EditorTools
{
    public static class TempShadowRun
    {
        const string OutDir = @"C:\Users\CHJ\AppData\Local\Temp\claude\C--Users-CHJ-Desktop-belief\f2e0237e-2ba3-4172-82a0-ed097780e5f7\scratchpad";

        static readonly List<ShadowComparisonRecord> All = new List<ShadowComparisonRecord>();
        static bool hooked;

        // 등급별 대표 카드 - B에서 매긴 baseCredibility 기준
        static readonly (string tier, string cardId)[] Plan =
        {
            ("High",   "C-ADM-01"),   // cred 0.70 / 행정기관 0.75
            ("High",   "C-SEC-01"),   // cred 0.65 / 순찰대 0.65
            ("Medium", "C-POL-03"),   // cred 0.55 / 순찰대 0.65
            ("Medium", "C-CRI-01"),   // cred 0.45 / 주점 0.30
            ("Low",    "C-POL-01"),   // cred 0.40 / 귀족가 0.45
            ("Low",    "C-SEC-02"),   // cred 0.30 / 익명 제보 0.25
        };

        [MenuItem("Belief/_Temp/Shadow 실측 3회 플레이 (Play Mode)", priority = 905)]
        public static async void Run()
        {
            if (!Application.isPlaying) { Debug.LogError("[검증] Play Mode에서 실행해야 합니다."); return; }
            Application.runInBackground = true;

            if (!hooked) { ShadowComparisonHub.RecordPublished += r => All.Add(r); hooked = true; }
            All.Clear();

            var gi = UnityEngine.Object.FindFirstObjectByType<GameInstaller>();
            if (gi == null || gi.Turns == null) { Debug.LogError("GameInstaller 미초기화"); return; }
            if (gi.ShadowJudgment == null) { Debug.LogError("Shadow가 꺼져 있습니다 - 씬의 shadowMode를 확인하세요."); return; }

            var pool = AssetDatabase.FindAssets("t:InformationCardPoolData")
                .Select(g => AssetDatabase.LoadAssetAtPath<InformationCardPoolData>(AssetDatabase.GUIDToAssetPath(g)))
                .First(p => p != null && p.name == "CardPool_Default");
            var byId = pool.cards.Where(c => c != null).ToDictionary(c => c.cardId, c => c);

            var targets = gi.Npcs.Values.Where(n => n.Data is MajorNpcData)
                .OrderBy(n => n.Data.npcId).ToList();

            int attempts = 0;
            // ── 3회 플레이 x 4턴 ─────────────────────────────────────────────────
            // 미션이 1턴에 끝나므로 TurnSystem을 거치지 않고 InfoDeliverySystem을 직접 구동한다
            // (실제 판단 경로 = NpcThinkingSystem + 실제 ShadowJudgmentSystem 그대로).
            for (int play = 1; play <= 3; play++)
            {
                for (int turn = 1; turn <= 4; turn++)
                {
                    // 턴마다 카드 하나를 NPC 한 명에게 - 등급이 골고루 섞이도록 순환
                    var (tier, cardId) = Plan[((play - 1) * 4 + (turn - 1)) % Plan.Length];
                    var card = byId[cardId];
                    var npc = targets[(play + turn) % targets.Count];

                    await gi.Delivery.DeliverCardToNpcAsync(card, npc);
                    attempts++;
                    await Task.Yield();
                }

                // 이 플레이의 Shadow 응답이 전부 돌아올 때까지 기다린다 - 기다리지 않고 바로
                // 재시작하면 진행 중이던 요청이 전부 StaleSession이 되어 계측할 것이 남지 않는다
                // (그 격리 동작 자체는 정상이며, 여기서는 계측을 위해 회피할 뿐이다).
                await DrainAsync();

                // 다음 플레이 = 새 시도.
                if (play < 3)
                {
                    var pc = ProgressionController.Instance;
                    if (pc != null) pc.RestartCurrentMission();
                    await Task.Yield();
                }
            }

            File.WriteAllText(Path.Combine(OutDir, "shadow_run_raw.tsv"), BuildTsv());
            Debug.LogWarning("=== Shadow 실측 완료 ===\n" + BuildSummary(attempts));
        }

        /// <summary>기록 수가 일정 시간 늘지 않으면 이번 플레이의 Shadow 요청이 전부 끝난 것으로 본다.</summary>
        static async Task DrainAsync()
        {
            // 응답이 평균 3초쯤 걸리므로 최소 대기를 충분히 준 뒤에 "더 이상 안 늘어남"을 본다.
            // 짧게 잡으면 첫 응답이 오기도 전에 안정으로 오판해 전부 StaleSession이 된다.
            for (int i = 0; i < 32; i++) await Task.Delay(250);   // 최소 8초

            int stable = 0, last = All.Count;
            for (int i = 0; i < 160 && stable < 16; i++)          // 이후 4초간 변화 없으면 종료
            {
                await Task.Delay(250);
                if (All.Count == last) stable++;
                else { stable = 0; last = All.Count; }
            }
        }

        static string BuildTsv()
        {
            var sb = new StringBuilder();
            sb.AppendLine("attempt\tturn\tnpc\tcard\tcred\tsrc\tsrcTrust\toutcome\tfail\tms\tinTok\toutTok\t"
                        + "ruleBelief\tllmBelief\tstepDelta\truleAction\tllmAction\tllmDest\t"
                        + "reason\tprofile\trelationship\tinterpretation\tllmGoal\tllmDialogue");
            foreach (var r in All)
                sb.AppendLine(string.Join("\t", new[]
                {
                    r.MissionAttemptId.ToString(), r.Turn.ToString(), r.NpcId, r.CardId,
                    r.CardCredibility.ToString("F2"), r.SourceId, r.SourceTrust.ToString("F2"),
                    r.Outcome.ToString(), r.FailureReason ?? "", r.LatencyMs.ToString("F0"),
                    r.InputTokens.ToString(), r.OutputTokens.ToString(),
                    r.RuleBelief, r.LlmBelief ?? "", r.BeliefStepDelta.ToString(),
                    r.RuleActionId ?? "", r.LlmActionId ?? "", r.LlmDestinationId ?? "",
                    r.LlmPrimaryReason ?? "", r.LlmProfileInfluence ?? "", r.LlmRelationshipInfluence ?? "",
                    Clean(r.LlmInterpretation), Clean(r.LlmGoal), Clean(r.LlmDialogue)
                }));
            return sb.ToString();
        }

        static string Clean(string s) => s == null ? "" : s.Replace("\t", " ").Replace("\n", " ").Replace("\r", "");

        static string BuildSummary(int attempts)
        {
            var sb = new StringBuilder();
            var compared = All.Where(r => r.Outcome == ShadowOutcome.Compared).ToList();

            sb.AppendLine($"[1] 총 Shadow 판단 시도       : {attempts}");
            sb.AppendLine($"[2] 실제 API 발사             : {All.Count(r => r.Outcome != ShadowOutcome.SkippedConcurrencyLimit)}");
            sb.AppendLine($"[4] SkippedConcurrencyLimit   : {All.Count(r => r.Outcome == ShadowOutcome.SkippedConcurrencyLimit)}");
            sb.AppendLine($"[5] 성공 {compared.Count} / Timeout {All.Count(r => r.FailureReason == "Timeout")} "
                        + $"/ 검증·파싱 실패 {All.Count(r => r.Outcome == ShadowOutcome.Failed && r.FailureReason != "Timeout")} "
                        + $"/ StaleSession {All.Count(r => r.Outcome == ShadowOutcome.StaleSession)}");
            foreach (var g in All.Where(r => r.Outcome == ShadowOutcome.Failed).GroupBy(r => r.FailureReason))
                sb.AppendLine($"      실패 사유 {g.Key}: {g.Count()}건");

            if (compared.Count > 0)
            {
                sb.AppendLine($"[6] 응답 시간(ms)  평균 {compared.Average(r => r.LatencyMs):F0} / 최소 {compared.Min(r => r.LatencyMs):F0} / 최대 {compared.Max(r => r.LatencyMs):F0}");
                var tok = compared.Where(r => r.TokensAvailable).ToList();
                if (tok.Count > 0)
                {
                    int inSum = tok.Sum(r => r.InputTokens), outSum = tok.Sum(r => r.OutputTokens);
                    double cost = inSum / 1_000_000.0 * 0.15 + outSum / 1_000_000.0 * 0.60;
                    sb.AppendLine($"[7] 토큰 (보고 {tok.Count}건)  입력 합계 {inSum} / 출력 합계 {outSum} / 건당 평균 {inSum / tok.Count}+{outSum / tok.Count}");
                    sb.AppendLine($"[8] 예상 비용 (gpt-4o-mini)  ${cost:F5}");
                }
                else sb.AppendLine("[7][8] 토큰 보고 없음");
                sb.AppendLine($"      프롬프트 평균 {compared.Average(r => r.PromptChars):F0}자 / 응답 평균 {compared.Average(r => r.ResponseChars):F0}자");

                int same = compared.Count(r => !r.BeliefDiffers);
                sb.AppendLine($"[9] Belief 일치율            : {same}/{compared.Count} ({100.0 * same / compared.Count:F0}%)");
                sb.AppendLine($"[10] 단계 차이 분포          : 동일 {compared.Count(r => r.BeliefStepDelta == 0)} / "
                            + $"±1 {compared.Count(r => Math.Abs(r.BeliefStepDelta) == 1)} / "
                            + $"±2이상 {compared.Count(r => Math.Abs(r.BeliefStepDelta) >= 2)}");
                foreach (var g in compared.GroupBy(r => r.BeliefStepDelta).OrderBy(g => g.Key))
                    sb.AppendLine($"       {g.Key:+0;-0;0}단계: {g.Count()}건");

                int actSame = compared.Count(r => !r.ActionDiffers);
                sb.AppendLine($"[11] Action 일치율           : {actSame}/{compared.Count} ({100.0 * actSame / compared.Count:F0}%)");
                sb.AppendLine($"[12] Destination            : stay {compared.Count(r => r.LlmDestinationId == "stay")} / 이동 {compared.Count(r => r.LlmDestinationId != "stay")}");
                sb.AppendLine("       (규칙 기반은 정보 판단에서 이동을 정하지 않으므로 직접 비교 대상이 없다)");

                sb.AppendLine("[13] primaryReason 분포      : " + string.Join(", ",
                    compared.GroupBy(r => r.LlmPrimaryReason).OrderByDescending(g => g.Count()).Select(g => $"{g.Key} {g.Count()}")));
                sb.AppendLine($"[14] profileInfluence 사용   : {compared.Count(r => r.LlmProfileInfluence != "none")}/{compared.Count}");
                sb.AppendLine($"[15] relationshipInfluence   : {compared.Count(r => r.LlmRelationshipInfluence != "none")}/{compared.Count}");

                sb.AppendLine("[16] 카드 등급별 평균 단계차");
                foreach (var g in compared.GroupBy(r => Tier(r.CardCredibility)).OrderByDescending(g => g.Key))
                    sb.AppendLine($"       {g.Key,-7} {g.Count()}건  평균 {g.Average(r => r.BeliefStepDelta):+0.00;-0.00;0.00}  "
                                + $"(규칙 {string.Join("/", g.GroupBy(x => x.RuleBelief).Select(x => x.Key + ":" + x.Count()))}"
                                + $" vs LLM {string.Join("/", g.GroupBy(x => x.LlmBelief).Select(x => x.Key + ":" + x.Count()))})");

                sb.AppendLine("[17] 동일 (NPC,카드) 반복 시 LLM Belief 안정성");
                foreach (var g in compared.GroupBy(r => r.NpcId + "|" + r.CardId).Where(g => g.Count() > 1))
                    sb.AppendLine($"       {g.Key}: {string.Join(" → ", g.Select(x => x.LlmBelief))}"
                                + $"  {(g.Select(x => x.LlmBelief).Distinct().Count() == 1 ? "일관" : "★변동")}");

                sb.AppendLine($"[18] 허용 밖 값 반환         : {All.Count(r => r.Outcome == ShadowOutcome.Failed && (r.FailureReason ?? "").Contains("Invalid") || (r.FailureReason ?? "").Contains("Unknown") || (r.FailureReason ?? "").Contains("Irrelevant"))}건 (전부 무효 처리됨)");
            }

            return sb.ToString();
        }

        static string Tier(float cred) => cred >= 0.60f ? "High" : cred >= 0.45f ? "Medium" : "Low";
    }
}
