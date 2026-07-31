namespace Belief.Domain.NPC
{
    /// <summary>NpcRuntimeDto.currentMove의 런타임(가변) 사본.</summary>
    public class NpcMoveState
    {
        public string TargetLocationId;
        public string ReasonGoalId;
        public string Status;
        public int StartedTurn;
    }
}
