using System.Threading.Tasks;

namespace Belief.AI
{
    /// <summary>
    /// 통합 판단(Interpretation/Belief/Goal/Action/Destination/Dialogue)을 한 번에 만드는 경로.
    ///
    /// <b>기존 <see cref="IMajorNpcThinker"/>와 완전히 분리된 인터페이스다.</b> 기존 계약
    /// ("Belief는 절대 반환하지 않는다")은 그대로 유지되며, RuleOnly/FakeLlm/Llm 세 모드는
    /// 지금까지와 한 줄도 다르지 않게 동작한다. Belief를 판단 결과로 내놓는 것은 이 인터페이스
    /// 뿐이고, 그것을 쓰는 모드는 IntegratedLlm 하나다.
    ///
    /// 비동기 계약은 기존과 동일하다 - Unity 메인 스레드를 동기적으로 막지 않는다
    /// (GetAwaiter().GetResult()/.Result/.Wait() 금지). 실제로 기다릴 것이 없는 규칙 기반
    /// 구현체는 Task.FromResult로 즉시 완료된 Task를 반환해도 된다.
    ///
    /// 반환 타입이 <see cref="ValidatedNpcJudgment"/>가 아니라 <see cref="NpcJudgmentValidation"/>인
    /// 이유: 구현체는 "판단을 만드는 것"까지만 책임지고, <b>적용해도 되는지의 최종 판정은 호출자가</b>
    /// 최신성·중복 확인과 함께 한 곳에서 내리기 때문이다. 구현체가 직접 적용 가능한 타입을
    /// 만들어 버리면 그 게이트를 우회할 수 있다.
    /// </summary>
    public interface IIntegratedNpcThinker
    {
        Task<NpcJudgmentValidation> DecideAsync(NpcJudgmentContext context, object trace);
    }
}
