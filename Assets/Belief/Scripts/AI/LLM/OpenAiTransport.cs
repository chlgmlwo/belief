using System;
using System.Collections;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using Belief.Data;

namespace Belief.AI.LLM
{
    [Serializable]
    class OpenAiChatMessage
    {
        public string role;
        public string content;
    }

    [Serializable]
    class OpenAiResponseFormat
    {
        public string type;
    }

    [Serializable]
    class OpenAiChatRequest
    {
        public string model;
        public float temperature;
        public int max_tokens;
        public OpenAiChatMessage[] messages;
        public OpenAiResponseFormat response_format;
    }

    [Serializable]
    class OpenAiChatResponseChoice
    {
        public OpenAiChatMessage message;
    }

    [Serializable]
    class OpenAiUsage
    {
        public int prompt_tokens;
        public int completion_tokens;
        public int total_tokens;
    }

    [Serializable]
    class OpenAiChatResponseEnvelope
    {
        public OpenAiChatResponseChoice[] choices;
        public OpenAiUsage usage;
    }

    /// <summary>
    /// OpenAI Chat Completions 호환 엔드포인트를 UnityWebRequest 코루틴으로 호출하는 실제 Transport.
    /// ILlmTransport 뒤에 완전히 숨어 있어 PromptBuilder/ResponseParser/LlmMajorThinker는 이 클래스의
    /// 존재를 전혀 모른다.
    ///
    /// 중요: 이 Transport는 게임 플레이 경로(ThinkerFactory/GameInstaller)에는 연결하지 않는다 -
    /// LlmMajorThinker는 이제 완전히 비동기(Task 기반, Timeout 포함)라 더 이상 메인 스레드가 막히지는
    /// 않지만, 실제 API 키/네트워크 연동은 여전히 별도 검토 없이 게임 플레이에 자동으로 붙이지 않는다는
    /// 기존 정책은 그대로 유지한다. 이 Transport는 BenchmarkRunner처럼 진짜 비동기 흐름(async/await)으로
    /// 호출하는 곳에서만 사용한다.
    ///
    /// API 키는 생성자로 주입받을 뿐 어디에도 저장/로그하지 않는다 - 호출자(BenchmarkRunner)가
    /// ApiKeyProvider로 얻은 값을 그대로 넘긴다.
    /// </summary>
    public class OpenAiTransport : ILlmTransport, ICancellableLlmTransport, ITokenUsageReporting
    {
        readonly LlmProviderConfig config;
        readonly string apiKey;

        OpenAiUsage lastUsage;

        public OpenAiTransport(LlmProviderConfig config, string apiKey)
        {
            this.config = config;
            this.apiKey = apiKey;
        }

        public Task<string> SendAsync(string prompt) => SendAsync(prompt, CancellationToken.None);

        public Task<string> SendAsync(string prompt, CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<string>();
            CoroutineRunner.Instance.StartCoroutine(SendRoutine(prompt, cancellationToken, tcs));
            return tcs.Task;
        }

        public bool TryGetLastUsage(out int inputTokens, out int outputTokens, out int totalTokens)
        {
            if (lastUsage == null)
            {
                inputTokens = 0;
                outputTokens = 0;
                totalTokens = 0;
                return false;
            }

            inputTokens = lastUsage.prompt_tokens;
            outputTokens = lastUsage.completion_tokens;
            totalTokens = lastUsage.total_tokens;
            return true;
        }

        IEnumerator SendRoutine(string prompt, CancellationToken cancellationToken, TaskCompletionSource<string> tcs)
        {
            // 응답을 기다리는 동안만 세어 둔다 - 하단 띠의 "정보 전파중" 표시가 이 값을 읽는다.
            // try/finally로 감싸는 이유는 아래에 yield break가 다섯 군데나 있고, 코루틴이 중간에
            // 멈춰도(오브젝트 파괴 등) 감소가 반드시 실행돼야 하기 때문이다. 하나라도 새면 표시가
            // 영영 켜진 채로 굳는다.
            LlmRequestMonitor.Begin();
            try
            {
                // 중계 서버 모드에서는 키가 클라이언트에 없는 것이 정상이다 - 키는 서버가 갖고 있다.
                if (!config.useProxy && string.IsNullOrEmpty(apiKey))
                {
                    tcs.SetException(new LlmTransportException("API 키가 설정되어 있지 않습니다 (환경 변수 또는 로컬 개발 설정을 확인하세요)."));
                    yield break;
                }

                string requestBody = BuildRequestBody(prompt);

                using (var request = new UnityWebRequest(config.endpoint, "POST"))
                {
                    byte[] bodyRaw = Encoding.UTF8.GetBytes(requestBody);
                    request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                    request.downloadHandler = new DownloadHandlerBuffer();
                    request.SetRequestHeader("Content-Type", "application/json");
                    // 중계 서버 모드에서는 Authorization을 붙이지 않는다 - 키를 클라이언트가 들고 있지
                    // 않을뿐더러, 브라우저에서 이 헤더를 붙이면 preflight(OPTIONS) 요청이 추가로 발생한다.
                    if (!config.useProxy) request.SetRequestHeader("Authorization", "Bearer " + apiKey);
                    request.timeout = Mathf.Max(1, config.timeoutSeconds);

                    var operation = request.SendWebRequest();

                    while (!operation.isDone)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            request.Abort();
                            tcs.SetException(new LlmTransportException("요청이 취소되었습니다.", wasCanceled: true));
                            yield break;
                        }
                        yield return null;
                    }

                    if (request.result == UnityWebRequest.Result.ConnectionError ||
                        request.result == UnityWebRequest.Result.DataProcessingError)
                    {
                        tcs.SetException(new LlmTransportException(
                            $"네트워크 오류: {request.error}", request.responseCode, request.downloadHandler?.text));
                        yield break;
                    }

                    if (request.result == UnityWebRequest.Result.ProtocolError)
                    {
                        tcs.SetException(new LlmTransportException(
                            $"HTTP 오류 {request.responseCode}: {request.error}", request.responseCode, request.downloadHandler?.text));
                        yield break;
                    }

                    string rawBody = request.downloadHandler.text;
                    ParseAndComplete(rawBody, request.responseCode, tcs);
                }
            }
            finally
            {
                LlmRequestMonitor.End();
            }
        }

        void ParseAndComplete(string rawBody, long statusCode, TaskCompletionSource<string> tcs)
        {
            try
            {
                var parsed = JsonUtility.FromJson<OpenAiChatResponseEnvelope>(rawBody);
                if (parsed?.choices == null || parsed.choices.Length == 0)
                {
                    tcs.SetException(new LlmTransportException("응답에 choices가 없습니다.", statusCode, rawBody));
                    return;
                }

                lastUsage = parsed.usage;
                tcs.SetResult(parsed.choices[0].message.content);
            }
            catch (Exception ex)
            {
                tcs.SetException(new LlmTransportException($"응답 본문 파싱 실패: {ex.Message}", statusCode, rawBody));
            }
        }

        string BuildRequestBody(string prompt)
        {
            var payload = new OpenAiChatRequest
            {
                model = config.modelId,
                temperature = config.temperature,
                max_tokens = config.maxOutputTokens,
                messages = new[] { new OpenAiChatMessage { role = "user", content = prompt } },
                response_format = new OpenAiResponseFormat { type = config.structuredOutput ? "json_object" : "text" }
            };
            return JsonUtility.ToJson(payload);
        }
    }
}
