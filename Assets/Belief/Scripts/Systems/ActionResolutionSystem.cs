using System.Collections.Generic;
using Belief.Data;
using Belief.Domain;
using Belief.Events;
using Belief.Systems.Movement;
using UnityEngine;

namespace Belief.Systems
{
    /// <summary>
    /// 상태를 바꾸는 지점을 두 갈래로 한정한다: 1) 저작된 NpcActionData의 Effect 실행(Apply),
    /// 2) 특정 Action 에셋에 묶이지 않는 규칙 기반 이동(MoveNpc, 예: Minor NPC 배회).
    /// 둘 다 이 클래스를 거쳐야 하고, Belief/Memory는 건드리지 않는다.
    /// </summary>
    public class ActionResolutionSystem
    {
        readonly IReadOnlyDictionary<LocationData, LocationState> locations;
        readonly IGameEventBus eventBus;

        public ActionResolutionSystem(IReadOnlyDictionary<LocationData, LocationState> locations, IGameEventBus eventBus)
        {
            this.locations = locations;
            this.eventBus = eventBus;
        }

        public void Apply(NpcActionData action, NpcState actor, InformationCardData judgedCard, LocationState currentLocation, int currentTurn)
        {
            if (action == null) return;

            actor.SetCurrentAction(action);
            if (action.effect == null) return;

            var context = new ActionEffectContext(locations, eventBus, currentTurn, judgedCard, currentLocation);
            action.effect.Apply(actor, context);
        }

        /// <summary>이번 스테이지에 등록되지 않은 장소인지 먼저 확인한다. MoveTo는 등록 여부와 무관하게
        /// CurrentLocation부터 바꾸는데, 미등록 장소면 PresentNpcs에 넣을 곳이 없어 그 NPC가 어느
        /// 장소의 재실 목록에도 없는 상태가 된다(월드에서 사라진 것처럼 보이고, 재실 기반 미션 조건과
        /// 재확산 대상 계산이 전부 어긋난다). movementCandidates는 NPC 에셋에 붙어 있어 그 NPC가
        /// 등장하는 모든 스테이지가 공유하므로, 한 스테이지에만 있는 장소가 후보에 섞이는 것은
        /// 데이터 실수가 아니라 정상적인 상황이다 - 그래서 데이터가 아니라 여기서 막는다.</summary>
        public void MoveNpc(NpcState actor, LocationData destination)
        {
            if (destination == null) return;

            if (locations == null || !locations.ContainsKey(destination))
            {
                Debug.LogWarning($"[ActionResolutionSystem] {actor?.Data?.npcId}의 목적지 "
                                 + $"{destination.locationId}가 이번 스테이지에 등록되지 않아 이동을 취소했습니다.");
                return;
            }

            var from = actor.CurrentLocation;
            NpcMovementService.MoveTo(actor, destination, locations);
            eventBus.Publish(new NpcRelocatedEvent(actor.Data, from, destination));
        }
    }
}
