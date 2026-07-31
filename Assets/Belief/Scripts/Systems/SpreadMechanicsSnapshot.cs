using Belief.Data;

namespace Belief.Systems
{
    /// <summary>Location Mechanics V1 - 재확산(TryReSpread) 1회에 대해 이미 계산이 끝난 spreadSpeed/
    /// npcDensity 값을 그대로 옮겨 담는 순수 관찰용 스냅샷. 이 구조체 자체는 어떤 계산도 하지 않는다
    /// (판단 로직에 관여하지 않음) - InfoDeliverySystem이 계산한 값을 NpcDecisionTraceRecord까지
    /// 옮기기 위한 용도로만 쓰인다. 플레이어의 직접 최초 전달(propagator == null)에는 이 스냅샷이
    /// 만들어지지 않는다(null로 유지).</summary>
    public readonly struct SpreadMechanicsSnapshot
    {
        public readonly float BaseSpreadPower;
        public readonly LocationSpreadSpeed SourceSpreadSpeed;
        public readonly float SpreadMultiplier;
        public readonly float EffectiveSpreadPower;

        public readonly LocationNpcDensity TargetNpcDensity;
        public readonly int CandidateNpcCount;
        public readonly int DensityTargetLimit;
        public readonly int SelectedSecondaryRecipientCount;
        public readonly int ExcludedRecipientCount;

        public SpreadMechanicsSnapshot(
            float baseSpreadPower, LocationSpreadSpeed sourceSpreadSpeed, float spreadMultiplier, float effectiveSpreadPower,
            LocationNpcDensity targetNpcDensity, int candidateNpcCount, int densityTargetLimit,
            int selectedSecondaryRecipientCount, int excludedRecipientCount)
        {
            BaseSpreadPower = baseSpreadPower;
            SourceSpreadSpeed = sourceSpreadSpeed;
            SpreadMultiplier = spreadMultiplier;
            EffectiveSpreadPower = effectiveSpreadPower;
            TargetNpcDensity = targetNpcDensity;
            CandidateNpcCount = candidateNpcCount;
            DensityTargetLimit = densityTargetLimit;
            SelectedSecondaryRecipientCount = selectedSecondaryRecipientCount;
            ExcludedRecipientCount = excludedRecipientCount;
        }

        /// <summary>density 관련 필드만 채운 새 값을 반환한다(spreadSpeed 쪽 필드는 그대로 유지) -
        /// TryReSpread가 spreadSpeed 부분을 먼저 계산하고, ExposeCardAtLocationAsync가 density
        /// 부분을 나중에 채워 넣는 2단계 계산 순서를 그대로 반영한다.</summary>
        public SpreadMechanicsSnapshot WithDensity(
            LocationNpcDensity targetNpcDensity, int candidateNpcCount, int densityTargetLimit,
            int selectedSecondaryRecipientCount, int excludedRecipientCount)
            => new SpreadMechanicsSnapshot(
                BaseSpreadPower, SourceSpreadSpeed, SpreadMultiplier, EffectiveSpreadPower,
                targetNpcDensity, candidateNpcCount, densityTargetLimit,
                selectedSecondaryRecipientCount, excludedRecipientCount);
    }
}
