namespace Belief.Domain.NPC
{
    /// <summary>NpcRuntimeDto.currentGoal의 런타임(가변) 사본.</summary>
    public class NpcGoalState
    {
        public string GoalId;
        public string GoalType;
        public string ReasonInformationId;
        public string TargetLocationId;
        public string Status;
    }
}
