using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Belief.Presentation.HUD
{
    /// <summary>StageBriefingCanvas.prefab(에디터에서 직접 배치된 하이어라키)의 자식 참조 테이블 -
    /// CardTileView.cs와 동일한 패턴. 폰트/크기/색/앵커 같은 정적 스타일은 전부 프리팹 자체에 이미
    /// 구워져 있으므로, 여기서는 런타임에 실제로 바뀌어야 하는 값(텍스트 내용, 미니맵 진행 상태)만
    /// Bind로 채워 넣는다. StageBriefingPresenter가 Instantiate 직후 호출한다.</summary>
    public class StageBriefingView : MonoBehaviour
    {
        /// <summary>각 슬롯은 지도 위 고정된 위치/크기에 미리 배치되어 있다(가이드 배치 기준 실측) -
        /// 슬롯0(현재 스테이지)은 크게, 슬롯1~3(잠김)은 작게, 전부 프리팹에 이미 구워져 있으므로
        /// BindMap은 스프라이트/이름/활성 여부만 바꾼다.</summary>
        [System.Serializable]
        public class MarkerSlot
        {
            public GameObject root;
            public Image icon;
            public TMP_Text nameText;
        }

        static readonly Color MutedText = new Color(0.72f, 0.78f, 0.74f);

        [SerializeField] CanvasGroup canvasGroup;
        [SerializeField] Button launchButton;
        [SerializeField] Button backButton;

        [Header("Text")]
        [SerializeField] TMP_Text stageLabelText;
        [SerializeField] TMP_Text titleText;
        [SerializeField] TMP_Text objectiveText;
        [SerializeField] TMP_Text turnLimitValueText;
        [SerializeField] TMP_Text blurbText;

        [Header("Map")]
        [SerializeField] TMP_Text mapStageLabelText;
        [SerializeField] MarkerSlot[] markerSlots;

        public CanvasGroup CanvasGroup => canvasGroup;
        public Button LaunchButton => launchButton;
        public Button BackButton => backButton;

        public void Bind(int stageNumber, string title, string objective, int turnLimit, string blurb)
        {
            if (stageLabelText != null) stageLabelText.text = $"STAGE {stageNumber}";
            if (mapStageLabelText != null) mapStageLabelText.text = $"STAGE {stageNumber}";
            if (titleText != null) titleText.text = title;
            if (objectiveText != null) objectiveText.text = objective;
            if (turnLimitValueText != null) turnLimitValueText.text = turnLimit > 0 ? turnLimit.ToString() : "-";
            if (blurbText != null) blurbText.text = blurb;
        }

        /// <summary>슬롯 0은 항상 현재 스테이지, 이후 슬롯은 잠긴 다음 스테이지들 - 원래 코드의
        /// "currentIndex부터 끝까지" 루프와 동일한 순서. remainingStageCount를 넘는 슬롯은 비활성화한다
        /// (전체 스테이지가 4개 고정이라 슬롯도 4개 고정 배치 - 5번째 스테이지가 생기면 슬롯을 추가해야 함).</summary>
        public void BindMap(string currentStageName, int remainingStageCount, Sprite currentIcon, Sprite lockedIcon)
        {
            if (markerSlots == null) return;
            for (int i = 0; i < markerSlots.Length; i++)
            {
                var slot = markerSlots[i];
                if (slot?.root == null) continue;

                bool active = i < remainingStageCount;
                slot.root.SetActive(active);
                if (!active) continue;

                bool isCurrent = i == 0;
                if (slot.icon != null)
                {
                    slot.icon.sprite = isCurrent ? currentIcon : lockedIcon;
                    slot.icon.preserveAspect = true;
                }
                if (slot.nameText != null)
                {
                    slot.nameText.text = isCurrent ? currentStageName : "???";
                    slot.nameText.color = isCurrent ? Color.white : MutedText;
                }
            }
        }
    }
}
