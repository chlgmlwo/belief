using System.Collections.Generic;
using UnityEngine;
using Belief.Core;
using Belief.Data;
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

        readonly List<InformationCardData> lastOwned = new List<InformationCardData>();
        readonly List<CardTileView> liveProxies = new List<CardTileView>();

        /// <summary>소멸 연출이 끝날 때까지 손패를 다시 칠하지 않는 시각(unscaled).</summary>
        float rebindSuspendedUntil;

        void LateUpdate()
        {
            if (installer == null || installer.Turns == null || proxyContainer == null) return;

            // 사용된 카드가 아래로 빠지는 동안에는 슬롯을 건드리지 않는다 - 지금 다시 칠하면
            // 떨어지는 카드가 이미 다음 카드의 내용으로 바뀌어 엉뚱한 카드가 떨어진다.
            if (Time.unscaledTime < rebindSuspendedUntil) return;

            var owned = installer.Turns.OwnedInformationCards;
            // 연출을 시작한 프레임에는 여기서 바로 빠진다 - 아래 CollapseSelectedVisual까지 흘러가면
            // 방금 시작한 낙하가 그 자리에서 취소된다.
            if (PlayConsumeForRemovedCard(owned)) return;

            var selectedCard = installer.Turns.SelectedCard;
            HandCardMockupView desiredExpanded = null;

            CollectLiveProxies();
            for (int i = 0; i < slots.Length; i++)
            {
                var proxy = i < liveProxies.Count ? liveProxies[i] : null;

                slots[i].AssignProxy(proxy);
                slots[i].MockupView.SetEmpty(proxy == null);
                if (proxy != null && slots[i].ComputeDesiredExpanded(selectedCard))
                    desiredExpanded = slots[i].MockupView;
            }

            if (desiredExpanded != null) handController.SetSelectedCard(desiredExpanded);
            else handController.CollapseSelectedVisual();
        }

        /// <summary>사라지는 중인 프록시는 건너뛴다. HudPresenter는 소멸 연출(0.2초) 동안 그 타일을
        /// 컨테이너에 그대로 두는데, 살아 있는 카드들이 앞쪽 인덱스로 밀리면서 죽어가는 타일이
        /// 마지막 자리로 온다 - 그 자리가 아직 SelectedCard인 카드를 물면 맨 오른쪽 카드가 혼자
        /// 솟았다가 내려앉는다.</summary>
        void CollectLiveProxies()
        {
            liveProxies.Clear();
            for (int i = 0; i < proxyContainer.childCount; i++)
            {
                var tile = proxyContainer.GetChild(i).GetComponent<CardTileView>();
                if (tile == null || tile.HandState == CardHandState.Removed) continue;
                liveProxies.Add(tile);
            }
        }

        /// <summary>직전 프레임까지 있던 카드가 손패에서 빠졌다면, 그 카드를 보여주던 슬롯에서
        /// 아래로 떨어지는 연출을 재생하고 그 시간만큼 재배치를 미룬다.</summary>
        bool PlayConsumeForRemovedCard(IReadOnlyList<InformationCardData> owned)
        {
            bool started = false;
            foreach (var prev in lastOwned)
            {
                // 매 프레임 도는 자리라 LINQ 대신 직접 훑는다(손패는 최대 4장이다).
                if (prev == null || Contains(owned, prev)) continue;

                foreach (var slot in slots)
                {
                    if (slot == null || !ReferenceEquals(slot.BoundCard, prev)) continue;
                    slot.MockupView.PlayConsumed();
                    rebindSuspendedUntil = Time.unscaledTime + HandCardMockupView.ConsumeDuration;
                    started = true;
                    break;
                }
                break;   // 한 턴에 사라지는 카드는 하나다
            }

            lastOwned.Clear();
            lastOwned.AddRange(owned);
            return started;
        }

        static bool Contains(IReadOnlyList<InformationCardData> list, InformationCardData card)
        {
            for (int i = 0; i < list.Count; i++)
                if (ReferenceEquals(list[i], card)) return true;
            return false;
        }
    }
}
