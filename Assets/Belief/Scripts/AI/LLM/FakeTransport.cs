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
                    ? "{\"destination\":\"does_not_exist_in_candidates\"," + Grounds + "}"
                    : "{\"action\":\"does_not_exist_in_candidates\",\"dialogue\":\"...\"," + Grounds + "}",
                _ => "{}"
            };

            return Task.FromResult(response);
        }

        /// <summary>어떤 NPC에게도 항상 유효한 최소 근거 - primaryReason="belief"는 profileInfluence나
        /// relationshipInfluence를 요구하지 않는 유일하게 안전한 조합이다. FakeTransport는 프롬프트
        /// 문자열만 보고 응답하므로 그 NPC가 어떤 태그/관계를 가졌는지 알 수 없고, 알아내려 해서도
        /// 안 된다(진짜 LLM처럼 텍스트만 주고받는다는 원칙).</summary>
        const string Grounds = "\"primaryReason\":\"belief\",\"profileInfluence\":\"none\",\"relationshipInfluence\":\"none\"";

        static string BuildSuccessResponse(string prompt)
        {
            // 반드시 [선택 가능한 행동] 구간 안에서만 찾는다 - 프롬프트에 "- "로 시작하는 줄이
            // [인물 메모]에도 있어서, 전체를 대상으로 하면 엉뚱한 문장을 행동 id로 집어낸다.
            string actionId = FirstListedIdAfter(prompt, "[선택 가능한 행동]") ?? "wait";
            return "{\"action\":\"" + actionId + "\",\"dialogue\":\"이 정보를 곱씹어 본다.\"," + Grounds + "}";
        }

        /// <summary>[이동 후보] 목록의 첫 locationId를 그대로 목적지로 되돌린다 - 후보가 없으면 stay.</summary>
        static string BuildMoveSuccessResponse(string prompt)
        {
            string destination = FirstListedIdAfter(prompt, "[이동 후보]") ?? "stay";
            return "{\"destination\":\"" + destination + "\"," + Grounds + "}";
        }

        static string FirstListedIdAfter(string prompt, string header)
        {
            int at = prompt.IndexOf(header, System.StringComparison.Ordinal);
            if (at < 0) return null;
            var match = ActionIdPattern.Match(prompt.Substring(at));
            return match.Success ? match.Groups[1].Value : null;
        }
    }
}
