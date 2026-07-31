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

        /// <summary>미션 시도 시작 시점의 pool/delivered 스냅샷(RestartCurrentMission 복원용). owned(손패)는
        /// 의도적으로 포함하지 않는다 - RestoreSnapshotForRestart가 항상 새로 뽑기 때문에 그 시점 손패를
        /// 따로 들고 있을 이유가 없다. 두 리스트 모두 방어적으로 복사한다 - 이후 실제 진행(Draw/Deliver)이
        /// 원본을 계속 바꿔도 스냅샷 내용은 캡처 시점 그대로 남는다.</summary>
        public readonly struct CardSystemSnapshot
        {
            public readonly List<InformationCardData> Pool;
            public readonly List<DeliveredCardRecord> Delivered;

            public CardSystemSnapshot(List<InformationCardData> pool, List<DeliveredCardRecord> delivered)
            {
                Pool = new List<InformationCardData>(pool);
                Delivered = new List<DeliveredCardRecord>(delivered);
            }
        }

        public CardSystemSnapshot CaptureSnapshot() => new CardSystemSnapshot(pool, delivered);

        /// <summary>RestartCurrentMission 전용 복원. Draw()가 pool에서 영구히 제거해 온 카드까지 포함해
        /// pool/delivered는 스냅샷 시점 그대로 되돌리지만, owned(손패)는 그 시점 그대로 복원하지 않고
        /// 비운 뒤 같은 규칙(GrantInitialSupply)으로 다시 뽑는다 - 실패한 시도의 나쁜 손패가 재시도마다
        /// 고정되는 문제를 막는다. pool을 먼저 복원한 뒤에 다시 뽑으므로, 실패한 시도에서 소비된 카드가
        /// pool로 돌아왔다가 다시 InitialSupply장만 빠져나가는 것과 같다 - 몇 번을 재시도해도 누적
        /// 소모량은 항상 "미션 시작 1회 지급분"으로 고정된다(고갈 방지 유지). 뽑기 확률/pool 구성 자체는
        /// 건드리지 않는다(Draw()를 그대로 재사용).</summary>
        public void RestoreSnapshotForRestart(CardSystemSnapshot snapshot)
        {
            pool.Clear();
            pool.AddRange(snapshot.Pool);

            delivered.Clear();
            delivered.AddRange(snapshot.Delivered);

            owned.Clear();
            GrantInitialSupply();
        }
    }
}
