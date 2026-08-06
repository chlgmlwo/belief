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
    /// <b>관계와 기억</b>이 실제 LLM 판단에 닿는지 확인한다. 신뢰도 3축 검증에서는 같은 장소에
    /// 다른 인물이 없고 전달자도 없어 <b>관계가 원리상 작동할 수 없는 조건</b>이었다 -
    /// relationshipInfluence가 78건 내내 none이었던 것은 "안 쓴 것"이 아니라 "쓸 수 없던 것"이다.
    ///
    /// 그래서 여기서는 관계가 성립하는 상황을 <b>실제로 만든다</b>:
    /// 관계 상대를 같은 장소에 세우고 그 인물을 전달자로 지정한다
    /// (JudgmentGroundsValidator는 "지금 자리에 있는 인물" 또는 "실제 전달자"만 근거로 허용한다).
    ///
    /// 기억은 프롬프트 개편(신뢰도 3섹션 추가) 전에 "5/5 변화, 가장 강력한 축"으로 측정됐던
    /// 것이라, 배치가 바뀐 지금도 그대로인지 함께 본다.
    ///
    /// <b>Play Mode 전용</b> - Edit 모드에서는 Transport 코루틴이 돌지 않아 전부 예외가 된다.
    /// </summary>
    public static class RelationMemoryTargetCheck
    {
        const string ProviderConfigPath = "Assets/Belief/Data/AI/LlmProviderConfig_Proxy.asset";
        const string OutPath = "Library/BeliefLogs/relation_memory_target_check.md";
        const int MaxCalls = 30;

        const double AvgInputTokens = 2000, AvgOutputTokens = 146;
        static double CostOf(int n) => n * (AvgInputTokens / 1e6 * 0.15 + AvgOutputTokens / 1e6 * 0.60);

        sealed class Budget : IJudgmentCallBudget
        {
            public int Used;
            public bool TryConsume(out string denyReason)
            {
                if (Used >= MaxCalls) { denyReason = "RelationMemoryCallLimit"; return false; }
                Used++; denyReason = null; return true;
            }
        }

        readonly struct Row
        {
            public readonly MajorNpcData Npc;
            public readonly NpcData Partner;   // null이면 관계 없음 조건
            public readonly bool WithMemory;
            public readonly int Repeat;
            public Row(MajorNpcData npc, NpcData partner, bool mem, int repeat)
            { Npc = npc; Partner = partner; WithMemory = mem; Repeat = repeat; }
            public string Label => (Partner != null ? "관계O" : "관계X") + "/" + (WithMemory ? "기억O" : "기억X");
        }

        [MenuItem("BELIEF/Diagnostics/관계·기억 표적 검증 (실제 API)", priority = 142)]
        static void Run()
        {
            if (!Application.isPlaying)
            {
                EditorUtility.DisplayDialog("관계·기억 표적 검증", "Play Mode에서 실행하세요.", "확인");
                return;
            }
            StartHeadless();
        }

        public static void StartHeadless()
        {
            if (!Application.isPlaying)
            {
                Debug.LogError("[관계·기억] Play Mode가 아닙니다 - 중단합니다.");
                return;
            }
            var rows = BuildScenarios(out string why);
            if (rows == null || rows.Count == 0) { Debug.LogError("[관계·기억] 시나리오를 만들 수 없습니다 - " + why); return; }
            Debug.Log($"[관계·기억] 시작 - {rows.Count}건, 상한 {MaxCalls}회, 예상 약 ${CostOf(rows.Count):F4}");
            _ = RunAsync(rows);
        }

        /// <summary>관계를 가진 NPC 2명을 실제 에셋에서 찾아 4조합 × 3회로 짠다.</summary>
        static List<Row> BuildScenarios(out string why)
        {
            why = null;
            var all = AssetDatabase.FindAssets("t:MajorNpcData")
                .Select(g => AssetDatabase.LoadAssetAtPath<MajorNpcData>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(n => n != null).ToList();

            // 관계 배열에 실재하는 상대가 있는 NPC만 후보로 쓴다.
            var withRel = all.Where(n => n.relationships != null
                                         && n.relationships.Any(r => r.other != null)).ToList();
            if (withRel.Count == 0) { why = "관계를 가진 MajorNpcData가 없습니다."; return null; }

            var picks = withRel.Take(2).ToList();
            var rows = new List<Row>();
            for (int r = 1; r <= 3; r++)
                foreach (var npc in picks)
                {
                    var partner = npc.relationships.First(x => x.other != null).other;
                    rows.Add(new Row(npc, partner, false, r));
                    rows.Add(new Row(npc, partner, true, r));
                    rows.Add(new Row(npc, null, false, r));
                    rows.Add(new Row(npc, null, true, r));
                }
            return rows;   // 2명 × 4조합 × 3회 = 24
        }

        static async Task RunAsync(List<Row> rows)
        {
            var config = AssetDatabase.LoadAssetAtPath<LlmProviderConfig>(ProviderConfigPath);
            var tuning = First<BeliefTuningData>();
            if (config == null || tuning == null) { Debug.LogError("[관계·기억] 설정 에셋을 찾을 수 없습니다."); return; }

            var evaluators = new IBeliefEvaluator[]
            {
                new PersonalityEvaluator(), new ExistingBeliefEvaluator(), new CredibilityEvaluator(),
                new SourceEvaluator(), new GoalEvaluator(), new SituationEvaluator(),
                new MemoryEvaluator(First<MemoryTuningData>()),
            };
            var beliefSystem = new BeliefSystem(evaluators, tuning, null, First<LocationMechanicsSettings>());
            var budget = new Budget();
            var thinker = ThinkerFactory.CreateIntegrated(beliefSystem, config, 30000, budget, false, null);
            if (thinker == null) { Debug.LogError("[관계·기억] thinker 조립 실패."); return; }

            var card = All<InformationCardData>().FirstOrDefault(c => c != null && c.information != null && c.source != null);
            var locData = All<LocationData>().FirstOrDefault(l => l != null && l.locationId == "LOC_GUARD_POST")
                          ?? All<LocationData>().FirstOrDefault(l => l != null);
            if (card == null || locData == null) { Debug.LogError("[관계·기억] 카드/장소를 찾을 수 없습니다."); return; }

            var sb = new StringBuilder();
            sb.AppendLine("# 관계·기억 표적 검증");
            sb.AppendLine($"- 카드 {card.cardId} / 장소 {locData.locationId}");
            sb.AppendLine();
            sb.AppendLine("| # | NPC | 조건 | Belief | primaryReason | relationInfluence | profile | 관계언급 | 기억언급 | 폴백 |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|");

            int i = 0, fallbacks = 0;
            foreach (var row in rows)
            {
                if (budget.Used >= MaxCalls) { Debug.LogWarning("[관계·기억] 상한 도달."); break; }
                i++;

                var npc = new NpcState(row.Npc);
                var where = new LocationState(locData);

                // 관계 조건: 상대를 같은 자리에 세우고 그 인물을 전달자로 준다.
                var present = new List<NpcState>();
                NpcState propagator = null;
                if (row.Partner != null)
                {
                    propagator = new NpcState(row.Partner);
                    present.Add(propagator);
                }

                var memory = row.WithMemory
                    ? new WorkingMemory(new List<MemoryEntry>
                      {
                          new MemoryEntry("예전에 이 출처가 전한 이야기가 사실로 확인된 적이 있다.",
                              turnRecorded: 1, importance: 0.8f,
                              relatedSourceId: card.source.sourceId, valence: 1f),
                      })
                    : null;

                var ctx = new NpcJudgmentContext(
                    npc, card, where, 3, BeliefState.Unknown, null, memory,
                    row.Npc.availableActions != null ? row.Npc.availableActions.Where(a => a != null).ToList()
                                                     : new List<NpcActionData>(),
                    new List<LocationData>(), present, propagator);

                var identity = new JudgmentRequestIdentity("STAGE_TEST", "MISSION_TEST", 1, 3,
                    row.Npc.npcId, card.cardId, $"rm-{i}");
                var outcome = await thinker.DecideAsync(ctx, identity, null);

                bool fb = outcome.Source == JudgmentResultSource.RuleBasedFallback;
                if (fb) fallbacks++;
                var j = outcome.Judgment;
                var g = j.Grounds;
                string text = (j.Interpretation ?? "") + " " + (j.Goal ?? "") + " " + (j.Dialogue ?? "");

                sb.AppendLine($"| {i} | {Short(row.Npc.npcId)} | {row.Label} | {j.Belief} | {g.PrimaryReason} | "
                    + $"{g.RelationshipInfluence ?? "-"} | {g.ProfileInfluence ?? "-"} | "
                    + $"{(row.Partner != null && text.Contains(row.Partner.displayName) ? "O" : "-")} | "
                    + $"{(text.Contains("예전") || text.Contains("과거") || text.Contains("확인된") ? "O" : "-")} | "
                    + $"{(fb ? outcome.FallbackReason : "-")} |");
            }

            sb.AppendLine();
            sb.AppendLine($"- 호출 {budget.Used}회 / 폴백 {fallbacks}건 / 약 ${CostOf(budget.Used):F4}");

            Directory.CreateDirectory(Path.GetDirectoryName(OutPath));
            File.WriteAllText(OutPath, sb.ToString());
            Debug.Log($"[관계·기억] 완료 - {budget.Used}회, 폴백 {fallbacks}건\n결과: {OutPath}");
        }

        static string Short(string id) => id.Replace("npc_major_", "").Replace("npc_", "");
        static IEnumerable<T> All<T>() where T : ScriptableObject =>
            AssetDatabase.FindAssets("t:" + typeof(T).Name)
                .Select(g => AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(g)));
        static T First<T>() where T : ScriptableObject => All<T>().FirstOrDefault(a => a != null);
    }
}
