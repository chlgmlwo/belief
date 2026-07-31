using System.Collections.Generic;
using Belief.Data;
using Belief.Events;
using UnityEngine;

namespace Belief.Systems
{
    /// <summary>전달 완료된 정보 카드 한 건의 기록.</summary>
    public readonly struct DeliveredCardRecord
    {
        public readonly InformationCardData Card;
        public readonly int Turn;

        public DeliveredCardRecord(InformationCardData card, int turn)
        {
            Card = card;
            Turn = turn;
        }
    }

    /// <summary>
    /// 정보 카드 상태의 유일한 소유자. InformationPool(미획득, 내부 전용) -&gt;
    /// OwnedInformationCards(보유, 최대 MaxOwned) -&gt; DeliveredInformationCards(전달 완료 기록)
    /// 세 영역을 관리한다. Presentation은 OwnedInformationCards만 읽는다 - 이 클래스를 거치지 않는
    /// 직접 수정은 없다.
    ///
    /// 규칙(고정): 게임 시작 시 InitialSupply(=MaxOwned)개 지급 -&gt; 매 턴 정확히 1개 사용 ->
    /// 다음 턴 시작 시 MaxOwned까지 보충(정보 풀 부족 시 있는 만큼만). 첫 행동 시점 4장, 사용 직후
    /// 3장, 다음 턴 행동 시점 다시 4장을 항상 유지한다 - 전달 직후 즉시 보충하지 않고, 보충은
    /// 반드시 다음 턴 시작에만 일어난다.
    /// </summary>
    public class InformationCardSystem
    {
        public const int InitialSupply = 4;
        public const int MaxOwned = 4;

        readonly List<InformationCardData> pool;
        readonly List<InformationCardData> owned = new List<InformationCardData>();
        readonly List<DeliveredCardRecord> delivered = new List<DeliveredCardRecord>();
        readonly IGameEventBus eventBus;

        public IReadOnlyList<InformationCardData> OwnedInformationCards => owned;
        public IReadOnlyList<DeliveredCardRecord> DeliveredInformationCards => delivered;

        /// <summary>디버그/QA 전용 진단 값. 게임 로직은 이 값을 판단에 사용하지 않는다.</summary>
        public int RemainingInPoolCount => pool.Count;

        public InformationCardSystem(InformationCardPoolData poolData, IGameEventBus eventBus)
        {
            pool = new List<InformationCardData>(poolData.cards);
            this.eventBus = eventBus;
        }

        public void GrantInitialSupply() => Draw(InitialSupply, isInitialSupply: true);

        /// <summary>MaxOwned까지 정확히 채운다(매 턴 1장 사용 규칙 하에서는 항상 1장만 뽑는다).
        /// 이미 MaxOwned 이상이면 아무것도 하지 않는다.</summary>
        public void RefillIfNeeded()
        {
            int room = MaxOwned - owned.Count;
            if (room <= 0) return;
            Draw(room, isInitialSupply: false);
        }

        void Draw(int count, bool isInitialSupply)
        {
            var drawn = new List<InformationCardData>(count);
            for (int i = 0; i < count && pool.Count > 0; i++)
            {
                int index = Random.Range(0, pool.Count);
                var card = pool[index];
                pool.RemoveAt(index);
                owned.Add(card);
                drawn.Add(card);
            }

            if (drawn.Count > 0)
                eventBus.Publish(new InformationAcquiredEvent(drawn, isInitialSupply));
        }

        /// <summary>보유 정보에서 전달 완료 정보로 옮긴다.</summary>
        public bool Deliver(InformationCardData card, int turn)
        {
            if (!owned.Remove(card)) return false;

            delivered.Add(new DeliveredCardRecord(card, turn));
            eventBus.Publish(new CardPlayedEvent(card));
            return true;
        }

        /// <summary>미션 시도 시작 시점의 pool/owned/delivered 스냅샷(RestartCurrentMission 복원용).
        /// 세 리스트 모두 방어적으로 복사한다 - 이후 실제 진행(Draw/Deliver)이 원본을 계속 바꿔도
        /// 스냅샷 내용은 캡처 시점 그대로 남는다.</summary>
        public readonly struct CardSystemSnapshot
        {
            public readonly List<InformationCardData> Pool;
            public readonly List<InformationCardData> Owned;
            public readonly List<DeliveredCardRecord> Delivered;

            public CardSystemSnapshot(List<InformationCardData> pool, List<InformationCardData> owned, List<DeliveredCardRecord> delivered)
            {
                Pool = new List<InformationCardData>(pool);
                Owned = new List<InformationCardData>(owned);
                Delivered = new List<DeliveredCardRecord>(delivered);
            }
        }

        public CardSystemSnapshot CaptureSnapshot() => new CardSystemSnapshot(pool, owned, delivered);

        /// <summary>Draw()가 pool에서 영구히 제거해 온 카드까지 포함해 스냅샷 시점 그대로 되돌린다 -
        /// 실패한 시도에서 뽑히거나 전달된 카드가 pool로 돌아오지 않아 반복 재시도마다 pool이
        /// 고갈되던 문제(카드 시스템 자체의 뽑기/보충 규칙은 변경하지 않음, 상태만 되돌린다).</summary>
        public void RestoreSnapshot(CardSystemSnapshot snapshot)
        {
            pool.Clear();
            pool.AddRange(snapshot.Pool);

            owned.Clear();
            owned.AddRange(snapshot.Owned);

            delivered.Clear();
            delivered.AddRange(snapshot.Delivered);
        }
    }
}
