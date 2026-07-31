namespace Belief.Domain.NPC
{
    /// <summary>NpcRuntimeDto.runtimeStatus의 런타임(가변) 사본.</summary>
    public class NpcRuntimeStatus
    {
        public string CurrentStageId;
        public string CurrentLocationId;
        public bool IsActive;
        public bool IsAvailable;
        public int LastUpdatedTurn;
    }
}
