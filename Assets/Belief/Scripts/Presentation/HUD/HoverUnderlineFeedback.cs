using UnityEngine;
using UnityEngine.EventSystems;

namespace Belief.Presentation.HUD
{
    /// <summary>버튼/링크 텍스트 위에 커서가 올라가면 그 아래 빨간 밑줄(skin.launchButtonHoverUnderline)만
    /// 켜고, 벗어나면 끈다 - MainMenu의 ButtonHoverFeedback(배경색 틴트)과는 다른 연출이라 별도 컴포넌트로
    /// 분리한다. 버튼 자체의 클릭/상호작용 로직은 전혀 건드리지 않는다.</summary>
    public class HoverUnderlineFeedback : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField] GameObject underline;

        void Awake()
        {
            if (underline != null) underline.SetActive(false);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (underline != null) underline.SetActive(true);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (underline != null) underline.SetActive(false);
        }
    }
}
