using Belief.Data;

namespace Belief.Systems
{
    /// <summary>RuleBasedMajorThinker는 PredefinedLine을, LlmMajorThinker(추후)는 GeneratedText를 채운다.</summary>
    public class DialogueContent
    {
        public DialogueLineData PredefinedLine { get; }
        public string GeneratedText { get; }
        public bool IsGenerated => GeneratedText != null;

        public DialogueContent(DialogueLineData predefinedLine)
        {
            PredefinedLine = predefinedLine;
        }

        public DialogueContent(string generatedText)
        {
            GeneratedText = generatedText;
        }
    }
}
