using Belief.Data;
using Belief.Domain;

namespace Belief.Systems
{
    public readonly struct BeliefContext
    {
        public readonly NpcState Npc;
        public readonly InformationCardData Card;
        public readonly LocationState CurrentLocation;
        public readonly WorkingMemory WorkingMemory;
        public readonly int CurrentTurn;

        /// <summary>Location Mechanics V1 - BeliefSystem.Evaluate가 evaluator 호출 전에 미리 계산해 둔
        /// "이 판단 시점의" 유효 신뢰도(card.information.baseCredibility + 장소 credibilityModifier
        /// 보정 + sensitiveInformationType 일치 보너스, Clamp01). CredibilityEvaluator는 카드 원본
        /// baseCredibility 대신 반드시 이 값을 읽는다 - 카드 ScriptableObject 자체는 절대 수정하지 않는다.</summary>
        public readonly float EffectiveCredibility;

        public BeliefContext(NpcState npc, InformationCardData card, LocationState currentLocation,
            WorkingMemory workingMemory, int currentTurn, float effectiveCredibility)
        {
            Npc = npc;
            Card = card;
            CurrentLocation = currentLocation;
            WorkingMemory = workingMemory;
            CurrentTurn = currentTurn;
            EffectiveCredibility = effectiveCredibility;
        }
    }
}
