using TMPro;
using UnityEngine;
using Belief.Core;

namespace Belief.Presentation.Mockup
{
    /// <summary>StageCard의 "StageName" 자리에 실제 구역 이름(StageData.regionName, 예:
    /// "북문(외곽)")을 보여준다. HudPresenter.RefreshHeader()가 원래 채우던 StageData.stageName은
    /// 이 스테이지 데이터에서 비어 있어(값이 없는 데이터 문제, 코드 버그 아님) 대신 regionName을
    /// 읽는다 - HudPresenter.cs는 건드리지 않고, HudView.stageNameText는 이 화면에서 비워둔 채
    /// 이 어댑터가 같은 텍스트 오브젝트를 직접 채운다.
    ///
    /// 이름 길이가 스테이지마다 크게 달라(7자 "북문(외곽)" ~ 13자 "귀족가 · 영주 저택 인근")
    /// 고정 폭 태그로는 긴 이름이 두 줄로 접히면서 아트 밖으로 삐져나왔다. 그래서 이름을 넣을 때마다
    /// <b>태그 폭을 글자 폭에 맞춰 다시 잡는다</b> - 오른쪽 끝은 고정하고 왼쪽으로만 자라게 해서
    /// 옆의 Turn 카드와의 겹침 관계를 시안 그대로 유지한다.</summary>
    public class StageRegionNameAdapter : MonoBehaviour
    {
        [SerializeField] TMP_Text targetText;

        /// <summary>왼쪽 여백 - 시안 기본값(카드 폭 147, 글자 상자 120 -> 한쪽 13.5) 그대로.</summary>
        [SerializeField] float leftPadding = 13.5f;
        /// <summary>오른쪽 여백은 왼쪽보다 넓다 - 시안상 Turn 카드가 이 태그의 오른쪽 끝 15px를 덮고
        /// 있어서(태그 우변 1757 vs Turn 카드 좌변 1742), 좌우를 같게 주면 이름 끝 글자가 Turn 카드
        /// 밑으로 들어간다. 겹치는 15px + 여유 7px = 22.</summary>
        [SerializeField] float rightPadding = 22f;
        /// <summary>이름이 짧아도 이 폭 아래로는 줄이지 않는다 - 위쪽 "STAGE n" 줄이 들어갈 최소 폭이라
        /// 이보다 좁아지면 그쪽이 삐져나온다(시안 기본 폭 147).</summary>
        [SerializeField] float minWidth = 147f;

        GameInstaller installer;
        string lastRegionName;

        RectTransform cardRect;
        /// <summary>카드의 오른쪽 끝 x - Awake에서 한 번만 기록해 두고, 폭이 바뀌어도 이 값을 유지한다.</summary>
        float rightEdgeX;
        bool captured;

        void Awake() => Capture();

        void Capture()
        {
            if (captured) return;
            cardRect = transform as RectTransform;
            if (cardRect == null) return;
            // pivot이 (0,1)이라 anchoredPosition.x가 곧 왼쪽 끝이다.
            rightEdgeX = cardRect.anchoredPosition.x + cardRect.rect.width;
            captured = true;
        }

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
            FitCardToName();
        }

        /// <summary>글자가 실제로 차지하는 폭을 재서 태그를 그만큼 넓힌다. 줄바꿈을 꺼야 preferredWidth가
        /// "한 줄로 폈을 때의 폭"을 돌려준다 - 켜져 있으면 상자 폭에 맞춰 접힌 뒤의 값이 나와서
        /// 아무리 재도 태그가 안 넓어진다.</summary>
        void FitCardToName()
        {
            Capture();
            if (cardRect == null) return;

            targetText.enableWordWrapping = false;
            targetText.ForceMeshUpdate();

            float width = Mathf.Max(minWidth, targetText.preferredWidth + leftPadding + rightPadding);
            cardRect.sizeDelta = new Vector2(width, cardRect.sizeDelta.y);
            cardRect.anchoredPosition = new Vector2(rightEdgeX - width, cardRect.anchoredPosition.y);
        }
    }
}
