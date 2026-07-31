using System.Threading;
using System.Threading.Tasks;

namespace Belief.AI.LLM
{
    /// <summary>
    /// ILlmTransport는 그대로 두고(SendAsync(prompt) 시그니처 불변), 취소를 지원하는 Transport만
    /// 선택적으로 이 인터페이스도 구현한다. 호출자는 "is ICancellableLlmTransport"로 확인 후에만 사용하므로
    /// FakeTransport 등 기존 구현체는 전혀 영향받지 않는다.
    /// </summary>
    public interface ICancellableLlmTransport
    {
        Task<string> SendAsync(string prompt, CancellationToken cancellationToken);
    }
}
