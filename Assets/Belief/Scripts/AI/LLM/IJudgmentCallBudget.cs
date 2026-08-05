namespace Belief.AI.LLM
{
    /// <summary>
    /// "이번 실행에서 LLM을 몇 번까지 부를 수 있는가"를 판정하는 외부 예산.
    ///
    /// <b>왜 Thinker 안에 카운터를 두지 않는가</b>: 상한은 판단 알고리즘의 성질이 아니라 실행
    /// 환경(파일럿인지, 전체 플레이인지)의 성질이다. 예산을 주입 가능한 값으로 두면 파일럿에서만
    /// 20회 상한을 걸고 일반 경로는 지금까지와 완전히 동일하게(=예산 없음) 돌릴 수 있다.
    ///
    /// 구현체는 <b>거부 사유 문자열을 그대로 FallbackReason으로 쓴다</b> - 그래서 사유는 사람이
    /// 읽고 원인을 특정할 수 있는 값이어야 한다(예: PilotCallLimitExceeded).
    /// </summary>
    public interface IJudgmentCallBudget
    {
        /// <summary>호출 1회를 소비한다. false를 반환하면 Transport를 <b>부르지 않고</b>
        /// 그 판단은 RuleBased 전체 폴백으로 확정된다(부분 적용 없음).</summary>
        bool TryConsume(out string denyReason);
    }

    /// <summary>
    /// 통합 판단이 <b>요청되는 순간</b>을 관찰한다. 계측 전용이라 판단 결과에 관여할 수단이 없다 -
    /// 반환값이 없고, 여기서 던진 예외는 호출자가 삼킨다.
    ///
    /// LLM 성공·폴백을 가리지 않고 <b>모든</b> 통합 판단 요청이 여기를 지난다. 그래서 파일럿의
    /// "실제로 어떤 카드가 누구에게 전달됐는가" 표본을 카드 선택 로직이나 배포 시스템을 전혀
    /// 건드리지 않고 모을 수 있다.
    /// </summary>
    public interface IIntegratedJudgmentObserver
    {
        void OnJudgmentRequested(Belief.AI.NpcJudgmentContext context, Belief.AI.JudgmentRequestIdentity identity);
    }
}
