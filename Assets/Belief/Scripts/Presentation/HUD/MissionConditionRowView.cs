using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Belief.Presentation.HUD
{
    /// <summary>미션 조건 한 줄(Goal 카드 스프라이트 배경 + 라벨 + 성공 배지)의 View - CardTileView와
    /// 같은 패턴. 카드 배경 스프라이트는 슬롯 순서로 3종 순환 배정되므로(HudPresenter가 skin에서
    /// 직접 고름) 이 View는 자기 배경/배지 Image를 노출만 하고, 값 대입은 Presenter가 한다.</summary>
    public class MissionConditionRowView : MonoBehaviour
    {
        [SerializeField] Image background;
        [SerializeField] TMP_Text goalTag;
        [SerializeField] TMP_Text titleText;
        [SerializeField] TMP_Text label;
        [SerializeField] GameObject successBadgeGo;
        [SerializeField] Image badgeImage;

        public Image Background => background;
        public Image BadgeImage => badgeImage;

        /// <summary>showBadge=false(배지 스프라이트가 없는 경우)면 배지를 완전히 숨기고, 호출부가
        /// labelText에 이미 "[X]/[ ] " ASCII 폴백을 붙였다고 가정한다(HudPresenter의 기존 fallback
        /// 로직을 그대로 유지 - 이 View는 배지 유무만 토글할 뿐 폴백 문구 자체를 만들지 않는다).
        /// goalNumber는 카드 우상단 "GOAL N" 라벨, missionTitle은 카드 공통 상단 제목(가이드의
        /// Goal 카드 스택은 같은 미션의 조건마다 카드 한 장씩, 제목은 공유하고 조건 설명만 다름).</summary>
        public void Bind(int goalNumber, string missionTitle, string labelText, bool showBadge, bool met)
        {
            if (goalTag != null) goalTag.text = $"GOAL {goalNumber}";
            if (titleText != null) titleText.text = missionTitle;
            if (label != null) label.text = labelText;
            if (successBadgeGo != null) successBadgeGo.SetActive(showBadge && met);
        }

        /// <summary>스택 뒤쪽 카드는 앞 카드와 겹치는 영역(카드 아트 모서리가 불투명하게 완전히
        /// 덮지 못하는 부분)에서 GoalTag가 비쳐 보여 앞 카드의 태그와 겹쳐 "라벨이 두 번 보이는"
        /// 것처럼 읽히는 문제가 있었다 - 맨 앞 카드(슬롯 0)만 GOAL 태그를 표시해 스택 전체가
        /// 하나의 덩어리로 읽히게 한다(뒤 카드는 제목/설명만 살짝 보이는 페이지 넘김처럼 유지).</summary>
        public void SetGoalTagVisible(bool visible)
        {
            if (goalTag != null) goalTag.gameObject.SetActive(visible);
        }
    }
}
