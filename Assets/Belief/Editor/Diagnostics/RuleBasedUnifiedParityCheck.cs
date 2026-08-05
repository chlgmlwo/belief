using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Belief.AI;
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
    /// <b>RuleBasedUnifiedThinker가 기존 RuleOnly 경로를 손실 없이 재현하는가</b>를 검증하는
    /// 에디터 전용 도구. API 호출은 하지 않는다(토큰 0).
    ///
    /// 비교 방식: 같은 판단 전 스냅샷에 대해
    ///   (기존) BeliefSystem.Evaluate → RuleBasedMajorThinker.DecideAsync / DecideMoveAsync
    ///   (신규) RuleBasedUnifiedThinker.DecideAsync
    /// 를 각각 돌리고 Belief/Action/Goal/Dialogue/Destination을 대조한다.
    ///
    /// 이동 점수식은 동점일 때 Random tie-break을 쓰므로, 두 경로 호출 직전에 같은 시드를 심어
    /// 무작위성 때문에 생기는 가짜 불일치를 제거한다. Effect는 실행하지 않는다.
    /// </summary>
    public static class RuleBasedUnifiedParityCheck
    {
        [MenuItem("BELIEF/Diagnostics/Verify RuleBased Unified Parity", priority = 101)]
        public static void Run()
        {
            var report = Execute();
            string path = Path.Combine(Application.dataPath, "..", "Logs", "rulebased_parity.tsv");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, LastTsv);
            }
            catch (Exception ex) { report += "\n원본 저장 실패: " + ex.Message; }
            Debug.Log("[Parity]\n" + report);
        }

        static string LastTsv = "";

        class Rig
        {
            public List<NpcState> Npcs;
            public Dictionary<string, InformationCardData> Cards;
            public Dictionary<LocationData, LocationState> Locations;
            public BeliefSystem BeliefSys;
            public MemorySelector Selector;
            public MemoryTuningData MemTuning;
            public ActionResolutionSystem Resolution;
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
            var repeatedLies = (MemoryCategoryData)so.FindProperty("repeatedLiesCategory").objectReferenceValue;
            var pool = (InformationCardPoolData)so.FindProperty("informationPool").objectReferenceValue;

            var locations = new Dictionary<LocationData, LocationState>();
            var la = so.FindProperty("allLocations");
            for (int i = 0; i < la.arraySize; i++)
            {
                var l = la.GetArrayElementAtIndex(i).objectReferenceValue as LocationData;
                if (l != null && !locations.ContainsKey(l)) locations[l] = new LocationState(l);
            }

            // Zone1 배치 NPC뿐 아니라 프로젝트의 모든 MajorNpcData를 대상으로 넓힌다 -
            // 행동 후보 수·이동 후보 수·관계 유무가 다양할수록 재현 검증이 강해진다.
            var npcMap = new Dictionary<NpcData, NpcState>();
            var npcs = new List<NpcState>();
            foreach (var n in AssetDatabase.FindAssets("t:MajorNpcData")
                .Select(g => AssetDatabase.LoadAssetAtPath<MajorNpcData>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(n => n != null && n.homeLocation != null
                            && n.availableActions != null && n.availableActions.Length > 0))
            {
                if (npcMap.ContainsKey(n)) continue;
                if (!locations.ContainsKey(n.homeLocation)) locations[n.homeLocation] = new LocationState(n.homeLocation);
                var st = new NpcState(n);
                locations[st.CurrentLocation].PresentNpcs.Add(st);
                npcMap[n] = st; npcs.Add(st);
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
            new MemorySystem(bus, npcMap, repeatedLies);

            return new Rig
            {
                Npcs = npcs,
                Cards = pool.cards.Where(c => c != null && c.information != null).ToDictionary(c => c.cardId, c => c),
                Locations = locations,
                BeliefSys = new BeliefSystem(evaluators, beliefTuning, new BeliefDebugRepository(), mech),
                Selector = new MemorySelector(),
                MemTuning = memTuning,
                Resolution = new ActionResolutionSystem(locations, bus),
            };
        }

        public static string Execute()
        {
            var rig = Build();
            var ruleBased = new RuleBasedMajorThinker();
            var unified = new RuleBasedUnifiedThinker(rig.BeliefSys, ruleBased);

            var tsv = new StringBuilder();
            tsv.AppendLine("case\tnpc\tcard\tcred\tmemory\trelations\tactionCands\tmoveCands\t"
                        + "belief_old\tbelief_new\taction_old\taction_new\tgoal_old\tgoal_new\t"
                        + "dialogue_old\tdialogue_new\tdest_old\tdest_new\tmatch");

            int total = 0, beliefOk = 0, actionOk = 0, goalOk = 0, dialogueOk = 0, destOk = 0, allOk = 0;
            var mismatches = new List<string>();

            // 카드 등급 대표 + 후보가 여러 개인 상황을 모두 포함한다.
            string[] cardIds = { "C-ADM-01", "C-SEC-01", "C-POL-03", "C-CRI-01", "C-POL-01", "C-SEC-02" };

            // 케이스 A: 기억 없음 / 케이스 B: Verify로 기억 1건을 만든 뒤
            // 3단계: 기억 없음 → Verify로 기억 생성 → 재확산(전달자 있음) 문맥
            foreach (var phase in new[] { "기억없음", "Verify후", "재확산" })
            {
                foreach (var npc in rig.Npcs)
                {
                    var major = (MajorNpcData)npc.Data;
                    if (major.availableActions == null || major.availableActions.Length == 0) continue;

                    foreach (var cardId in cardIds)
                    {
                        if (!rig.Cards.TryGetValue(cardId, out var card)) continue;
                        var loc = rig.Locations[npc.CurrentLocation];
                        var wm = rig.Selector.Select(npc, new MemorySelectionContext(card, loc, 2), rig.MemTuning);
                        var present = loc.PresentNpcs.Where(n => n != npc).ToList();

                        // 재확산 단계에서는 전달자를 실제로 채운다 - 전달자·동석 인물이 규칙 기반
                        // 결과에 영향을 주지 않는다는 것까지 확인하기 위함이다.
                        NpcState propagator = null;
                        if (phase == "재확산")
                        {
                            var rel = major.relationships?.FirstOrDefault(r => r.other != null);
                            if (rel.HasValue)
                                propagator = rig.Npcs.FirstOrDefault(n => n.Data == rel.Value.other);
                            if (propagator == null) propagator = present.FirstOrDefault();
                        }

                        var ctx = new NpcJudgmentContext(
                            npc, card, loc, 2, npc.GetBelief(card), npc.CurrentGoal, wm,
                            major.availableActions, major.movementCandidates, present, propagator, rig.Locations);

                        // ── 기존 RuleOnly 경로 ──────────────────────────────────
                        int seed = (npc.Data.npcId + cardId + phase).GetHashCode();
                        UnityEngine.Random.InitState(seed);
                        var oldBelief = rig.BeliefSys.Evaluate(npc, card, loc, wm, 2).FinalBelief;
                        var oldThink = ruleBased.DecideAsync(new NpcThinkContext(
                            npc, card, oldBelief, wm, loc, major.availableActions, 2, present, propagator), null).Result;
                        var oldMove = ruleBased.DecideMoveAsync(new NpcMoveContext(
                            npc, loc.Data, major.movementCandidates, 2, present), null).Result;
                        string oldGoal = npc.CurrentGoal ?? "";
                        string oldDialogue = Dlg(oldThink.Dialogue);

                        // ── 신규 통합 경로 (같은 시드) ──────────────────────────
                        UnityEngine.Random.InitState(seed);
                        var v = unified.DecideAsync(ctx, null).Result;

                        total++;
                        if (!v.IsValid)
                        {
                            mismatches.Add($"{npc.Data.npcId}/{cardId}/{phase}: 통합 판단 실패 ({v.FailureReason})");
                            continue;
                        }
                        var j = v.Judgment;

                        bool bOk = oldBelief == j.Belief;
                        bool aOk = oldThink.ChosenAction == j.Action;
                        bool gOk = oldGoal == j.Goal;
                        bool dOk = oldDialogue == j.Dialogue;
                        bool destMatch = oldMove.Destination == j.Destination;
                        bool all = bOk && aOk && gOk && dOk && destMatch;

                        if (bOk) beliefOk++; if (aOk) actionOk++; if (gOk) goalOk++;
                        if (dOk) dialogueOk++; if (destMatch) destOk++; if (all) allOk++;

                        if (!all)
                            mismatches.Add($"{npc.Data.npcId}/{cardId}/{phase}: "
                                + (bOk ? "" : $"Belief {oldBelief}≠{j.Belief} ")
                                + (aOk ? "" : $"Action {Id(oldThink.ChosenAction)}≠{Id(j.Action)} ")
                                + (gOk ? "" : "Goal 불일치 ")
                                + (dOk ? "" : "Dialogue 불일치 ")
                                + (destMatch ? "" : $"Dest {Loc(oldMove.Destination)}≠{Loc(j.Destination)}"));

                        tsv.AppendLine(string.Join("\t", new[]
                        {
                            phase, npc.Data.npcId, cardId, card.information.baseCredibility.ToString("F2"),
                            npc.LongMemory.Count.ToString(),
                            (major.relationships?.Length ?? 0).ToString(),
                            major.availableActions.Length.ToString(),
                            (major.movementCandidates?.Length ?? 0).ToString(),
                            oldBelief.ToString(), j.Belief.ToString(),
                            Id(oldThink.ChosenAction), Id(j.Action),
                            Flat(oldGoal), Flat(j.Goal),
                            Flat(oldDialogue), Flat(j.Dialogue),
                            Loc(oldMove.Destination), Loc(j.Destination),
                            all ? "OK" : "MISMATCH"
                        }));
                    }
                }

                // 1차 통과 후: Verify를 실제로 실행해 기억을 만든 상태에서 다시 비교한다.
                if (phase == "기억없음")
                    foreach (var npc in rig.Npcs)
                    {
                        var major = (MajorNpcData)npc.Data;
                        var verify = major.availableActions?.FirstOrDefault(a => a != null && a.intent == NpcActionIntent.Verify);
                        if (verify == null) continue;
                        if (!rig.Cards.TryGetValue("C-POL-03", out var c)) continue;
                        rig.Resolution.Apply(verify, npc, c, rig.Locations[npc.CurrentLocation], 1);
                    }
            }

            LastTsv = tsv.ToString();

            var sb = new StringBuilder();
            sb.AppendLine($"표본 {total}건 (NPC {rig.Npcs.Count}명 x 카드 {cardIds.Length}장 x 3단계)");
            sb.AppendLine($"  Belief      {beliefOk}/{total} ({Pct(beliefOk, total)})");
            sb.AppendLine($"  Action      {actionOk}/{total} ({Pct(actionOk, total)})");
            sb.AppendLine($"  Goal        {goalOk}/{total} ({Pct(goalOk, total)})");
            sb.AppendLine($"  Dialogue    {dialogueOk}/{total} ({Pct(dialogueOk, total)})");
            sb.AppendLine($"  Destination {destOk}/{total} ({Pct(destOk, total)})");
            sb.AppendLine($"  전체 일치   {allOk}/{total} ({Pct(allOk, total)})");
            if (mismatches.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"불일치 {mismatches.Count}건:");
                foreach (var m in mismatches.Take(20)) sb.AppendLine("  " + m);
                if (mismatches.Count > 20) sb.AppendLine($"  … 외 {mismatches.Count - 20}건");
            }
            return sb.ToString();
        }

        static string Pct(int a, int b) => b == 0 ? "-" : $"{100.0 * a / b:F0}%";
        static string Id(NpcActionData a) => a != null ? a.actionId : "-";
        static string Loc(LocationData l) => l != null ? l.locationId : "stay";
        static string Flat(string s) => s == null ? "" : s.Replace("\t", " ").Replace("\n", " ").Replace("\r", "");
        static string Dlg(Belief.Systems.DialogueContent c) =>
            c == null ? "" : (c.IsGenerated ? (c.GeneratedText ?? "") : (c.PredefinedLine != null ? c.PredefinedLine.text : ""));
    }
}
