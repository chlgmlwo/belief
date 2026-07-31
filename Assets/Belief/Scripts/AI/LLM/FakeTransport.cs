using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;

namespace Belief.AI.LLM
{
    public enum FakeTransportMode
    {
        AlwaysSuccess,
        AlwaysInvalidJson,
        AlwaysInvalidAction,
        Random
    }

    /// <summary>
    /// 실제 서비스 없이 전체 파이프라인(PromptBuilder -> Transport -> Parser -> Validation -> Fallback)을
    /// 검증하기 위한 가짜 구현체. 프롬프트 "문자열"만 보고 응답하며, 게임 타입(NpcActionData 등)을
    /// 전혀 알지 못한다 - 진짜 LLM처럼 텍스트만 주고받는다.
    /// AlwaysSuccess/Random의 "성공" 응답은 프롬프트 안의 [선택 가능한 행동] 목록에서
    /// 첫 번째 id를 정규식으로 뽑아 그대로 되돌려주는 방식으로 만든다.
    /// </summary>
    public class FakeTransport : ILlmTransport
    {
        static readonly Regex ActionIdPattern = new Regex(@"^- (\S+):", RegexOptions.Multiline);
        static readonly Regex MoveCandidatePattern = new Regex(@"^- (\S+):", RegexOptions.Multiline);

        readonly FakeTransportMode mode;

        public FakeTransport(FakeTransportMode mode)
        {
            this.mode = mode;
        }

        public Task<string> SendAsync(string prompt)
        {
            var effectiveMode = mode == FakeTransportMode.Random
                ? (Random.value < 0.5f ? FakeTransportMode.AlwaysSuccess : FakeTransportMode.AlwaysInvalidJson)
                : mode;

            bool isMovePrompt = prompt.Contains("[이동 후보]");

            string response = effectiveMode switch
            {
                FakeTransportMode.AlwaysSuccess => isMovePrompt ? BuildMoveSuccessResponse(prompt) : BuildSuccessResponse(prompt),
                FakeTransportMode.AlwaysInvalidJson => "{ 이것은 올바른 JSON이 아닙니다",
                FakeTransportMode.AlwaysInvalidAction => isMovePrompt
                    ? "{\"destination\":\"does_not_exist_in_candidates\"}"
                    : "{\"action\":\"does_not_exist_in_candidates\",\"dialogue\":\"...\"}",
                _ => "{}"
            };

            return Task.FromResult(response);
        }

        static string BuildSuccessResponse(string prompt)
        {
            var match = ActionIdPattern.Match(prompt);
            string actionId = match.Success ? match.Groups[1].Value : "wait";
            return "{\"action\":\"" + actionId + "\",\"dialogue\":\"이 정보를 곱씹어 본다.\"}";
        }

        /// <summary>[이동 후보] 목록의 첫 locationId를 그대로 목적지로 되돌린다 - 후보가 없으면 stay.</summary>
        static string BuildMoveSuccessResponse(string prompt)
        {
            var section = prompt.Substring(prompt.IndexOf("[이동 후보]"));
            var match = MoveCandidatePattern.Match(section);
            string destination = match.Success ? match.Groups[1].Value : "stay";
            return "{\"destination\":\"" + destination + "\"}";
        }
    }
}
