using Belief.AI;

namespace Belief.AI.LLM.Benchmark
{
    /// <summary>
    /// PromptBuilder(게임 정식 프롬프트 생성기)는 절대 수정하지 않는다 - 그 위에 벤치마크 전용
    /// reason/confidence 요청 문구를 얇게 덧붙이는 래퍼일 뿐이다. 게임 플레이가 실제로 만드는
    /// 프롬프트는 이 클래스를 거치지 않으므로 전혀 영향받지 않는다.
    /// </summary>
    public static class BenchmarkPromptBuilder
    {
        public static string Build(NpcThinkContext context, bool requestReasonAndConfidence)
        {
            string basePrompt = PromptBuilder.Build(context);
            if (!requestReasonAndConfidence) return basePrompt;

            return basePrompt +
                "\n[벤치마크 전용 추가 필드]\n" +
                "가능하다면 위 응답에 reason(이 행동을 고른 이유, 한 문장)과 confidence(0.0~1.0 사이 숫자, 이 판단에 대한 확신도)를 추가로 포함하세요.\n" +
                "{\"action\":\"<행동 id>\",\"dialogue\":\"<대사>\",\"reason\":\"<이유>\",\"confidence\":0.0}\n" +
                "reason/confidence를 채울 수 없다면 기존 형식(action, dialogue만)으로 응답해도 됩니다.";
        }
    }
}
