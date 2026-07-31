using UnityEngine;

namespace Belief.Data
{
    /// <summary>RuleBasedMajorThinker(개발/폴백 경로) 전용 고정 대사 풀. LLM 경로는 생성 텍스트를 쓴다.</summary>
    [CreateAssetMenu(fileName = "Dialogue_", menuName = "Belief/Dialogue Line", order = 8)]
    public class DialogueLineData : ScriptableObject
    {
        public string contextTag;
        [TextArea(1, 3)] public string text;
    }
}
