using System;

namespace Belief.AI.LLM.Benchmark
{
    /// <summary>
    /// 벤치마크 전용 응답 스키마. 게임 런타임이 쓰는 LlmResponse(action/dialogue)는 그대로 두고
    /// 건드리지 않는다 - reason/confidence는 게임 로직에 필요 없고(RuleBasedMajorThinker도
    /// DialogueContent도 쓰지 않음) 벤치마크 분석에만 쓰이므로 여기서만 확장한다.
    /// confidence는 모델이 채우지 않으면 JSON에 없을 수 있어 기본값을 "값 없음"을 뜻하는 -1로 둔다.
    /// </summary>
    [Serializable]
    public class BenchmarkLlmResponse
    {
        public string action;
        public string dialogue;
        public string reason;
        public float confidence = -1f;
    }
}
