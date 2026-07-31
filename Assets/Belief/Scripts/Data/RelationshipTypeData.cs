using UnityEngine;

namespace Belief.Data
{
    /// <summary>연출/대사용 라벨(존경/신뢰/충성/공포/경쟁/우정/적대 등). 수치 보정은 RelationshipEntry.strength가 담당한다.</summary>
    [CreateAssetMenu(fileName = "RelationshipType_", menuName = "Belief/Relationship Type", order = 6)]
    public class RelationshipTypeData : ScriptableObject
    {
        public string relationshipTypeId;
        public string displayName;
    }
}
