using UnityEngine;
using Belief.Core;
using Belief.Presentation.HUD;

namespace Belief.Presentation.Mockup
{
    /// <summary>보이는 HandCard1~4(각각 MockupCardTileAdapter)와, 화면 밖 ProxyContainer 아래
    /// 기존 HudPresenter가 관리하는(수정 없음) CardTileView 인스턴스들을 매 프레임 인덱스로
    /// 재연결한다. 실제 선택 상태(GameInstaller.Turns.SelectedCard)는 여기서 절대 직접 바꾸지
    /// 않는다 - 읽기만 하고, 실제 선택 요청은 각 어댑터가 프록시의 Button.onClick을 통해서만
    /// 전달한다(HandCardHudBridge -> MockupCardTileAdapter -> 프록시 -> HudPresenter.OnCardClicked
    /// 순으로, 어느 방향으로도 되돌아 덮어쓰지 않는 단일 경로).</summary>
    public class HandCardHudBridge : MonoBehaviour
    {
        [SerializeField] Transform proxyContainer;
        [SerializeField] MockupCardTileAdapter[] slots;
        [SerializeField] HandCardSelectionController handController;

        GameInstaller installer;

        void Start()
        {
            installer = FindFirstObjectByType<GameInstaller>();
        }

        void LateUpdate()
        {
            if (installer == null || installer.Turns == null || proxyContainer == null) return;

            var selectedCard = installer.Turns.SelectedCard;
            HandCardMockupView desiredExpanded = null;

            for (int i = 0; i < slots.Length; i++)
            {
                var proxy = i < proxyContainer.childCount
                    ? proxyContainer.GetChild(i).GetComponent<CardTileView>()
                    : null;

                slots[i].AssignProxy(proxy);
                if (slots[i].ComputeDesiredExpanded(selectedCard))
                    desiredExpanded = slots[i].MockupView;
            }

            if (desiredExpanded != null) handController.SetSelectedCard(desiredExpanded);
            else handController.CollapseSelectedVisual();
        }
    }
}
