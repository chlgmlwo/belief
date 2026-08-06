using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Belief.Data;
using Belief.Domain;
using Belief.Events;
using Belief.Systems;

namespace Belief.EditorTools
{
    /// <summary>
    /// Fresh Completion(미션 시작 이후의 변화로만 클리어 인정) 결정적 검증. 실제 API 호출 0회,
    /// 씬·미션·조건·카드 데이터 무변경 - 조건 SO는 전부 CreateInstance로 그 자리에서 만들고 버린다.
    ///
    /// 여기서 TurnSystem 전체를 세우지 않고 MissionSystem.BeginAttempt/Evaluate를 직접 부르는 이유:
    /// 검증 대상이 "기준점을 언제 만드는가"가 아니라 "기준점이 있을 때 판정이 어떻게 달라지는가"이기
    /// 때문이다. 호출 순서(baseline이 첫 평가보다 먼저)는 TurnSystem 코드 자체로 보장되고,
    /// 전 구역 RuleOnly 플레이로 따로 확인한다.
    /// </summary>
    public static class FreshCompletionCheck
    {
        static int pass, fail;
        static StringBuilder sb;

        [MenuItem("BELIEF/Diagnostics/Fresh Completion 결정적 검증 (호출 0회)", priority = 120)]
        public static void Run()
        {
            pass = 0; fail = 0;
            sb = new StringBuilder();
            sb.AppendLine("# Fresh Completion 결정적 검증");

            SectionA();
            SectionB();
            SectionC();
            SectionD();
            SectionE();
            SectionF();
            SectionG();
            SectionH();

            sb.AppendLine();
            sb.AppendLine($"## 결과: {pass}/{pass + fail} PASS" + (fail == 0 ? "" : $" - **{fail}건 실패**"));

            System.IO.Directory.CreateDirectory("Library/BeliefLogs");
            System.IO.File.WriteAllText("Library/BeliefLogs/fresh_completion_check.md", sb.ToString());

            if (fail == 0) Debug.Log(sb.ToString());
            else Debug.LogError(sb.ToString());
        }

        static void Check(string name, bool ok, string detail = null)
        {
            if (ok) { pass++; sb.AppendLine($"- PASS {name}" + (detail != null ? $" ({detail})" : "")); }
            else { fail++; sb.AppendLine($"- **FAIL** {name}" + (detail != null ? $" ({detail})" : "")); }
        }

        // ── 공용 리그 ────────────────────────────────────────────────────────────

        class World
        {
            public Dictionary<LocationData, LocationState> Locations = new Dictionary<LocationData, LocationState>();
            public Dictionary<NpcData, NpcState> Npcs = new Dictionary<NpcData, NpcState>();
            public MissionEvaluationContext Ctx =>
                new MissionEvaluationContext(Locations, Npcs, new List<DeliveredCardRecord>());

            public NpcState Add(MajorNpcData data, LocationData at)
            {
                Ensure(at);
                var npc = new NpcState(data) { CurrentLocation = at };
                Npcs[data] = npc;
                Locations[at].PresentNpcs.Add(npc);
                return npc;
            }
            public LocationState Ensure(LocationData l)
            {
                if (l != null && !Locations.ContainsKey(l)) Locations[l] = new LocationState(l);
                return l != null ? Locations[l] : null;
            }
            public void Move(NpcState npc, LocationData to)
            {
                Ensure(to);
                if (npc.CurrentLocation != null && Locations.TryGetValue(npc.CurrentLocation, out var from))
                    from.PresentNpcs.Remove(npc);
                npc.CurrentLocation = to;
                Locations[to].PresentNpcs.Add(npc);
            }
        }

        static MajorNpcData Npc(string id) => AssetDatabase.FindAssets("t:MajorNpcData")
            .Select(g => AssetDatabase.LoadAssetAtPath<MajorNpcData>(AssetDatabase.GUIDToAssetPath(g)))
            .First(n => n != null && n.npcId == id);

        static LocationData Loc(string id) => AssetDatabase.FindAssets("t:LocationData")
            .Select(g => AssetDatabase.LoadAssetAtPath<LocationData>(AssetDatabase.GUIDToAssetPath(g)))
            .First(l => l != null && l.locationId == id);

        static InformationCardData Card(string id) => AssetDatabase.FindAssets("t:InformationCardData")
            .Select(g => AssetDatabase.LoadAssetAtPath<InformationCardData>(AssetDatabase.GUIDToAssetPath(g)))
            .First(c => c != null && c.cardId == id);

        static T Cond<T>() where T : MissionConditionData => ScriptableObject.CreateInstance<T>();

        static MissionData Mission(MissionClearMode mode, params MissionConditionData[] conditions)
        {
            var m = ScriptableObject.CreateInstance<MissionData>();
            m.missionId = "TEST_MISSION";
            m.clearMode = mode;
            m.successConditions = conditions;
            return m;
        }

        /// <summary>미션을 활성화하고 기준점을 만든다 - TurnSystem이 하는 순서(LoadMission → BeginAttempt)
        /// 를 그대로 흉내 낸다.</summary>
        static MissionSystem Begin(MissionData mission, World w)
        {
            var sys = new MissionSystem(mission, new GameEventBus());
            sys.BeginAttempt(w.Ctx);
            return sys;
        }

        static bool Complete(MissionSystem sys, World w)
        {
            sys.Evaluate(w.Ctx);
            return sys.State.IsComplete;
        }

        // ── A. 시작부터 위치 조건 만족 ───────────────────────────────────────────

        static void SectionA()
        {
            sb.AppendLine();
            sb.AppendLine("### A. 시작부터 위치 조건(장소 비우기) 만족");

            var w = new World();
            var post = Loc("LOC_GUARD_POST");
            var front = Loc("LOC_MANOR_FRONT");
            var captain = w.Add(Npc("npc_guard_captain"), front);      // 이미 초소를 떠나 있음
            var guard = w.Add(Npc("npc_major_lowrank_guard"), front);  // 이미 초소를 떠나 있음
            var steward = w.Add(Npc("npc_major_steward"), front);      // 무관한 NPC
            w.Ensure(post);

            var leave = Cond<NpcsLeaveLocationCondition>();
            leave.watchedLocation = post;
            leave.watchedNpcs = new NpcData[] { captain.Data, guard.Data };
            leave.TargetCount = 2;

            var mission = Mission(MissionClearMode.Any, leave);
            Check("사전 조건: 현재 상태만 보면 이미 만족", leave.GetCurrentProgress(w.Ctx) >= leave.TargetCount);

            var sys = Begin(mission, w);
            Check("첫 평가에서 클리어 금지", !Complete(sys, w));
            Check("반복 평가해도 클리어 금지", !Complete(sys, w) && !Complete(sys, w));

            w.Move(steward, post);   // 무관한 NPC만 이동
            Check("무관한 NPC 이동으로는 클리어 금지", !Complete(sys, w));

            w.Move(captain, post);   // 감시 대상이 들어왔다가
            Check("조건이 깨진 동안 클리어 아님", !Complete(sys, w));
            w.Move(captain, front);  // 다시 떠남 = 미션 시작 이후의 실제 위치 변화
            Check("감시 대상이 실제로 이동하면 클리어", Complete(sys, w));
        }

        // ── B. 시작부터 집합 조건 만족 ───────────────────────────────────────────

        static void SectionB()
        {
            sb.AppendLine();
            sb.AppendLine("### B. 시작부터 집합 조건 만족");

            var w = new World();
            var plaza = Loc("loc_plaza");
            var inn = Loc("LOC_INN");
            var innkeeper = w.Add(Npc("npc_major_innkeeper"), plaza);
            var maid = w.Add(Npc("npc_major_maid"), plaza);
            var priest = w.Add(Npc("npc_major_priest"), plaza);   // 무관한 NPC
            w.Ensure(inn);

            var gather = Cond<NpcsGatherAtLocationCondition>();
            gather.watchedLocation = plaza;
            gather.watchedNpcs = new NpcData[] { innkeeper.Data, maid.Data };
            gather.TargetCount = 2;

            var sys = Begin(Mission(MissionClearMode.Any, gather), w);
            Check("첫 평가에서 클리어 금지", !Complete(sys, w));
            Check("구성 변화 없이 반복 평가해도 클리어 금지", !Complete(sys, w) && !Complete(sys, w));

            w.Move(priest, inn);
            Check("무관한 NPC 이동으로는 클리어 금지", !Complete(sys, w));

            w.Move(innkeeper, inn);
            Check("감시 대상이 빠지면 클리어 아님", !Complete(sys, w));
            w.Move(innkeeper, plaza);
            Check("다시 모이면 클리어", Complete(sys, w));
        }

        // ── C. 시작부터 Belief 조건 만족 ─────────────────────────────────────────

        static void SectionC()
        {
            sb.AppendLine();
            sb.AppendLine("### C. 시작부터 Belief 조건 만족");

            var w = new World();
            var post = Loc("LOC_GUARD_POST");
            var captain = w.Add(Npc("npc_guard_captain"), post);
            var guard = w.Add(Npc("npc_major_lowrank_guard"), post);
            var c1 = Card("C-POL-03");
            var c2 = Card("C-SEC-03");

            captain.SetBelief(c1, BeliefState.NeedsVerification); // 이전 미션에서 이미 판단해 둔 상태

            var belief = Cond<NpcAnyBeliefReachedCondition>();
            belief.targetNpc = captain.Data;
            belief.thresholdState = BeliefState.NeedsVerification;
            belief.atOrBelow = true;

            var sys = Begin(Mission(MissionClearMode.Any, belief), w);
            Check("첫 평가에서 클리어 금지", !Complete(sys, w));

            guard.SetBelief(c1, BeliefState.Doubtful);
            Check("무관한 NPC 판단으로는 클리어 금지", !Complete(sys, w));

            captain.SetBelief(c2, BeliefState.NeedsVerification);   // 대상 NPC가 새 카드를 판단
            Check("대상 NPC가 새로 판단하면 클리어", Complete(sys, w));

            // 값이 그대로여도 "다시 판단했다"는 사실 자체가 새 진척이어야 한다.
            var w2 = new World();
            var cap2 = w2.Add(Npc("npc_guard_captain"), Loc("LOC_GUARD_POST"));
            cap2.SetBelief(c1, BeliefState.NeedsVerification);
            var b2 = Cond<NpcAnyBeliefReachedCondition>();
            b2.targetNpc = cap2.Data; b2.thresholdState = BeliefState.NeedsVerification; b2.atOrBelow = true;
            var sys2 = Begin(Mission(MissionClearMode.Any, b2), w2);
            Check("같은 값 재판단 전에는 클리어 금지", !Complete(sys2, w2));
            cap2.SetBelief(c1, BeliefState.NeedsVerification);
            Check("같은 값이어도 재판단하면 클리어", Complete(sys2, w2));

            // 특정 카드를 지목하는 조건은 그 카드의 판단만 인정해야 한다.
            var w3 = new World();
            var cap3 = w3.Add(Npc("npc_guard_captain"), Loc("LOC_GUARD_POST"));
            cap3.SetBelief(c1, BeliefState.Doubtful);
            var b3 = Cond<NpcBeliefReachedCondition>();
            b3.targetNpc = cap3.Data; b3.referenceCard = c1;
            b3.thresholdState = BeliefState.Doubtful; b3.atOrBelow = true;
            var sys3 = Begin(Mission(MissionClearMode.Any, b3), w3);
            Check("특정 카드 조건 - 첫 평가 클리어 금지", !Complete(sys3, w3));
            cap3.SetBelief(c2, BeliefState.Doubtful);
            Check("다른 카드 판단으로는 클리어 금지", !Complete(sys3, w3));
            cap3.SetBelief(c1, BeliefState.Doubtful);
            Check("지목된 카드를 재판단하면 클리어", Complete(sys3, w3));
        }

        // ── D. 시작부터 조사 기록 존재 ───────────────────────────────────────────

        static void SectionD()
        {
            sb.AppendLine();
            sb.AppendLine("### D. 시작부터 조사 기록 존재");

            var w = new World();
            var inn = Loc("LOC_INN");
            var innkeeper = w.Add(Npc("npc_major_innkeeper"), inn);
            var other = w.Add(Npc("npc_major_priest"), inn);
            var info = Card("C-PUB-01").information;
            var locState = w.Locations[inn];

            var existing = new InformationWorldState(info, info != null ? info.categoryId : null, inn,
                innkeeper.Data, InformationResultType.Investigating, 1);
            locState.InvestigationStates.Add(existing);

            var cond = Cond<InformationWorldStateCondition>();
            cond.targetLocation = inn;
            cond.requiredActor = innkeeper.Data;
            cond.requiredResultType = InformationResultType.Investigating;

            var sys = Begin(Mission(MissionClearMode.Any, cond), w);
            Check("첫 평가에서 클리어 금지", !Complete(sys, w));

            locState.InvestigationStates.Add(new InformationWorldState(info, info != null ? info.categoryId : null,
                inn, other.Data, InformationResultType.Investigating, 2));
            Check("다른 행위자의 새 기록으로는 클리어 금지", !Complete(sys, w));

            existing.Refresh(2);   // 같은 (장소·행위자·결과) 기록이 다시 갱신됨
            Check("해당 기록이 갱신되면 클리어", Complete(sys, w));
        }

        // ── E. 시작부터 확산 조건 만족 ───────────────────────────────────────────

        static void SectionE()
        {
            sb.AppendLine();
            sb.AppendLine("### E. 시작부터 확산 조건 만족");

            var w = new World();
            var market = Loc("LOC_MARKET_SQUARE");
            var inn = Loc("LOC_INN");
            w.Ensure(market); w.Ensure(inn);
            var card = Card("C-PUB-01");
            var info = card.information;

            var existing = new RumorState(info, card, market, null, 1); // propagator=null = 플레이어 확산
            w.Locations[market].ActiveRumors.Add(existing);

            var cond = Cond<LocationRumorActiveCondition>();
            cond.targetLocation = market;

            var sys = Begin(Mission(MissionClearMode.Any, cond), w);
            Check("첫 평가에서 클리어 금지", !Complete(sys, w));

            w.Locations[inn].ActiveRumors.Add(new RumorState(info, card, inn, null, 2));
            Check("다른 장소 확산으로는 클리어 금지", !Complete(sys, w));

            existing.Refresh(2);
            Check("해당 장소에 새로 확산되면 클리어", Complete(sys, w));
        }

        // ── F. 재시작 ────────────────────────────────────────────────────────────

        static void SectionF()
        {
            sb.AppendLine();
            sb.AppendLine("### F. 재시작 - 이전 시도의 변화는 인정하지 않음");

            var w = new World();
            var post = Loc("LOC_GUARD_POST");
            var front = Loc("LOC_MANOR_FRONT");
            var captain = w.Add(Npc("npc_guard_captain"), post);
            w.Ensure(front);

            var at = Cond<NpcAtLocationCondition>();
            at.targetNpc = captain.Data;
            at.targetLocation = front;

            var mission = Mission(MissionClearMode.Any, at);
            var sys = Begin(mission, w);
            Check("시작 시 미충족", !Complete(sys, w));

            var snapshot = captain.CaptureSnapshot();   // 시도 시작 시점 스냅샷
            w.Move(captain, front);
            Check("시도 중 조건 성립", Complete(sys, w));

            // 재시작: 상태 복원 → 새 기준점
            captain.RestoreSnapshot(snapshot);
            w.Locations[front].PresentNpcs.Remove(captain);
            w.Locations[post].PresentNpcs.Add(captain);
            int before = sys.Baseline.AttemptId;
            sys.BeginAttempt(w.Ctx);
            Check("새 attemptId 발급", sys.Baseline.AttemptId == before + 1,
                $"{before} → {sys.Baseline.AttemptId}");
            Check("복원으로 위치 되돌아감", captain.CurrentLocation == post, captain.CurrentLocation.locationId);
            Check("복원 후 클리어 아님", !Complete(sys, w));

            // 이전 시도가 남긴 높은 스탬프가 새 시도에서 새 진척으로 잘못 인정되지 않아야 한다.
            Check("복원으로 이동 스탬프도 되돌아감", captain.LocationChangeStamp <= sys.Baseline.StartStamp,
                $"stamp={captain.LocationChangeStamp} baseline={sys.Baseline.StartStamp}");

            w.Move(captain, front);
            Check("새 시도에서 다시 달성하면 클리어", Complete(sys, w));

            // 얕은 복사 교정 확인 - 시도 중 Refresh된 조사 기록이 복원으로 되돌아가는가
            var loc = w.Locations[post];
            var info = Card("C-PUB-01").information;
            loc.InvestigationStates.Add(new InformationWorldState(info, info != null ? info.categoryId : null,
                post, captain.Data, InformationResultType.Investigating, 1));
            var locSnap = loc.CaptureSnapshot();
            long stampBefore = loc.InvestigationStates[0].LastChangedStamp;
            loc.InvestigationStates[0].Refresh(3);
            Check("Refresh가 스냅샷을 오염시키지 않음",
                locSnap.InvestigationStates[0].LastChangedStamp == stampBefore,
                $"snap={locSnap.InvestigationStates[0].LastChangedStamp} live={loc.InvestigationStates[0].LastChangedStamp}");
            loc.RestoreSnapshot(locSnap);
            Check("복원으로 Refresh가 되돌아감", loc.InvestigationStates[0].LastChangedStamp == stampBefore,
                loc.InvestigationStates[0].LastChangedStamp.ToString());
        }

        // ── G. 직렬화 왕복(Save/Load 대체) ───────────────────────────────────────

        static void SectionG()
        {
            sb.AppendLine();
            sb.AppendLine("### G. 스탬프 직렬화 왕복 (세이브 시스템 부재로 축소 검증)");

            var w = new World();
            var captain = w.Add(Npc("npc_guard_captain"), Loc("LOC_GUARD_POST"));
            w.Move(captain, Loc("LOC_MANOR_FRONT"));

            long stamp = captain.LocationChangeStamp;
            string json = JsonUtility.ToJson(new StampHolder { Stamp = stamp });
            long round = JsonUtility.FromJson<StampHolder>(json).Stamp;

            Check("스탬프가 long으로 왕복 보존", round == stamp, $"{stamp} → {round}");
            Check("스탬프가 단조 증가", WorldChangeClock.Next() > stamp);
            sb.AppendLine("  - 주: 이 프로젝트에는 세이브/로드 시스템이 없어 G는 직렬화 보존 확인으로 축소했다.");
        }

        [System.Serializable] class StampHolder { public long Stamp; }

        // ── H. 무회귀 ────────────────────────────────────────────────────────────

        static void SectionH()
        {
            sb.AppendLine();
            sb.AppendLine("### H. 무회귀");

            // H-1. 처음부터 미충족인 미션은 기존과 동일하게 동작
            {
                var w = new World();
                var post = Loc("LOC_GUARD_POST");
                var front = Loc("LOC_MANOR_FRONT");
                var captain = w.Add(Npc("npc_guard_captain"), post);
                w.Ensure(front);
                var at = Cond<NpcAtLocationCondition>();
                at.targetNpc = captain.Data; at.targetLocation = front;

                var sys = Begin(Mission(MissionClearMode.Any, at), w);
                Check("H-1 시작 미충족 → 클리어 아님", !Complete(sys, w));
                w.Move(captain, front);
                Check("H-1 미션 안에서 달성 → 정상 클리어", Complete(sys, w));
            }

            // H-2. clearMode=Any - 한 조건은 시작부터 만족(무효), 다른 조건이 새로 성립하면 성공
            {
                var w = new World();
                var post = Loc("LOC_GUARD_POST");
                var front = Loc("LOC_MANOR_FRONT");
                var captain = w.Add(Npc("npc_guard_captain"), front);   // 이미 front
                var steward = w.Add(Npc("npc_major_steward"), post);
                var stale = Cond<NpcAtLocationCondition>();
                stale.targetNpc = captain.Data; stale.targetLocation = front;   // 시작부터 만족
                var fresh = Cond<NpcAtLocationCondition>();
                fresh.targetNpc = steward.Data; fresh.targetLocation = front;   // 미충족

                var sys = Begin(Mission(MissionClearMode.Any, stale, fresh), w);
                Check("H-2 시작 직후 클리어 금지", !Complete(sys, w));
                w.Move(steward, front);
                Check("H-2 다른 조건이 새로 성립하면 성공", Complete(sys, w));
            }

            // H-3. clearMode=All - 하나라도 무효면 성공하지 않는다
            {
                var w = new World();
                var post = Loc("LOC_GUARD_POST");
                var front = Loc("LOC_MANOR_FRONT");
                var captain = w.Add(Npc("npc_guard_captain"), front);
                var steward = w.Add(Npc("npc_major_steward"), post);
                var stale = Cond<NpcAtLocationCondition>();
                stale.targetNpc = captain.Data; stale.targetLocation = front;
                var other = Cond<NpcAtLocationCondition>();
                other.targetNpc = steward.Data; other.targetLocation = front;

                var sys = Begin(Mission(MissionClearMode.All, stale, other), w);
                w.Move(steward, front);
                Check("H-3 All - 시작부터 만족한 조건이 섞이면 성공 금지", !Complete(sys, w));
                w.Move(captain, post); w.Move(captain, front);   // 그 조건도 새로 성립시키면
                Check("H-3 All - 전부 새로 성립하면 성공", Complete(sys, w));
            }

            // H-4. baseline이 없으면 기존 동작 그대로
            {
                var w = new World();
                var front = Loc("LOC_MANOR_FRONT");
                var captain = w.Add(Npc("npc_guard_captain"), front);
                var at = Cond<NpcAtLocationCondition>();
                at.targetNpc = captain.Data; at.targetLocation = front;
                var mission = Mission(MissionClearMode.Any, at);

                var sys = new MissionSystem(mission, new GameEventBus());   // BeginAttempt 미호출
                sys.Evaluate(w.Ctx);
                Check("H-4 baseline 없음 → 기존 동작(즉시 완료)", sys.State.IsComplete);
                Check("H-4 필터 없는 GetSuccessProgress도 그대로",
                    mission.GetSuccessProgress(w.Ctx) >= mission.SuccessTarget);
            }

            // H-5. 실패 조건은 신선도 게이트를 타지 않는다
            {
                var w = new World();
                var front = Loc("LOC_MANOR_FRONT");
                var captain = w.Add(Npc("npc_guard_captain"), front);
                var at = Cond<NpcAtLocationCondition>();
                at.targetNpc = captain.Data; at.targetLocation = front;
                var mission = Mission(MissionClearMode.Any, Cond<NpcAtLocationCondition>());
                mission.failureConditions = new MissionConditionData[] { at };

                Check("H-5 실패 조건은 즉시 판정됨", mission.IsAnyFailureConditionMet(w.Ctx));
            }

            // H-6. 모르는 조건 종류는 기존 동작으로 통과시킨다
            {
                var w = new World();
                var front = Loc("LOC_MANOR_FRONT");
                w.Add(Npc("npc_guard_captain"), front);
                var unknown = Cond<AlwaysTrueTestCondition>();
                var sys = Begin(Mission(MissionClearMode.Any, unknown), w);
                Check("H-6 미지의 조건은 막지 않음", Complete(sys, w));
            }
        }

        /// <summary>MissionFreshnessEvaluator가 알지 못하는 조건 종류가 진행을 막지 않는지 확인하기
        /// 위한 테스트 전용 조건. 에셋으로 만들지 않는다.</summary>
        class AlwaysTrueTestCondition : MissionConditionData
        {
            public override int GetCurrentProgress(MissionEvaluationContext context) => TargetCount;
        }
    }
}
