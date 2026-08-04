using TMPro;
using UnityEngine;

namespace Belief.Presentation.HUD
{
    /// <summary>NPC 관계도 한 줄 - 시안(`[배치가이드] 플레이 화면 UI 각이드 _ 프로필.jpg`)의
    /// 3열 표 구조를 그대로 따른다: 관계 대상 | 관계 유형 | 반응 차이. 각 열은 작은 헤더 라벨과
    /// 그 아래 값 텍스트로 이루어지고, 열 사이는 세로 구분선으로 나뉜다.
    ///
    /// 레이아웃 컴포넌트(VerticalLayoutGroup/ContentSizeFitter)를 일부러 쓰지 않는다 - 배경 아트의
    /// 회색 박스 3칸이 서로 다른 간격(114/95px)으로 그려져 있어 균일 간격 레이아웃으로는 맞출 수
    /// 없고, 무엇보다 이 패널은 슬라이드 애니메이션이 끝난 뒤에야 활성화되는 구조라 "비활성 상태에서
    /// Bind → 레이아웃이 폭을 못 잡음 → 나중에 활성화돼도 TMP 메시가 다시 안 그려짐" 버그가
    /// 반복해서 발생했다(2026-08-05, HANDOFF 3-68/3-70). 모든 열 위치·크기를 고정값으로 두면
    /// 레이아웃 시스템 자체에 의존하지 않으므로 그 버그 부류가 구조적으로 사라진다.</summary>
    public class NpcRelationshipRowView : MonoBehaviour
    {
        [SerializeField] TMP_Text targetText;
        [SerializeField] TMP_Text typeText;
        [SerializeField] TMP_Text diffText;

        public void Bind(string target, string type, string diff)
        {
            if (targetText != null) targetText.text = target;
            if (typeText != null) typeText.text = type;
            if (diffText != null) diffText.text = diff;
        }
    }
}
