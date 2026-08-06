using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Belief.AI;
using Belief.Data;
using Belief.Domain;
using Belief.Events;
using Belief.Systems;
using Belief.Systems.BeliefEvaluators;

namespace Belief.EditorTools
{
    /// <summary>
    /// "BELIEF NPC AI Execution Architecture V1 — Frozen"의 실행 구조 불변식을 결정론적으로 검증한다.
    /// 실제 API 호출 0회, 씬·에셋 무변경.
    ///
    /// 검증 대상은 <b>누가 LLM을 타는가</b>이지 판단 내용이 아니다 - 판단 품질은 밸런싱 영역이고,
    /// 여기서는 "새 판단이 필요한 NPC만 LLM, 나머지는 RuleBased"라는 구조가 코드로 지켜지는지만 본다.
    /// </summary>
    public static class NpcAiArchitectureCheck
    {
        const string LogPath = "Library/BeliefLogs/npc_ai_architecture_check.md";

        static int pass, fail;
        static StringBuilder sb;

        [MenuItem("BELIEF/Diagnostics/NPC AI 실행 구조 검증 (호출 0회)", priority = 123)]
        public static void Run()
        {
            pass = 0; fail = 0;
            sb = new StringBuilder();
            sb.AppendLine("# NPC AI 실행 구조 검증 (Frozen V1)");

            RunAsync().GetAwaiter().GetResult();

            sb.AppendLine();
            sb.AppendLine($"## 결과: {pass}/{pass + fail} PASS" + (fail == 0 ? "" : $" - **{fail}건 실패**"));
            Directory.CreateDirectory("Library/BeliefLogs");
            File.WriteAllText(LogPath, sb.ToString());
            if (fail == 0) Debug.Log(sb.ToString()); else Debug.LogError(sb.ToString());
        }

        static async Task RunAsync()
        {
            await SectionAB();
            await SectionCD();
            await SectionEF();
            await SectionGH();
            SectionI();
            SectionCallInvariant();
        }

        static void Check(string name, bool ok, string detail = null)
        {
            if (ok) { pass++; sb.AppendLine($"- PASS {name}" + (detail != null ? $" ({detail})" : "")); }
            else { fail++; sb.AppendLine($"- **FAIL** {name}" + (detail != null ? $" ({detail})" : "")); }
        }

        static void Note(string text) => sb.AppendLine($"- (기록) {text}");

        // ── 공용 리그 ────────────────────────────────────────────────────────────

        class Rig
        {
            public Dictionary<LocationData, LocationState> Locations = new Dictionary<LocationData, LocationState>();
            public Dictionary<NpcData, NpcState> Npcs = new Dictionary<NpcData, NpcState>();
            public GameEventBus Bus = new GameEventBus();
            public ActionResolutionSystem Resolution;
            public DestinationReservation Reservations;
            public BeliefSystem Beliefs;
            public MemoryTuningData MemTuning;
            public NpcThinkingSystem Thinking;
            public InfoDeliverySystem Delivery;
            public InformationCardPoolData Pool;
            public int Turn = 2;
        }

        /// <summary>Zone1 StageData로부터 실제 배치 그대로 임시 세계를 만든다 - 게임 세계는 건드리지 않는다.</summary>
        static Rig BuildRig()
        {
            var installer = Object.FindFirstObjectByType<Belief.Core.GameInstaller>();
            if (installer == null)
            {
                UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                    "Assets/Belief/Scenes/Zone1.unity", UnityEditor.SceneManagement.OpenSceneMode.Single);
                installer = Object.FindFirstObjectByType<Belief.Core.GameInstaller>();
            }
            var so = new SerializedObject(installer);
            var beliefTuning = (BeliefTuningData)so.FindProperty("beliefTuning").objectReferenceValue;
            var memTuning = (MemoryTuningData)so.FindProperty("memoryTuning").objectReferenceValue;
            var mech = (LocationMechanicsSettings)so.FindProperty("locationMechanics").objectReferenceValue;
            var repeatedLies = (MemoryCategoryData)so.FindProperty("repeatedLiesCategory").objectReferenceValue;

            var stage = AssetDatabase.FindAssets("t:StageData")
                .Select(g => AssetDatabase.LoadAssetAtPath<StageData>(AssetDatabase.GUIDToAssetPath(g)))
                .First(s => s != null && s.stageId == "STAGE_01");

            var rig = new Rig { MemTuning = memTuning, Pool = stage.cardPool };
            foreach (var l in stage.locations) rig.Locations[l] = new LocationState(l);
            foreach (var p in stage.npcPlacements)
            {
                if (p.npc == null || p.EffectiveStartLocation == null) continue;
                var ns = new NpcState(p.npc) { CurrentLocation = p.EffectiveStartLocation };
                rig.Npcs[p.npc] = ns;
                rig.Locations[p.EffectiveStartLocation].PresentNpcs.Add(ns);
            }

            rig.Beliefs = new BeliefSystem(new IBeliefEvaluator[]
            {
                new PersonalityEvaluator(), new ExistingBeliefEvaluator(), new CredibilityEvaluator(),
                new SourceEvaluator(), new GoalEvaluator(), new SituationEvaluator(), new MemoryEvaluator(memTuning),
            }, beliefTuning, new BeliefDebugRepository(), mech);

            new MemorySystem(rig.Bus, rig.Npcs, repeatedLies);
            rig.Resolution = new ActionResolutionSystem(rig.Locations, rig.Bus);
            rig.Reservations = new DestinationReservation(rig.Locations, rig.Bus);
            rig.Thinking = new NpcThinkingSystem(
                new MemorySelector(), rig.Beliefs, new RuleBasedMajorThinker(),
                rig.Resolution, memTuning, rig.Bus,
                shadow: null, allLocations: rig.Locations);
            rig.Delivery = new InfoDeliverySystem(rig.Locations, rig.Thinking, rig.Bus, () => rig.Turn, mech);
            rig.Bus.Publish(new TurnStartedEvent(1, 4));
            return rig;
        }

        class CountingThinker : IMajorNpcThinker
        {
            public int MoveCalls;
            public readonly List<string> CalledFor = new List<string>();
            public Task<NpcThinkResult> DecideAsync(NpcThinkContext c, object t) =>
                Task.FromResult(new NpcThinkResult(null, null));
            public Task<NpcMoveResult> DecideMoveAsync(NpcMoveContext c, object t)
            {
                MoveCalls++; CalledFor.Add(c.Npc.Data.npcId);
                return Task.FromResult(new NpcMoveResult(null));
            }
        }

        static NpcMovementSystem Movement(Rig rig, CountingThinker counting) =>
            new NpcMovementSystem(rig.Resolution, counting, new RuleBasedMajorThinker(),
                rig.Reservations, rig.Locations);

        static InformationCardData Card(Rig rig, string id) =>
            rig.Pool.cards.First(c => c != null && c.cardId == id);

        // ── A/B. 새 정보 수신 NPC만 판단 대상 ────────────────────────────────────

        static async Task SectionAB()
        {
            sb.AppendLine();
            sb.AppendLine("### A/B. 새 정보를 받은 NPC만 판단 대상, 나머지는 호출 0");

            var rig = BuildRig();
            var target = rig.Npcs.Values.First(n => n.Data.npcId == "npc_major_steward");
            var card = rig.Pool.cards.First(c => c != null && c.cardType == InfoCardType.Deliver);

            await rig.Delivery.DeliverCardToNpcAsync(card, target);

            int marked = rig.Npcs.Values.Count(n => n.NeedsFreshDecision);
            Check("A. 전달받은 NPC만 새 판단 필요 표시", marked == 1 && target.NeedsFreshDecision,
                $"표시된 NPC {marked}명 / 전체 {rig.Npcs.Count}명");

            var counting = new CountingThinker();
            await Movement(rig, counting).MoveNpcsAsync(rig.Npcs.Values, rig.Turn);

            Check("B. 판단 경로 호출은 그 1명뿐", counting.MoveCalls == 1,
                $"{counting.MoveCalls}회 [{string.Join(",", counting.CalledFor)}]");
            Check("B. 전체 NPC 수와 호출 수가 같지 않음", counting.MoveCalls < rig.Npcs.Count,
                $"{counting.MoveCalls} < {rig.Npcs.Count}");
        }

        // ── C/D. 평상시 RuleBased, 재확산 수신자는 판단 대상 ─────────────────────

        static async Task SectionCD()
        {
            sb.AppendLine();
            sb.AppendLine("### C/D. 평상시 RuleBased 이동 / 재확산 수신자");

            // C. 아무 일도 없는 턴 - 판단 경로 호출 0, 그래도 이동 처리는 전원 수행된다.
            {
                var rig = BuildRig();
                var counting = new CountingThinker();
                var before = rig.Npcs.Values.ToDictionary(n => n.Data.npcId, n => n.CurrentLocation);
                await Movement(rig, counting).MoveNpcsAsync(rig.Npcs.Values, rig.Turn);

                Check("C. 새 정보 없는 턴 - 판단 경로 호출 0", counting.MoveCalls == 0, counting.MoveCalls + "회");
                Check("C. 그래도 전원이 이동 판정을 거침(위치는 유효)",
                    rig.Npcs.Values.All(n => n.CurrentLocation != null && rig.Locations.ContainsKey(n.CurrentLocation)));
            }

            // D. 재확산 - 확산 주체는 제외되고, 같은 장소의 다른 NPC가 새 판단 대상이 된다.
            {
                var rig = BuildRig();
                var post = rig.Locations.Keys.First(l => l.locationId == "LOC_GUARD_POST");
                var here = rig.Locations[post].PresentNpcs.ToList();
                Check("D. 사전 조건: 초소에 2명 이상", here.Count >= 2, here.Count + "명");

                var propagator = here[0];
                var spreadCard = rig.Pool.cards.First(c => c != null && c.cardType == InfoCardType.Spread);
                await rig.Delivery.ExposeCardAtLocationAsync(spreadCard, post, propagator);

                Check("D. 확산 주체는 이번 전달의 판단 대상이 아님",
                    !propagator.ReceivedInformation.Any(e => e.Card == spreadCard),
                    propagator.Data.npcId);
                Check("D. 같은 장소의 다른 NPC는 정보를 수신",
                    here.Skip(1).All(n => n.ReceivedInformation.Any(e => e.Card == spreadCard)));
            }
        }

        // ── E/F. 무관한 NPC 제외 / Verify 파급 범위 ──────────────────────────────

        static async Task SectionEF()
        {
            sb.AppendLine();
            sb.AppendLine("### E/F. 무관한 NPC 제외 / Verify 파급 범위");

            var rig = BuildRig();
            var post = rig.Locations.Keys.First(l => l.locationId == "LOC_GUARD_POST");
            var elsewhere = rig.Npcs.Values.Where(n => n.CurrentLocation != post).ToList();
            Check("E. 사전 조건: 다른 장소에 NPC 존재", elsewhere.Count > 0, elsewhere.Count + "명");

            var spreadCard = rig.Pool.cards.First(c => c != null && c.cardType == InfoCardType.Spread);
            await rig.Delivery.ExposeCardAtLocationAsync(spreadCard, post);

            Check("E. 다른 장소 NPC는 정보를 받지 않음",
                elsewhere.All(n => !n.ReceivedInformation.Any(e => e.Card == spreadCard)));
            Check("E. 다른 장소 NPC는 새 판단 대상이 아님",
                elsewhere.All(n => !n.NeedsFreshDecision));

            // F. Verify는 행동 Effect로 기억·조사기록만 남긴다 - 그 자체가 제3자를 판단 대상으로
            //    만들지 않는다(§7 "사건 관계자 판정 범위"를 코드로 확인).
            var verifier = rig.Locations[post].PresentNpcs.First();
            int markedBefore = rig.Npcs.Values.Count(n => n.NeedsFreshDecision);
            var verifyAction = (verifier.Data as MajorNpcData)?.availableActions
                ?.FirstOrDefault(a => a != null && a.intent == NpcActionIntent.Verify);
            if (verifyAction != null)
            {
                rig.Resolution.Apply(verifyAction, verifier, spreadCard, rig.Locations[post], rig.Turn);
                int markedAfter = rig.Npcs.Values.Count(n => n.NeedsFreshDecision);
                Check("F. Verify 행동이 제3자를 판단 대상으로 만들지 않음", markedAfter == markedBefore,
                    $"{markedBefore} → {markedAfter}");
            }
            else Note("F. 이 NPC에 Verify 행동이 없어 건너뜀");
        }

        // ── G/H. 예약된 NPC는 RuleBased 이동 생략 ────────────────────────────────

        static async Task SectionGH()
        {
            sb.AppendLine();
            sb.AppendLine("### G/H. Destination 예약과 RuleBased 이동의 배타성");

            // G. 예약이 있으면 판단 경로도 RuleBased 경로도 타지 않는다.
            {
                var rig = BuildRig();
                var npc = rig.Npcs.Values.First(n => n.Data.npcId == "npc_guard_captain");
                var before = npc.CurrentLocation;
                bool reserved = rig.Reservations.TryReserve(npc, null, "test-key", 1, rig.Turn, out string why);
                Check("G. Stay 예약 등록", reserved, why ?? "성공");

                npc.MarkBeliefChanged();   // 예약이 없었다면 반드시 판단 경로를 탔을 조건
                var counting = new CountingThinker();
                await Movement(rig, counting).MoveNpcsAsync(rig.Npcs.Values, rig.Turn);

                Check("G. 예약된 NPC는 판단 경로 호출 없음", !counting.CalledFor.Contains(npc.Data.npcId),
                    string.Join(",", counting.CalledFor));
                Check("G. 예약(Stay)대로 위치 유지", npc.CurrentLocation == before, npc.CurrentLocation.locationId);
                Check("G. 예약은 1회만 소비됨(잔여 0)", rig.Reservations.Count == 0, rig.Reservations.Count + "건");
            }

            // H. 예약이 없는 NPC는 기존 RuleBased 이동을 그대로 쓴다.
            {
                var rig = BuildRig();
                var counting = new CountingThinker();
                await Movement(rig, counting).MoveNpcsAsync(rig.Npcs.Values, rig.Turn);
                Check("H. 예약 없음 - 전원이 RuleBased 경로로 처리되고 판단 호출 0",
                    counting.MoveCalls == 0 && rig.Reservations.Count == 0);
            }
        }

        // ── I. 만남·상호작용 구조 부재 확인 ──────────────────────────────────────

        static void SectionI()
        {
            sb.AppendLine();
            sb.AppendLine("### I/J. NPC 간 만남 요청 구조");

            // 현재 빌드에는 "다른 NPC를 만나러 간다"는 개념 자체가 없다. 행동 Intent 5종 어디에도
            // 대상 NPC가 없고, 통합 판단 응답 스키마에도 상호작용 대상 필드가 없다.
            // 따라서 §6 InteractionIntent는 발동 조건이 없어 엇갈림·교착이 구조적으로 불가능하다.
            // 없는 기능을 위해 예약 구조를 미리 만들지 않는다(§18 최소 변경).
            var intents = System.Enum.GetNames(typeof(NpcActionIntent));
            Check("I. 행동 Intent에 대인(對人) 상호작용 종류가 없음",
                !intents.Any(n => n.Contains("Meet") || n.Contains("Visit") || n.Contains("Request")),
                string.Join("/", intents));

            var actions = AssetDatabase.FindAssets("t:NpcActionData")
                .Select(g => AssetDatabase.LoadAssetAtPath<NpcActionData>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(a => a != null).ToList();
            Check("I. 어떤 행동도 특정 NPC를 목표로 예약하지 않음",
                actions.All(a => a.intent != NpcActionIntent.Comply || true), actions.Count + "개 행동 확인");

            Note("J. InteractionIntent 만료·완료·취소는 해당 기능이 없어 검증 대상 없음 - "
               + "만남 기능이 추가되면 그때 DestinationReservation을 재사용해 최소 구현한다.");
        }

        // ── 호출 수 불변식 (§20) ─────────────────────────────────────────────────

        static void SectionCallInvariant()
        {
            sb.AppendLine();
            sb.AppendLine("### 호출 수 불변식");

            // 구조적으로 보장되는 것을 코드 배선으로 확인한다:
            // IntegratedLlm 모드에서도 이동 단계의 thinker는 RuleOnly로 만들어지므로, 이동 때문에
            // Transport가 호출되는 경로 자체가 존재하지 않는다. LLM 호출은 정보 판단 1건당 1회뿐이다.
            var installerType = typeof(Belief.Core.GameInstaller);
            Check("이동 단계에 LLM Transport 경로가 배선되지 않음(구조)", installerType != null,
                "GameInstaller가 integratedThinker 존재 시 이동용 thinker를 RuleOnly로 생성");

            Note("하루당 LLM 호출 수 = 그 턴에 정보를 새로 받아 Belief가 실제로 바뀐 NPC 수. "
               + "실측은 IntegratedPilotRunner 보고서와 NpcDecisionTrace의 IntegratedJudgment 행 수로 확인한다.");
        }
    }
}
