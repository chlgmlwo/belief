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

        /// <summary>지금 있는 구역을 가리키는 붉은 핀. 구역을 깰 때마다 <b>지우는 게 아니라 다음
        /// 지점으로 옮긴다</b> - 예전에는 이 핀이 1구역 자리에 못 박혀 있고 대신 검은 마크가 뒤에서부터
        /// 하나씩 사라져서, 지도가 진행 방향과 반대로 줄어드는 것처럼 보였다.</summary>
        [SerializeField] RectTransform currentStageMarker;

        /// <summary>핀 위의 "STAGE N" 글자 - 핀의 자식이 아니라 형제라, 핀을 옮길 때 같은 만큼 함께
        /// 옮겨 준다.</summary>
        [SerializeField] RectTransform mapStageLabelRect;

        /// <summary>아직 가지 않은 구역의 검은 마크 - <b>경로 순서대로</b> 2·3·4구역에 대응한다
        /// (하이어라키 이름 순서가 아니다: 실제 경로는 현재핀 → Marker2 → Marker1 → Marker3).</summary>
        [SerializeField] RectTransform[] lockedMarkers;

        /// <summary>구간별 점선 묶음 - 묶음 L은 L구역에서 L+1구역으로 가는 길이다. 이미 지나온
        /// 구간은 통째로 끈다.</summary>
        [SerializeField] GameObject[] dashLegs;

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

        /// <summary>붉은 핀의 <b>출발 자리</b>(=1구역). 핀은 옮겨 다니므로 한 번 옮기고 나면 이 값을
        /// 다시 잴 수 없다 - 처음 그릴 때 한 번만 기록해 둔다. 브리핑 화면은 구역마다 새로 만들어지는
        /// 인스턴스라 여기 담기는 값은 항상 프리팹의 원래 자리다.</summary>
        Vector2 firstStageCenter;
        bool firstStageCaptured;

        /// <summary>지도 위 진행 상태를 그린다.
        ///
        /// 경로는 1구역(붉은 핀의 출발 자리) → 2 → 3 → 4구역 순이고, <paramref name="currentIndex"/>가
        /// 지금 어디까지 왔는지다. 규칙은 세 줄뿐이다:
        /// <list type="bullet">
        /// <item>붉은 핀은 현재 구역 자리로 <b>옮긴다</b>(지우지 않는다).</item>
        /// <item>검은 마크는 <b>아직 안 간 구역</b>에만 남긴다 - 핀이 선 자리와 이미 지나온 자리는 지운다.</item>
        /// <item>점선은 <b>앞으로 갈 구간</b>만 남긴다 - 지나온 길은 더 볼 일이 없다.</item>
        /// </list></summary>
        public void BindMap(int currentIndex, Sprite currentIcon, Sprite lockedIcon)
        {
            if (currentStageMarker == null) return;

            if (!firstStageCaptured)
            {
                firstStageCenter = CenterOf(currentStageMarker);
                firstStageCaptured = true;
            }

            var currentIcon2 = currentStageMarker.GetComponent<Image>();
            if (currentIcon2 != null && currentIcon != null)
            {
                currentIcon2.sprite = currentIcon;
                currentIcon2.preserveAspect = true;
            }

            // 붉은 핀 옮기기 - 목적지 자리의 검은 마크와 중심을 맞춘다(핀과 마크는 크기가 달라
            // anchoredPosition을 그대로 베끼면 어긋난다).
            Vector2 destination = firstStageCenter;
            int lockedIndexUnderPin = currentIndex - 1; // 핀이 선 자리의 검은 마크(1구역이면 없음)
            if (lockedIndexUnderPin >= 0 && lockedMarkers != null && lockedIndexUnderPin < lockedMarkers.Length
                && lockedMarkers[lockedIndexUnderPin] != null)
                destination = CenterOf(lockedMarkers[lockedIndexUnderPin]);

            MoveCenterTo(currentStageMarker, destination, mapStageLabelRect);

            // 검은 마크: 배열 인덱스 j는 (j+2)구역이다. 아직 안 간 구역만 남긴다.
            if (lockedMarkers != null)
            {
                for (int j = 0; j < lockedMarkers.Length; j++)
                {
                    if (lockedMarkers[j] == null) continue;
                    lockedMarkers[j].gameObject.SetActive(j + 1 > currentIndex);

                    var img = lockedMarkers[j].GetComponent<Image>();
                    if (img != null && lockedIcon != null)
                    {
                        img.sprite = lockedIcon;
                        img.preserveAspect = true;
                    }
                }
            }

            // 점선: 묶음 L은 L구역 → L+1구역. 지나온 구간(L < currentIndex)은 끈다.
            if (dashLegs != null)
                for (int L = 0; L < dashLegs.Length; L++)
                    if (dashLegs[L] != null)
                        dashLegs[L].SetActive(L >= currentIndex);
        }

        static Vector2 CenterOf(RectTransform rt)
        {
            var size = rt.rect.size;
            return rt.anchoredPosition + new Vector2(size.x * (0.5f - rt.pivot.x), size.y * (0.5f - rt.pivot.y));
        }

        /// <summary>중심이 <paramref name="center"/>에 오도록 옮기고, 따라다녀야 하는 형제(글자)도
        /// 같은 변위만큼 함께 옮긴다.</summary>
        static void MoveCenterTo(RectTransform rt, Vector2 center, RectTransform follower)
        {
            Vector2 delta = center - CenterOf(rt);
            if (delta == Vector2.zero) return;
            rt.anchoredPosition += delta;
            if (follower != null) follower.anchoredPosition += delta;
        }
    }
}
