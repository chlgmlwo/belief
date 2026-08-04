using UnityEngine;

namespace Belief.Data
{
    [CreateAssetMenu(fileName = "Loc_", menuName = "Belief/Location", order = 1)]
    public class LocationData : ScriptableObject
    {
        [Header("Identity")]
        public string locationId;
        public string displayName;
        [TextArea(2, 4)] public string description;

        [Header("World")]
        [Tooltip("City 씬에서 NpcActorView가 이동할 앵커 좌표.")]
        public Vector2 worldPosition;

        [Header("Visual")]
        [Tooltip("LocationSiteView의 사진 프레임 안에 표시할 실제 건물 사진 - 비어 있으면 기존 placeholder 단색으로 대체된다.")]
        public Sprite locationPhoto;

        [Header("Spread Behaviour")]
        [Range(0f, 2f)] public float spreadModifier = 1f;
        public LocationData[] connectedLocations;

        [Header("Content Characteristics (장소·NPC 기획서 2.2 - Location Mechanics V1이 LocationMechanicsSettings와 함께 실제 계산에 사용)")]
        public LocationSpreadSpeed spreadSpeed;
        public LocationNpcDensity npcDensity;
        public LocationSensitiveInfoType sensitiveInformationType;
        public LocationAccessType accessType;
        public LocationCredibilityModifier credibilityModifier;
    }
}
