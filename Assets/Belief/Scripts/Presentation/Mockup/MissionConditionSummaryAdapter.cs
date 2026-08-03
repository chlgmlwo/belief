using System.Text;
using TMPro;
using UnityEngine;

namespace Belief.Presentation.Mockup
{
    /// <summary>HudPresenter가 missionConditionsRoot 아래에 동적으로 쌓아 올리는 실제
    /// MissionConditionRowView_Mockup 클론들(수정 없음)을 읽어, "GOAL 카드"와 별개로 옆에 있는
    /// 요약 패널(MissionSummaryCard)에 조건 목록을 불릿 텍스트로 다시 보여준다. 실제 데이터/판정
    /// 로직은 전혀 새로 만들지 않고, 이미 각 카드에 채워진 설명 텍스트를 그대로 읽어 합칠 뿐이다.</summary>
    public class MissionConditionSummaryAdapter : MonoBehaviour
    {
        [SerializeField] Transform conditionsRoot;
        [SerializeField] TMP_Text summaryText;
        [SerializeField] GameObject connectorGo;

        // 조건 개수는 많아야 서너 개라 매 프레임 다시 조합해도 비용이 거의 없다 - 변경 감지 없이
        // 항상 새로 읽는다(HudPresenter.RefreshMission이 갱신 시점을 알려주는 이벤트를 노출하지
        // 않으므로 폴링이 가장 안전하다).
        //
        // RefreshMission()은 ClearMissionConditionRows()로 먼저 전부 지운 뒤에야 다시 채우므로,
        // 그 사이의 한두 프레임은 conditionsRoot가 일시적으로 비어 있을 수 있다(실측: 미션 전환/재판정
        // 시점에 실제로 관측됨). 그 순간에 요약을 비워버리면 화면이 깜빡이므로, 조건이 하나라도 있을
        // 때만 다시 조합하고 없으면 마지막으로 보여준 내용을 그대로 유지한다.
        void LateUpdate()
        {
            if (conditionsRoot == null || summaryText == null) return;
            if (conditionsRoot.childCount == 0) return;

            // 목업 원본의 "Goal UI 연결 이미지"(카드 사이 클립 장식) - 카드가 2장 이상 쌓였을 때만
            // 보인다(목업도 정확히 GoalCard01/02 두 장 사이에만 있었다). Connector 자신도
            // conditionsRoot의 자식이라 childCount에 항상 +1 섞여 들어가므로 그만큼 빼고 센다.
            if (connectorGo != null)
            {
                int realConditionCount = conditionsRoot.childCount - (connectorGo.transform.parent == conditionsRoot ? 1 : 0);
                connectorGo.SetActive(realConditionCount >= 2);
            }

            var sb = new StringBuilder();
            for (int i = conditionsRoot.childCount - 1; i >= 0; i--)
            {
                // AddMissionConditionRow가 매번 SetSiblingIndex(0)으로 새 카드를 앞으로 밀어 넣으므로
                // sibling 순서는 최신이 0번 - 원래 조건 순서(먼저 추가된 것부터)로 읽으려면 뒤에서부터.
                var desc = conditionsRoot.GetChild(i).Find("Texts/Description")?.GetComponent<TMP_Text>();
                if (desc == null || string.IsNullOrEmpty(desc.text)) continue;
                sb.AppendLine($"• {desc.text}");
            }

            summaryText.text = sb.ToString();
        }
    }
}
