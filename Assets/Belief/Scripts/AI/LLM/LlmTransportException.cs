using System;

namespace Belief.AI.LLM
{
    /// <summary>
    /// 실제 네트워크 Transport 호출 실패를 감싼다. HTTP 상태 코드/원시 응답 본문/취소 여부를
    /// 보존해 BenchmarkLogger가 실패 원인을 그대로 기록할 수 있게 한다.
    /// </summary>
    public class LlmTransportException : Exception
    {
        public long HttpStatusCode { get; }
        public string RawResponseBody { get; }
        public bool WasCanceled { get; }

        public LlmTransportException(string message, long httpStatusCode = 0, string rawResponseBody = null, bool wasCanceled = false)
            : base(message)
        {
            HttpStatusCode = httpStatusCode;
            RawResponseBody = rawResponseBody;
            WasCanceled = wasCanceled;
        }
    }
}
