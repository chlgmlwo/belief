using UnityEngine;

namespace Belief.Data
{
    /// <summary>
    /// Location Mechanics V1의 유일한 수치 보관소. LocationData의 5개 콘텐츠 특성값(enum)을 실제
    /// 배율/보정값(float/int)으로 바꾸는 계산은 전부 이 클래스의 메서드 안에서만 이루어진다 - 다른
    /// 시스템 코드 어디에서도 enum 값을 직접 숫자로 스위치하지 않는다. 여러 스테이지가 이 자산 하나를
    /// 공유하며, 장소/스테이지 ID로 분기하지 않는다.
    ///
    /// 아래 초기값은 전부 V1 플레이테스트용이다(Frozen 아님) - 값 조정은 Inspector에서만.
    /// </summary>
    [CreateAssetMenu(fileName = "LocationMechanicsSettings", menuName = "Belief/Location Mechanics Settings", order = 3)]
    public class LocationMechanicsSettings : ScriptableObject
    {
        [Header("spreadSpeed → 재확산 시 유효 확산력(spreadPower) 배율")]
        public float spreadSpeedMultiplierUnspecified = 1.00f;
        public float spreadSpeedMultiplierLow = 0.75f;
        public float spreadSpeedMultiplierMedium = 1.00f;
        public float spreadSpeedMultiplierHigh = 1.25f;

        [Header("npcDensity → 재확산 1회당 추가로 영향받는 NPC 수 상한")]
        public int densityTargetLimitUnspecified = 1;
        public int densityTargetLimitLow = 1;
        public int densityTargetLimitMedium = 2;
        public int densityTargetLimitHigh = 3;

        [Header("sensitiveInformationType 일치 시 신뢰도 가산")]
        public float sensitiveTypeMatchBonus = 0.10f;

        [Header("credibilityModifier → 유효 신뢰도 가감")]
        public float credibilityDeltaUnspecified = 0.00f;
        public float credibilityDeltaLow = -0.10f;
        public float credibilityDeltaNeutral = 0.00f;
        public float credibilityDeltaHigh = 0.10f;
        public float credibilityDeltaVeryHigh = 0.20f;

        [Header("accessType 차단 시 플레이어에게 보여줄 문구 (공통 메시지 위치)")]
        [TextArea(2, 4)]
        public string restrictedLocationTargetMessage =
            "이 장소는 출입이 제한되어 있어 장소 전체에 정보를 퍼뜨릴 수 없습니다.\n개별 인물을 선택하세요.";

        public float GetSpreadSpeedMultiplier(LocationSpreadSpeed value) => value switch
        {
            LocationSpreadSpeed.Low => spreadSpeedMultiplierLow,
            LocationSpreadSpeed.Medium => spreadSpeedMultiplierMedium,
            LocationSpreadSpeed.High => spreadSpeedMultiplierHigh,
            _ => spreadSpeedMultiplierUnspecified
        };

        public int GetDensityTargetLimit(LocationNpcDensity value) => value switch
        {
            LocationNpcDensity.Low => densityTargetLimitLow,
            LocationNpcDensity.Medium => densityTargetLimitMedium,
            LocationNpcDensity.High => densityTargetLimitHigh,
            _ => densityTargetLimitUnspecified
        };

        public float GetCredibilityDelta(LocationCredibilityModifier value) => value switch
        {
            LocationCredibilityModifier.Low => credibilityDeltaLow,
            LocationCredibilityModifier.Neutral => credibilityDeltaNeutral,
            LocationCredibilityModifier.High => credibilityDeltaHigh,
            LocationCredibilityModifier.VeryHigh => credibilityDeltaVeryHigh,
            _ => credibilityDeltaUnspecified
        };

        /// <summary>카드 정보 유형과 장소의 민감 정보 유형이 실제로 일치하는지 - 둘 중 하나라도
        /// Unspecified면 항상 불일치. 두 enum은 별도 타입이지만 이름을 1:1로 맞춰 관리하므로
        /// 이름 비교 하나로 충분하다(정수값 순서가 바뀌어도 안전).</summary>
        public bool IsSensitiveTypeMatch(InformationType cardType, LocationSensitiveInfoType locationType)
        {
            if (cardType == InformationType.Unspecified || locationType == LocationSensitiveInfoType.Unspecified)
                return false;
            return cardType.ToString() == locationType.ToString();
        }

        /// <summary>플레이어가 이 장소를 "장소 전체" 대상으로 직접 지정(SPREAD 카드)할 수 있는지 -
        /// NPC 개별 지정(DELIVER 카드)이나 NPC 자체 이동 판단에는 전혀 영향을 주지 않는다.
        /// Unspecified는 Public과 동일하게 취급한다(값이 아직 채워지지 않은 장소를 막지 않기 위함).</summary>
        public bool CanTargetLocationDirectly(LocationData location) =>
            location == null
            || location.accessType == LocationAccessType.Unspecified
            || location.accessType == LocationAccessType.Public;
    }
}
