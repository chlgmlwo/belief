using System.Collections.Generic;
using Belief.Domain;
using Belief.Events;
using UnityEngine;

namespace Belief.Data
{
    /// <summary>
    /// NpcActionEffect/ActionEffectContext는 Data 레이어 예외로 Domain/Events를 직접 참조한다 -
    /// 전략 패턴으로 실행 가능한 행동을 표현하는 게 목적이라 런타임 상태 접근이 불가피하다.
    /// Domain/Events 쪽에서는 이 타입들을 절대 참조하지 않으므로 순환 참조는 아니다.
    /// </summary>
    public readonly struct ActionEffectContext
    {
        public readonly IReadOnlyDictionary<LocationData, LocationState> Locations;
        public readonly IGameEventBus EventBus;
        public readonly int CurrentTurn;
        public readonly InformationCardData JudgedCard;
        public readonly LocationState CurrentLocation;

        public ActionEffectContext(IReadOnlyDictionary<LocationData, LocationState> locations, IGameEventBus eventBus,
            int currentTurn, InformationCardData judgedCard = null, LocationState currentLocation = null)
        {
            Locations = locations;
            EventBus = eventBus;
            CurrentTurn = currentTurn;
            JudgedCard = judgedCard;
            CurrentLocation = currentLocation;
        }
    }

    public abstract class NpcActionEffect : ScriptableObject
    {
        public abstract void Apply(NpcState actor, ActionEffectContext context);
    }
}
