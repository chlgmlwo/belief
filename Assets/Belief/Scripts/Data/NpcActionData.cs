using UnityEngine;

namespace Belief.Data
{
    /// <summary>IMajorNpcThinker/RuleBasedMajorThinker가 belief -> intent 매핑에 쓰는 분류. 문자열 비교 대신 사용.</summary>
    public enum NpcActionIntent
    {
        Comply,
        Escalate,
        Verify,
        Ignore,
        Wait
    }

    [CreateAssetMenu(fileName = "Action_", menuName = "Belief/NPC Action", order = 7)]
    public class NpcActionData : ScriptableObject
    {
        public string actionId;
        public string displayLabel;
        public NpcActionIntent intent = NpcActionIntent.Wait;
        public NpcActionEffect effect;
    }
}
