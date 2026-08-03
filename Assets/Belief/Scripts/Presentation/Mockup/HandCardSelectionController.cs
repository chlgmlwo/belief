using UnityEngine;

namespace Belief.Presentation.Mockup
{
    /// <summary>UI_PlayHudMockup 하단 손패 카드 4장의 선택 상태를 한 곳에서 관리한다 - 항상 최대
    /// 한 장만 상승 상태를 유지하고, 나머지 기능(전달/장소/NPC 선택)은 다루지 않는다.</summary>
    public class HandCardSelectionController : MonoBehaviour
    {
        [SerializeField] HandCardMockupView[] cards;

        [SerializeField] float expandedYOffset = 230f;
        [SerializeField] float animationDuration = 0.25f;
        [SerializeField] float selectedScale = 1.03f;

        HandCardMockupView selectedCard;

        void Awake()
        {
            foreach (var card in cards)
            {
                if (card == null) continue;
                card.Clicked += HandleCardClicked;
            }
        }

        void Start()
        {
            // 모든 카드가 각자 Awake에서 현재 위치를 캡처한 뒤(Unity의 Awake→Start 실행 순서 보장) 공용
            // 파라미터를 배분한다 - 스크립트 실행 순서에 의존하지 않는다.
            foreach (var card in cards)
            {
                if (card == null) continue;
                card.Configure(expandedYOffset, animationDuration, selectedScale);
            }
        }

        void OnDestroy()
        {
            foreach (var card in cards)
            {
                if (card != null) card.Clicked -= HandleCardClicked;
            }
        }

        void HandleCardClicked(HandCardMockupView card)
        {
            if (selectedCard == card)
            {
                card.SetSelected(false);
                selectedCard = null;
                return;
            }

            if (selectedCard != null) selectedCard.SetSelected(false);

            card.SetSelected(true);
            selectedCard = card;
        }
    }
}
