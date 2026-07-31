using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Belief.AI.LLM.Benchmark
{
    /// <summary>
    /// 벤치마크 호출 결과를 누적 저장하고 JSON/CSV로 내보낸다. PromptRepository(NPC별 최근 1건
    /// 덮어쓰기)는 그대로 두고 손대지 않는다 - 이 클래스가 별도로 다건 이력을 담당한다.
    /// Assets 폴더에는 절대 쓰지 않고 Application.persistentDataPath 아래 쓰기 가능한 경로만 쓴다.
    /// </summary>
    public class BenchmarkLogger
    {
        readonly List<BenchmarkResult> results = new List<BenchmarkResult>();

        public IReadOnlyList<BenchmarkResult> Results => results;

        public void Record(BenchmarkResult result) => results.Add(result);

        public void Clear() => results.Clear();

        public static string RootDirectory
        {
            get
            {
                string dir = Path.Combine(Application.persistentDataPath, "BenchmarkLogs");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        public string ExportJson(string runId, string modelId, string dateStamp)
        {
            string path = Path.Combine(RootDirectory, BuildFileName(runId, modelId, dateStamp, "json"));
            File.WriteAllText(path, ToJsonArray(results), Encoding.UTF8);
            return path;
        }

        public string ExportCsv(string runId, string modelId, string dateStamp)
        {
            string path = Path.Combine(RootDirectory, BuildFileName(runId, modelId, dateStamp, "csv"));
            File.WriteAllText(path, ToCsv(results), Encoding.UTF8);
            return path;
        }

        static string BuildFileName(string runId, string modelId, string dateStamp, string ext) =>
            $"benchmark_{dateStamp}_{Sanitize(modelId)}_{Sanitize(runId)}.{ext}";

        static string Sanitize(string value)
        {
            if (string.IsNullOrEmpty(value)) return "unknown";
            var sb = new StringBuilder(value.Length);
            foreach (char c in value)
                sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_');
            return sb.ToString();
        }

        static string ToJsonArray(List<BenchmarkResult> list)
        {
            // JsonUtility는 최상위 배열을 직접 직렬화하지 못하므로 래퍼 객체로 감싼다.
            var wrapper = new BenchmarkResultListWrapper { items = list.ToArray() };
            return JsonUtility.ToJson(wrapper, true);
        }

        static readonly string[] CsvHeader =
        {
            "benchmarkRunId", "scenarioId", "provider", "modelId", "npcId", "informationCardId",
            "requestTimestamp", "responseTimestamp", "latencyMs", "parseSuccess", "parsedAction",
            "parsedDialogue", "parsedReason", "parsedConfidence", "parsedConfidenceAvailable",
            "inputTokens", "outputTokens", "totalTokens", "tokensAvailable", "errorType", "errorMessage"
        };

        // rawPrompt/rawResponse는 CSV 가독성을 위해 제외한다 - 전체 원문은 JSON export에만 담는다.
        static string ToCsv(List<BenchmarkResult> list)
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(",", CsvHeader));
            foreach (var r in list)
                sb.AppendLine(string.Join(",", CsvRow(r)));
            return sb.ToString();
        }

        static IEnumerable<string> CsvRow(BenchmarkResult r) => new[]
        {
            Csv(r.benchmarkRunId), Csv(r.scenarioId), Csv(r.provider), Csv(r.modelId), Csv(r.npcId), Csv(r.informationCardId),
            Csv(r.requestTimestamp), Csv(r.responseTimestamp), r.latencyMs.ToString(), r.parseSuccess.ToString(), Csv(r.parsedAction),
            Csv(r.parsedDialogue), Csv(r.parsedReason), r.parsedConfidence.ToString("F2"), r.parsedConfidenceAvailable.ToString(),
            r.inputTokens.ToString(), r.outputTokens.ToString(), r.totalTokens.ToString(), r.tokensAvailable.ToString(),
            Csv(r.errorType), Csv(r.errorMessage)
        };

        static string Csv(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            string escaped = value.Replace("\"", "\"\"").Replace("\n", " ").Replace("\r", " ");
            return $"\"{escaped}\"";
        }
    }

    [System.Serializable]
    class BenchmarkResultListWrapper
    {
        public BenchmarkResult[] items;
    }
}
