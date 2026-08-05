using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Belief.AI.LLM;
using Belief.Core;
using Belief.Data;
using Belief.Debugging;
using Belief.Domain;
using Belief.Events;
using Belief.Systems;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Belief.EditorTools.Diagnostics
{
    /// <summary>
    /// Shadow 판단 표본을 반복 측정하기 위한 <b>에디터 전용</b> 진단 도구.
    /// Editor 폴더에 있으므로 플레이어 빌드에는 포함되지 않는다.
    ///
    /// 이 도구는 <b>실제 OpenAI API를 호출해 요금이 발생한다.</b> 그래서 실행 전 반드시 확인
    /// 대화상자로 최대 호출 수와 예상 비용을 보여주고, 취소하면 호출을 하나도 하지 않는다.
    ///
    /// 안전 설계:
    /// <list type="bullet">
    /// <item><b>씬을 전혀 건드리지 않는다.</b> 자체 ShadowJudgmentSystem과 도메인 상태를 새로 만들어
    ///   측정하므로 shadowMode를 켤 필요가 없고, 씬이 Dirty가 될 일도 없다.</item>
    /// <item>실제 게임의 NpcState/LocationState/미션에 접근하지 않는다 - 런타임 상태가 오염되지 않는다.</item>
    /// <item>LLM 결과는 비교 기록으로만 나가고 월드에 적용되지 않는다(ShadowJudgmentSystem 자체가
    ///   월드 변경 API를 참조하지 않는다).</item>
    /// <item>한 번 실행에 <see cref="MaxCallsPerRun"/>회를 넘지 않으며, 자동 반복은 없다.</item>
    /// <item>완료·실패·중단 어느 경우든 finally에서 Shadow를 끄고 씬 설정이 false인지 확인한다.</item>
    /// </list>
    /// </summary>
    public static class ShadowRunHarness
    {
        /// <summary>한 번 실행에서 발사할 수 있는 최대 API 호출 수. 자동 반복은 없다.</summary>
        public const int MaxCallsPerRun = 24;

        /// <summary>비용 안내용 실측 평균(2026-08-06 표본 14건 기준). gpt-4o-mini 단가로 환산한다.</summary>
        const int AvgInputTokens = 1363;
        const int AvgOutputTokens = 149;
        const double InputPricePerMillion = 0.15;
        const double OutputPricePerMillion = 0.60;

        const int TimeoutMs = 15000;

        static readonly (string tier, string cardId)[] SamplePlan =
        {
            ("High",   "C-ADM-01"),   // cred 0.70 / 행정기관 0.75
            ("High",   "C-SEC-01"),   // cred 0.65 / 순찰대 0.65
            ("Medium", "C-POL-03"),   // cred 0.55 / 순찰대 0.65
            ("Medium", "C-CRI-01"),   // cred 0.45 / 주점 0.30
            ("Low",    "C-POL-01"),   // cred 0.40 / 귀족가 0.45
            ("Low",    "C-SEC-02"),   // cred 0.30 / 익명 제보 0.25
        };

        static bool running;
        static readonly List<ShadowComparisonRecord> Collected = new List<ShadowComparisonRecord>();
        static Action<ShadowComparisonRecord> handler;

        static double EstimatedCost =>
            MaxCallsPerRun * (AvgInputTokens / 1_000_000.0 * InputPricePerMillion
                            + AvgOutputTokens / 1_000_000.0 * OutputPricePerMillion);

        [MenuItem("BELIEF/Diagnostics/Run Shadow Sample", priority = 100)]
        public static void RunMenu()
        {
            if (running)
            {
                EditorUtility.DisplayDialog("Shadow Sample", "이미 실행 중입니다. 끝날 때까지 기다리세요.", "확인");
                return;
            }

            if (!Application.isPlaying)
            {
                EditorUtility.DisplayDialog("Shadow Sample",
                    "Play Mode에서 실행해야 합니다.\n\nTimeout이 코루틴 기반이라 에디터 정지 상태에서는 응답을 기다릴 수 없습니다.",
                    "확인");
                return;
            }

            bool go = EditorUtility.DisplayDialog(
                "Shadow Sample - 실제 API를 호출합니다",
                "이 진단은 실제 OpenAI API를 호출하며 요금이 발생합니다.\n\n"
                + $"• 최대 호출 수 : {MaxCallsPerRun}회 (이 값을 넘지 않습니다)\n"
                + $"• 예상 비용    : 약 ${EstimatedCost:F4} (gpt-4o-mini, 실측 평균 기준)\n"
                + $"• 예상 소요    : 약 {MaxCallsPerRun * 3 / 4}초 (동시 4건)\n\n"
                + "게임 판단과 월드 상태에는 영향을 주지 않습니다.\n"
                + "결과는 비교 로그로만 기록됩니다.\n\n"
                + "실행하시겠습니까?",
                "실행", "취소");

            if (!go)
            {
                Debug.Log("[ShadowSample] 취소했습니다 - API 호출 0회.");
                return;
            }

            _ = RunAsync();
        }

        /// <summary>
        /// <b>자동 계측 전용</b> 진입점 - 확인 대화상자를 띄우지 않고 바로 실행한다.
        /// 대화상자를 클릭할 수 없는 자동화 환경(MCP 등)에서 표본을 수집하기 위한 것이며,
        /// 이름을 길게 둔 이유는 실수로 이 경로를 쓰는 일을 막기 위해서다.
        ///
        /// <b>사람이 메뉴로 실행하는 경로에는 확인 대화상자가 그대로 남아 있다.</b>
        /// 이 메서드는 호출자가 비용을 이미 인지했다고 선언하는 용도이고, 시작 시 최대 호출 수와
        /// 예상 최대 비용을 경고 로그로 남긴다. 호출 수 상한·재진입 방지·복원 처리는 메뉴 경로와
        /// 완전히 동일하게 적용된다.
        ///
        /// 이 클래스는 Editor 폴더에 있어 Assembly-CSharp-Editor에만 들어가므로, 이 public 진입점이
        /// 플레이어 빌드에 포함되는 일은 없다.
        /// </summary>
        public static void RunShadowSampleWithoutConfirmationForAutomation()
        {
            if (running)
            {
                Debug.LogWarning("[ShadowSample] 이미 실행 중입니다 - 중복 실행을 막았습니다.");
                return;
            }
            if (!Application.isPlaying)
            {
                Debug.LogError("[ShadowSample] Play Mode에서만 실행할 수 있습니다.");
                return;
            }

            Debug.LogWarning(
                "[ShadowSample] 자동화 실행입니다 - 확인 대화상자를 건너뛰고 실제 OpenAI API를 호출합니다.\n"
                + $"  최대 호출 수 : {MaxCallsPerRun}회\n"
                + $"  예상 최대 비용 : 약 ${EstimatedCost:F4} (gpt-4o-mini)\n"
                + "  결과는 비교 로그로만 기록되며 씬·월드·미션 상태는 수정하지 않습니다.");

            _ = RunAsync();
        }

        static async Task RunAsync()
        {
            running = true;
            ShadowJudgmentSystem shadow = null;
            try
            {
                Application.runInBackground = true;
                Collected.Clear();
                handler = r => Collected.Add(r);
                ShadowComparisonHub.RecordPublished += handler;

                var rig = BuildRig(out shadow);
                if (rig == null) return;

                int fired = 0;
                foreach (var npc in rig.Npcs)
                {
                    foreach (var (_, cardId) in SamplePlan)
                    {
                        if (fired >= MaxCallsPerRun) break;
                        if (!rig.Cards.TryGetValue(cardId, out var card)) continue;

                        rig.Observe(npc, card, 1);
                        fired++;
                        await Task.Yield();
                    }
                    if (fired >= MaxCallsPerRun) break;
                }

                Debug.Log($"[ShadowSample] {fired}건 발사 (상한 {MaxCallsPerRun}) - 응답 대기 중…");
                await DrainAsync(fired);
                Report(fired);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ShadowSample] 진단 중 예외: {ex.Message}");
            }
            finally
            {
                // 완료·실패·중단 무관하게 항상 복원한다.
                shadow?.Disable();
                if (handler != null) { ShadowComparisonHub.RecordPublished -= handler; handler = null; }
                EnsureShadowDisabledInScenes();
                running = false;
            }
        }

        // ── 자체 세계 (실제 런타임 상태를 건드리지 않는다) ────────────────────────
        class Rig
        {
            public List<NpcState> Npcs;
            public Dictionary<string, InformationCardData> Cards;
            public Dictionary<LocationData, LocationState> Locations;
            // 이름을 Belief로 두면 루트 네임스페이스 Belief와 충돌해 Belief.Debugging 같은 참조가 깨진다.
            public BeliefSystem BeliefSys;
            public MemorySelector Selector;
            public MemoryTuningData MemTuning;
            public ShadowJudgmentSystem Shadow;

            /// <summary>실제 판단(규칙 기반)을 이 하네스 안에서 계산하고, 같은 스냅샷으로 Shadow를 관찰한다.
            /// ActionResolutionSystem을 호출하지 않으므로 어떤 Effect도 실행되지 않는다.</summary>
            public void Observe(NpcState npc, InformationCardData card, int turn)
            {
                var loc = Locations[npc.CurrentLocation];
                var wm = Selector.Select(npc, new MemorySelectionContext(card, loc, turn), MemTuning);

                var beliefBefore = npc.GetBelief(card);
                var result = BeliefSys.Evaluate(npc, card, loc, wm, turn);   // Apply 하지 않는다

                var major = (MajorNpcData)npc.Data;
                var present = loc.PresentNpcs.Where(n => n != npc).ToList();
                var ctx = new global::Belief.AI.NpcJudgmentContext(
                    npc, card, loc, turn, beliefBefore, npc.CurrentGoal, wm,
                    major.availableActions, major.movementCandidates, present, null, Locations);

                var ruleAction = major.availableActions?.FirstOrDefault(
                    a => a != null && a.intent == global::Belief.Debugging.NpcDecisionTraceBuilder.IntentMapping(result.FinalBelief))
                    ?? major.availableActions?.FirstOrDefault();

                Shadow.Observe(ctx, result.FinalBelief, ruleAction, npc.CurrentGoal, null);
            }
        }

        static Rig BuildRig(out ShadowJudgmentSystem shadow)
        {
            shadow = null;
            var installer = UnityEngine.Object.FindFirstObjectByType<GameInstaller>();
            if (installer == null)
            {
                Debug.LogError("[ShadowSample] GameInstaller를 찾을 수 없습니다 - Zone 씬에서 실행하세요.");
                return null;
            }

            var so = new SerializedObject(installer);
            var beliefTuning = (BeliefTuningData)so.FindProperty("beliefTuning").objectReferenceValue;
            var memTuning = (MemoryTuningData)so.FindProperty("memoryTuning").objectReferenceValue;
            var mech = (LocationMechanicsSettings)so.FindProperty("locationMechanics").objectReferenceValue;
            var pool = (InformationCardPoolData)so.FindProperty("informationPool").objectReferenceValue;
            var config = (LlmProviderConfig)AssetDatabase.LoadAssetAtPath<LlmProviderConfig>(
                "Assets/Belief/Data/AI/LlmProviderConfig_Proxy.asset");

            var transport = ThinkerFactory.CreateShadowTransport(config);
            if (transport == null)
            {
                Debug.LogError("[ShadowSample] Shadow Transport를 만들 수 없습니다 - LlmProviderConfig_Proxy를 확인하세요.");
                return null;
            }

            var locations = new Dictionary<LocationData, LocationState>();
            var la = so.FindProperty("allLocations");
            for (int i = 0; i < la.arraySize; i++)
            {
                var l = la.GetArrayElementAtIndex(i).objectReferenceValue as LocationData;
                if (l != null && !locations.ContainsKey(l)) locations[l] = new LocationState(l);
            }

            var npcs = new List<NpcState>();
            var na = so.FindProperty("allNpcs");
            for (int i = 0; i < na.arraySize; i++)
            {
                var n = na.GetArrayElementAtIndex(i).objectReferenceValue as MajorNpcData;
                if (n == null || n.homeLocation == null) continue;
                if (!locations.ContainsKey(n.homeLocation)) locations[n.homeLocation] = new LocationState(n.homeLocation);
                var st = new NpcState(n);
                locations[st.CurrentLocation].PresentNpcs.Add(st);
                npcs.Add(st);
            }
            npcs = npcs.OrderBy(n => n.Data.npcId, StringComparer.Ordinal).ToList();

            var bus = new GameEventBus();
            var evaluators = new IBeliefEvaluator[]
            {
                new Belief.Systems.BeliefEvaluators.PersonalityEvaluator(),
                new Belief.Systems.BeliefEvaluators.ExistingBeliefEvaluator(),
                new Belief.Systems.BeliefEvaluators.CredibilityEvaluator(),
                new Belief.Systems.BeliefEvaluators.SourceEvaluator(),
                new Belief.Systems.BeliefEvaluators.GoalEvaluator(),
                new Belief.Systems.BeliefEvaluators.SituationEvaluator(),
                new Belief.Systems.BeliefEvaluators.MemoryEvaluator(memTuning),
            };

            shadow = new ShadowJudgmentSystem(transport, TimeoutMs, logPrompts: false, bus,
                () => "DIAGNOSTIC", () => "SAMPLE");
            bus.Publish(new TurnStartedEvent(1, 4));

            return new Rig
            {
                Npcs = npcs,
                Cards = pool.cards.Where(c => c != null && c.information != null).ToDictionary(c => c.cardId, c => c),
                Locations = locations,
                BeliefSys = new BeliefSystem(evaluators, beliefTuning, new BeliefDebugRepository(), mech),
                Selector = new MemorySelector(),
                MemTuning = memTuning,
                Shadow = shadow,
            };
        }

        static async Task DrainAsync(int expected)
        {
            for (int i = 0; i < 240 && Collected.Count < expected; i++) await Task.Delay(250);
            for (int i = 0; i < 16; i++) await Task.Delay(250);   // 늦은 도착분 여유
        }

        static void Report(int fired)
        {
            var ok = Collected.Where(r => r.Outcome == ShadowOutcome.Compared).ToList();
            var sb = new StringBuilder();
            sb.AppendLine($"발사 {fired}회 (상한 {MaxCallsPerRun}) / 기록 {Collected.Count}건");
            sb.AppendLine($"성공 {ok.Count} / 실패 {Collected.Count(r => r.Outcome == ShadowOutcome.Failed)} "
                        + $"/ Stale {Collected.Count(r => r.Outcome == ShadowOutcome.StaleSession)} "
                        + $"/ Skipped {Collected.Count(r => r.Outcome == ShadowOutcome.SkippedConcurrencyLimit)}");

            if (ok.Count > 0)
            {
                int inSum = ok.Where(r => r.TokensAvailable).Sum(r => r.InputTokens);
                int outSum = ok.Where(r => r.TokensAvailable).Sum(r => r.OutputTokens);
                double cost = inSum / 1_000_000.0 * InputPricePerMillion + outSum / 1_000_000.0 * OutputPricePerMillion;
                sb.AppendLine($"응답 시간  평균 {ok.Average(r => r.LatencyMs):F0}ms / 최소 {ok.Min(r => r.LatencyMs):F0} / 최대 {ok.Max(r => r.LatencyMs):F0}");
                sb.AppendLine($"토큰       입력 {inSum} / 출력 {outSum}   실제 비용 ${cost:F5}");
                sb.AppendLine($"Belief 일치 {ok.Count(r => !r.BeliefDiffers)}/{ok.Count}"
                            + $"  단계차 동일 {ok.Count(r => r.BeliefStepDelta == 0)} / ±1 {ok.Count(r => Math.Abs(r.BeliefStepDelta) == 1)} / ±2이상 {ok.Count(r => Math.Abs(r.BeliefStepDelta) >= 2)}");
                sb.AppendLine($"Destination  stay {ok.Count(r => r.LlmDestinationId == "stay")} / 이동 {ok.Count(r => r.LlmDestinationId != "stay")}");
                sb.AppendLine("정규화 사유  " + string.Join(", ",
                    Collected.GroupBy(r => r.DestinationNormalizationReason)
                             .OrderByDescending(g => g.Count()).Select(g => $"{g.Key} {g.Count()}")));
                sb.AppendLine("NPC별 이동 후보 수  " + string.Join(", ",
                    ok.Select(r => r.NpcId).Distinct().Select(id => id + ":" + CandidateCount(id))));
                sb.AppendLine("Action별 destination  " + string.Join(" | ",
                    ok.GroupBy(r => r.LlmActionId).Select(g =>
                        $"{g.Key} → " + string.Join(",", g.GroupBy(x => x.NormalizedLlmDestinationId ?? "stay")
                            .Select(x => x.Key + ":" + x.Count())))));
                sb.AppendLine("primaryReason  " + string.Join(", ",
                    ok.GroupBy(r => r.LlmPrimaryReason).OrderByDescending(g => g.Count()).Select(g => $"{g.Key} {g.Count()}")));
                sb.AppendLine($"relationshipInfluence 사용 {ok.Count(r => r.LlmRelationshipInfluence != "none")}/{ok.Count}");
            }

            string path = Path.Combine(Application.dataPath, "..", "Logs", "shadow_sample.tsv");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, BuildTsv());
                sb.AppendLine($"원본: {Path.GetFullPath(path)}");
            }
            catch (Exception ex) { sb.AppendLine($"원본 저장 실패: {ex.Message}"); }

            Debug.Log("[ShadowSample]\n" + sb);
        }

        /// <summary>이 NPC의 이동 후보 개수 - stay 편향이 "후보가 없어서"인지 가리기 위한 값.</summary>
        static int CandidateCount(string npcId)
        {
            var n = AssetDatabase.FindAssets("t:MajorNpcData")
                .Select(g => AssetDatabase.LoadAssetAtPath<MajorNpcData>(AssetDatabase.GUIDToAssetPath(g)))
                .FirstOrDefault(x => x != null && x.npcId == npcId);
            return n?.movementCandidates?.Length ?? 0;
        }

        static string BuildTsv()
        {
            var sb = new StringBuilder();
            sb.AppendLine("npc\tcandidates\tcard\tcred\tsrc\tsrcTrust\toutcome\tfail\tms\tinTok\toutTok\t"
                        + "ruleBelief\tllmBelief\tstepDelta\truleAction\tllmAction\t"
                        + "rawDest\tnormDest\tdestReason\t"
                        + "reason\tprofile\trelationship\tinterpretation\tgoal\tdialogue");
            foreach (var r in Collected)
                sb.AppendLine(string.Join("\t", new[]
                {
                    r.NpcId, CandidateCount(r.NpcId).ToString(),
                    r.CardId, r.CardCredibility.ToString("F2"), r.SourceId, r.SourceTrust.ToString("F2"),
                    r.Outcome.ToString(), r.FailureReason ?? "", r.LatencyMs.ToString("F0"),
                    r.InputTokens.ToString(), r.OutputTokens.ToString(),
                    r.RuleBelief, r.LlmBelief ?? "", r.BeliefStepDelta.ToString(),
                    r.RuleActionId ?? "", r.LlmActionId ?? "",
                    r.RawLlmDestinationId ?? "", r.NormalizedLlmDestinationId ?? "", r.DestinationNormalizationReason.ToString(),
                    r.LlmPrimaryReason ?? "", r.LlmProfileInfluence ?? "", r.LlmRelationshipInfluence ?? "",
                    Flat(r.LlmInterpretation), Flat(r.LlmGoal), Flat(r.LlmDialogue)
                }));
            return sb.ToString();
        }

        static string Flat(string s) => s == null ? "" : s.Replace("\t", " ").Replace("\n", " ").Replace("\r", "");

        /// <summary>이 도구는 씬을 켜지 않지만, 다른 경로로 켜져 있었다면 여기서 확실히 끈다.
        /// 씬을 수정하지 않았다면 Dirty로 만들지도 않는다.</summary>
        static void EnsureShadowDisabledInScenes()
        {
            if (Application.isPlaying) return;   // 플레이 중에는 씬 저장을 시도하지 않는다

            foreach (var entry in EditorBuildSettings.scenes.Where(s => s.enabled))
            {
                var scene = EditorSceneManager.OpenScene(entry.path, OpenSceneMode.Single);
                var installer = UnityEngine.Object.FindFirstObjectByType<GameInstaller>();
                if (installer == null) continue;

                var so = new SerializedObject(installer);
                var p = so.FindProperty("shadowMode");
                if (p == null || !p.boolValue) continue;    // 이미 꺼져 있으면 손대지 않는다(Dirty 방지)

                p.boolValue = false;
                var log = so.FindProperty("shadowPromptLogging");
                if (log != null) log.boolValue = false;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Debug.LogWarning($"[ShadowSample] {entry.path}의 shadowMode를 껐습니다.");
            }
        }
    }
}
