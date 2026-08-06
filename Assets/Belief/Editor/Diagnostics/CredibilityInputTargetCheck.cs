using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Belief.AI;
using Belief.AI.LLM;
using Belief.Data;
using Belief.Domain;
using Belief.Systems;
using Belief.Systems.BeliefEvaluators;
using UnityEditor;
using UnityEngine;

namespace Belief.EditorTools.Diagnostics
{
    /// <summary>
    /// 이번에 프롬프트로 연결한 세 수치(내용 신뢰도 · 출처 신뢰도 · 장소 신뢰도 보정)가
    /// <b>실제 LLM 판단에 닿는지</b>를 실제 API로 확인한다. 구조 테스트
    /// (<see cref="PromptCredibilityInputCheck"/>)를 먼저 통과시킨 뒤에 쓴다.
    ///
    /// 게임 세계를 건드리지 않는다 - 임시 NpcState/LocationState를 만들어 판단만 받고 버린다.
    /// 카드 수치는 <b>메모리에서만</b> 잠시 덮어쓰고 finally에서 원래대로 되돌린다
    /// (에셋을 Dirty로 만들지도, 저장하지도 않는다).
    ///
    /// <b>Play Mode 전용</b> - OpenAiTransport가 UnityWebRequest를 CoroutineRunner(MonoBehaviour)
    /// 코루틴으로 돌려서, Edit 모드에서는 요청이 아예 나가지 않고 전부 TransportException이 된다.
    /// </summary>
    public static class CredibilityInputTargetCheck
    {
        const string ProviderConfigPath = "Assets/Belief/Data/AI/LlmProviderConfig_Proxy.asset";
        const string OutPath = "Library/BeliefLogs/credibility_input_target_check.md";

        /// <summary>Transport 호출 절대 상한 - 시나리오가 늘어도 이 값을 넘지 않는다(작업 지시 §8: 36회 이내).</summary>
        const int MaxCalls = 36;

        const double AvgInputTokens = 1900, AvgOutputTokens = 146;
        const double InPricePerM = 0.15, OutPricePerM = 0.60;
        static double CostOf(int calls) =>
            calls * (AvgInputTokens / 1e6 * InPricePerM + AvgOutputTokens / 1e6 * OutPricePerM);

        sealed class Budget : IJudgmentCallBudget
        {
            public int Used;
            public bool TryConsume(out string denyReason)
            {
                if (Used >= MaxCalls) { denyReason = "CredibilityTargetCallLimit"; return false; }
                Used++; denyReason = null; return true;
            }
        }

        readonly struct Row
        {
            public readonly string Npc, Label, Loc;
            public readonly float Cred, Trust;
            public readonly int Repeat;

            /// <summary>값이 있으면 <b>고정된 한 장소</b>를 쓰면서 credibilityModifier만 이 값으로
            /// 덮어쓴다 - 설명·민감유형·확산속도·밀집도·이름이 전부 그대로라 보정만 변수로 남는다.
            /// null이면 보정에 맞는 실제 장소를 골라 쓴다(그 경우 다른 속성도 함께 달라진다).</summary>
            public readonly LocationCredibilityModifier? Override;

            public Row(string npc, string label, float cred, float trust, string loc, int repeat,
                LocationCredibilityModifier? over = null)
            { Npc = npc; Label = label; Cred = cred; Trust = trust; Loc = loc; Repeat = repeat; Override = over; }
        }

        [MenuItem("BELIEF/Diagnostics/신뢰도 입력 표적 검증 (실제 API)", priority = 141)]
        static void Run()
        {
            if (!Application.isPlaying)
            {
                EditorUtility.DisplayDialog("신뢰도 입력 표적 검증",
                    "Play Mode에서 실행하세요.\n\n"
                    + "LLM Transport가 UnityWebRequest 코루틴을 쓰기 때문에 Edit 모드에서는 요청이 나가지 않습니다.\n"
                    + "게임 세계는 건드리지 않으므로 아무 씬이나 Play로 띄운 뒤 실행하면 됩니다.", "확인");
                return;
            }

            var rows = BuildScenarios();
            if (!EditorUtility.DisplayDialog("신뢰도 입력 표적 검증 - 실제 API를 호출합니다",
                    $"• 시나리오 : {rows.Count}건 (호출 상한 {MaxCalls}회)\n"
                    + $"• 예상 비용 : 약 ${CostOf(rows.Count):F4}\n"
                    + $"• 최악 비용 : 약 ${CostOf(MaxCalls):F4}\n\n"
                    + "카드 수치는 메모리에서만 잠시 바꾸고 즉시 되돌립니다 - 에셋은 저장하지 않습니다.",
                    "실행", "취소"))
                return;

            _ = RunAsync(rows);
        }

        /// <summary>확인 대화상자 없이 실행한다 - 자동화(에디터 커맨드)용 진입점.
        /// <b>실제 API 요금이 발생하므로</b> 호출자가 비용을 이미 확인했다는 전제다.</summary>
        public static void StartHeadless()
        {
            if (!Application.isPlaying)
            {
                Debug.LogError("[신뢰도 표적] Play Mode가 아닙니다 - 요청이 나가지 않으므로 중단합니다.");
                return;
            }
            var rows = BuildScenarios();
            Debug.Log($"[신뢰도 표적] 시작 - 시나리오 {rows.Count}건, 상한 {MaxCalls}회, 예상 약 ${CostOf(rows.Count):F4}");
            _ = RunAsync(rows);
        }

        /// <summary>장소 보정만 분리해서 본다 - 같은 장소에서 credibilityModifier만 Low/High로
        /// 덮어쓰고 나머지는 전부 고정한다. 앞선 검증이 보정별로 다른 장소를 써서 설명·민감유형·
        /// 확산속도가 함께 바뀐 탓에 방향을 판정할 수 없었던 것을 바로잡는다.</summary>
        public static void StartLocationIsolationHeadless()
        {
            if (!Application.isPlaying)
            {
                Debug.LogError("[장소 격리] Play Mode가 아닙니다 - 요청이 나가지 않으므로 중단합니다.");
                return;
            }
            var rows = BuildLocationIsolationScenarios();
            Debug.Log($"[장소 격리] 시작 - 시나리오 {rows.Count}건, 상한 {MaxCalls}회, 예상 약 ${CostOf(rows.Count):F4}");
            _ = RunAsync(rows, "장소 보정 분리 검증");
        }

        /// <summary>NPC 2명 × Low/High 2조건 × 6회 = 24회. 대비가 가장 큰 두 값(-0.10 / +0.10)에
        /// 표본을 몰아준다 - 동일 입력 반복이 절반가량 흔들리므로 조건당 6회는 있어야 방향이 보인다.</summary>
        static List<Row> BuildLocationIsolationScenarios()
        {
            var rows = new List<Row>();
            string[] npcs = { "npc_guard_captain", "npc_major_guild_master" };
            var mods = new[] { LocationCredibilityModifier.Low, LocationCredibilityModifier.High };
            for (int r = 1; r <= 6; r++)
                foreach (var n in npcs)
                    foreach (var m in mods)
                        rows.Add(new Row(n, "보정만 " + m, 0.50f, 0.50f, null, r, m));
            return rows;
        }

        /// <summary>NPC 3명 × 신뢰도 4조합 × 2회 = 24, 장소 3종 × 2회 = 6. 합계 30회.</summary>
        static List<Row> BuildScenarios()
        {
            var rows = new List<Row>();
            // 공작부인의 실제 에셋 id는 npc_major_lords_wife다(npc_major_duchess는 존재하지 않는다).
            string[] npcs = { "npc_guard_captain", "npc_major_guild_master", "npc_major_lords_wife" };
            var combos = new (string label, float cred, float trust)[]
            {
                ("내용H/출처H", 0.85f, 0.85f),
                ("내용H/출처L", 0.85f, 0.15f),
                ("내용L/출처H", 0.15f, 0.85f),
                ("내용L/출처L", 0.15f, 0.15f),
            };
            for (int r = 1; r <= 2; r++)
                foreach (var n in npcs)
                    foreach (var c in combos)
                        rows.Add(new Row(n, c.label, c.cred, c.trust, null, r));

            // 장소 비교는 신뢰도를 중간에 고정해 장소만 변수로 남긴다.
            for (int r = 1; r <= 2; r++)
                foreach (var mod in new[] { "High", "Neutral", "Low" })
                    rows.Add(new Row(npcs[0], "장소 " + mod, 0.50f, 0.50f, mod, r));

            return rows;
        }

        static async Task RunAsync(List<Row> rows) => await RunAsync(rows, "신뢰도 입력 표적 검증");

        static async Task RunAsync(List<Row> rows, string title)
        {
            var config = AssetDatabase.LoadAssetAtPath<LlmProviderConfig>(ProviderConfigPath);
            var tuning = FirstAsset<BeliefTuningData>();
            var mechanics = FirstAsset<LocationMechanicsSettings>();
            if (config == null || tuning == null)
            {
                Debug.LogError("[신뢰도 표적] LlmProviderConfig 또는 BeliefTuningData를 찾을 수 없습니다.");
                return;
            }

            // GameInstaller.BuildBeliefSystem과 같은 구성 - 폴백 경로가 실제 게임과 같아야
            // "폴백이 났는가"를 이 도구에서 판정하는 의미가 있다.
            var evaluators = new IBeliefEvaluator[]
            {
                new PersonalityEvaluator(),
                new ExistingBeliefEvaluator(),
                new CredibilityEvaluator(),
                new SourceEvaluator(),
                new GoalEvaluator(),
                new SituationEvaluator(),
                new MemoryEvaluator(FirstAsset<MemoryTuningData>()),
            };
            var beliefSystem = new BeliefSystem(evaluators, tuning, null, mechanics);
            var budget = new Budget();
            var thinker = ThinkerFactory.CreateIntegrated(beliefSystem, config, 30000, budget, false, null);
            if (thinker == null) { Debug.LogError("[신뢰도 표적] IntegratedLlmThinker 조립 실패."); return; }

            var card = AllAssets<InformationCardData>()
                .FirstOrDefault(c => c != null && c.information != null && c.source != null);
            if (card == null) { Debug.LogError("[신뢰도 표적] 쓸 수 있는 카드가 없습니다."); return; }

            float origCred = card.information.baseCredibility;
            float origTrust = card.source.baseTrustModifier;

            // 장소 격리 모드에서 보정을 덮어쓸 고정 장소 - 이 한 곳만 값을 바꿨다 되돌린다.
            var fixedLoc = AllAssets<LocationData>().FirstOrDefault(l => l != null && l.locationId == "LOC_GUARD_POST")
                           ?? AllAssets<LocationData>().FirstOrDefault(l => l != null);
            var origLocMod = fixedLoc != null ? fixedLoc.credibilityModifier : LocationCredibilityModifier.Unspecified;

            var sb = new StringBuilder();
            sb.AppendLine("# " + title);
            sb.AppendLine($"- 카드 {card.cardId} / 원본 cred {origCred:0.00} · trust {origTrust:0.00}");
            if (rows.Any(r => r.Override.HasValue))
                sb.AppendLine($"- 고정 장소 {fixedLoc?.locationId} (원래 보정 {origLocMod}) — 보정만 덮어쓰고 나머지 속성은 전부 동일");
            sb.AppendLine();
            sb.AppendLine("| # | NPC | 조합 | 장소 | Belief | Action | Dest | primaryReason | profile | relation | 내용언급 | 출처언급 | 장소언급 | 폴백 |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");

            int i = 0, fallbacks = 0;
            try
            {
                foreach (var row in rows)
                {
                    if (budget.Used >= MaxCalls) { Debug.LogWarning("[신뢰도 표적] 호출 상한 도달 - 남은 시나리오를 건너뜁니다."); break; }
                    i++;

                    // 메모리 전용 오버라이드 - SetDirty/SaveAssets를 부르지 않으므로 파일은 그대로다.
                    card.information.baseCredibility = row.Cred;
                    card.source.baseTrustModifier = row.Trust;

                    var npc = MakeNpc(row.Npc);
                    if (npc == null) { sb.AppendLine($"| {i} | {row.Npc} | - | - | (NPC 에셋 없음) |||||||||"); continue; }

                    LocationState where;
                    if (row.Override.HasValue)
                    {
                        if (fixedLoc == null) { sb.AppendLine($"| {i} | {row.Npc} | {row.Label} | - | (고정 장소 없음) |||||||||"); continue; }
                        fixedLoc.credibilityModifier = row.Override.Value;   // 메모리 전용, finally에서 복원
                        where = new LocationState(fixedLoc);
                    }
                    else where = MakeLocation(row.Loc);
                    if (where == null) { sb.AppendLine($"| {i} | {row.Npc} | {row.Label} | {row.Loc} | (장소 없음) |||||||||"); continue; }

                    var ctx = new NpcJudgmentContext(
                        npc, card, where, 3, BeliefState.Unknown, null, null,
                        ActionsOf(npc), new List<LocationData>(), new List<NpcState>(), null);

                    string prompt = UnifiedPromptBuilder.Build(ctx);
                    var identity = new JudgmentRequestIdentity("STAGE_TEST", "MISSION_TEST", 1, 3,
                        row.Npc, card.cardId, $"cred-{i}");

                    var outcome = await thinker.DecideAsync(ctx, identity, null);

                    bool fb = outcome.Source == JudgmentResultSource.RuleBasedFallback;
                    if (fb) fallbacks++;
                    var j = outcome.Judgment;
                    var g = j.Grounds;
                    string interp = j.Interpretation ?? "";
                    string reasonText = interp + " " + (g.PrimaryReason ?? "");

                    sb.AppendLine($"| {i} | {Short(row.Npc)} | {row.Label} | {row.Loc ?? "-"} | {j.Belief} | "
                        + $"{(j.Action != null ? j.Action.actionId : "-")} | "
                        + $"{(j.Destination != null ? j.Destination.locationId : "stay")} | "
                        + $"{g.PrimaryReason} | {g.ProfileInfluence ?? "-"} | {g.RelationshipInfluence ?? "-"} | "
                        + $"{Mark(reasonText, "내용", "주장", "그럴듯", "신빙")} | "
                        + $"{Mark(reasonText, "출처", "행정", "순찰")} | {Mark(reasonText, "장소", "초소", "여기")} | "
                        + $"{(fb ? outcome.FallbackReason : "-")} |");

                    if (i <= 2) sb.AppendLine($"\n<details><summary>프롬프트 표본 {i}</summary>\n\n```\n{prompt}\n```\n</details>\n");
                }
            }
            finally
            {
                card.information.baseCredibility = origCred;
                card.source.baseTrustModifier = origTrust;
                if (fixedLoc != null) fixedLoc.credibilityModifier = origLocMod;
            }

            sb.AppendLine();
            sb.AppendLine($"- 호출 {budget.Used}회 / 상한 {MaxCalls} / 폴백 {fallbacks}건");
            sb.AppendLine($"- 실비 추정 약 ${CostOf(budget.Used):F4}");
            sb.AppendLine($"- 카드 수치 복원: cred {card.information.baseCredibility:0.00} · trust {card.source.baseTrustModifier:0.00}");
            if (fixedLoc != null) sb.AppendLine($"- 장소 보정 복원: {fixedLoc.locationId} = {fixedLoc.credibilityModifier}");

            string outPath = rows.Any(r => r.Override.HasValue)
                ? "Library/BeliefLogs/credibility_location_isolation.md" : OutPath;
            Directory.CreateDirectory(Path.GetDirectoryName(outPath));
            File.WriteAllText(outPath, sb.ToString());
            Debug.Log($"[{title}] 완료 - 호출 {budget.Used}회, 폴백 {fallbacks}건, 약 ${CostOf(budget.Used):F4}\n결과: {outPath}");
        }

        // ── 헬퍼 ────────────────────────────────────────────────────────────────

        static string Mark(string text, params string[] keys) =>
            keys.Any(k => !string.IsNullOrEmpty(text) && text.Contains(k)) ? "O" : "-";

        static string Short(string npcId) => npcId.Replace("npc_major_", "").Replace("npc_", "");

        static IEnumerable<T> AllAssets<T>() where T : ScriptableObject =>
            AssetDatabase.FindAssets("t:" + typeof(T).Name)
                .Select(g => AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(g)));

        static T FirstAsset<T>() where T : ScriptableObject => AllAssets<T>().FirstOrDefault(a => a != null);

        static NpcState MakeNpc(string npcId)
        {
            var data = AllAssets<MajorNpcData>().FirstOrDefault(n => n != null && n.npcId == npcId);
            return data != null ? new NpcState(data) : null;
        }

        static List<NpcActionData> ActionsOf(NpcState npc) =>
            npc.Data is MajorNpcData m && m.availableActions != null
                ? m.availableActions.Where(a => a != null).ToList()
                : new List<NpcActionData>();

        /// <summary>장소 보정별 실제 장소를 고른다. null이면 아무 장소나(보정 무관) 쓴다.</summary>
        static LocationState MakeLocation(string modifierName)
        {
            var all = AllAssets<LocationData>().Where(l => l != null).ToList();
            LocationData pick;
            if (string.IsNullOrEmpty(modifierName))
                pick = all.FirstOrDefault(l => l.locationId == "LOC_GUARD_POST") ?? all.FirstOrDefault();
            else
            {
                var want = (LocationCredibilityModifier)Enum.Parse(typeof(LocationCredibilityModifier), modifierName);
                pick = all.FirstOrDefault(l => l.credibilityModifier == want);
            }
            return pick != null ? new LocationState(pick) : null;
        }
    }
}
