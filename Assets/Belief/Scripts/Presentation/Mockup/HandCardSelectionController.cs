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

        // 클릭 구독은 더 이상 이 컨트롤러가 직접 하지 않는다 - 실제 게임 데이터 연동판(HandCard1~4에
        // 붙는 MockupCardTileAdapter)에서는 클릭이 먼저 실제 카드 선택 시스템을 통과해야 하므로,
        // 이 컨트롤러는 그 결과만 아래 public 메서드로 전달받아 시각 상태만 담당한다
        // (카드 상승/하강, 선택 카드 교체, Expanded Y Offset/Duration/Scale은 기존 그대로).

        /// <summary>card를 펼쳐진 카드로 확정한다 - 기존에 펼쳐져 있던 다른 카드가 있으면 먼저 접는다.</summary>
        public void SetSelectedCard(HandCardMockupView card)
        {
            if (card == null || selectedCard == card) return;
            if (selectedCard != null) selectedCard.SetSelected(false);
            card.SetSelected(true);
            selectedCard = card;
        }

        /// <summary>현재 펼쳐진 카드가 있으면 접는다(어떤 카드인지는 신경 쓰지 않는다) -
        /// 같은 카드 재클릭으로 접는 경우와 선택된 카드가 소비된 경우 모두에 쓰인다.</summary>
        public void CollapseSelectedVisual()
        {
            if (selectedCard == null) return;
            selectedCard.SetSelected(false);
            selectedCard = null;
        }

        public void ClearSelectionVisual() => CollapseSelectedVisual();
    }
}
