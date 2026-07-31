using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using Belief.Data.NPC;

namespace Belief.Systems.NPC
{
    /// <summary>StreamingAssets/Belief/NPC 아래의 Profile/RuntimeInitial JSON을 읽는다.
    /// StreamingAssets는 플랫폼에 따라(Android 등) File.ReadAllText로 읽을 수 없으므로
    /// UnityWebRequest 기반 비동기 로딩을 기본으로 한다(Editor/Windows에서도 동일하게 동작).
    /// Profile 목록은 코드에 하드코딩하지 않고 npc_profile_manifest.json을 통해 얻는다.</summary>
    public class NpcJsonLoader
    {
        const string ProfileManifestFileName = "npc_profile_manifest.json";
        const string RuntimeInitialFileName = "npc_runtime_initial.json";

        public static string ProfileDirectory => Application.streamingAssetsPath + "/Belief/NPC/Profile";
        public static string RuntimeInitialDirectory => Application.streamingAssetsPath + "/Belief/NPC/RuntimeInitial";
        public static string RuntimeInitialFilePath => RuntimeInitialDirectory + "/" + RuntimeInitialFileName;

        public async Task<List<NpcProfileDto>> LoadAllProfilesAsync()
        {
            var result = new List<NpcProfileDto>();

            string manifestPath = $"{ProfileDirectory}/{ProfileManifestFileName}";
            string manifestJson = await ReadTextAsync(manifestPath);
            if (manifestJson == null)
            {
                Debug.LogError($"[NpcJsonLoader] Profile manifest를 찾을 수 없습니다: {manifestPath}");
                return result;
            }

            var manifest = JsonUtility.FromJson<NpcProfileManifestDto>(manifestJson);
            if (manifest?.profileFiles == null)
            {
                Debug.LogError($"[NpcJsonLoader] Profile manifest 파싱 실패: {manifestPath}");
                return result;
            }

            foreach (var fileName in manifest.profileFiles)
            {
                string path = $"{ProfileDirectory}/{fileName}";
                string json = await ReadTextAsync(path);
                if (json == null)
                {
                    Debug.LogError($"[NpcJsonLoader] Profile 파일을 읽을 수 없습니다: {path}");
                    continue;
                }

                NpcProfileDto dto = null;
                try { dto = JsonUtility.FromJson<NpcProfileDto>(json); }
                catch (System.Exception e) { Debug.LogError($"[NpcJsonLoader] Profile JSON 파싱 실패: {fileName} ({e.Message})"); }

                if (dto == null)
                {
                    Debug.LogError($"[NpcJsonLoader] Profile JSON 파싱 실패: {fileName}");
                    continue;
                }
                result.Add(dto);
            }

            return result;
        }

        public async Task<NpcRuntimeDatabaseDto> LoadInitialRuntimeDatabaseAsync()
        {
            string json = await ReadTextAsync(RuntimeInitialFilePath);
            if (json == null)
            {
                Debug.LogError($"[NpcJsonLoader] RuntimeInitial 파일을 찾을 수 없습니다: {RuntimeInitialFilePath}");
                return null;
            }

            try
            {
                return JsonUtility.FromJson<NpcRuntimeDatabaseDto>(json);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[NpcJsonLoader] RuntimeInitial JSON 파싱 실패: {e.Message}");
                return null;
            }
        }

        async Task<string> ReadTextAsync(string path)
        {
            string url = path.Contains("://") ? path : "file://" + path;
            using (var request = UnityWebRequest.Get(url))
            {
                var op = request.SendWebRequest();
                while (!op.isDone) await Task.Yield();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"[NpcJsonLoader] 파일 로드 실패: {path} ({request.error})");
                    return null;
                }
                return request.downloadHandler.text;
            }
        }
    }
}
