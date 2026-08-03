using TMPro;
using UnityEngine;
using Belief.Core;

namespace Belief.Presentation.Mockup
{
    /// <summary>목업 StageCard의 "StageName" 자리에 실제 구역 이름(StageData.regionName, 예:
    /// "북문(외곽)")을 보여준다. HudPresenter.RefreshHeader()가 원래 채우던 StageData.stageName은
    /// 이 스테이지 데이터에서 비어 있어(값이 없는 데이터 문제, 코드 버그 아님) 대신 regionName을
    /// 읽는다 - HudPresenter.cs는 건드리지 않고, HudView.stageNameText는 이 화면에서 비워둔 채
    /// 이 어댑터가 같은 텍스트 오브젝트를 직접 채운다.</summary>
    public class StageRegionNameAdapter : MonoBehaviour
    {
        [SerializeField] TMP_Text targetText;

        GameInstaller installer;
        string lastRegionName;

        void Start()
        {
            installer = FindFirstObjectByType<GameInstaller>();
        }

        void LateUpdate()
        {
            if (installer == null || targetText == null) return;

            var stageAsset = installer.StageAsset;
            string regionName = stageAsset != null ? stageAsset.regionName : "";
            if (regionName == lastRegionName) return;

            lastRegionName = regionName;
            targetText.text = regionName;
        }
    }
}
