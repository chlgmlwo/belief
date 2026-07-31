using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Belief.Data.NPC;

namespace Belief.EditorTools
{
    /// <summary>NPC JSON 파일을 표준 폴더 구조(Assets/StreamingAssets/Belief/NPC/...)로 정리하고,
    /// 정리된 데이터의 정합성을 검증하는 에디터 전용 도구. 실행 결과가 반복 실행해도 달라지지
    /// 않는 멱등 구조다 - 이미 올바른 위치에 있는 파일은 건드리지 않는다.</summary>
    public static class NpcJsonProjectOrganizer
    {
        const string RootDir = "Assets/StreamingAssets/Belief/NPC";
        const string ProfileDir = RootDir + "/Profile";
        const string RuntimeInitialDir = RootDir + "/RuntimeInitial";
        const string RuntimeIndividualsDir = RuntimeInitialDir + "/Individuals";
        const string ManifestFileName = "npc_profile_manifest.json";
        const string RuntimeInitialFileName = "npc_runtime_initial.json";

        static readonly Regex StageIdPattern = new Regex(@"^STAGE_\d+$");
        static readonly Regex LocationIdPattern = new Regex(@"^LOC_[A-Z0-9_]+$");

        [MenuItem("Tools/Belief/NPC/Organize NPC JSON Files")]
        public static void OrganizeNpcJsonFiles()
        {
            EnsureFolder(RootDir);
            EnsureFolder(ProfileDir);
            EnsureFolder(RuntimeInitialDir);
            EnsureFolder(RuntimeIndividualsDir);

            int moved = 0, skipped = 0, errors = 0;

            var jsonPaths = FindAllJsonAssetPaths();
            foreach (var path in jsonPaths)
            {
                string fileName = Path.GetFileName(path);
                string targetDir = ClassifyTargetDirectory(fileName);
                if (targetDir == null) continue; // NPC JSON이 아님 - 건드리지 않는다.

                string currentDir = Path.GetDirectoryName(path)?.Replace('\\', '/');
                if (currentDir == targetDir)
                {
                    skipped++;
                    continue;
                }

                string targetPath = $"{targetDir}/{fileName}";
                if (AssetDatabase.LoadAssetAtPath<Object>(targetPath) != null)
                {
                    Debug.LogWarning($"[NpcJsonProjectOrganizer] 대상 경로에 이미 파일이 있어 건너뜁니다: {targetPath} (원본: {path})");
                    skipped++;
                    continue;
                }

                string moveError = AssetDatabase.MoveAsset(path, targetPath);
                if (!string.IsNullOrEmpty(moveError))
                {
                    Debug.LogError($"[NpcJsonProjectOrganizer] 이동 실패: {path} -> {targetPath} ({moveError})");
                    errors++;
                    continue;
                }

                moved++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            int manifestCount = RegenerateManifest();

            Debug.Log($"[NpcJsonProjectOrganizer] 정리 완료 - 이동 {moved}건, 건너뜀 {skipped}건, 오류 {errors}건. " +
                $"manifest에 {manifestCount}개 Profile 파일 등록.");
        }

        /// <summary>AssetDatabase.FindAssets의 검색 인덱스는 같은 에디터 세션 안에서 방금
        /// MoveAsset으로 옮겨진/새로 생긴 파일을 곧바로 반영하지 못하는 경우가 실측으로 확인되어
        /// (Refresh 직후에도 갱신되지 않음), 탐색 자체는 파일 시스템을 직접 순회해 신뢰성을
        /// 확보한다. 실제 이동 동작은 아래에서 AssetDatabase.MoveAsset만 사용한다.</summary>
        static IEnumerable<string> FindAllJsonAssetPaths()
        {
            string assetsRoot = Application.dataPath;
            string projectRoot = Directory.GetParent(assetsRoot).FullName;

            return Directory.GetFiles(assetsRoot, "*.json", SearchOption.AllDirectories)
                .Select(fullPath => Path.GetRelativePath(projectRoot, fullPath).Replace('\\', '/'));
        }

        /// <summary>파일명 규칙으로 목적지 폴더를 정한다. NPC JSON이 아니면 null.</summary>
        static string ClassifyTargetDirectory(string fileName)
        {
            if (fileName == ManifestFileName) return ProfileDir;
            if (fileName == RuntimeInitialFileName) return RuntimeInitialDir;
            if (fileName.EndsWith("_Runtime.json")) return RuntimeIndividualsDir;
            if (fileName.StartsWith("NPC_") && !fileName.Contains("_Runtime")) return ProfileDir;
            return null;
        }

        static void EnsureFolder(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath)) return;

            string parent = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            string folderName = Path.GetFileName(assetPath);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);

            AssetDatabase.CreateFolder(parent, folderName);
        }

        /// <summary>Profile 폴더의 실제 내용을 기준으로 manifest를 다시 생성한다 - 항상 최신 상태를
        /// 반영하므로 반복 실행해도 동일한 결과가 나온다(멱등).</summary>
        static int RegenerateManifest()
        {
            string fullProfileDir = Path.Combine(Directory.GetCurrentDirectory(), ProfileDir);
            var files = Directory.Exists(fullProfileDir)
                ? Directory.GetFiles(fullProfileDir, "*.json")
                    .Select(Path.GetFileName)
                    .Where(f => f != ManifestFileName)
                    .OrderBy(f => f, System.StringComparer.Ordinal)
                    .ToList()
                : new List<string>();

            var manifest = new NpcProfileManifestDto { schemaVersion = 1, profileFiles = files };
            string json = JsonUtility.ToJson(manifest, true);

            string fullPath = Path.Combine(Directory.GetCurrentDirectory(), $"{ProfileDir}/{ManifestFileName}");
            File.WriteAllText(fullPath, json);

            AssetDatabase.ImportAsset($"{ProfileDir}/{ManifestFileName}");
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            return files.Count;
        }

        // ------------------------------------------------------------ Validate

        [MenuItem("Tools/Belief/NPC/Validate NPC JSON Data")]
        public static void ValidateNpcJsonData()
        {
            var issues = new List<string>();

            var profiles = LoadAllProfilesFromDisk(issues);
            var individualRuntimes = LoadIndividualRuntimesFromDisk(issues);
            var runtimeDatabase = LoadRuntimeDatabaseFromDisk(issues);

            if (profiles.Count != 16)
                issues.Add($"Profile 파일 수가 16개가 아닙니다: {profiles.Count}개");
            if (individualRuntimes.Count != 16)
                issues.Add($"개별 Runtime 파일 수가 16개가 아닙니다: {individualRuntimes.Count}개");

            var combinedStates = runtimeDatabase?.npcRuntimeStates ?? new List<NpcRuntimeDto>();
            if (combinedStates.Count != 16)
                issues.Add($"통합 Runtime 상태 수가 16개가 아닙니다: {combinedStates.Count}개");

            var profileIds = new HashSet<string>();
            foreach (var p in profiles)
            {
                if (string.IsNullOrEmpty(p.npcId)) { issues.Add("npcId가 비어 있는 Profile이 있습니다."); continue; }
                if (!profileIds.Add(p.npcId)) issues.Add($"Profile npcId 중복: {p.npcId}");

                if (string.IsNullOrEmpty(p.displayName)) issues.Add($"{p.npcId}: displayName이 비어 있습니다.");
                if (string.IsNullOrEmpty(p.stageId)) issues.Add($"{p.npcId}: stageId가 비어 있습니다.");
                else if (!StageIdPattern.IsMatch(p.stageId)) issues.Add($"{p.npcId}: stageId 형식이 올바르지 않습니다: {p.stageId}");

                string defaultLoc = p.basicInfo?.defaultLocationId;
                if (string.IsNullOrEmpty(defaultLoc)) issues.Add($"{p.npcId}: basicInfo.defaultLocationId가 비어 있습니다.");
                else if (!LocationIdPattern.IsMatch(defaultLoc)) issues.Add($"{p.npcId}: defaultLocationId 형식이 올바르지 않습니다: {defaultLoc}");
            }

            var combinedIds = new HashSet<string>();
            foreach (var r in combinedStates)
            {
                if (string.IsNullOrEmpty(r.npcId)) { issues.Add("npcId가 비어 있는 통합 Runtime 상태가 있습니다."); continue; }
                if (!combinedIds.Add(r.npcId)) issues.Add($"통합 Runtime npcId 중복: {r.npcId}");

                string loc = r.runtimeStatus?.currentLocationId;
                if (string.IsNullOrEmpty(loc)) issues.Add($"{r.npcId}: runtimeStatus.currentLocationId가 비어 있습니다.");
                else if (!LocationIdPattern.IsMatch(loc)) issues.Add($"{r.npcId}: currentLocationId 형식이 올바르지 않습니다: {loc}");
            }

            if (!profileIds.SetEquals(combinedIds))
            {
                var onlyProfile = profileIds.Except(combinedIds).ToList();
                var onlyRuntime = combinedIds.Except(profileIds).ToList();
                if (onlyProfile.Count > 0) issues.Add($"Profile에만 있고 Runtime에 없는 npcId: {string.Join(",", onlyProfile)}");
                if (onlyRuntime.Count > 0) issues.Add($"Runtime에만 있고 Profile에 없는 npcId: {string.Join(",", onlyRuntime)}");
            }

            var runtimeByNpcId = combinedStates.Where(r => !string.IsNullOrEmpty(r.npcId))
                .GroupBy(r => r.npcId).ToDictionary(g => g.Key, g => g.First());

            foreach (var p in profiles)
            {
                if (p.npcId == null || !runtimeByNpcId.TryGetValue(p.npcId, out var r)) continue;

                string profileLoc = p.basicInfo?.defaultLocationId;
                string runtimeLoc = r.runtimeStatus?.currentLocationId;
                if (profileLoc != runtimeLoc)
                    issues.Add($"{p.npcId}: 초기 위치 불일치 (profile={profileLoc}, runtime={runtimeLoc})");

                var profileBeliefIds = (p.initialBeliefs ?? new List<NpcInitialBeliefDto>())
                    .Select(b => b.informationId).OrderBy(x => x).ToList();
                var runtimeBeliefIds = (r.beliefStates ?? new List<NpcBeliefStateDto>())
                    .Select(b => b.informationId).OrderBy(x => x).ToList();
                if (!profileBeliefIds.SequenceEqual(runtimeBeliefIds))
                    issues.Add($"{p.npcId}: initialBeliefs informationId 집합 불일치");
                else if (p.initialBeliefs != null)
                {
                    foreach (var pb in p.initialBeliefs)
                    {
                        var rb = r.beliefStates.FirstOrDefault(b => b.informationId == pb.informationId);
                        if (rb != null && pb.initialLevel != rb.currentLevel)
                            issues.Add($"{p.npcId}: {pb.informationId} Level 불일치 (profile={pb.initialLevel}, runtime={rb.currentLevel})");
                    }
                }

                if (p.relationships != null)
                {
                    foreach (var rel in p.relationships)
                    {
                        if (rel.targetType == "Role") continue;
                        if (!profileIds.Contains(rel.targetId))
                            issues.Add($"{p.npcId}: relationships.targetId가 존재하지 않는 NPC를 참조합니다: {rel.targetId}");
                    }
                }
            }

            foreach (var r in combinedStates)
            {
                if (r.relationshipStates == null) continue;
                foreach (var rel in r.relationshipStates)
                {
                    if (rel.targetType == "Role") continue;
                    if (!combinedIds.Contains(rel.targetId))
                        issues.Add($"{r.npcId}: relationshipStates.targetId가 존재하지 않는 NPC를 참조합니다: {rel.targetId}");
                }
            }

            if (issues.Count == 0)
            {
                Debug.Log("[NPC Validation] Success — 16 profiles and 16 runtime states are valid.");
            }
            else
            {
                Debug.LogError($"[NPC Validation] Failed — {issues.Count}건의 문제가 발견되었습니다.");
                foreach (var issue in issues)
                    Debug.LogError($"[NPC Validation] {issue}");
            }
        }

        static List<NpcProfileDto> LoadAllProfilesFromDisk(List<string> issues)
        {
            var result = new List<NpcProfileDto>();
            if (!Directory.Exists(ProfileDir)) return result;

            foreach (var file in Directory.GetFiles(ProfileDir, "*.json"))
            {
                string fileName = Path.GetFileName(file);
                if (fileName == ManifestFileName) continue;

                string json = File.ReadAllText(file);
                NpcProfileDto dto = null;
                try { dto = JsonUtility.FromJson<NpcProfileDto>(json); }
                catch (System.Exception e) { issues.Add($"Profile JSON 파싱 실패: {fileName} ({e.Message})"); }

                if (dto == null) { issues.Add($"Profile JSON 파싱 실패: {fileName}"); continue; }
                result.Add(dto);
            }
            return result;
        }

        static List<NpcRuntimeDto> LoadIndividualRuntimesFromDisk(List<string> issues)
        {
            var result = new List<NpcRuntimeDto>();
            if (!Directory.Exists(RuntimeIndividualsDir)) return result;

            foreach (var file in Directory.GetFiles(RuntimeIndividualsDir, "*.json"))
            {
                string json = File.ReadAllText(file);
                NpcRuntimeDto dto = null;
                try { dto = JsonUtility.FromJson<NpcRuntimeDto>(json); }
                catch (System.Exception e) { issues.Add($"개별 Runtime JSON 파싱 실패: {Path.GetFileName(file)} ({e.Message})"); }

                if (dto == null) { issues.Add($"개별 Runtime JSON 파싱 실패: {Path.GetFileName(file)}"); continue; }
                result.Add(dto);
            }
            return result;
        }

        static NpcRuntimeDatabaseDto LoadRuntimeDatabaseFromDisk(List<string> issues)
        {
            string path = $"{RuntimeInitialDir}/{RuntimeInitialFileName}";
            if (!File.Exists(path))
            {
                issues.Add($"통합 Runtime 파일을 찾을 수 없습니다: {path}");
                return null;
            }

            string json = File.ReadAllText(path);
            try
            {
                return JsonUtility.FromJson<NpcRuntimeDatabaseDto>(json);
            }
            catch (System.Exception e)
            {
                issues.Add($"통합 Runtime JSON 파싱 실패: {e.Message}");
                return null;
            }
        }
    }
}
