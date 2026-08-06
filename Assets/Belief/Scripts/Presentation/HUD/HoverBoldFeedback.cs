using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Belief.Presentation.HUD
{
    /// <summary>커서가 올라가 있는 동안 버튼 글자를 더 굵은 폰트로 바꾸고, 벗어나면 원래 굵기로
    /// 되돌린다. MainMenu의 <see cref="MainMenu.ButtonHoverFeedback"/>(배경색 틴트),
    /// <see cref="HoverUnderlineFeedback"/>(밑줄)과 같은 자리의 형제 연출이라 별도 컴포넌트로 둔다.
    ///
    /// <b>굵기 전환에 TMP의 가짜 볼드(FontStyles.Bold)가 아니라 진짜 폰트 에셋을 쓴다.</b> 가짜 볼드는
    /// 글자를 부풀리는 방식이라 자간이 미세하게 늘어 글자가 흔들려 보인다. SUIT는 굵기별 자간/행높이가
    /// 완전히 같아서(전 굵기를 13~18pt로 실측한 결과 필요 높이가 32/35/37/40px로 동일) 에셋만 갈아끼우면
    /// 글자 위치가 1px도 안 움직인다. boldFont가 비어 있을 때만 가짜 볼드로 폴백한다.</summary>
    [RequireComponent(typeof(TMP_Text))]
    public class HoverBoldFeedback : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField] TMP_Text label;
        [SerializeField] TMP_FontAsset boldFont;

        TMP_FontAsset normalFont;
        FontStyles normalStyle;
        bool hovered;

        void Awake()
        {
            if (label == null) label = GetComponent<TMP_Text>();
            normalFont = label.font;
            normalStyle = label.fontStyle;
        }

        public void OnPointerEnter(PointerEventData eventData) => SetHovered(true);
        public void OnPointerExit(PointerEventData eventData) => SetHovered(false);

        /// <summary>커서 밑에 있는 채로 비활성화되면 OnPointerExit가 오지 않아 굵은 채로 굳는다.</summary>
        void OnDisable()
        {
            if (hovered) SetHovered(false);
        }

        void SetHovered(bool value)
        {
            if (hovered == value || label == null) return;
            hovered = value;

            if (boldFont != null)
            {
                label.font = value ? boldFont : normalFont;
                return;
            }

            label.fontStyle = value ? normalStyle | FontStyles.Bold : normalStyle;
        }
    }
}
