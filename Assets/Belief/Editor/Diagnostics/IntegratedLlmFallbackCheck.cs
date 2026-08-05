using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Belief.AI;
using Belief.AI.LLM;
using Belief.Core;
using Belief.Data;
using Belief.Domain;
using Belief.Events;
using Belief.Systems;
using UnityEditor;
using UnityEngine;

namespace Belief.EditorTools.Diagnostics
{
    /// <summary>
    /// IntegratedLlmThinker의 실패 10종이 각각 <b>RuleBased 전체 폴백을 정확히 1회</b> 만드는지
    /// 검증하는 에디터 전용 도구. <b>실제 API를 호출하지 않는다</b>(전부 제어된 가짜 Transport).
    /// 월드에도 아무것도 적용하지 않는다.
    /// </summary>
    public static class IntegratedLlmFallbackCheck
    {
        /// <summary>지정한 응답을 그대로 돌려주거나, 지정한 방식으로 실패하는 Transport.</summary>
        class ScriptedTransport : ILlmTransport, ICancellableLlmTransport
        {
            public string Response;
            public bool ThrowOnSend;
            public bool NeverComplete;          // Timeout 유발
            public bool ReturnEmpty;
            public int SendCount;
            public TaskCompletionSource<string> Pending;

            public Task<string> SendAsync(string prompt)
            {
                SendCount++;
                if (ThrowOnSend) throw new InvalidOperationException("의도된 전송 실패");
                if (NeverComplete) { Pending = new TaskCompletionSource<string>(); return Pending.Task; }
                if (ReturnEmpty) return Task.FromResult("");
                return Task.FromResult(Response);
            }

            public Task<string> SendAsync(string prompt, CancellationToken t) => SendAsync(prompt);
        }

        /// <summary>폴백이 몇 번 호출됐는지 세는 래퍼 - "정확히 1회"를 실제로 확인하기 위함.</summary>
        class CountingFallback : IIntegratedNpcThinker
        {
            readonly IIntegratedNpcThinker inner;
            public int Calls;
            public CountingFallback(IIntegratedNpcThinker inner) { this.inner = inner; }
            public Task<NpcJudgmentValidation> DecideAsync(NpcJudgmentContext c, object t)
            { Calls++; return inner.DecideAsync(c, t); }
        }

        class Rig
        {
            public NpcState Npc; public InformationCardData Card; public LocationState Loc;
            public Dictionary<LocationData, LocationState> Locations;
            public BeliefSystem BeliefSys; public MemorySelector Selector; public MemoryTuningData MemTuning;
            public NpcJudgmentContext Ctx;
            public JudgmentRequestIdentity Identity;
        }

        static Rig Build()
        {
            var installer = UnityEngine.Object.FindFirstObjectByType<GameInstaller>();
            if (installer == null)
            {
                UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                    "Assets/Belief/Scenes/Zone1.unity", UnityEditor.SceneManagement.OpenSceneMode.Single);
                installer = UnityEngine.Object.FindFirstObjectByType<GameInstaller>();
            }
            var so = new SerializedObject(installer);
            var beliefTuning = (BeliefTuningData)so.FindProperty("beliefTuning").objectReferenceValue;
            var memTuning = (MemoryTuningData)so.FindProperty("memoryTuning").objectReferenceValue;
            var mech = (LocationMechanicsSettings)so.FindProperty("locationMechanics").objectReferenceValue;
            var pool = (InformationCardPoolData)so.FindProperty("informationPool").objectReferenceValue;

            var data = AssetDatabase.FindAssets("t:MajorNpcData")
                .Select(g => AssetDatabase.LoadAssetAtPath<MajorNpcData>(AssetDatabase.GUIDToAssetPath(g)))
                .First(n => n != null && n.npcId == "npc_guard_captain");

            var locations = new Dictionary<LocationData, LocationState>();
            void Ensure(LocationData l) { if (l != null && !locations.ContainsKey(l)) locations[l] = new LocationState(l); }
            Ensure(data.homeLocation);
            foreach (var c in data.movementCandidates) Ensure(c);

            var npc = new NpcState(data);
            locations[npc.CurrentLocation].PresentNpcs.Add(npc);
            var loc = locations[npc.CurrentLocation];
            var card = pool.cards.First(c => c != null && c.cardId == "C-POL-03");

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
            var beliefSys = new BeliefSystem(evaluators, beliefTuning, new BeliefDebugRepository(), mech);
            var selector = new MemorySelector();
            var wm = selector.Select(npc, new MemorySelectionContext(card, loc, 2), memTuning);

            return new Rig
            {
                Npc = npc, Card = card, Loc = loc, Locations = locations,
                BeliefSys = beliefSys, Selector = selector, MemTuning = memTuning,
                Ctx = new NpcJudgmentContext(npc, card, loc, 2, npc.GetBelief(card), npc.CurrentGoal, wm,
                    data.availableActions, data.movementCandidates, new List<NpcState>(), null, locations),
                Identity = new JudgmentRequestIdentity("STAGE_01", "M01", 1, 2, data.npcId, card.cardId, "req-1"),
            };
        }

        static string Json(string action, string dest, string belief = "Plausible",
            string interpretation = "해석", string goal = "목표", string dialogue = "대사",
            string reason = "belief", string profile = "none", string relationship = "none") =>
            "{\"interpretation\":\"" + interpretation + "\",\"belief\":\"" + belief + "\",\"goal\":\"" + goal
            + "\",\"action\":\"" + action + "\",\"destinationId\":\"" + dest + "\",\"dialogue\":\"" + dialogue
            + "\",\"primaryReason\":\"" + reason + "\",\"profileInfluence\":\"" + profile
            + "\",\"relationshipInfluence\":\"" + relationship + "\"}";

        [MenuItem("BELIEF/Diagnostics/Verify IntegratedLlm Fallback", priority = 102)]
        public static async void Run()
        {
            if (!Application.isPlaying)
            {
                EditorUtility.DisplayDialog("Fallback Check",
                    "Play Mode에서 실행해야 합니다(Timeout이 코루틴 기반입니다).", "확인");
                return;
            }
            Application.runInBackground = true;
            Debug.LogWarning("=== IntegratedLlm 실패 10종 검증 ===\n" + await Execute());
        }

        public static async Task<string> Execute()
        {
            var sb = new StringBuilder();
            var rig = Build();
            string validAction = ((MajorNpcData)rig.Npc.Data).availableActions[0].actionId;

            // 기준선: 같은 컨텍스트에 대한 순수 RuleBasedUnified 결과
            var baseline = await new RuleBasedUnifiedThinker(rig.BeliefSys, new RuleBasedMajorThinker())
                .DecideAsync(rig.Ctx, null);
            var b = baseline.Judgment;
            sb.AppendLine($"기준선(RuleBasedUnified): {b.Belief} / {b.Action.actionId} / "
                        + $"{(b.Destination != null ? b.Destination.locationId : "stay")}");
            sb.AppendLine();

            int pass = 0, count = 0;
            async Task Case(string label, Action<ScriptedTransport> setup, string expectReason)
            {
                count++;
                var transport = new ScriptedTransport();
                setup(transport);
                var counting = new CountingFallback(new RuleBasedUnifiedThinker(rig.BeliefSys, new RuleBasedMajorThinker()));
                var thinker = new IntegratedLlmThinker(transport, counting, 400);

                var outcome = await thinker.DecideAsync(rig.Ctx, rig.Identity, null);

                bool isFallback = outcome.Source == JudgmentResultSource.RuleBasedFallback;
                bool oneCall = counting.Calls == 1;
                bool noRetry = transport.SendCount <= 1;
                bool reasonOk = outcome.FallbackReason == expectReason;
                // 혼합 0건: 폴백 결과가 기준선과 필드 단위로 완전히 같아야 한다.
                bool identical = outcome.HasJudgment
                    && outcome.Judgment.Belief == b.Belief
                    && outcome.Judgment.Action == b.Action
                    && outcome.Judgment.Destination == b.Destination
                    && outcome.Judgment.Goal == b.Goal
                    && outcome.Judgment.Dialogue == b.Dialogue
                    && outcome.Judgment.Interpretation == b.Interpretation;

                bool ok = isFallback && oneCall && noRetry && reasonOk && identical;
                if (ok) pass++;
                sb.AppendLine($"{(ok ? "PASS" : "*** FAIL ***")} {label,-28} "
                            + $"source={outcome.Source} fallback={counting.Calls}회 send={transport.SendCount}회 "
                            + $"reason={outcome.FallbackReason ?? "-"} 기준선일치={identical}");

                // 늦은 응답이 이미 반환된 결과를 바꾸지 않는지
                if (transport.Pending != null)
                {
                    var beforeBelief = outcome.Judgment.Belief;
                    transport.Pending.TrySetResult(Json(validAction, "stay", "Trusted"));
                    await Task.Yield();
                    bool unchanged = outcome.Judgment.Belief == beforeBelief;
                    sb.AppendLine($"     늦은 응답 도착 후 결과 변화 없음 = {(unchanged ? "PASS" : "*** FAIL ***")}");
                }
            }

            await Case("1. Transport 예외", t => t.ThrowOnSend = true, "TransportException");
            await Case("2. Timeout", t => t.NeverComplete = true, "Timeout");
            await Case("3. 빈 응답", t => t.ReturnEmpty = true, "EmptyResponse");
            await Case("4. 쓰레기 JSON", t => t.Response = "{ 이건 JSON이 아니다", "JsonParseFailure");
            await Case("5. 잘못된 Belief enum", t => t.Response = Json(validAction, "stay", belief: "매우믿음"), "InvalidBelief");
            await Case("6. 후보 밖 Action", t => t.Response = Json("act_does_not_exist", "stay"), "InvalidAction");
            await Case("7. 후보 밖 Destination", t => t.Response = Json(validAction, "LOC_NOWHERE"), "InvalidDestination");
            await Case("8. 허구 Profile 근거", t => t.Response = Json(validAction, "stay", reason: "profile", profile: "#없는태그"), "InvalidProfileInfluence");
            await Case("9. 무관한 Relationship 근거", t => t.Response = Json(validAction, "stay", reason: "relationship", relationship: "npc_major_steward"), "IrrelevantRelationship");
            await Case("10. Interpretation 빈 문자열", t => t.Response = Json(validAction, "stay", interpretation: ""), "EmptyInterpretation");

            // 성공 경로
            {
                count++;
                var transport = new ScriptedTransport { Response = Json(validAction, "stay", "Plausible") };
                var counting = new CountingFallback(new RuleBasedUnifiedThinker(rig.BeliefSys, new RuleBasedMajorThinker()));
                var thinker = new IntegratedLlmThinker(transport, counting, 400);
                var outcome = await thinker.DecideAsync(rig.Ctx, rig.Identity, null);

                bool ok = outcome.Source == JudgmentResultSource.IntegratedLlm
                          && counting.Calls == 0
                          && outcome.Judgment.Belief == BeliefState.Plausible
                          && outcome.Judgment.Identity.RequestId == "req-1";
                if (ok) pass++;
                sb.AppendLine();
                sb.AppendLine($"{(ok ? "PASS" : "*** FAIL ***")} 성공 경로                     "
                            + $"source={outcome.Source} fallback={counting.Calls}회 "
                            + $"belief={outcome.Judgment.Belief} dest={outcome.Judgment.Destination?.locationId ?? "stay"} "
                            + $"identity={outcome.Judgment.Identity.RequestId} key={outcome.Judgment.Identity.ApplicationKey}");
            }

            sb.AppendLine();
            sb.AppendLine($"합계 {pass}/{count} PASS");

            // PilotPolicy 판정
            sb.AppendLine();
            sb.AppendLine("### IntegratedLlmPilotPolicy");
            foreach (var stage in new[] { "STAGE_01", "STAGE_02", "STAGE_04", "", null })
            {
                bool allowed = IntegratedLlmPilotPolicy.IsAllowed(stage, out string why);
                sb.AppendLine($"  {(stage ?? "null"),-10} → {(allowed ? "허용" : "거부")}{(allowed ? "" : "  (" + why + ")")}");
            }
            sb.AppendLine($"  IsPilotBuild = {IntegratedLlmPilotPolicy.IsPilotBuild}");
            return sb.ToString();
        }
    }
}
