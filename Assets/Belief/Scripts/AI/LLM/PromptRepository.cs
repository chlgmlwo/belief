using System.Collections.Generic;
using Belief.Domain;

namespace Belief.AI.LLM
{
    public readonly struct PromptDebugEntry
    {
        public readonly string Prompt;
        public readonly string RawResponse;
        public readonly LlmValidationResult Validation;
        public readonly bool UsedFallback;

        public PromptDebugEntry(string prompt, string rawResponse, LlmValidationResult validation, bool usedFallback)
        {
            Prompt = prompt;
            RawResponse = rawResponse;
            Validation = validation;
            UsedFallback = usedFallback;
        }
    }

    /// <summary>Debug Overlay가 읽는 쪽 - 쓰기 메서드가 없다.</summary>
    public interface IPromptRepository
    {
        bool TryGetLast(NpcState npc, out PromptDebugEntry entry);
    }

    /// <summary>
    /// 최근 Prompt/Raw Response/Validation/Fallback 여부를 NPC별로 보관한다. LlmMajorThinker만
    /// Record를 호출한다. NpcState나 Presentation에는 이 데이터를 절대 넣지 않는다.
    /// </summary>
    public class PromptRepository : IPromptRepository
    {
        readonly Dictionary<NpcState, PromptDebugEntry> lastByNpc = new Dictionary<NpcState, PromptDebugEntry>();

        public void Record(NpcState npc, string prompt, string rawResponse, LlmValidationResult validation, bool usedFallback) =>
            lastByNpc[npc] = new PromptDebugEntry(prompt, rawResponse, validation, usedFallback);

        public bool TryGetLast(NpcState npc, out PromptDebugEntry entry) => lastByNpc.TryGetValue(npc, out entry);
    }
}
