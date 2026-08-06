using System;
using System.Collections.Generic;
using UnityEngine;

namespace Belief.Core
{
    /// <summary>오토세이브 1슬롯의 저장 형식. 필드를 늘릴 때는 <see cref="AutoSaveService.Version"/>도
    /// 함께 올린다 - 옛 저장본은 불러오지 않고 버린다(진행이 조금 되돌아가는 게, 절반만 복원된
    /// 상태로 계속하는 것보다 낫다).</summary>
    [Serializable]
    public class AutoSaveDto
    {
        public int version;
        /// <summary>재진입 시 로드할 씬. 인덱스가 아니라 이름으로 들고 있는다 - ProgressionData의
        /// 스테이지 순서가 바뀌어도 저장본이 엉뚱한 구역을 가리키지 않는다.</summary>
        public string stageSceneName;
        public bool metropolisUnlocked;
        public string[] completedStageIds;
        public string[] completedMissionIds;

        /// <summary>저장 시점의 세계 상태(NPC 위치·믿음, 장소 소문·조사 기록). 없으면 그 구역을
        /// 초기 상태로 시작한다 - 옛 저장본과의 호환이 아니라, 세계를 담을 수 없었던 경우의 폴백이다.</summary>
        public WorldSaveDto world;

        /// <summary>표시/디버깅용. 복원 판단에는 쓰지 않는다.</summary>
        public string savedAtUtc;
    }

    /// <summary>
    /// 미션 단위 체크포인트 오토세이브. 저장하는 것은 <see cref="GameProgressState"/>가 들고 있는
    /// "어디까지 깼는가"뿐이고, 진행 중이던 미션의 턴/손패/NPC 상태는 저장하지 않는다 - 재진입하면
    /// 그 구역으로 들어가 현재 미션을 처음부터 다시 시작한다.
    ///
    /// 저장 위치는 PlayerPrefs다. WebGL에서도 IndexedDB로 그대로 동작해서 빌드별 분기가 필요 없고,
    /// 이 정도 크기(문자열 몇 개)에는 파일 IO를 쓸 이유가 없다.
    /// </summary>
    public static class AutoSaveService
    {
        /// <summary>2 - 세계 상태(world)가 추가되었다. 진행만 담던 v1 저장본은 불러오지 않는다.</summary>
        public const int Version = 2;
        const string Key = "belief.autosave.v1";

        public static bool HasSave => PlayerPrefs.HasKey(Key);

        public static void Save(GameProgressState progress, string stageSceneName, WorldSaveDto world)
        {
            if (progress == null || string.IsNullOrEmpty(stageSceneName)) return;

            var dto = new AutoSaveDto
            {
                version = Version,
                stageSceneName = stageSceneName,
                metropolisUnlocked = progress.MetropolisUnlocked,
                completedStageIds = ToArray(progress.CompletedStageIds),
                completedMissionIds = ToArray(progress.CompletedMissionIds),
                world = world,
                savedAtUtc = DateTime.UtcNow.ToString("o"),
            };

            PlayerPrefs.SetString(Key, JsonUtility.ToJson(dto));
            // WebGL은 Save()를 불러 줘야 IndexedDB에 실제로 반영된다 - 탭을 그냥 닫으면 사라진다.
            PlayerPrefs.Save();
        }

        /// <summary>저장본이 없거나, 형식이 깨졌거나, 버전이 다르면 false. 깨진 저장본은 여기서
        /// 지워서 다음 실행부터는 "저장본 없음"으로 깔끔하게 시작하게 한다.</summary>
        public static bool TryLoad(out AutoSaveDto dto)
        {
            dto = null;
            if (!HasSave) return false;

            try { dto = JsonUtility.FromJson<AutoSaveDto>(PlayerPrefs.GetString(Key)); }
            catch (Exception e)
            {
                Debug.LogWarning($"[AutoSave] 저장본을 읽지 못해 폐기합니다: {e.Message}");
                Clear();
                return false;
            }

            if (dto == null || dto.version != Version || string.IsNullOrEmpty(dto.stageSceneName))
            {
                Debug.LogWarning("[AutoSave] 저장본 형식/버전이 맞지 않아 폐기합니다.");
                Clear();
                dto = null;
                return false;
            }

            return true;
        }

        public static void Clear()
        {
            PlayerPrefs.DeleteKey(Key);
            PlayerPrefs.Save();
        }

        static string[] ToArray(HashSet<string> set)
        {
            var arr = new string[set.Count];
            set.CopyTo(arr);
            return arr;
        }
    }
}
