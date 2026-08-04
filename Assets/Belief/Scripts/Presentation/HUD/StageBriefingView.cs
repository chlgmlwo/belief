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
        /// <summary>시안의 큰 제목 자리 - 구역 이름(StageData.regionName)이 들어간다.</summary>
        [SerializeField] TMP_Text titleText;
        /// <summary>제목 바로 아래 어두운 강조 띠 위의 한 줄 - 구역 짧은 설명이 들어간다.</summary>
        [SerializeField] TMP_Text regionDescText;
        /// <summary>위 한 줄 뒤에 깔리는 어두운 강조 띠 - 시안에도 있는 요소라, 글자 길이에 맞춰
        /// 폭/높이를 런타임에 맞춰준다(고정 크기로 두면 짧은 문구엔 띠가 남고 긴 문구엔 글자가 삐져나온다).</summary>
        [SerializeField] RectTransform regionDescBgRect;
        [SerializeField] TMP_Text turnLimitValueText;

        /// <summary>갈색 메모지 위의 줄들 - 이 구역의 GOAL 제목을 위에서부터 채운다(스테이지4는 3개).
        /// 남는 줄은 비활성화하고, 쓰는 줄만 메모 중앙에 오도록 세로 위치를 다시 잡는다.</summary>
        [Header("Goal Memo")]
        [SerializeField] TMP_Text[] goalLineTexts;
        [SerializeField] Color goalLabelColor = new Color(0.55f, 0.23f, 0.23f);

        [Header("Map")]
        [SerializeField] TMP_Text mapStageLabelText;
        [SerializeField] MarkerSlot[] markerSlots;

        public CanvasGroup CanvasGroup => canvasGroup;
        public Button LaunchButton => launchButton;
        public Button BackButton => backButton;

        public void Bind(int stageNumber, string regionName, string regionDescription, int turnLimit)
        {
            if (stageLabelText != null) stageLabelText.text = $"STAGE {stageNumber}";
            if (mapStageLabelText != null) mapStageLabelText.text = $"STAGE {stageNumber}";
            if (titleText != null) titleText.text = regionName;
            if (turnLimitValueText != null) turnLimitValueText.text = turnLimit > 0 ? turnLimit.ToString() : "-";
            BindRegionDescription(regionDescription);
        }

        /// <summary>프리팹에 구워진 글자 상자 폭이 곧 "시안 기준 줄바꿈 폭"이다.</summary>
        float baseDescWidth;
        bool baseDescCaptured;

        void CaptureDescBaseline()
        {
            if (baseDescCaptured || regionDescText == null) return;
            baseDescWidth = regionDescText.rectTransform.sizeDelta.x;
            baseDescCaptured = true;
        }

        /// <summary>설명 줄과 그 뒤 강조 띠를 함께 맞춘다. 띠를 프리팹의 고정 크기(458×19)로 두면
        /// 짧은 문구엔 빈 띠가 남고, 무엇보다 **높이 19가 한글 글리프 높이 23.4보다 낮아 글자
        /// 아래쪽이 띠 밖(흰 종이 위)으로 나가 밝은 글씨가 안 보였다**. 그래서 띠를 실제로 그려진
        /// 글리프의 바운딩 박스에 맞춰 다시 잡는다 - 줄 수와 무관하게 한 번에 맞으므로 한 줄/여러 줄을
        /// 따로 처리하지 않는다. 설명이 비면 띠까지 숨긴다.</summary>
        void BindRegionDescription(string description)
        {
            if (regionDescText == null) return;
            CaptureDescBaseline();

            bool has = !string.IsNullOrWhiteSpace(description);
            regionDescText.gameObject.SetActive(has);
            if (regionDescBgRect != null) regionDescBgRect.gameObject.SetActive(has);
            if (!has) return;

            var textRect = regionDescText.rectTransform;
            regionDescText.text = description;

            // 재는 동안에는 높이를 넉넉히 준다. overflowMode가 Truncate라 상자가 한 줄 높이보다
            // 조금이라도 낮으면 TMP가 그 줄을 통째로 버려서 글자가 아예 안 보인다(프리팹에 구워진
            // 28은 fs 26의 실제 줄 높이 32.45보다 낮아 실제로 그렇게 사라졌다).
            textRect.sizeDelta = new Vector2(baseDescWidth, MeasureHeight);
            regionDescText.ForceMeshUpdate();
            textRect.sizeDelta = new Vector2(baseDescWidth, regionDescText.preferredHeight);

            if (regionDescBgRect == null) return;

            // TMP의 글리프 좌표는 글자 상자의 피벗(좌상단) 기준이고, 띠도 같은 앵커·피벗이라
            // 글자 상자 위치에 그대로 더하면 된다.
            var info = regionDescText.textInfo;
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            bool any = false;
            for (int i = 0; i < info.characterCount; i++)
            {
                var ci = info.characterInfo[i];
                if (!ci.isVisible) continue;
                any = true;
                minX = Mathf.Min(minX, ci.topLeft.x);
                maxX = Mathf.Max(maxX, ci.bottomRight.x);
                minY = Mathf.Min(minY, ci.bottomRight.y);
                maxY = Mathf.Max(maxY, ci.topLeft.y);
            }
            if (!any) { regionDescBgRect.gameObject.SetActive(false); return; }

            var textPos = textRect.anchoredPosition;
            regionDescBgRect.anchoredPosition = new Vector2(textPos.x + minX - BgPadX, textPos.y + maxY + BgPadY);
            regionDescBgRect.sizeDelta = new Vector2(maxX - minX + BgPadX * 2f, maxY - minY + BgPadY * 2f);
        }

        const float BgPadX = 5f;
        const float BgPadY = 3f;
        const float MeasureHeight = 1000f;

        /// <summary>메모지 줄 간격/중앙 정렬은 프리팹에 구워둔 슬롯 좌표에서 그대로 계산한다 -
        /// 슬롯 y 좌표가 등간격이라는 것만 가정하고, 실제 간격/중심은 매번 슬롯에서 읽는다.</summary>
        public void BindGoals(IList<string> goalTitles)
        {
            if (goalLineTexts == null || goalLineTexts.Length == 0) return;

            int count = goalTitles != null ? Mathf.Min(goalTitles.Count, goalLineTexts.Length) : 0;

            float first = goalLineTexts[0].rectTransform.anchoredPosition.y;
            float last = goalLineTexts[goalLineTexts.Length - 1].rectTransform.anchoredPosition.y;
            float spacing = goalLineTexts.Length > 1 ? (first - last) / (goalLineTexts.Length - 1) : 0f;
            float center = (first + last) * 0.5f;
            float top = center + spacing * (count - 1) * 0.5f;

            string labelHex = ColorUtility.ToHtmlStringRGB(goalLabelColor);
            for (int i = 0; i < goalLineTexts.Length; i++)
            {
                var line = goalLineTexts[i];
                if (line == null) continue;

                bool active = i < count;
                line.gameObject.SetActive(active);
                if (!active) continue;

                line.text = $"<color=#{labelHex}>GOAL {i + 1}</color>   {goalTitles[i]}";
                var rect = line.rectTransform;
                rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, top - spacing * i);
            }
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
