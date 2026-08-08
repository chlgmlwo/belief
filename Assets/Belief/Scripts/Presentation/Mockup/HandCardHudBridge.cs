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
        Belief.Presentation.TargetingController targeting;

        void Start()
        {
            installer = FindFirstObjectByType<GameInstaller>();
            // 손패를 눌러 둘지 판단하려고 읽기만 한다 - 이 브리지는 여전히 어떤 게임 상태도 바꾸지 않는다.
            targeting = FindFirstObjectByType<Belief.Presentation.TargetingController>();
        }

        readonly List<InformationCardData> lastOwned = new List<InformationCardData>();
        readonly List<CardTileView> liveProxies = new List<CardTileView>();

        /// <summary>소멸 연출이 끝날 때까지 손패를 다시 칠하지 않는 시각(unscaled).</summary>
        float rebindSuspendedUntil;

        void LateUpdate()
        {
            if (installer == null || installer.Turns == null || proxyContainer == null) return;

            // 잠금 표시는 아래 어떤 조기 반환보다도 먼저 건다 - 카드를 내면 곧바로 소멸 연출이
            // 시작되면서(그 동안 아래에서 return한다) 전달 처리도 함께 시작되므로, 뒤에 두면 정작
            // 잠긴 그 순간에 표시가 안 걸린다. SetLocked는 값이 바뀔 때만 실제로 움직인다.
            bool locked = targeting != null && targeting.IsDelivering;
            foreach (var slot in slots)
                if (slot != null) slot.MockupView.SetLocked(locked);

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

            // 갈아끼우기 전에 각 슬롯이 무엇을 보여주고 있었는지 찍어 둔다 - 새 내용이 "옆 슬롯에서
            // 온 것"인지 "새로 뽑힌 것"인지는 이 이전 상태와 비교해야만 알 수 있다.
            for (int i = 0; i < slots.Length; i++)
                previousSlotCards[i] = slots[i].BoundCard;

            for (int i = 0; i < slots.Length; i++)
            {
                var proxy = i < liveProxies.Count ? liveProxies[i] : null;

                slots[i].AssignProxy(proxy);
                slots[i].MockupView.SetEmpty(proxy == null);
                if (proxy != null && slots[i].ComputeDesiredExpanded(selectedCard))
                    desiredExpanded = slots[i].MockupView;
            }

            PlayEntryAnimations();

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

        readonly InformationCardData[] previousSlotCards = new InformationCardData[8];

        /// <summary>이번 갱신에서 내용이 바뀐 슬롯에 연출을 건다.
        /// - 직전에 <b>오른쪽 슬롯</b>에 있던 카드가 왔다 → 그 자리에서 출발시켜 옆으로 미끄러지게
        /// - 직전에 어느 슬롯에도 없던 카드가 왔다 → 새로 뽑힌 것이므로 밑에서 올라오게
        /// 첫 배치(직전 내용이 통째로 비어 있는 상태)에서는 전부 "새로 뽑힘"으로 잡히는데, 그게
        /// 손패가 처음 채워지는 모습으로도 맞다.</summary>
        void PlayEntryAnimations()
        {
            for (int i = 0; i < slots.Length; i++)
            {
                var now = slots[i].BoundCard;
                if (now == null || ReferenceEquals(now, previousSlotCards[i])) continue;

                int cameFrom = -1;
                for (int j = 0; j < slots.Length; j++)
                    if (ReferenceEquals(previousSlotCards[j], now)) { cameFrom = j; break; }

                if (cameFrom > i)
                {
                    float dx = slots[cameFrom].MockupView.CollapsedPosition.x
                               - slots[i].MockupView.CollapsedPosition.x;
                    slots[i].MockupView.PlaySlideFrom(dx);
                }
                else if (cameFrom < 0)
                {
                    slots[i].MockupView.PlayDrawn();
                }
                // cameFrom < i(왼쪽에서 옴)는 지금 손패 규칙상 생기지 않는다 - 생겨도 그냥 제자리에 둔다.
            }
        }

        static bool Contains(IReadOnlyList<InformationCardData> list, InformationCardData card)
        {
            for (int i = 0; i < list.Count; i++)
                if (ReferenceEquals(list[i], card)) return true;
            return false;
        }
    }
}
