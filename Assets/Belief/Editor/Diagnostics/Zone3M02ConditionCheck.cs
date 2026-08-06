using System.Collections.Generic;
using System.Text;
using Belief.Data;
using Belief.Domain;
using Belief.Systems;
using UnityEditor;
using UnityEngine;

namespace Belief.EditorTools.Diagnostics
{
    /// <summary>
    /// Zone3 M02 "가면을 벗겨라"의 성공 경로 B(핵심 인물 응접실 이탈)를 <b>실제 API 없이</b> 검증한다.
    ///
    /// 경로 B의 TargetCount를 2에서 1로 낮추면서 가장 위험한 것은 "미션 시작 시점에 이미 한 명이
    /// 밖에 있으면 아무것도 안 해도 즉시 클리어"가 되는 것이다. 그 방어는 MissionFreshnessEvaluator가
    /// 담당하므로, 여기서는 <b>실제 평가 경로와 똑같이</b> 판정한다 -
    /// <c>MissionData.GetSuccessProgress(context, MissionFreshnessEvaluator.CountsForCompletion)</c>.
    /// 조건 클래스나 평가 코드를 흉내 내지 않고 그대로 호출하므로, 이 테스트가 통과하면 런타임도 같다.
    ///
    /// 세계는 이 미션이 보는 것만 최소로 만든다(응접실 + 무관한 장소 하나, 영주부인·시녀장·무관 NPC).
    /// </summary>
    public static class Zone3M02ConditionCheck
    {
        const string MissionPath = "Assets/Belief/Data/Missions/Mission_Stage03_02.asset";
        const string OutPath = "Library/BeliefLogs/zone3_m02_condition_check.md";

        sealed class World
        {
            public Dictionary<LocationData, LocationState> Locations = new Dictionary<LocationData, LocationState>();
            public Dictionary<NpcData, NpcState> Npcs = new Dictionary<NpcData, NpcState>();
            public MissionEvaluationContext Context =>
                new MissionEvaluationContext(Locations, Npcs, new List<DeliveredCardRecord>());
        }

        [MenuItem("BELIEF/Diagnostics/Zone3 M02 조건 결정적 테스트 (API 없음)", priority = 143)]
        public static void Run()
        {
            var mission = AssetDatabase.LoadAssetAtPath<MissionData>(MissionPath);
            if (mission == null) { Debug.LogError("[Zone3M02] 미션 에셋을 찾을 수 없습니다."); return; }

            var reception = FindLocation("MANOR_RECEPTION");
            var elsewhere = FindLocation("GARDEN") ?? FindLocation("CHAPEL");
            var wife = FindNpc("npc_major_lords_wife");
            var maid = FindNpc("npc_major_head_maid");
            var other = FindNpc("npc_major_priest") ?? FindNpc("npc_major_maid");

            if (reception == null || elsewhere == null || wife == null || maid == null || other == null)
            {
                Debug.LogError($"[Zone3M02] 필요한 에셋 누락 - reception={reception} elsewhere={elsewhere} " +
                               $"wife={wife} maid={maid} other={other}");
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine("# Zone3 M02 조건 결정적 테스트");
            sb.AppendLine($"- 미션 {mission.missionId} / clearMode={mission.clearMode} / turnLimit={mission.turnLimit}");
            foreach (var c in mission.successConditions)
                if (c != null) sb.AppendLine($"- 조건 `{c.name}` [{c.GetType().Name}] TargetCount={c.TargetCount}");
            sb.AppendLine();
            sb.AppendLine("| # | 시나리오 | 기대 | 실제 | 판정 |");
            sb.AppendLine("|---|---|---|---|---|");

            int pass = 0, fail = 0;

            void Case(string id, string name, bool expected, System.Func<bool> run)
            {
                bool actual;
                try { actual = run(); }
                catch (System.Exception e) { Debug.LogError($"[Zone3M02] {id} 예외: {e}"); actual = !expected; }
                bool ok = actual == expected;
                if (ok) pass++; else fail++;
                sb.AppendLine($"| {id} | {name} | {(expected ? "성공" : "실패")} | {(actual ? "성공" : "실패")} | {(ok ? "PASS" : "**FAIL**")} |");
            }

            // ── 경로 B ────────────────────────────────────────────────────────────
            Case("A", "둘 다 응접실에 있음", false, () =>
            {
                var w = Build(reception, elsewhere, wife, maid, other, wifeAt: reception, maidAt: reception);
                var baseline = MissionStartBaseline.Capture(mission, w.Context, 1);
                return Evaluate(mission, w, baseline);
            });

            Case("B", "영주부인만 이탈", true, () =>
            {
                var w = Build(reception, elsewhere, wife, maid, other, wifeAt: reception, maidAt: reception);
                var baseline = MissionStartBaseline.Capture(mission, w.Context, 1);
                Move(w, wife, elsewhere);
                return Evaluate(mission, w, baseline);
            });

            Case("C", "시녀장만 이탈", true, () =>
            {
                var w = Build(reception, elsewhere, wife, maid, other, wifeAt: reception, maidAt: reception);
                var baseline = MissionStartBaseline.Capture(mission, w.Context, 1);
                Move(w, maid, elsewhere);
                return Evaluate(mission, w, baseline);
            });

            Case("D", "둘 다 이탈", true, () =>
            {
                var w = Build(reception, elsewhere, wife, maid, other, wifeAt: reception, maidAt: reception);
                var baseline = MissionStartBaseline.Capture(mission, w.Context, 1);
                Move(w, wife, elsewhere);
                Move(w, maid, elsewhere);
                return Evaluate(mission, w, baseline);
            });

            Case("E", "무관한 NPC만 이탈", false, () =>
            {
                var w = Build(reception, elsewhere, wife, maid, other, wifeAt: reception, maidAt: reception);
                var baseline = MissionStartBaseline.Capture(mission, w.Context, 1);
                Move(w, other, elsewhere);
                return Evaluate(mission, w, baseline);
            });

            // ── Fresh Completion ─────────────────────────────────────────────────
            Case("F", "시작부터 시녀장이 밖 / 이후 변화 없음", false, () =>
            {
                var w = Build(reception, elsewhere, wife, maid, other, wifeAt: reception, maidAt: elsewhere);
                var baseline = MissionStartBaseline.Capture(mission, w.Context, 1);
                return Evaluate(mission, w, baseline);
            });

            Case("G", "시작부터 시녀장이 밖 / 이후 영주부인이 새로 이탈", true, () =>
            {
                var w = Build(reception, elsewhere, wife, maid, other, wifeAt: reception, maidAt: elsewhere);
                var baseline = MissionStartBaseline.Capture(mission, w.Context, 1);
                Move(w, wife, elsewhere);
                return Evaluate(mission, w, baseline);
            });

            Case("H", "재시도 - 이전 시도의 이탈은 새 시도 성공으로 불인정", false, () =>
            {
                var w = Build(reception, elsewhere, wife, maid, other, wifeAt: reception, maidAt: reception);
                var attempt1 = MissionStartBaseline.Capture(mission, w.Context, 1);
                Move(w, maid, elsewhere);
                if (!Evaluate(mission, w, attempt1)) return true; // 1차 시도가 성공해야 이 테스트가 의미 있다
                var attempt2 = MissionStartBaseline.Capture(mission, w.Context, 2);   // 위치는 그대로, 기준점만 새로
                return Evaluate(mission, w, attempt2);
            });

            // ── 경로 A / clearMode ───────────────────────────────────────────────
            Case("I", "경로 A - 영주부인의 조사 기록", true, () =>
            {
                var w = Build(reception, elsewhere, wife, maid, other, wifeAt: reception, maidAt: reception);
                var baseline = MissionStartBaseline.Capture(mission, w.Context, 1);
                AddInvestigation(w, reception, wife, InformationResultType.Investigating);
                return Evaluate(mission, w, baseline);
            });

            Case("J", "clearMode Any - 경로 B 불성립인데 경로 A만 성립", true, () =>
            {
                var w = Build(reception, elsewhere, wife, maid, other, wifeAt: reception, maidAt: reception);
                var baseline = MissionStartBaseline.Capture(mission, w.Context, 1);
                AddInvestigation(w, reception, wife, InformationResultType.Monitoring);
                return Evaluate(mission, w, baseline);   // 아무도 이동하지 않았다
            });

            Case("E2", "경로 A - 무관한 NPC의 조사 기록은 불인정", false, () =>
            {
                var w = Build(reception, elsewhere, wife, maid, other, wifeAt: reception, maidAt: reception);
                var baseline = MissionStartBaseline.Capture(mission, w.Context, 1);
                AddInvestigation(w, reception, other, InformationResultType.Investigating);
                return Evaluate(mission, w, baseline);
            });

            sb.AppendLine();
            sb.AppendLine($"- PASS {pass} / FAIL {fail}");

            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(OutPath));
            System.IO.File.WriteAllText(OutPath, sb.ToString());

            if (fail == 0) Debug.Log($"[Zone3M02] 결정적 테스트 전부 통과 ({pass}건)\n결과: {OutPath}");
            else Debug.LogError($"[Zone3M02] 결정적 테스트 실패 {fail}건 (통과 {pass}건)\n결과: {OutPath}");
        }

        // ── 헬퍼 ────────────────────────────────────────────────────────────────

        /// <summary>실제 런타임과 같은 판정식 - 조건 단위 Fresh 필터를 그대로 끼운다.</summary>
        static bool Evaluate(MissionData mission, World w, MissionStartBaseline baseline)
        {
            var ctx = w.Context;
            return mission.GetSuccessProgress(ctx,
                c => MissionFreshnessEvaluator.CountsForCompletion(c, baseline, ctx)) >= mission.SuccessTarget;
        }

        static World Build(LocationData reception, LocationData elsewhere,
            NpcData wife, NpcData maid, NpcData other, LocationData wifeAt, LocationData maidAt)
        {
            var w = new World();
            foreach (var l in new[] { reception, elsewhere })
                if (!w.Locations.ContainsKey(l)) w.Locations[l] = new LocationState(l);

            foreach (var pair in new[] { (wife, wifeAt), (maid, maidAt), (other, reception) })
            {
                var state = new NpcState(pair.Item1);
                state.CurrentLocation = pair.Item2;     // setter가 스탬프를 찍는다
                w.Npcs[pair.Item1] = state;
                w.Locations[pair.Item2].PresentNpcs.Add(state);
            }
            return w;
        }

        static void Move(World w, NpcData npc, LocationData to)
        {
            var state = w.Npcs[npc];
            var from = state.CurrentLocation;
            if (from != null && w.Locations.TryGetValue(from, out var fromState)) fromState.PresentNpcs.Remove(state);
            state.CurrentLocation = to;
            if (w.Locations.TryGetValue(to, out var toState)) toState.PresentNpcs.Add(state);
        }

        static void AddInvestigation(World w, LocationData at, NpcData actor, InformationResultType type)
        {
            w.Locations[at].InvestigationStates.Add(
                new InformationWorldState(null, null, at, actor, type, 1));
        }

        static LocationData FindLocation(string locationId)
        {
            foreach (var g in AssetDatabase.FindAssets("t:LocationData"))
            {
                var l = AssetDatabase.LoadAssetAtPath<LocationData>(AssetDatabase.GUIDToAssetPath(g));
                if (l != null && l.locationId == locationId) return l;
            }
            return null;
        }

        static NpcData FindNpc(string npcId)
        {
            foreach (var g in AssetDatabase.FindAssets("t:NpcData"))
            {
                var n = AssetDatabase.LoadAssetAtPath<NpcData>(AssetDatabase.GUIDToAssetPath(g));
                if (n != null && n.npcId == npcId) return n;
            }
            return null;
        }
    }
}
