using System.Threading.Tasks;

namespace Belief.AI.LLM
{
    /// <summary>
    /// 실제 어떤 LLM 서비스를 쓰는지는 이 인터페이스 뒤에 완전히 숨긴다.
    /// 프로젝트 코드 어디에도 특정 서비스 이름/SDK가 등장하지 않는다 - 지금은
    /// FakeTransport만 존재하고, 나중에 실제 서비스를 붙일 때는 이 인터페이스의
    /// 구현체 하나만 새로 추가하면 된다(ThinkerFactory에 분기 하나 추가).
    /// </summary>
    public interface ILlmTransport
    {
        Task<string> SendAsync(string prompt);
    }
}
