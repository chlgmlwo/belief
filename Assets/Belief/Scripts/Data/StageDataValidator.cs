using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Belief.Data
{
    public enum StageValidationSeverity { Error, Warning }

    public readonly struct StageValidationIssue
    {
        public readonly StageValidationSeverity Severity;
        public readonly string Message;

        public StageValidationIssue(StageValidationSeverity severity, string message)
        {
            Severity = severity;
            Message = message;
        }
    }

    /// <summary>
    /// StageData 에셋 하나에 대한 최소한의 데이터 정합성 검사. 미션 클리어/벨리프 같은 실제 판정
    /// 로직에는 전혀 관여하지 않고, 데이터 입력 실수를 컴파일 타임이 아니라 에셋 검사 시점에 잡아내는
    /// 것만 목적으로 한다. MissionConditionData 하위 타입은 리플렉션으로 훑어 NpcData 참조를 찾으므로,
    /// 새 조건 타입이 추가되어도 이 파일을 수정할 필요가 없다.
    /// </summary>
    public static class StageDataValidator
    {
        public static List<StageValidationIssue> Validate(StageData stage)
        {
            var issues = new List<StageValidationIssue>();
            if (stage == null)
            {
                issues.Add(Error("StageData가 null입니다."));
                return issues;
            }

            CheckNullReferences(stage, issues);
            CheckDuplicateIds(stage, issues);
            CheckStartLocations(stage, issues);
            CheckMissionTargetNpcs(stage, issues);
            CheckCardDeliveryTargetMismatch(stage, issues);
            CheckMissionTurnLimits(stage, issues);

            return issues;
        }

        /// <summary>Validate 결과를 콘솔에 출력하는 공용 헬퍼. Editor 버튼과 런타임(GameInstaller.Awake)이
        /// 같은 출력 형식을 쓰도록 한 곳에 모아둔다 - 중복 구현 방지.</summary>
        public static void LogIssues(StageData stage, List<StageValidationIssue> issues)
        {
            var label = stage != null ? stage.name : "(null)";
            if (issues.Count == 0)
            {
                Debug.Log($"[StageData] '{label}' 검증 통과 - 문제 없음.");
                return;
            }

            foreach (var issue in issues)
            {
                if (issue.Severity == StageValidationSeverity.Error)
                    Debug.LogError($"[StageData] {label}: {issue.Message}");
                else
                    Debug.LogWarning($"[StageData] {label}: {issue.Message}");
            }
        }

        static void CheckNullReferences(StageData stage, List<StageValidationIssue> issues)
        {
            if (stage.locations == null || stage.locations.Length == 0)
                issues.Add(Error("locations가 비어 있습니다."));
            else
                for (int i = 0; i < stage.locations.Length; i++)
                    if (stage.locations[i] == null)
                        issues.Add(Error($"locations[{i}]가 null입니다."));

            if (stage.npcPlacements == null || stage.npcPlacements.Length == 0)
                issues.Add(Error("npcPlacements가 비어 있습니다."));
            else
                for (int i = 0; i < stage.npcPlacements.Length; i++)
                {
                    var p = stage.npcPlacements[i];
                    if (p.npc == null)
                    {
                        issues.Add(Error($"npcPlacements[{i}].npc가 null입니다."));
                        continue;
                    }
                    if (p.initialBeliefs == null) continue;
                    for (int j = 0; j < p.initialBeliefs.Length; j++)
                        if (p.initialBeliefs[j].card == null)
                            issues.Add(Error($"npcPlacements[{i}]({p.npc.npcId}).initialBeliefs[{j}].card가 null입니다."));
                }

            if (stage.cardPool == null)
                issues.Add(Error("cardPool이 null입니다."));

            if (stage.missions == null || stage.missions.Length == 0)
                issues.Add(Error("missions가 비어 있습니다."));
            else
                for (int i = 0; i < stage.missions.Length; i++)
                    if (stage.missions[i] == null)
                        issues.Add(Error($"missions[{i}]가 null입니다."));

            if (stage.startMission == null)
                issues.Add(Error("startMission이 null입니다."));
            else if (stage.missions != null && Array.IndexOf(stage.missions, stage.startMission) < 0)
                issues.Add(Error("startMission이 missions 목록에 포함되어 있지 않습니다."));
        }

        static void CheckDuplicateIds(StageData stage, List<StageValidationIssue> issues)
        {
            CheckDuplicates(stage.locations, l => l != null ? l.locationId : null, "locationId", issues);
            CheckDuplicates(stage.npcPlacements, p => p.npc != null ? p.npc.npcId : null, "npcId", issues);
            CheckDuplicates(stage.missions, m => m != null ? m.missionId : null, "missionId", issues);
            if (stage.cardPool != null && stage.cardPool.cards != null)
                CheckDuplicates(stage.cardPool.cards, c => c != null ? c.cardId : null, "cardId", issues);
        }

        static void CheckDuplicates<T>(T[] items, Func<T, string> idSelector, string label, List<StageValidationIssue> issues)
        {
            if (items == null) return;
            var seen = new HashSet<string>();
            foreach (var item in items)
            {
                var id = idSelector(item);
                if (string.IsNullOrEmpty(id)) continue;
                if (!seen.Add(id))
                    issues.Add(Error($"{label} 중복: {id}"));
            }
        }

        static void CheckStartLocations(StageData stage, List<StageValidationIssue> issues)
        {
            if (stage.npcPlacements == null) return;
            var locationSet = new HashSet<LocationData>();
            if (stage.locations != null)
                foreach (var l in stage.locations)
                    if (l != null) locationSet.Add(l);

            foreach (var p in stage.npcPlacements)
            {
                if (p.npc == null) continue;
                var effective = p.EffectiveStartLocation;
                if (effective == null)
                    issues.Add(Error($"{p.npc.npcId}: 시작 장소가 없습니다(startLocation 미지정 + homeLocation도 없음)."));
                else if (!locationSet.Contains(effective))
                    issues.Add(Error($"{p.npc.npcId}: 시작 장소 '{effective.locationId}'가 이 스테이지의 locations 목록에 없습니다."));
            }
        }

        static void CheckMissionTargetNpcs(StageData stage, List<StageValidationIssue> issues)
        {
            if (stage.missions == null) return;
            var npcSet = new HashSet<NpcData>();
            if (stage.npcPlacements != null)
                foreach (var p in stage.npcPlacements)
                    if (p.npc != null) npcSet.Add(p.npc);

            foreach (var mission in stage.missions)
            {
                if (mission == null) continue;
                foreach (var condition in AllConditions(mission.successConditions))
                    foreach (var npc in ReferencedNpcs(condition))
                        if (!npcSet.Contains(npc))
                            issues.Add(Error($"{mission.missionId}: 조건 '{condition.name}'이 참조하는 NPC '{npc.npcId}'가 이 스테이지의 npcPlacements에 없습니다."));

                foreach (var condition in AllConditions(mission.failureConditions))
                    foreach (var npc in ReferencedNpcs(condition))
                        if (!npcSet.Contains(npc))
                            issues.Add(Error($"{mission.missionId}: 조건 '{condition.name}'이 참조하는 NPC '{npc.npcId}'가 이 스테이지의 npcPlacements에 없습니다."));
            }
        }

        static void CheckCardDeliveryTargetMismatch(StageData stage, List<StageValidationIssue> issues)
        {
            if (stage.missions == null) return;

            var spreadInformation = new HashSet<InformationData>();
            var spreadCategories = new HashSet<string>();
            if (stage.cardPool != null && stage.cardPool.cards != null)
                foreach (var card in stage.cardPool.cards)
                {
                    if (card == null || card.cardType != InfoCardType.Spread || card.information == null) continue;
                    spreadInformation.Add(card.information);
                    if (!string.IsNullOrEmpty(card.information.categoryId))
                        spreadCategories.Add(card.information.categoryId);
                }

            foreach (var mission in stage.missions)
            {
                if (mission == null) continue;
                foreach (var condition in AllConditions(mission.successConditions))
                    CheckRumorCondition(mission, condition, spreadInformation, spreadCategories, issues);
                foreach (var condition in AllConditions(mission.failureConditions))
                    CheckRumorCondition(mission, condition, spreadInformation, spreadCategories, issues);
            }
        }

        static void CheckRumorCondition(MissionData mission, MissionConditionData condition,
            HashSet<InformationData> spreadInformation, HashSet<string> spreadCategories, List<StageValidationIssue> issues)
        {
            if (!(condition is LocationRumorActiveCondition rumor)) return;

            // RumorState(ActiveRumors)는 ExposeCardAtLocation(=SPREAD 카드 재생/재확산) 경로로만 생성된다
            // (InfoDeliverySystem 참고) - 이 조건이 요구하는 정보/카테고리를 SPREAD 카드로 전달할 방법이
            // 이 스테이지의 cardPool에 전혀 없다면, DELIVER 전용 카드만 있는 등 "카드 전달 타입과 대상
            // 타입 불일치"로 이 조건은 영원히 충족될 수 없다.
            if (rumor.requiredInformation != null && !spreadInformation.Contains(rumor.requiredInformation))
                issues.Add(Error($"{mission.missionId}: 조건 '{rumor.name}'이 요구하는 정보를 SPREAD 타입 카드로 전달할 방법이 cardPool에 없습니다(카드 전달 타입과 대상 타입 불일치)."));

            if (rumor.requiredInformation == null && !string.IsNullOrEmpty(rumor.requiredCategoryId) &&
                !spreadCategories.Contains(rumor.requiredCategoryId))
                issues.Add(Error($"{mission.missionId}: 조건 '{rumor.name}'이 요구하는 카테고리 '{rumor.requiredCategoryId}'를 SPREAD 타입 카드로 전달할 방법이 cardPool에 없습니다(카드 전달 타입과 대상 타입 불일치)."));
        }

        static void CheckMissionTurnLimits(StageData stage, List<StageValidationIssue> issues)
        {
            if (stage.missions == null) return;
            foreach (var mission in stage.missions)
            {
                if (mission == null) continue;
                if (mission.turnLimit < 0)
                    issues.Add(Error($"{mission.missionId}: turnLimit이 음수입니다({mission.turnLimit})."));
                else if (mission.turnLimit > 0 && mission.turnLimit > stage.maxTurns)
                    issues.Add(Error($"{mission.missionId}: turnLimit({mission.turnLimit})이 스테이지 maxTurns({stage.maxTurns})보다 큽니다."));
            }
        }

        static IEnumerable<MissionConditionData> AllConditions(MissionConditionData[] roots)
        {
            if (roots == null) yield break;
            foreach (var root in roots)
                foreach (var c in AllConditions(root))
                    yield return c;
        }

        static IEnumerable<MissionConditionData> AllConditions(MissionConditionData root)
        {
            if (root == null) yield break;
            yield return root;

            foreach (var field in root.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (field.FieldType != typeof(MissionConditionData[])) continue;
                if (!(field.GetValue(root) is MissionConditionData[] arr)) continue;
                foreach (var sub in arr)
                    foreach (var nested in AllConditions(sub))
                        yield return nested;
            }
        }

        static IEnumerable<NpcData> ReferencedNpcs(MissionConditionData condition)
        {
            foreach (var field in condition.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (field.FieldType == typeof(NpcData))
                {
                    if (field.GetValue(condition) is NpcData single && single != null)
                        yield return single;
                }
                else if (field.FieldType == typeof(NpcData[]))
                {
                    if (!(field.GetValue(condition) is NpcData[] arr)) continue;
                    foreach (var n in arr)
                        if (n != null) yield return n;
                }
            }
        }

        static StageValidationIssue Error(string message) => new StageValidationIssue(StageValidationSeverity.Error, message);
    }
}
