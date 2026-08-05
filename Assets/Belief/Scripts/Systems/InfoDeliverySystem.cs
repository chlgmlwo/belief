using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Belief.Data;
using Belief.Domain;
using Belief.Events;
using UnityEngine;

namespace Belief.Systems
{
    /// <summary>
    /// 카드 재생을 장소/NPC 노출로 변환하고 판단을 NpcThinkingSystem으로 넘긴다. NPC 등급 구분이
    /// 없으므로 라우팅 분기도 없다 - 모든 NPC가 같은 판단 경로를 타고, 재확산(TryReSpread) 역시
    /// 어떤 NPC든 Belief 판단이 끝나 Plausible/Trusted에 도달하면 같은 자격으로 시도한다.
    /// 판단(NpcThinkingSystem.HandleExposureAsync)이 LLM Timeout까지 기다릴 수 있어 이 클래스도
    /// 비동기다 - 한 장소에 여러 NPC가 있으면 순차로 await한다(기존 처리 순서 그대로, 병렬 실행 없음).
    ///
    /// Location Mechanics V1: spreadSpeed는 재확산의 유효 확산력(TryReSpread의 확률 게이트)에만,
    /// npcDensity는 재확산 1회가 추가로 영향을 주는 NPC 수 상한에만 적용한다 - 플레이어의 최초 직접
    /// 노출(propagator == null)에는 둘 다 전혀 관여하지 않는다.
    /// </summary>
    public class InfoDeliverySystem
    {
        readonly IReadOnlyDictionary<LocationData, LocationState> locations;
        readonly NpcThinkingSystem thinking;
        readonly IGameEventBus eventBus;
        readonly Func<int> currentTurnProvider;
        readonly LocationMechanicsSettings locationMechanics;

        public InfoDeliverySystem(
            IReadOnlyDictionary<LocationData, LocationState> locations,
            NpcThinkingSystem thinking,
            IGameEventBus eventBus,
            Func<int> currentTurnProvider,
            LocationMechanicsSettings locationMechanics)
        {
            this.locations = locations;
            this.thinking = thinking;
            this.eventBus = eventBus;
            this.currentTurnProvider = currentTurnProvider;
            this.locationMechanics = locationMechanics;
        }

        /// <summary>propagator는 재확산(TryReSpread) 내부 호출에서만 채워진다 - 플레이어의 SPREAD는
        /// 항상 정보원을 통한 전달이라 propagator가 없다(RumorState.PropagatedBy == null).
        /// spreadInfo도 재확산 경로에서만 채워지며, 그 경우에만 npcDensity 상한이 적용된다.</summary>
        public async Task ExposeCardAtLocationAsync(
            InformationCardData card, LocationData location, NpcState propagator = null,
            SpreadMechanicsSnapshot? spreadInfo = null)
        {
            if (!locations.TryGetValue(location, out var locState)) return;

            RecordRumor(locState, card, propagator?.Data);
            eventBus.Publish(new InfoSpreadEvent(card, location));

            // 확산 주체 본인은 이미 이 정보를 알고 퍼뜨리는 쪽이므로 이번 전달 처리에서는
            // 판단 대상에서 제외한다(영구 제외가 아니라 이 호출 1회 한정 - 재확산/별도 사건으로
            // 나중에 같은 정보를 접하면 그때는 다시 판단 대상이 된다).
            var present = new List<NpcState>(locState.PresentNpcs);
            present.RemoveAll(n => n == propagator);

            var targets = present;

            if (propagator != null) // 재확산 경로에서만 중복 수신 제외/밀집도 상한을 적용한다 - 최초 직접 노출은 예외.
            {
                // 같은 NPC에게 같은 카드를 중복 전달하지 않는다 - 이 필터가 반복적으로 걸리면(모든
                // 대상이 이미 수신) 그 장소에서는 더 이상 신규 타겟이 없어 재확산 체인이 자연히
                // 끊긴다(무한 재귀 방지의 실질적 장치이기도 하다).
                present.RemoveAll(n => HasAlreadyReceived(n, card));

                int candidateCount = present.Count;
                int limit = locationMechanics != null ? locationMechanics.GetDensityTargetLimit(location.npcDensity) : int.MaxValue;

                // 기존 대상 선택 순서(PresentNpcs 순서)를 그대로 유지하고 최종 인원 수에만 상한을
                // 적용한다 - 로그를 위한 추가 Random 호출 없음.
                if (present.Count > limit)
                    targets = present.Take(limit).ToList();

                if (spreadInfo.HasValue)
                {
                    spreadInfo = spreadInfo.Value.WithDensity(
                        location.npcDensity, candidateCount, limit, targets.Count, candidateCount - targets.Count);
                }
            }

            foreach (var npc in targets)
                await Judge(npc, card, locState, spreadInfo, propagator);
        }

        public async Task DeliverCardToNpcAsync(InformationCardData card, NpcState target)
        {
            eventBus.Publish(new InfoDeliveredEvent(card, target.Data));

            // 플레이어가 정보원을 통해 직접 전달한 경로 - 전달한 "인물"이 존재하지 않으므로
            // propagator는 항상 null이다. 관계 근거를 만들어 주려고 여기에 가짜 전달자를 끼워
            // 넣지 않는다(없는 사실을 판단 근거로 쓰게 되므로).
            if (target.CurrentLocation != null && locations.TryGetValue(target.CurrentLocation, out var where))
                await Judge(target, card, where, null, null);
        }

        void RecordRumor(LocationState locState, InformationCardData card, NpcData propagator)
        {
            int turn = currentTurnProvider();
            foreach (var r in locState.ActiveRumors)
            {
                if (r.Information == card.information && r.PropagatedBy == propagator)
                {
                    r.Refresh(turn);
                    return;
                }
            }
            locState.ActiveRumors.Add(new RumorState(card.information, card, locState.Data, propagator, turn));
        }

        /// <summary>Belief 판단만 Major/Minor로 라우팅하고, 그 결과(BeliefState)로 재확산 조건을
        /// 판정하는 마지막 단계는 항상 공통으로 실행한다 - 재확산 자격을 NPC 유형으로 제한하지 않는다.</summary>
        async Task Judge(NpcState npc, InformationCardData card, LocationState where, SpreadMechanicsSnapshot? spreadInfo,
            NpcState propagator)
        {
            int turn = currentTurnProvider();
            npc.RecordReceivedInformation(card, turn);

            // NPC 등급 구분 없이 모든 NPC가 같은 판단 경로를 탄다 - "AI가 정보를 받고 각 NPC가
            // 해석해서 행동한다"가 이 게임의 핵심이므로, 일부 NPC만 기억 없이 판단하던 별도 경로
            // (구 MinorNpcBehaviorSystem)를 없앴다.
            var belief = await thinking.HandleExposureAsync(npc, card, where, turn, spreadInfo, propagator);

            await TryReSpread(card, where, npc, belief);
        }

        static bool HasAlreadyReceived(NpcState npc, InformationCardData card)
        {
            foreach (var entry in npc.ReceivedInformation)
                if (entry.Card == card) return true;
            return false;
        }

        async Task TryReSpread(InformationCardData card, LocationState from, NpcState carrier, BeliefState belief)
        {
            if (card.cardType != InfoCardType.Spread) return;
            if (belief != BeliefState.Plausible && belief != BeliefState.Trusted) return;

            var connected = from.Data.connectedLocations;
            if (connected == null || connected.Length == 0) return;

            // Location Mechanics V1 - spreadSpeed는 "재확산이 발생하는(=이 정보를 퍼뜨리는 원천인) 장소"
            // 기준으로 계산한다. 카드 원본 spreadPower는 건드리지 않고, 이 확률 게이트에만 쓰는 별도
            // effectiveSpreadPower를 만든다. LocationData.spreadModifier(SituationEvaluator 전용)와는
            // 절대 곱하지 않는다 - 역할이 다른 별개 수치.
            float baseSpreadPower = card.information.spreadPower;
            var sourceSpreadSpeed = from.Data.spreadSpeed;
            float multiplier = locationMechanics != null ? locationMechanics.GetSpreadSpeedMultiplier(sourceSpreadSpeed) : 1f;
            float effectiveSpreadPower = Mathf.Clamp01(baseSpreadPower * multiplier);

            if (UnityEngine.Random.value > effectiveSpreadPower) return;

            var target = connected[UnityEngine.Random.Range(0, connected.Length)];

            var snapshot = new SpreadMechanicsSnapshot(
                baseSpreadPower, sourceSpreadSpeed, multiplier, effectiveSpreadPower,
                LocationNpcDensity.Unspecified, 0, 0, 0, 0); // density 부분은 ExposeCardAtLocationAsync가 채운다

            await ExposeCardAtLocationAsync(card, target, carrier, snapshot);
        }
    }
}
