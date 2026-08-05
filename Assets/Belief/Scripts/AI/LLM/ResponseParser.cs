using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Belief.Data;
using Belief.Domain;
using Belief.AI.LLM.Benchmark;

namespace Belief.AI.LLM
{
    /// <summary>
    /// LLM 원시 응답 문자열을 검증한다. 어떤 경우에도 예외를 던지지 않고
    /// LlmValidationResult로만 성공/실패를 알린다.
    /// </summary>
    public static class ResponseParser
    {
        /// <summary>행동 판단 응답 검증. 기존 검증(빈 응답/JSON/행동 화이트리스트/대사)에 더해
        /// 근거 3필드를 JudgmentGroundsValidator로 검증한다 - 지어낸 인물이나 태그가 하나라도
        /// 있으면 그 응답 전체를 무효로 보고 호출자가 RuleBased 폴백 1회를 태운다.</summary>
        public static LlmValidationResult Parse(
            string rawResponse, IReadOnlyList<NpcActionData> candidateActions, int maxDialogueLength,
            NpcState self, IReadOnlyList<NpcState> presentNpcs, NpcState propagator)
        {
            if (string.IsNullOrWhiteSpace(rawResponse))
                return LlmValidationResult.Failure("빈 응답");

            LlmResponse parsed;
            try
            {
                parsed = JsonUtility.FromJson<LlmResponse>(rawResponse);
            }
            catch
            {
                return LlmValidationResult.Failure("JSON 파싱 실패");
            }

            if (parsed == null)
                return LlmValidationResult.Failure("JSON 파싱 실패");

            if (string.IsNullOrEmpty(parsed.action))
                return LlmValidationResult.Failure("action 없음");

            if (candidateActions == null || candidateActions.Count == 0)
                return LlmValidationResult.Failure("CandidateActions가 비어 있음");

            var matched = candidateActions.FirstOrDefault(a => a.actionId == parsed.action);
            if (matched == null)
                return LlmValidationResult.Failure($"CandidateActions에 없는 action: '{parsed.action}'");

            if (parsed.dialogue == null)
                return LlmValidationResult.Failure("dialogue가 null");

            if (parsed.dialogue.Length > maxDialogueLength)
                return LlmValidationResult.Failure($"dialogue 길이 초과 ({parsed.dialogue.Length} > {maxDialogueLength})");

            if (!JudgmentGroundsValidator.TryValidate(
                    parsed.primaryReason, parsed.profileInfluence, parsed.relationshipInfluence,
                    self, presentNpcs, propagator, out var grounds, out var groundsFailure))
                return LlmValidationResult.Failure(groundsFailure);

            return LlmValidationResult.Success(matched, parsed.dialogue, grounds);
        }

        /// <summary>LLM 이동 응답을 검증한다. "stay"는 유효한 성공 응답(머묾)이고, [이동 후보] 밖의
        /// locationId는 실패로 취급해 RuleBased 폴백을 태운다 - 없는 장소로 이동시키지 않기 위함.
        /// 근거 3필드는 행동 판단과 완전히 같은 규칙으로 검증한다(이동은 카드와 무관하게 매 턴
        /// 호출되므로 전달자가 없고, 관계 근거는 같은 장소 NPC로만 성립한다).</summary>
        public static LlmMoveValidationResult ParseMove(
            string rawResponse, IReadOnlyList<LocationData> candidates,
            NpcState self, IReadOnlyList<NpcState> presentNpcs)
        {
            if (string.IsNullOrWhiteSpace(rawResponse))
                return LlmMoveValidationResult.Failure("빈 응답");

            LlmMoveResponse parsed;
            try
            {
                parsed = JsonUtility.FromJson<LlmMoveResponse>(rawResponse);
            }
            catch
            {
                return LlmMoveValidationResult.Failure("JSON 파싱 실패");
            }

            if (parsed == null || string.IsNullOrEmpty(parsed.destination))
                return LlmMoveValidationResult.Failure("destination 없음");

            if (!JudgmentGroundsValidator.TryValidate(
                    parsed.primaryReason, parsed.profileInfluence, parsed.relationshipInfluence,
                    self, presentNpcs, null, out var grounds, out var groundsFailure))
                return LlmMoveValidationResult.Failure(groundsFailure);

            if (parsed.destination == "stay")
                return LlmMoveValidationResult.Success(null, grounds);

            if (candidates == null || candidates.Count == 0)
                return LlmMoveValidationResult.Failure("이동 후보가 비어 있음");

            var matched = candidates.FirstOrDefault(c => c != null && c.locationId == parsed.destination);
            if (matched == null)
                return LlmMoveValidationResult.Failure($"이동 후보에 없는 destination: '{parsed.destination}'");

            return LlmMoveValidationResult.Success(matched, grounds);
        }

        /// <summary>
        /// 벤치마크 전용 경로. 위 Parse()와 완전히 같은 검증 규칙(빈 응답/JSON 파싱/action 화이트리스트/
        /// dialogue 누락·길이)을 BenchmarkLlmResponse(reason/confidence 포함)에 대해 그대로 적용한다.
        /// 기존 Parse()의 동작/시그니처는 전혀 건드리지 않는다.
        /// </summary>
        public static BenchmarkParseResult ParseForBenchmark(string rawResponse, IReadOnlyList<NpcActionData> candidateActions, int maxDialogueLength)
        {
            if (string.IsNullOrWhiteSpace(rawResponse))
                return BenchmarkParseResult.Failure("빈 응답");

            BenchmarkLlmResponse parsed;
            try
            {
                parsed = JsonUtility.FromJson<BenchmarkLlmResponse>(rawResponse);
            }
            catch
            {
                return BenchmarkParseResult.Failure("JSON 파싱 실패");
            }

            if (parsed == null)
                return BenchmarkParseResult.Failure("JSON 파싱 실패");

            if (string.IsNullOrEmpty(parsed.action))
                return BenchmarkParseResult.Failure("action 없음");

            if (candidateActions == null || candidateActions.Count == 0)
                return BenchmarkParseResult.Failure("CandidateActions가 비어 있음");

            var matched = candidateActions.FirstOrDefault(a => a.actionId == parsed.action);
            if (matched == null)
                return BenchmarkParseResult.Failure($"CandidateActions에 없는 action: '{parsed.action}'");

            if (parsed.dialogue == null)
                return BenchmarkParseResult.Failure("dialogue가 null");

            if (parsed.dialogue.Length > maxDialogueLength)
                return BenchmarkParseResult.Failure($"dialogue 길이 초과 ({parsed.dialogue.Length} > {maxDialogueLength})");

            bool confidenceAvailable = parsed.confidence >= 0f && parsed.confidence <= 1f;
            return BenchmarkParseResult.Success(parsed.action, parsed.dialogue, parsed.reason,
                confidenceAvailable ? parsed.confidence : 0f, confidenceAvailable);
        }
    }
}
