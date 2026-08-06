using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Belief.AI;
using Belief.AI.LLM;
using Belief.Data;
using Belief.Domain;
using Belief.Events;
using Belief.Systems;
using Belief.Systems.BeliefEvaluators;

namespace Belief.EditorTools
{
    /// <summary>
    /// IntegratedLlm 표적 비교. 5개 미션의 핵심 NPC에 대해 카드 등급과 맥락만 바꿔가며 실제 API를
    /// 호출하고, 같은 입력을 RuleOnly로도 돌려 나란히 기록한다.
    ///
    /// <b>Play Mode가 아니다</b> - 게임 세계를 건드리지 않고 StageData로부터 그때그때 임시 세계를
    /// 만들어 판단만 받는다. 판단 결과도 임시 세계에만 적용해 미션 성립 여부를 계산하고 버린다.
    /// 따라서 씬·저장된 진행·에셋은 전혀 바뀌지 않는다.
    ///
    /// 호출 상한은 <see cref="MaxCalls"/> 하나로 강제한다 - 시나리오를 잘못 짜서 조합이 폭발해도
    /// 그 이상은 Transport를 부르지 않는다.
    /// </summary>
    public static class IntegratedTargetCheck
    {
        const string ProviderConfigPath = "Assets/Belief/Data/AI/LlmProviderConfig_Proxy.asset";
        static string LogPath = "Library/BeliefLogs/integrated_target_check.md";
        static string JsonPath = "Library/BeliefLogs/integrated_target_check.jsonl";

        /// <summary>Transport 호출 절대 상한. 시나리오 수와 무관하게 이 값을 넘지 않는다.</summary>
        const int MaxCalls = 80;

        // 실측 평균(전 구역 플레이 62건 기준) - 사전 비용 표시에만 쓴다.
        const double AvgInputTokens = 1776, AvgOutputTokens = 146;
        const double InPricePerM = 0.15, OutPricePerM = 0.60;

        class Budget : IJudgmentCallBudget
        {
            public int Used;
            public bool TryConsume(out string denyReason)
            {
                if (Used >= MaxCalls) { denyReason = "TargetCheckCallLimit"; return false; }
                Used++; denyReason = null; return true;
            }
        }

        class Scenario
        {
            public string Block, MissionId, NpcId, CardId, Context;
            public bool WithCompanion, NeutralLocation, WithMemory, ViaRespread;
            public int Repeat;

            /// <summary>보류값 검증 전용 - 이 호출 동안만 카드 신뢰도를 이 값으로 바꾼다.
            /// 호출이 끝나면 반드시 원래대로 되돌리고 에셋에는 쓰지 않는다(SetDirty/SaveAssets 없음).</summary>
            public float? CardCredOverride;

            /// <summary>보류값 검증 전용 - 이 호출 동안만 대상 NPC의 trustBias를 이 값으로 바꾼다.</summary>
            public float? NpcBiasOverride;

            /// <summary>이 장소에 NPC를 세워 두고 판단시킨다(이동 후보와 무관하게 강제).
            /// 1차 측정에서 "중립 장소" 변형을 이동 후보 중에서 골랐더니 5명 중 3명은 조건에 맞는
            /// 후보가 없어 장소가 그대로였고, 그 결과가 "장소는 결과를 안 바꾼다"로 잘못 집계됐다.
            /// 진단 목적이므로 여기서는 이동 규칙을 따지지 않고 그냥 세운다.</summary>
            public LocationData ForceLocation;
        }

        class Row
        {
            public Scenario S;
            public string Interpretation, Goal, ActionId, Destination, PrimaryReason, ProfileInfluence, RelationshipInfluence;
            public BeliefState Belief;
            public bool Fallback; public string FallbackReason;
            public bool Success;
            public BeliefState RuleBelief; public bool RuleSuccess;
            public bool MentionsCard, MentionsSource;
        }

        static Budget budget;
        static readonly List<Row> rows = new List<Row>();

        [MenuItem("BELIEF/Diagnostics/IntegratedLlm 표적 비교 (실제 API)", priority = 121)]
        static void Run()
        {
            // Play Mode가 필요하다. OpenAiTransport는 UnityWebRequest를 CoroutineRunner(MonoBehaviour)
            // 코루틴으로 돌리는데, Edit 모드에서는 그 코루틴이 한 번도 tick하지 않아 요청이 아예 나가지
            // 않고 전부 TransportException으로 끝난다(그래서 요금도 발생하지 않는다).
            if (!Application.isPlaying)
            {
                EditorUtility.DisplayDialog("표적 비교",
                    "Play Mode에서 실행하세요.\n\n"
                    + "LLM Transport가 UnityWebRequest 코루틴을 쓰기 때문에 Edit 모드에서는 요청이 나가지 않습니다.\n"
                    + "이 검사는 게임 세계를 건드리지 않고 임시 세계에서 판단만 받으므로, "
                    + "아무 씬이나 Play로 띄운 뒤 실행하면 됩니다.", "확인");
                return;
            }

            var scenarios = BuildScenarios();
            double cost = scenarios.Count * (AvgInputTokens / 1e6 * InPricePerM + AvgOutputTokens / 1e6 * OutPricePerM);

            bool go = EditorUtility.DisplayDialog(
                "IntegratedLlm 표적 비교 - 실제 API를 호출합니다",
                $"5개 미션의 핵심 NPC를 카드 등급·맥락별로 비교합니다.\n\n"
                + $"• 시나리오 : {scenarios.Count}건\n"
                + $"• 호출 상한 : {MaxCalls}회 (초과분은 Transport를 부르지 않고 RuleBased 폴백)\n"
                + $"• 예상 비용 : 약 ${cost:F4} (gpt-4o-mini, 실측 평균 기준)\n"
                + $"• 최악 비용 : 약 ${MaxCalls * (AvgInputTokens / 1e6 * InPricePerM + AvgOutputTokens / 1e6 * OutPricePerM):F4}\n\n"
                + "씬·저장 진행·에셋은 수정하지 않습니다 - 임시 세계에서 판단만 받습니다.",
                "실행", "취소");
            if (!go) return;

            rows.Clear();
            budget = new Budget();
            _ = RunAllAsync(scenarios);
        }

        // ── 시나리오 구성 ────────────────────────────────────────────────────────

        static readonly (string mission, string npc)[] Targets =
        {
            ("MISSION_STAGE01_02", "npc_guard_captain"),
            ("MISSION_STAGE02_01", "npc_major_bookkeeper"),
            ("MISSION_STAGE02_02", "npc_major_guild_master"),
            ("MISSION_STAGE03_01", "npc_major_rival_noblewoman"),
            ("MISSION_STAGE03_02", "npc_major_lords_wife"),
        };

        // 등급마다 저신뢰 출처 1장 + 고신뢰 출처 1장을 넣어, credibility와 sourceTrust를 분리해서 본다.
        static readonly string[] HighCards = { "C-REL-02", "C-DIS-02" };   // cred 0.60 / src 0.25, 0.65
        static readonly string[] MidCards = { "C-ECO-02", "C-PUB-02" };    // cred 0.50, 0.40 / src 0.35, 0.40
        static readonly string[] LowCards = { "C-SEC-02", "C-MIL-02" };    // cred 0.30, 0.35 / src 0.25, 0.60
        const string ContextCard = "C-ECO-02";                              // 맥락 변형용 고정 카드

        // ── 보류값 검증 (카드 4장 + 수석하녀) ────────────────────────────────────

        /// <summary>카드 신뢰도, 내용 부합 NPC, 내용 충돌 NPC, 그 NPC가 대상인 미션.</summary>
        static readonly (string card, float orig, float cur, string fitNpc, string fitMission, string clashNpc, string clashMission)[] PendingCards =
        {
            ("C-CRI-02", 0.30f, 0.55f, "npc_major_customs_officer_s2", "MISSION_STAGE02_01", "npc_major_priest",     "MISSION_STAGE03_01"),
            ("C-REL-02", 0.30f, 0.60f, "npc_major_priest",             "MISSION_STAGE03_01", "npc_guard_captain",   "MISSION_STAGE01_02"),
            ("C-MIL-02", 0.55f, 0.35f, "npc_major_knight_commander",   "MISSION_STAGE04_02", "npc_major_innkeeper", "MISSION_STAGE04_03"),
            ("C-ECO-02", 0.35f, 0.50f, "npc_major_bookkeeper",         "MISSION_STAGE02_01", "npc_major_lords_wife","MISSION_STAGE03_02"),
        };

        /// <summary>수석하녀용 신뢰도×출처 2×2. 값은 건드리지 않고 기존 카드를 그대로 쓴다.</summary>
        static readonly (string card, string label)[] HeadMaidCards =
        {
            ("C-DIS-02", "高신뢰도/高출처"),
            ("C-REL-02", "高신뢰도/低출처"),
            ("C-MIL-02", "低신뢰도/高출처"),
            ("C-SEC-02", "低신뢰도/低출처"),
        };

        [MenuItem("BELIEF/Diagnostics/IntegratedLlm 보류값 검증 (실제 API)", priority = 124)]
        static void RunPendingValues()
        {
            if (!Application.isPlaying)
            {
                EditorUtility.DisplayDialog("보류값 검증",
                    "Play Mode에서 실행하세요 (Transport가 코루틴을 씁니다).", "확인");
                return;
            }

            var scenarios = BuildPendingScenarios();
            double cost = scenarios.Count * (AvgInputTokens / 1e6 * InPricePerM + AvgOutputTokens / 1e6 * OutPricePerM);
            if (!EditorUtility.DisplayDialog("보류값 검증 - 실제 API",
                $"미승인 보류 5항목을 원래값 vs 현재값으로 비교합니다.\n\n"
                + $"• 시나리오 : {scenarios.Count}건 (카드 4장 32 + 수석하녀 16)\n"
                + $"• 예상 비용 : 약 ${cost:F4}\n\n"
                + "카드 신뢰도와 trustBias는 호출 동안만 메모리에서 바뀌고 즉시 원복됩니다 — 에셋 미변경.",
                "실행", "취소")) return;

            rows.Clear();
            budget = new Budget();
            LogPath = "Library/BeliefLogs/pending_value_check.md";
            JsonPath = "Library/BeliefLogs/pending_value_check.jsonl";
            _ = RunAllAsync(scenarios);
        }

        [MenuItem("BELIEF/Diagnostics/IntegratedLlm 원복 스모크 테스트 (실제 API)", priority = 125)]
        static void RunRevertSmoke()
        {
            if (!Application.isPlaying)
            {
                EditorUtility.DisplayDialog("원복 스모크 테스트",
                    "Play Mode에서 실행하세요 (Transport가 코루틴을 씁니다).", "확인");
                return;
            }

            var scenarios = BuildSmokeScenarios();
            double cost = scenarios.Count * (AvgInputTokens / 1e6 * InPricePerM + AvgOutputTokens / 1e6 * OutPricePerM);
            if (!EditorUtility.DisplayDialog("원복 스모크 테스트 - 실제 API",
                $"원복된 값이 프롬프트·파싱·적용 경로를 정상 통과하는지만 확인합니다.\n\n"
                + $"• 시나리오 : {scenarios.Count}건 (조합당 1회)\n"
                + $"• 예상 비용 : 약 ${cost:F4}\n\n"
                + "새 밸런스 통계를 만드는 것이 아니라 스모크 테스트입니다 - 값 치환 없이 현재 에셋 값 그대로 사용합니다.",
                "실행", "취소")) return;

            rows.Clear();
            budget = new Budget();
            LogPath = "Library/BeliefLogs/revert_smoke_check.md";
            JsonPath = "Library/BeliefLogs/revert_smoke_check.jsonl";
            _ = RunAllAsync(scenarios);
        }

        /// <summary>원복 후 스모크 - 값 치환(Override) 없이 현재 에셋 값 그대로, 조합당 1회.</summary>
        static List<Scenario> BuildSmokeScenarios()
        {
            var list = new List<Scenario>();
            foreach (var p in PendingCards)
            {
                list.Add(new Scenario { Block = "스모크:" + p.card, MissionId = p.fitMission,
                    NpcId = p.fitNpc, CardId = p.card, Context = "부합" });
                list.Add(new Scenario { Block = "스모크:" + p.card, MissionId = p.clashMission,
                    NpcId = p.clashNpc, CardId = p.card, Context = "충돌" });
            }
            foreach (var (card, label) in HeadMaidCards)
                list.Add(new Scenario { Block = "스모크:수석하녀", MissionId = "MISSION_STAGE03_02",
                    NpcId = "npc_major_head_maid", CardId = card, Context = label });
            return list;
        }

        static List<Scenario> BuildPendingScenarios()
        {
            var list = new List<Scenario>();

            foreach (var p in PendingCards)
                foreach (var (npc, mission, role) in new[] { (p.fitNpc, p.fitMission, "부합"), (p.clashNpc, p.clashMission, "충돌") })
                    foreach (var (val, tag) in new[] { (p.orig, "원래"), (p.cur, "현재") })
                        for (int rep = 1; rep <= 2; rep++)
                            list.Add(new Scenario
                            {
                                Block = "카드:" + p.card, MissionId = mission, NpcId = npc, CardId = p.card,
                                Context = $"{role}/{tag} {val:0.00} #{rep}",
                                CardCredOverride = val, Repeat = rep
                            });

            foreach (var (card, label) in HeadMaidCards)
                foreach (var (bias, tag) in new[] { (0.80f, "원래0.80"), (0.62f, "현재0.62") })
                    for (int rep = 1; rep <= 2; rep++)
                        list.Add(new Scenario
                        {
                            Block = "수석하녀", MissionId = "MISSION_STAGE03_02",
                            NpcId = "npc_major_head_maid", CardId = card,
                            Context = $"{label}/{tag} #{rep}", NpcBiasOverride = bias, Repeat = rep
                        });

            return list;
        }

        [MenuItem("BELIEF/Diagnostics/IntegratedLlm 장소 영향 재측정 (실제 API)", priority = 122)]
        static void RunLocationOnly()
        {
            if (!Application.isPlaying)
            {
                EditorUtility.DisplayDialog("장소 영향 재측정",
                    "Play Mode에서 실행하세요 (Transport가 코루틴을 씁니다).", "확인");
                return;
            }

            var scenarios = BuildLocationScenarios();
            if (scenarios.Count == 0) { Debug.LogError("[장소측정] 시나리오를 만들지 못했습니다."); return; }
            double cost = scenarios.Count * (AvgInputTokens / 1e6 * InPricePerM + AvgOutputTokens / 1e6 * OutPricePerM);

            if (!EditorUtility.DisplayDialog("장소 영향 재측정 - 실제 API",
                $"같은 카드를 신뢰도 보정이 가장 높은 장소와 가장 낮은 장소에서 각각 판단시킵니다.\n\n"
                + $"• 시나리오 : {scenarios.Count}건 (NPC 5명 × 장소 2곳 × 2회 반복)\n"
                + $"• 예상 비용 : 약 ${cost:F4}\n\n"
                + "2회 반복은 LLM 비결정성(실측 10건 중 4건 불일치)을 걸러내기 위한 것입니다.",
                "실행", "취소")) return;

            rows.Clear();
            budget = new Budget();
            LogPath = "Library/BeliefLogs/integrated_location_check.md";   // 1차 결과를 덮어쓰지 않는다
            JsonPath = "Library/BeliefLogs/integrated_location_check.jsonl";
            _ = RunAllAsync(scenarios);
        }

        /// <summary>각 스테이지에서 credibilityModifier가 가장 높은 장소와 가장 낮은 장소를 뽑아
        /// 같은 NPC·같은 카드를 양쪽에서 판단시킨다. 대비가 최대일 때도 결과가 같다면 그때야
        /// "장소가 결과를 바꾸지 않는다"고 말할 수 있다.</summary>
        static List<Scenario> BuildLocationScenarios()
        {
            int Rank(LocationCredibilityModifier m) => m switch
            {
                LocationCredibilityModifier.VeryHigh => 4,
                LocationCredibilityModifier.High => 3,
                LocationCredibilityModifier.Neutral => 2,
                LocationCredibilityModifier.Unspecified => 2,
                LocationCredibilityModifier.Low => 1,
                _ => 2
            };

            var list = new List<Scenario>();
            foreach (var (mission, npc) in Targets)
            {
                var stage = AssetDatabase.FindAssets("t:StageData")
                    .Select(g => AssetDatabase.LoadAssetAtPath<StageData>(AssetDatabase.GUIDToAssetPath(g)))
                    .FirstOrDefault(st => st != null && (st.missions ?? new MissionData[0])
                        .Any(m => m != null && m.missionId == mission));
                if (stage?.locations == null || stage.locations.Length < 2) continue;

                var best = stage.locations.Where(l => l != null).OrderByDescending(l => Rank(l.credibilityModifier)).First();
                var worst = stage.locations.Where(l => l != null).OrderBy(l => Rank(l.credibilityModifier)).First();
                if (best == worst) continue;

                for (int rep = 1; rep <= 2; rep++)
                {
                    list.Add(new Scenario { Block = "D.장소", MissionId = mission, NpcId = npc, CardId = ContextCard,
                        Context = $"유리({best.locationId}/{best.credibilityModifier}) #{rep}", ForceLocation = best, Repeat = rep });
                    list.Add(new Scenario { Block = "D.장소", MissionId = mission, NpcId = npc, CardId = ContextCard,
                        Context = $"불리({worst.locationId}/{worst.credibilityModifier}) #{rep}", ForceLocation = worst, Repeat = rep });
                }
            }
            return list;
        }

        static List<Scenario> BuildScenarios()
        {
            var list = new List<Scenario>();
            foreach (var (mission, npc) in Targets)
            {
                foreach (var card in HighCards.Concat(MidCards).Concat(LowCards))
                    list.Add(new Scenario { Block = "A.카드등급", MissionId = mission, NpcId = npc, CardId = card, Context = "기본" });

                list.Add(new Scenario { Block = "B.맥락", MissionId = mission, NpcId = npc, CardId = ContextCard, Context = "동석있음", WithCompanion = true });
                list.Add(new Scenario { Block = "B.맥락", MissionId = mission, NpcId = npc, CardId = ContextCard, Context = "중립장소", NeutralLocation = true });
                list.Add(new Scenario { Block = "B.맥락", MissionId = mission, NpcId = npc, CardId = ContextCard, Context = "기억있음", WithMemory = true });
                list.Add(new Scenario { Block = "B.맥락", MissionId = mission, NpcId = npc, CardId = ContextCard, Context = "재확산", ViaRespread = true, WithCompanion = true });

                list.Add(new Scenario { Block = "C.반복", MissionId = mission, NpcId = npc, CardId = HighCards[0], Context = "기본(2회차)", Repeat = 2 });
                list.Add(new Scenario { Block = "C.반복", MissionId = mission, NpcId = npc, CardId = LowCards[0], Context = "기본(2회차)", Repeat = 2 });
            }
            return list;
        }

        // ── 실행 ────────────────────────────────────────────────────────────────

        static async Task RunAllAsync(List<Scenario> scenarios)
        {
            var provider = AssetDatabase.LoadAssetAtPath<LlmProviderConfig>(ProviderConfigPath);
            if (provider == null) { Debug.LogError("[표적비교] Provider 설정을 찾을 수 없습니다: " + ProviderConfigPath); return; }

            var (beliefSystem, memTuning) = BuildBeliefSystem();
            if (beliefSystem == null) return;
            var thinker = ThinkerFactory.CreateIntegrated(beliefSystem, provider, 15000, budget);
            if (thinker == null) { Debug.LogError("[표적비교] Thinker를 만들 수 없습니다."); return; }

            var ruleThinker = new RuleBasedUnifiedThinker(beliefSystem, new RuleBasedMajorThinker());

            for (int i = 0; i < scenarios.Count; i++)
            {
                var s = scenarios[i];
                EditorUtility.DisplayProgressBar("IntegratedLlm 표적 비교",
                    $"{i + 1}/{scenarios.Count}  {s.NpcId} / {s.CardId} / {s.Context}", (float)i / scenarios.Count);
                try
                {
                    var row = await RunOneAsync(s, thinker, ruleThinker, beliefSystem, memTuning);
                    if (row != null) rows.Add(row);
                }
                catch (Exception ex) { Debug.LogWarning($"[표적비교] {s.NpcId}/{s.CardId} 실패: {ex.Message}"); }
            }
            EditorUtility.ClearProgressBar();

            WriteReport();
            Debug.Log($"[표적비교] 완료 - 호출 {budget.Used}/{MaxCalls}, 기록 {rows.Count}건\n{LogPath}");
        }

        static async Task<Row> RunOneAsync(Scenario s, IntegratedLlmThinker thinker,
            RuleBasedUnifiedThinker ruleThinker, BeliefSystem beliefSystem, MemoryTuningData memTuning)
        {
            // 보류값 검증용 임시 치환 - 반드시 finally에서 원복하고 에셋에는 쓰지 않는다.
            InformationData overriddenInfo = null; float savedCred = 0f;
            NpcData overriddenNpc = null; float savedBias = 0f;
            try
            {
                if (s.CardCredOverride.HasValue)
                {
                    var c = AssetDatabase.FindAssets("t:InformationCardData")
                        .Select(g => AssetDatabase.LoadAssetAtPath<InformationCardData>(AssetDatabase.GUIDToAssetPath(g)))
                        .FirstOrDefault(x => x != null && x.cardId == s.CardId);
                    if (c?.information != null)
                    {
                        overriddenInfo = c.information;
                        savedCred = overriddenInfo.baseCredibility;
                        overriddenInfo.baseCredibility = s.CardCredOverride.Value;
                    }
                }
                if (s.NpcBiasOverride.HasValue)
                {
                    overriddenNpc = AssetDatabase.FindAssets("t:NpcData")
                        .Select(g => AssetDatabase.LoadAssetAtPath<NpcData>(AssetDatabase.GUIDToAssetPath(g)))
                        .FirstOrDefault(n => n != null && n.npcId == s.NpcId);
                    if (overriddenNpc != null)
                    {
                        savedBias = overriddenNpc.trustBias;
                        overriddenNpc.trustBias = s.NpcBiasOverride.Value;
                    }
                }
                return await RunOneCore(s, thinker, ruleThinker, memTuning);
            }
            finally
            {
                if (overriddenInfo != null) overriddenInfo.baseCredibility = savedCred;
                if (overriddenNpc != null) overriddenNpc.trustBias = savedBias;
            }
        }

        static async Task<Row> RunOneCore(Scenario s, IntegratedLlmThinker thinker,
            RuleBasedUnifiedThinker ruleThinker, MemoryTuningData memTuning)
        {
            var world = World.Build(s, memTuning);
            if (world == null) return null;

            var identity = new JudgmentRequestIdentity("TARGET_CHECK", s.MissionId, 1, 1, world.Npc.Data.npcId,
                world.Card.cardId, Guid.NewGuid().ToString("N").Substring(0, 8));

            var outcome = await thinker.DecideAsync(world.Context, identity, null);

            var row = new Row { S = s };
            if (outcome.HasJudgment)
            {
                var j = outcome.Judgment;
                row.Interpretation = j.Interpretation; row.Belief = j.Belief; row.Goal = j.Goal;
                row.ActionId = j.Action != null ? j.Action.actionId : "-";
                row.Destination = j.Destination != null ? j.Destination.locationId : "stay";
                row.PrimaryReason = j.Grounds.PrimaryReason;
                row.ProfileInfluence = j.Grounds.ProfileInfluence;
                row.RelationshipInfluence = j.Grounds.RelationshipInfluence;
                row.Success = world.ApplyAndEvaluate(j.Belief, j.Action);
                row.MentionsCard = Mentions(j.Interpretation, world.Card);
                row.MentionsSource = world.Card.source != null && !string.IsNullOrEmpty(world.Card.source.displayName)
                                     && j.Interpretation != null && j.Interpretation.Contains(world.Card.source.displayName);
            }
            row.Fallback = outcome.Source == JudgmentResultSource.RuleBasedFallback;
            row.FallbackReason = outcome.FallbackReason;

            // 같은 입력을 RuleOnly로도 - 비교 기준선.
            var ruleWorld = World.Build(s, memTuning);
            var ruleResult = await ruleThinker.DecideAsync(ruleWorld.Context, null);
            if (ruleResult.IsValid)
            {
                var rj = ruleResult.Judgment;
                row.RuleBelief = rj.Belief;
                row.RuleSuccess = ruleWorld.ApplyAndEvaluate(rj.Belief, rj.Action);
            }
            return row;
        }

        static bool Mentions(string text, InformationCardData card)
        {
            if (string.IsNullOrEmpty(text) || card?.information == null) return false;
            var title = card.information.title;
            if (string.IsNullOrEmpty(title)) return false;
            foreach (var word in title.Split(' '))
                if (word.Length >= 2 && text.Contains(word)) return true;
            return false;
        }

        static (BeliefSystem, MemoryTuningData) BuildBeliefSystem()
        {
            // Play 중이므로 씬을 열지 않는다 - 현재 씬의 GameInstaller에서 튜닝 자산 참조만 읽는다.
            var installer = UnityEngine.Object.FindFirstObjectByType<Belief.Core.GameInstaller>();
            if (installer == null)
            {
                Debug.LogError("[표적비교] 현재 씬에 GameInstaller가 없습니다 - Zone1 등 정식 씬을 Play로 띄우세요.");
                return (null, null);
            }
            var so = new SerializedObject(installer);
            var bt = (BeliefTuningData)so.FindProperty("beliefTuning").objectReferenceValue;
            var mt = (MemoryTuningData)so.FindProperty("memoryTuning").objectReferenceValue;
            var mech = (LocationMechanicsSettings)so.FindProperty("locationMechanics").objectReferenceValue;
            var sys = new BeliefSystem(new IBeliefEvaluator[]
            {
                new PersonalityEvaluator(), new ExistingBeliefEvaluator(), new CredibilityEvaluator(),
                new SourceEvaluator(), new GoalEvaluator(), new SituationEvaluator(), new MemoryEvaluator(mt),
            }, bt, new BeliefDebugRepository(), mech);
            return (sys, mt);
        }

        // ── 임시 세계 ────────────────────────────────────────────────────────────

        class World
        {
            public NpcState Npc;
            public InformationCardData Card;
            public NpcJudgmentContext Context;
            public MissionSystem Mission;
            public MissionData MissionData;
            public Dictionary<LocationData, LocationState> Locations;
            public Dictionary<NpcData, NpcState> Npcs;
            public ActionResolutionSystem Resolution;
            public LocationState Where;

            public static World Build(Scenario s, MemoryTuningData memTuning)
            {
                var stage = AssetDatabase.FindAssets("t:StageData")
                    .Select(g => AssetDatabase.LoadAssetAtPath<StageData>(AssetDatabase.GUIDToAssetPath(g)))
                    .FirstOrDefault(st => st != null && (st.missions ?? new MissionData[0])
                        .Any(m => m != null && m.missionId == s.MissionId));
                if (stage == null) return null;
                var mission = stage.missions.First(m => m != null && m.missionId == s.MissionId);
                var card = stage.cardPool.cards.FirstOrDefault(c => c != null && c.cardId == s.CardId);
                if (card == null) return null;

                var w = new World { Card = card, MissionData = mission };
                w.Locations = new Dictionary<LocationData, LocationState>();
                foreach (var l in stage.locations ?? new LocationData[0]) w.Locations[l] = new LocationState(l);
                w.Npcs = new Dictionary<NpcData, NpcState>();
                foreach (var p in stage.npcPlacements ?? new NpcPlacementEntry[0])
                {
                    if (p.npc == null || p.EffectiveStartLocation == null) continue;
                    if (!w.Locations.ContainsKey(p.EffectiveStartLocation))
                        w.Locations[p.EffectiveStartLocation] = new LocationState(p.EffectiveStartLocation);
                    var ns = new NpcState(p.npc) { CurrentLocation = p.EffectiveStartLocation };
                    w.Npcs[p.npc] = ns;
                    w.Locations[p.EffectiveStartLocation].PresentNpcs.Add(ns);
                }
                var target = w.Npcs.Values.FirstOrDefault(n => n.Data.npcId == s.NpcId);
                if (target == null) return null;
                w.Npc = target;

                // 장소 강제 - 이동 규칙과 무관하게 지정한 장소에 세운다(D 블록 전용).
                if (s.ForceLocation != null && w.Locations.ContainsKey(s.ForceLocation)
                    && s.ForceLocation != target.CurrentLocation)
                {
                    w.Locations[target.CurrentLocation].PresentNpcs.Remove(target);
                    target.CurrentLocation = s.ForceLocation;
                    w.Locations[s.ForceLocation].PresentNpcs.Add(target);
                }

                // 중립 장소 - 그 NPC의 이동 후보 중 credibilityModifier가 Neutral/Unspecified인 곳.
                if (s.NeutralLocation && target.Data is MajorNpcData mj && mj.movementCandidates != null)
                {
                    var neutral = mj.movementCandidates.FirstOrDefault(l => l != null && w.Locations.ContainsKey(l)
                        && l != target.CurrentLocation
                        && (l.credibilityModifier == LocationCredibilityModifier.Neutral
                            || l.credibilityModifier == LocationCredibilityModifier.Unspecified));
                    if (neutral != null)
                    {
                        w.Locations[target.CurrentLocation].PresentNpcs.Remove(target);
                        target.CurrentLocation = neutral;
                        w.Locations[neutral].PresentNpcs.Add(target);
                    }
                }

                // 동석 인물 - 관계가 정의된 NPC를 같은 장소로 데려온다(관계 데이터는 바꾸지 않는다).
                NpcState companion = null;
                if ((s.WithCompanion || s.ViaRespread) && target.Data is MajorNpcData mjr && mjr.relationships != null)
                {
                    foreach (var rel in mjr.relationships)
                    {
                        if (rel.other == null) continue;
                        var other = w.Npcs.Values.FirstOrDefault(n => n.Data == rel.other);
                        if (other == null) continue;
                        if (other.CurrentLocation != target.CurrentLocation)
                        {
                            w.Locations[other.CurrentLocation].PresentNpcs.Remove(other);
                            other.CurrentLocation = target.CurrentLocation;
                            w.Locations[target.CurrentLocation].PresentNpcs.Add(other);
                        }
                        companion = other; break;
                    }
                }
                else
                {
                    // 기본 맥락은 단독 - 같은 장소의 다른 NPC를 치운다.
                    foreach (var other in w.Locations[target.CurrentLocation].PresentNpcs.Where(n => n != target).ToList())
                        w.Locations[target.CurrentLocation].PresentNpcs.Remove(other);
                }

                // 관련 기억 - 같은 카테고리의 과거 확인 기억을 주입한다(런타임 상태, 데이터 아님).
                if (s.WithMemory)
                    target.RecordMemory(new MemoryEntry(
                        "예전에 같은 종류의 이야기를 확인해 보니 사실이 아니었다.", 1, 0.8f,
                        relatedInformationCategoryId: card.information.categoryId, valence: -1f));

                w.Where = w.Locations[target.CurrentLocation];
                var wm = new MemorySelector().Select(target, new MemorySelectionContext(card, w.Where, 1), memTuning);

                var major = target.Data as MajorNpcData;
                w.Context = new NpcJudgmentContext(
                    target, card, w.Where, 1, target.GetBelief(card), target.CurrentGoal, wm,
                    major != null ? major.availableActions : new NpcActionData[0],
                    major != null ? major.movementCandidates : new LocationData[0],
                    w.Where.PresentNpcs.Where(n => n != target).ToList(),
                    s.ViaRespread ? companion : null,
                    w.Locations);

                var bus = new GameEventBus();
                w.Resolution = new ActionResolutionSystem(w.Locations, bus);
                w.Mission = new MissionSystem(mission, bus);
                w.Mission.BeginAttempt(new MissionEvaluationContext(w.Locations, w.Npcs, new List<DeliveredCardRecord>()));
                return w;
            }

            public bool ApplyAndEvaluate(BeliefState belief, NpcActionData action)
            {
                Npc.SetBelief(Card, belief);
                Npc.RecordReceivedInformation(Card, 1);
                if (action != null) Resolution.Apply(action, Npc, Card, Where, 1);
                return Mission.EvaluateSuccessProgress(MissionData,
                    new MissionEvaluationContext(Locations, Npcs, new List<DeliveredCardRecord>())) >= MissionData.SuccessTarget;
            }
        }

        // ── 보고 ────────────────────────────────────────────────────────────────

        static void WriteReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# IntegratedLlm 표적 비교");
            sb.AppendLine($"호출 {budget.Used}/{MaxCalls}, 기록 {rows.Count}건, 폴백 {rows.Count(r => r.Fallback)}건");
            sb.AppendLine();

            sb.AppendLine("## 전체 기록");
            sb.AppendLine("| 블록 | NPC | 카드 | 맥락 | LLM Belief | 성공 | Rule Belief | Rule성공 | Action | 목적지 | primaryReason | profile | relationship |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|");
            foreach (var r in rows)
                sb.AppendLine($"| {r.S.Block} | {Short(r.S.NpcId)} | {r.S.CardId} | {r.S.Context} | {r.Belief} | {(r.Success ? "O" : "-")} "
                    + $"| {r.RuleBelief} | {(r.RuleSuccess ? "O" : "-")} | {r.ActionId} | {r.Destination} | {r.PrimaryReason} | {r.ProfileInfluence} | {r.RelationshipInfluence} |");

            sb.AppendLine();
            sb.AppendLine("## NPC별 카드 등급에 따른 Belief 분포 (블록 A)");
            foreach (var g in rows.Where(r => r.S.Block == "A.카드등급").GroupBy(r => r.S.NpcId))
            {
                var byGrade = new[] { ("High", HighCards), ("Mid", MidCards), ("Low", LowCards) };
                sb.Append($"- **{Short(g.Key)}** : ");
                foreach (var (name, ids) in byGrade)
                {
                    var sub = g.Where(r => ids.Contains(r.S.CardId)).ToList();
                    sb.Append($"{name}[{string.Join("/", sub.Select(r => r.Belief.ToString().Substring(0, 4)))}] ");
                }
                int win = g.Count(r => r.Success);
                sb.AppendLine($"  성공 {win}/{g.Count()}  서로 다른 Belief 등급 {g.Select(r => r.Belief).Distinct().Count()}종");
            }

            sb.AppendLine();
            sb.AppendLine("## 맥락이 결과를 바꿨는가 (블록 B vs 블록 A의 같은 카드)");
            foreach (var g in rows.Where(r => r.S.Block == "B.맥락").GroupBy(r => r.S.NpcId))
            {
                var base_ = rows.FirstOrDefault(r => r.S.Block == "A.카드등급" && r.S.NpcId == g.Key && r.S.CardId == ContextCard);
                sb.Append($"- **{Short(g.Key)}** 기준 {(base_ != null ? base_.Belief.ToString() : "?")} → ");
                sb.AppendLine(string.Join(", ", g.Select(r =>
                    $"{r.S.Context}={r.Belief}{(base_ != null && r.Belief != base_.Belief ? " **변화**" : "")}")));
            }

            sb.AppendLine();
            sb.AppendLine("## 반복 안정성 (블록 C vs 블록 A 동일 입력)");
            foreach (var r in rows.Where(x => x.S.Block == "C.반복"))
            {
                var first = rows.FirstOrDefault(x => x.S.Block == "A.카드등급" && x.S.NpcId == r.S.NpcId && x.S.CardId == r.S.CardId);
                sb.AppendLine($"- {Short(r.S.NpcId)} / {r.S.CardId} : 1회차 {(first != null ? first.Belief.ToString() : "?")} vs 2회차 {r.Belief}"
                    + $" → {(first != null && first.Belief == r.Belief ? "동일" : "**불일치**")}");
            }

            if (rows.Any(r => r.S.Block == "D.장소"))
            {
                sb.AppendLine();
                sb.AppendLine("## 장소 영향 (블록 D - 같은 카드, 대비 최대 두 장소, 2회 반복)");
                foreach (var g in rows.Where(r => r.S.Block == "D.장소").GroupBy(r => r.S.NpcId))
                {
                    var good = g.Where(r => r.S.Context.StartsWith("유리")).ToList();
                    var bad = g.Where(r => r.S.Context.StartsWith("불리")).ToList();
                    bool llmDiff = good.Select(r => r.Belief).Distinct().Except(bad.Select(r => r.Belief).Distinct()).Any()
                                || bad.Select(r => r.Belief).Distinct().Except(good.Select(r => r.Belief).Distinct()).Any();
                    bool ruleDiff = good.Select(r => r.RuleBelief).Distinct().Except(bad.Select(r => r.RuleBelief).Distinct()).Any()
                                 || bad.Select(r => r.RuleBelief).Distinct().Except(good.Select(r => r.RuleBelief).Distinct()).Any();
                    sb.AppendLine($"- **{Short(g.Key)}**");
                    sb.AppendLine($"    유리 {string.Join("/", good.Select(r => r.Belief))} (rule {string.Join("/", good.Select(r => r.RuleBelief))})"
                        + $"  성공 {good.Count(r => r.Success)}/{good.Count}");
                    sb.AppendLine($"    불리 {string.Join("/", bad.Select(r => r.Belief))} (rule {string.Join("/", bad.Select(r => r.RuleBelief))})"
                        + $"  성공 {bad.Count(r => r.Success)}/{bad.Count}");
                    sb.AppendLine($"    → LLM 장소 영향 {(llmDiff ? "**있음**" : "없음")} / RuleOnly 장소 영향 {(ruleDiff ? "있음" : "없음")}");
                }
            }

            if (rows.Any(r => r.S.Block.StartsWith("카드:") || r.S.Block == "수석하녀"))
            {
                sb.AppendLine();
                sb.AppendLine("## 보류값 비교 (원래 vs 현재)");
                foreach (var g in rows.Where(r => r.S.Block.StartsWith("카드:")).GroupBy(r => r.S.Block))
                {
                    sb.AppendLine($"### {g.Key}");
                    foreach (var byNpc in g.GroupBy(r => r.S.NpcId))
                    {
                        var orig = byNpc.Where(r => r.S.Context.Contains("원래")).ToList();
                        var cur = byNpc.Where(r => r.S.Context.Contains("현재")).ToList();
                        string role = byNpc.First().S.Context.StartsWith("부합") ? "부합" : "충돌";
                        bool diff = orig.Select(r => r.Belief).Distinct().Except(cur.Select(r => r.Belief).Distinct()).Any()
                                 || cur.Select(r => r.Belief).Distinct().Except(orig.Select(r => r.Belief).Distinct()).Any();
                        sb.AppendLine($"- **{Short(byNpc.Key)}** ({role})");
                        sb.AppendLine($"    원래 {string.Join("/", orig.Select(r => r.Belief))}  성공 {orig.Count(r => r.Success)}/{orig.Count}"
                            + $"  행동 {string.Join("/", orig.Select(r => r.ActionId))}");
                        sb.AppendLine($"    현재 {string.Join("/", cur.Select(r => r.Belief))}  성공 {cur.Count(r => r.Success)}/{cur.Count}"
                            + $"  행동 {string.Join("/", cur.Select(r => r.ActionId))}");
                        sb.AppendLine($"    → 값 변경이 결과를 {(diff ? "**바꿈**" : "바꾸지 않음")}");
                        sb.AppendLine($"    근거: " + string.Join(" | ", byNpc.Select(r =>
                            $"{r.PrimaryReason}/{(string.IsNullOrEmpty(r.ProfileInfluence) ? "-" : r.ProfileInfluence)}")));
                    }
                }

                var hm = rows.Where(r => r.S.Block == "수석하녀").ToList();
                if (hm.Count > 0)
                {
                    sb.AppendLine("### 수석하녀 trustBias 0.80 vs 0.62");
                    foreach (var byCard in hm.GroupBy(r => r.S.CardId))
                    {
                        var o = byCard.Where(r => r.S.Context.Contains("원래")).ToList();
                        var c = byCard.Where(r => r.S.Context.Contains("현재")).ToList();
                        string label = byCard.First().S.Context.Split('/')[0];
                        sb.AppendLine($"- **{byCard.Key}** ({label})");
                        sb.AppendLine($"    0.80 → {string.Join("/", o.Select(r => r.Belief))}  행동 {string.Join("/", o.Select(r => r.ActionId))}"
                            + $"  목적지 {string.Join("/", o.Select(r => r.Destination))}");
                        sb.AppendLine($"    0.62 → {string.Join("/", c.Select(r => r.Belief))}  행동 {string.Join("/", c.Select(r => r.ActionId))}"
                            + $"  목적지 {string.Join("/", c.Select(r => r.Destination))}");
                    }
                    sb.AppendLine($"- 성공(S03_M02): 0.80 {hm.Count(r => r.S.Context.Contains("원래") && r.Success)}/{hm.Count(r => r.S.Context.Contains("원래"))}"
                        + $" vs 0.62 {hm.Count(r => r.S.Context.Contains("현재") && r.Success)}/{hm.Count(r => r.S.Context.Contains("현재"))}");
                    sb.AppendLine($"- 관계 근거 사용: 0.80 {hm.Count(r => r.S.Context.Contains("원래") && !string.IsNullOrEmpty(r.RelationshipInfluence) && r.RelationshipInfluence != "none")}"
                        + $" vs 0.62 {hm.Count(r => r.S.Context.Contains("현재") && !string.IsNullOrEmpty(r.RelationshipInfluence) && r.RelationshipInfluence != "none")}");
                }
            }

            sb.AppendLine();
            sb.AppendLine("## 근거 필드 사용률");
            sb.AppendLine($"- primaryReason: " + string.Join(", ",
                rows.GroupBy(r => r.PrimaryReason ?? "-").OrderByDescending(x => x.Count()).Select(x => $"{x.Key} {x.Count()}")));
            sb.AppendLine($"- relationshipInfluence 사용(none 아님): {rows.Count(r => !string.IsNullOrEmpty(r.RelationshipInfluence) && r.RelationshipInfluence != "none")}/{rows.Count}");
            sb.AppendLine($"- profileInfluence 사용(none 아님): {rows.Count(r => !string.IsNullOrEmpty(r.ProfileInfluence) && r.ProfileInfluence != "none")}/{rows.Count}");
            sb.AppendLine($"- 해석에 카드 내용 언급: {rows.Count(r => r.MentionsCard)}/{rows.Count}");
            sb.AppendLine($"- 해석에 출처명 언급: {rows.Count(r => r.MentionsSource)}/{rows.Count}");

            sb.AppendLine();
            sb.AppendLine("## LLM vs RuleOnly 차이");
            sb.AppendLine($"- Belief 일치: {rows.Count(r => r.Belief == r.RuleBelief)}/{rows.Count}");
            sb.AppendLine($"- 성공 여부 일치: {rows.Count(r => r.Success == r.RuleSuccess)}/{rows.Count}");
            sb.AppendLine($"- LLM 성공률 {rows.Count(r => r.Success)}/{rows.Count}, RuleOnly 성공률 {rows.Count(r => r.RuleSuccess)}/{rows.Count}");

            Directory.CreateDirectory("Library/BeliefLogs");
            File.WriteAllText(LogPath, sb.ToString());

            var js = new StringBuilder();
            foreach (var r in rows)
                js.AppendLine(JsonUtility.ToJson(new Flat
                {
                    block = r.S.Block, mission = r.S.MissionId, npc = r.S.NpcId, card = r.S.CardId, context = r.S.Context,
                    belief = r.Belief.ToString(), success = r.Success, ruleBelief = r.RuleBelief.ToString(), ruleSuccess = r.RuleSuccess,
                    action = r.ActionId, destination = r.Destination, primaryReason = r.PrimaryReason,
                    profileInfluence = r.ProfileInfluence, relationshipInfluence = r.RelationshipInfluence,
                    interpretation = r.Interpretation, goal = r.Goal, fallback = r.Fallback, fallbackReason = r.FallbackReason
                }));
            File.WriteAllText(JsonPath, js.ToString());
        }

        [Serializable]
        class Flat
        {
            public string block, mission, npc, card, context, belief, ruleBelief, action, destination;
            public string primaryReason, profileInfluence, relationshipInfluence, interpretation, goal, fallbackReason;
            public bool success, ruleSuccess, fallback;
        }

        static string Short(string npcId) =>
            npcId.Replace("npc_major_", "").Replace("npc_minor_", "").Replace("npc_", "");
    }
}
