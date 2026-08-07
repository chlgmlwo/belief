namespace Belief.AI.LLM
{
    /// <summary>지금 응답을 기다리고 있는 LLM 요청이 몇 개인지만 센다. 판단 결과에는 아무 영향도
    /// 주지 않는 순수 관측값이다.
    ///
    /// <b>왜 필요한가:</b> 하단 띠의 "정보 전파중" 표시는 <i>기다리는 동안에만</i> 떠 있어야 하는데,
    /// 예전에는 전달 전체 구간(TargetingController.IsDelivering)에 맞춰 켜 두고 있었다. 그 구간은
    /// 판단이 끝난 뒤의 NPC 대사·이동 연출까지 포함하므로, 이미 세계가 반응하고 있는데도 "기다리는
    /// 중"이라는 표시가 함께 떠 있었다. 여러 NPC가 차례로 판단하는 확산에서는 대기와 연출이 번갈아
    /// 나오기 때문에 어떤 때는 겹치고 어떤 때는 안 겹쳐 더 헷갈렸다.
    ///
    /// 세는 자리는 Transport 한 곳이라 어떤 Thinker를 쓰든 자동으로 잡힌다. 요청은 전부
    /// <see cref="CoroutineRunner"/> 코루틴(메인 스레드)에서 시작·종료되므로 잠금이 필요 없다.</summary>
    public static class LlmRequestMonitor
    {
        /// <summary>응답을 기다리는 중인 요청 수.</summary>
        public static int InFlight { get; private set; }

        /// <summary>하나라도 기다리는 중인지 - UI가 매 프레임 읽는 값이다.</summary>
        public static bool IsWaiting => InFlight > 0;

        public static void Begin() => InFlight++;

        public static void End()
        {
            if (InFlight > 0) InFlight--;
        }

        /// <summary>씬을 새로 열거나 세션을 접을 때처럼 "기다리던 것이 없던 일이 된" 자리에서 쓴다 -
        /// 짝이 안 맞아 값이 남으면 표시가 영영 켜진 채로 굳는다.</summary>
        public static void Reset() => InFlight = 0;
    }
}
