namespace Belief.Domain.NPC
{
    /// <summary>NpcRuntimeDto.relationshipStates 원소 하나의 런타임(가변) 사본.</summary>
    public class RelationshipState
    {
        public string TargetType;
        public string TargetId;
        public string Trust;
        public string Intimacy;
        public string Influence;
        public int TrustDelta;
        public int IntimacyDelta;
        public int InfluenceDelta;
        public bool InitializedFromProfile;
    }
}
