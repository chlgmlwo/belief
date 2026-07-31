namespace Belief.Data
{
    [UnityEngine.CreateAssetMenu(fileName = "Npc_Minor_", menuName = "Belief/NPC/Minor", order = 2)]
    public class MinorNpcData : NpcData
    {
        public override NpcRank Rank => NpcRank.Minor;
    }
}
