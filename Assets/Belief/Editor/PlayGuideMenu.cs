using UnityEditor;
using UnityEngine;
using Belief.Presentation.HUD;

namespace Belief.EditorTools
{
    /// <summary>플레이 가이드는 한 번 보고 나면 PlayerPrefs에 표시가 남아 다시 뜨지 않는다 - 확인하려면
    /// 매번 그 표시를 지워야 하는데, 키 이름을 기억해 두었다가 손으로 지우는 것은 실수하기 쉽다.</summary>
    public static class PlayGuideMenu
    {
        [MenuItem("Belief/가이드/플레이 가이드 다시 보기", priority = 30)]
        public static void ResetPlayGuide()
        {
            PlayerPrefs.DeleteKey(PlayGuideOverlay.CompletedPrefKey);
            PlayerPrefs.Save();
            Debug.Log("[Belief] 플레이 가이드 완료 표시를 지웠다 - 1구역에 들어가면 다시 뜬다.");
        }

        [MenuItem("Belief/가이드/현재 상태 보기", priority = 31)]
        public static void ShowPlayGuideState()
        {
            int done = PlayerPrefs.GetInt(PlayGuideOverlay.CompletedPrefKey, 0);
            Debug.Log($"[Belief] 플레이 가이드 완료 표시 = {done} ({(done == 1 ? "다시 안 뜸" : "다음 1구역 진입에 뜸")})");
        }
    }
}
