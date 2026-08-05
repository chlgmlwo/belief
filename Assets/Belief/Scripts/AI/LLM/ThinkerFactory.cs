using UnityEngine;
using Belief.Data;

namespace Belief.AI.LLM
{
    public enum ThinkerMode
    {
        /// <summary>AI를 아예 호출하지 않는다. 자동 테스트와 회귀 검증의 기본값 - 토큰을 쓰지 않고
        /// 결과가 결정적이라 "게임 시스템이 망가졌는지"를 판정하기에 적합하다.</summary>
        RuleOnly,

        /// <summary>네트워크 없이 가짜 응답으로 LLM 경로 전체(프롬프트 조립 → 파싱 → 검증 → 폴백)를
        /// 돌려 보는 모드. 배선이 맞는지 확인할 때 쓴다.</summary>
        FakeLlm,

        /// <summary>실제 AI 호출. LlmProviderConfig의 endpoint로 요청한다(중계 서버 또는 AI 회사 서버).</summary>
        Llm
    }

    /// <summary>
    /// 어떤 Thinker를 쓸지 결정하는 유일한 조립 지점. 새 AI 서비스를 붙이려면
    /// ILlmTransport 구현체 하나만 추가하고 여기 분기를 하나 넣으면 된다 -
    /// LlmMajorThinker/PromptBuilder/ResponseParser는 손대지 않는다.
    /// </summary>
    public static class ThinkerFactory
    {
        /// <summary>timeoutMs는 LlmMajorThinker에 그대로 전달되는 것 외에 여기서 별도로 해석/가공하지
        /// 않는다 - 실제 사용값을 한 곳(LlmMajorThinker.DefaultTimeoutMs 또는 이 인자)에서만 관리하기
        /// 위함. RuleOnly 모드는 이 값을 아예 쓰지 않는다(LlmMajorThinker 자체를 생성하지 않음).
        ///
        /// Llm 모드에서 설정이나 키가 준비되지 않았으면 <b>예외를 던지지 않고 RuleOnly로 내려앉는다</b> -
        /// 설정 실수 하나로 게임이 아예 시작되지 않는 것보다, 규칙 기반으로라도 굴러가면서 경고를
        /// 남기는 쪽이 안전하기 때문이다(이 게임의 폴백 정책과 같은 방향).</summary>
        public static IMajorNpcThinker Create(
            ThinkerMode mode, PromptRepository promptRepository, FakeTransportMode fakeTransportMode,
            int timeoutMs = LlmMajorThinker.DefaultTimeoutMs, LlmProviderConfig providerConfig = null)
        {
            var ruleBased = new RuleBasedMajorThinker();

            switch (mode)
            {
                case ThinkerMode.FakeLlm:
                    return new LlmMajorThinker(new FakeTransport(fakeTransportMode), ruleBased, promptRepository, timeoutMs);

                case ThinkerMode.Llm:
                {
                    if (providerConfig == null)
                    {
                        Debug.LogWarning("[ThinkerFactory] ThinkerMode.Llm인데 LlmProviderConfig가 비어 있어 RuleOnly로 동작합니다 - GameInstaller의 Llm Provider Config를 지정하세요.");
                        return ruleBased;
                    }

                    string apiKey = null;
                    if (!providerConfig.useProxy
                        && !ApiKeyProvider.TryGetApiKey(providerConfig.provider.ToString(), out apiKey))
                    {
                        Debug.LogWarning($"[ThinkerFactory] API 키를 찾을 수 없어 RuleOnly로 동작합니다 (환경 변수 BELIEF_LLM_API_KEY_{providerConfig.provider.ToString().ToUpperInvariant()} 또는 에디터 설정을 확인하세요). 중계 서버를 쓸 계획이면 LlmProviderConfig의 useProxy를 켜세요.");
                        return ruleBased;
                    }

                    var transport = providerConfig.provider switch
                    {
                        LlmProviderType.OpenAi => new OpenAiTransport(providerConfig, apiKey),
                        _ => null
                    };

                    if (transport == null)
                    {
                        Debug.LogWarning($"[ThinkerFactory] 지원하지 않는 provider({providerConfig.provider})라 RuleOnly로 동작합니다.");
                        return ruleBased;
                    }

                    return new LlmMajorThinker(transport, ruleBased, promptRepository, timeoutMs);
                }

                default:
                    return ruleBased;
            }
        }
    }
}
