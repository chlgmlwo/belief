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
using Belief.Debugging;
using Belief.Domain;
using Belief.Events;
using Belief.Systems;
using UnityEditor;
using UnityEngine;

namespace Belief.EditorTools.Diagnostics
{
    /// <summary>
    /// 3단계(실제 적용 구조) 결정적 테스트. <b>실제 API를 호출하지 않는다</b> - 응답을 직접
    /// 지정하는 가짜 Transport만 쓴다. 씬·데이터도 수정하지 않고 자체 도메인 상태로 검증한다.
    /// </summary>
    public static class JudgmentApplicationCheck
    {
        class ScriptedTransport : ILlmTransport, ICancellableLlmTransport
        {
            public string Response;
            public int SendCount;
            public Task<string> SendAsync(string prompt) { SendCount++; return Task.FromResult(Response); }
            public Task<string> SendAsync(string prompt, CancellationToken t) => SendAsync(prompt);
        }

        class Rig
        {
            public NpcState Npc; public MajorNpcData Data;
            public InformationCardData Card; public LocationState Loc;
            public Dictionary<LocationData, LocationState> Locations;
            public Dictionary<NpcData, NpcState> NpcMap;
            public BeliefSystem BeliefSys; public MemorySelector Selector; public MemoryTuningData MemTuning;
            public ActionResolutionSystem Resolution;
            public DestinationReservation Reservations;
            public JudgmentApplicationSystem Application;
            public GameEventBus Bus;
            public NpcJudgmentContext Ctx;
            public int SpokeCount, JudgedCount;
        }

        static Rig Build(int turn = 2)
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
            var repeatedLies = (MemoryCategoryData)so.FindProperty("repeatedLiesCategory").objectReferenceValue;
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

            var bus = new GameEventBus();
            var npcMap = new Dictionary<NpcData, NpcState> { { data, npc } };
            new MemorySystem(bus, npcMap, repeatedLies);

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
            var resolution = new ActionResolutionSystem(locations, bus);
            var reservations = new DestinationReservation(locations, bus);
            var application = new JudgmentApplicationSystem(resolution, reservations, bus,
                () => "STAGE_01", () => "M01");

            bus.Publish(new TurnStartedEvent(1, 4));   // attemptId = 1

            var rig = new Rig
            {
                Npc = npc, Data = data, Card = card, Loc = loc, Locations = locations, NpcMap = npcMap,
                BeliefSys = beliefSys, Selector = new MemorySelector(), MemTuning = memTuning,
                Resolution = resolution, Reservations = reservations, Application = application, Bus = bus,
            };
            bus.Subscribe<NpcSpokeEvent>(_ => rig.SpokeCount++);
            bus.Subscribe<CardJudgedEvent>(_ => rig.JudgedCount++);

            var wm = rig.Selector.Select(npc, new MemorySelectionContext(card, loc, turn), memTuning);
            rig.Ctx = new NpcJudgmentContext(npc, card, loc, turn, npc.GetBelief(card), npc.CurrentGoal, wm,
                data.availableActions, data.movementCandidates, new List<NpcState>(), null, locations);
            return rig;
        }

        static string Json(string action, string dest, string belief = "Plausible") =>
            "{\"interpretation\":\"해석\",\"belief\":\"" + belief + "\",\"goal\":\"새 목표\",\"action\":\"" + action
            + "\",\"destinationId\":\"" + dest + "\",\"dialogue\":\"대사\",\"primaryReason\":\"belief\","
            + "\"profileInfluence\":\"none\",\"relationshipInfluence\":\"none\"}";

        static IntegratedLlmThinker Thinker(Rig rig, ScriptedTransport t) =>
            new IntegratedLlmThinker(t, new RuleBasedUnifiedThinker(rig.BeliefSys, new RuleBasedMajorThinker()), 2000);

        [MenuItem("BELIEF/Diagnostics/Verify Judgment Application", priority = 103)]
        public static async void Run()
        {
            if (!Application.isPlaying)
            { EditorUtility.DisplayDialog("Application Check", "Play Mode에서 실행해야 합니다.", "확인"); return; }
            UnityEngine.Application.runInBackground = true;
            Debug.LogWarning("=== 3단계 적용 구조 검증 ===\n" + await Execute());
        }

        public static async Task<string> Execute()
        {
            var sb = new StringBuilder();
            int pass = 0, total = 0;
            void Check(string label, bool ok, string detail = null)
            { total++; if (ok) pass++; sb.AppendLine($"  {(ok ? "PASS" : "*** FAIL ***")} {label}{(detail != null ? "  → " + detail : "")}"); }

            // ── A. 정상 통합 적용 ────────────────────────────────────────────────
            sb.AppendLine("### A. 정상 통합 적용");
            {
                var rig = Build();
                string verifyAction = rig.Data.availableActions.First(a => a.intent == NpcActionIntent.Verify).actionId;
                var t = new ScriptedTransport { Response = Json(verifyAction, "stay", "Plausible") };
                var id = rig.Application.CreateIdentity(rig.Npc, rig.Card, 2, "req-A");
                var outcome = await Thinker(rig, t).DecideAsync(rig.Ctx, id, null);
                var r = rig.Application.Apply(outcome, rig.Ctx, 2, null);

                Check("Belief 적용 1회", r.BeliefApplied && rig.Npc.GetBelief(rig.Card) == BeliefState.Plausible, rig.Npc.GetBelief(rig.Card).ToString());
                Check("Goal 적용 1회", r.GoalApplied && rig.Npc.CurrentGoal == "새 목표", rig.Npc.CurrentGoal);
                Check("Action Effect 적용 1회", r.ActionApplied && rig.Npc.CurrentAction != null, rig.Npc.CurrentAction?.actionId);
                Check("Verify 기억 생성 1회", rig.Npc.LongMemory.Count == 1, rig.Npc.LongMemory.Count + "건");
                Check("Destination 예약 1회 (Stay)", r.DestinationReserved && r.ReservationType == DestinationReservationType.Stay);
                Check("Dialogue 발행 1회", rig.SpokeCount == 1, rig.SpokeCount + "회");
                Check("CardJudgedEvent 발행 1회", rig.JudgedCount == 1, rig.JudgedCount + "회");
                Check("applicationKey 기록", r.Applied && !string.IsNullOrEmpty(r.ApplicationKey), r.ApplicationKey);
                Check("source=IntegratedLlm", r.ResultSource == JudgmentResultSource.IntegratedLlm);
            }

            // ── B. Stay 예약 → 기존 이동 판단 미실행 ─────────────────────────────
            sb.AppendLine();
            sb.AppendLine("### B. Stay 예약");
            {
                var rig = Build();
                var t = new ScriptedTransport { Response = Json(rig.Data.availableActions[0].actionId, "stay") };
                var id = rig.Application.CreateIdentity(rig.Npc, rig.Card, 2, "req-B");
                rig.Application.Apply(await Thinker(rig, t).DecideAsync(rig.Ctx, id, null), rig.Ctx, 2, null);

                var before = rig.Npc.CurrentLocation;
                var counting = new CountingThinker();
                var movement = new NpcMovementSystem(rig.Resolution, counting, new RuleBasedMajorThinker(), rig.Reservations);
                rig.Npc.MarkBeliefChanged();   // 예약이 없었다면 반드시 thinker를 탔을 조건
                await movement.MoveNpcsAsync(new[] { rig.Npc }, 2);

                Check("기존 DecideMoveAsync 미호출", counting.MoveCalls == 0, counting.MoveCalls + "회");
                Check("NPC 위치 유지", rig.Npc.CurrentLocation == before, rig.Npc.CurrentLocation.locationId);
                Check("예약 소비됨(잔여 0)", rig.Reservations.Count == 0, rig.Reservations.Count + "건");
            }

            // ── C. Move 예약 ─────────────────────────────────────────────────────
            sb.AppendLine();
            sb.AppendLine("### C. Move 예약");
            {
                var rig = Build();
                var target = rig.Data.movementCandidates.First(c => c != null && c != rig.Loc.Data);
                var t = new ScriptedTransport { Response = Json(rig.Data.availableActions[0].actionId, target.locationId) };
                var id = rig.Application.CreateIdentity(rig.Npc, rig.Card, 2, "req-C");
                var r = rig.Application.Apply(await Thinker(rig, t).DecideAsync(rig.Ctx, id, null), rig.Ctx, 2, null);
                Check("MoveTo 예약", r.ReservationType == DestinationReservationType.MoveTo);

                var counting = new CountingThinker();
                var movement = new NpcMovementSystem(rig.Resolution, counting, new RuleBasedMajorThinker(), rig.Reservations);
                await movement.MoveNpcsAsync(new[] { rig.Npc }, 2);

                Check("정확히 한 번 이동", rig.Npc.CurrentLocation == target, rig.Npc.CurrentLocation.locationId);
                Check("기존 DecideMoveAsync 미호출", counting.MoveCalls == 0);
                Check("PresentNpcs 갱신", rig.Locations[target].PresentNpcs.Contains(rig.Npc)
                                        && !rig.Locations[rig.Loc.Data].PresentNpcs.Contains(rig.Npc));

                // 미등록 장소는 예약 단계에서 차단
                var orphan = ScriptableObject.CreateInstance<LocationData>();
                orphan.locationId = "LOC_ORPHAN";
                bool reserved = rig.Reservations.TryReserve(rig.Npc, orphan, "k", 1, 2, out string why);
                Check("미등록 장소 예약 차단", !reserved, why);
                UnityEngine.Object.DestroyImmediate(orphan);
            }

            // ── D. 중복 ──────────────────────────────────────────────────────────
            sb.AppendLine();
            sb.AppendLine("### D. 중복 적용 차단");
            {
                var rig = Build();
                string verifyAction = rig.Data.availableActions.First(a => a.intent == NpcActionIntent.Verify).actionId;
                var t = new ScriptedTransport { Response = Json(verifyAction, "stay", "Trusted") };
                var id = rig.Application.CreateIdentity(rig.Npc, rig.Card, 2, "req-D");
                var outcome = await Thinker(rig, t).DecideAsync(rig.Ctx, id, null);

                rig.Application.Apply(outcome, rig.Ctx, 2, null);
                var beliefAfter1 = rig.Npc.GetBelief(rig.Card);
                int mem1 = rig.Npc.LongMemory.Count, spoke1 = rig.SpokeCount, judged1 = rig.JudgedCount;
                rig.Reservations.ClearRemaining();

                var r2 = rig.Application.Apply(outcome, rig.Ctx, 2, null);

                Check("DuplicateBlocked", r2.DuplicateBlocked && !r2.Applied);
                Check("Belief 변경 0", rig.Npc.GetBelief(rig.Card) == beliefAfter1);
                Check("기억 추가 0", rig.Npc.LongMemory.Count == mem1, rig.Npc.LongMemory.Count + "건");
                Check("이동 예약 0", rig.Reservations.Count == 0);
                Check("Dialogue 이벤트 0", rig.SpokeCount == spoke1);
                Check("CardJudged 이벤트 0", rig.JudgedCount == judged1);
            }

            // ── E. Stale ─────────────────────────────────────────────────────────
            sb.AppendLine();
            sb.AppendLine("### E. Stale 차단");
            {
                async Task Stale(string label, Func<Rig, JudgmentRequestIdentity> makeId, int applyTurn)
                {
                    var rig = Build();
                    var t = new ScriptedTransport { Response = Json(rig.Data.availableActions[0].actionId, "stay", "Trusted") };
                    var outcome = await Thinker(rig, t).DecideAsync(rig.Ctx, makeId(rig), null);
                    var beliefBefore = rig.Npc.GetBelief(rig.Card);
                    var r = rig.Application.Apply(outcome, rig.Ctx, applyTurn, null);
                    Check(label, r.StaleRequestDiscarded && !r.Applied
                                 && rig.Npc.GetBelief(rig.Card) == beliefBefore
                                 && rig.Npc.LongMemory.Count == 0 && rig.Reservations.Count == 0
                                 && rig.SpokeCount == 0 && rig.JudgedCount == 0, r.FailureReason);
                }
                await Stale("attemptId 불일치", r => new JudgmentRequestIdentity("STAGE_01", "M01", 99, 2, r.Data.npcId, r.Card.cardId, "x"), 2);
                await Stale("turn 불일치", r => r.Application.CreateIdentity(r.Npc, r.Card, 7, "x"), 2);
                await Stale("stage 불일치", r => new JudgmentRequestIdentity("STAGE_09", "M01", 1, 2, r.Data.npcId, r.Card.cardId, "x"), 2);
                await Stale("mission 불일치", r => new JudgmentRequestIdentity("STAGE_01", "M99", 1, 2, r.Data.npcId, r.Card.cardId, "x"), 2);
                await Stale("npc 불일치", r => new JudgmentRequestIdentity("STAGE_01", "M01", 1, 2, "npc_other", r.Card.cardId, "x"), 2);
                await Stale("card 불일치", r => new JudgmentRequestIdentity("STAGE_01", "M01", 1, 2, r.Data.npcId, "C-XXX", "x"), 2);
            }

            // ── F. fallback 결과 적용 = RuleOnly 동일성 ──────────────────────────
            sb.AppendLine();
            sb.AppendLine("### F. fallback 적용");
            {
                var rig = Build();
                var baseline = await new RuleBasedUnifiedThinker(rig.BeliefSys, new RuleBasedMajorThinker())
                    .DecideAsync(rig.Ctx, null);

                var t = new ScriptedTransport { Response = "{ 깨진 JSON" };
                var id = rig.Application.CreateIdentity(rig.Npc, rig.Card, 2, "req-F");
                var outcome = await Thinker(rig, t).DecideAsync(rig.Ctx, id, null);
                var r = rig.Application.Apply(outcome, rig.Ctx, 2, null);

                Check("source=RuleBasedFallback", r.ResultSource == JudgmentResultSource.RuleBasedFallback, outcome.FallbackReason);
                Check("Belief이 RuleOnly와 동일", rig.Npc.GetBelief(rig.Card) == baseline.Judgment.Belief,
                    $"{rig.Npc.GetBelief(rig.Card)} vs {baseline.Judgment.Belief}");
                Check("Action이 RuleOnly와 동일", rig.Npc.CurrentAction == baseline.Judgment.Action);
                Check("LLM 필드 혼합 0 (Goal 유지)", rig.Npc.CurrentGoal == baseline.Judgment.Goal);
            }

            // ── G. 무회귀: 예약 없으면 기존 경로 그대로 ──────────────────────────
            sb.AppendLine();
            sb.AppendLine("### G. 무회귀");
            {
                var rig = Build();
                var counting = new CountingThinker();
                var movement = new NpcMovementSystem(rig.Resolution, counting, new RuleBasedMajorThinker(), rig.Reservations);
                rig.Npc.MarkBeliefChanged();
                await movement.MoveNpcsAsync(new[] { rig.Npc }, 2);
                Check("예약 없으면 기존 판단 호출", counting.MoveCalls == 1, counting.MoveCalls + "회");

                var noReservation = new NpcMovementSystem(rig.Resolution, counting, new RuleBasedMajorThinker());
                rig.Npc.MarkBeliefChanged();
                await noReservation.MoveNpcsAsync(new[] { rig.Npc }, 3);
                Check("예약 저장소 null이면 기존 동작", counting.MoveCalls == 2, counting.MoveCalls + "회");

                Check("Zone2 정책 거부", !IntegratedLlmPilotPolicy.IsAllowed("STAGE_02", out _));
                Check("Zone1 정책 허용", IntegratedLlmPilotPolicy.IsAllowed("STAGE_01", out _));
            }

            // ── H. 이벤트 예외 ───────────────────────────────────────────────────
            sb.AppendLine();
            sb.AppendLine("### H. 관찰 구독자 예외");
            {
                var rig = Build();
                rig.Bus.Subscribe<NpcSpokeEvent>(_ => throw new InvalidOperationException("의도된 Dialogue 구독자 예외"));
                rig.Bus.Subscribe<CardJudgedEvent>(_ => throw new InvalidOperationException("의도된 CardJudged 구독자 예외"));

                string verifyAction = rig.Data.availableActions.First(a => a.intent == NpcActionIntent.Verify).actionId;
                var t = new ScriptedTransport { Response = Json(verifyAction, "stay", "Plausible") };
                var id = rig.Application.CreateIdentity(rig.Npc, rig.Card, 2, "req-H");
                var r = rig.Application.Apply(await Thinker(rig, t).DecideAsync(rig.Ctx, id, null), rig.Ctx, 2, null);

                Check("Belief 유지", rig.Npc.GetBelief(rig.Card) == BeliefState.Plausible);
                Check("Goal 유지", rig.Npc.CurrentGoal == "새 목표");
                Check("Action 유지", rig.Npc.CurrentAction != null);
                Check("기억 유지", rig.Npc.LongMemory.Count == 1, rig.Npc.LongMemory.Count + "건");
                Check("예약 유지", r.DestinationReserved);
                Check("완료 키 기록됨(중복 차단됨)", rig.Application.Apply(
                    await Thinker(rig, t).DecideAsync(rig.Ctx, id, null), rig.Ctx, 2, null).DuplicateBlocked);
            }

            sb.AppendLine();
            sb.AppendLine($"합계 {pass}/{total} PASS");
            return sb.ToString();
        }

        /// <summary>이동 판단 호출 횟수를 세는 thinker - 예약된 NPC가 정말 판단을 건너뛰는지 확인용.</summary>
        class CountingThinker : IMajorNpcThinker
        {
            public int MoveCalls;
            public Task<NpcThinkResult> DecideAsync(NpcThinkContext c, object t) =>
                Task.FromResult(new NpcThinkResult(null, null));
            public Task<NpcMoveResult> DecideMoveAsync(NpcMoveContext c, object t)
            { MoveCalls++; return Task.FromResult(new NpcMoveResult(null)); }
        }
    }
}
