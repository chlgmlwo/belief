using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using Belief.Debugging;

namespace Belief.EditorTools
{
    /// <summary>NPC Decision Trace의 Editor 전용 저장소. NpcDecisionTraceHub를 구독해 기록을
    /// 메모리 링 버퍼에 담고, Export를 위한 JSONL 직렬화를 담당한다. UI(NpcDecisionTraceWindow)는
    /// 이 클래스를 통해서만 데이터를 읽는다 - 게임 판단 코드는 이 클래스의 존재를 전혀 모른다.</summary>
    public class NpcDecisionTraceStore
    {
        /// <summary>메모리에 보관할 최대 기록 수 - 초과분은 가장 오래된 것부터 제거한다.
        /// 이 값을 조정하려면 여기 한 곳만 고치면 된다.</summary>
        public const int MaxRecords = 800;

        readonly List<NpcDecisionTraceRecord> records = new List<NpcDecisionTraceRecord>();

        /// <summary>새 기록이 추가되거나 Clear()가 호출되면 알린다 - Window가 Repaint하는 용도.</summary>
        public event Action Changed;

        public bool IsSubscribed { get; private set; }

        public IReadOnlyList<NpcDecisionTraceRecord> Records => records;

        public void Subscribe()
        {
            if (IsSubscribed) return;
            NpcDecisionTraceHub.RecordPublished += OnRecordPublished;
            IsSubscribed = true;
        }

        public void Unsubscribe()
        {
            if (!IsSubscribed) return;
            NpcDecisionTraceHub.RecordPublished -= OnRecordPublished;
            IsSubscribed = false;
        }

        void OnRecordPublished(NpcDecisionTraceRecord record)
        {
            if (record == null) return;
            records.Add(record);
            while (records.Count > MaxRecords)
                records.RemoveAt(0);
            Changed?.Invoke();
        }

        public void Clear()
        {
            records.Clear();
            Changed?.Invoke();
        }

        /// <summary>필터/검색 조건에 맞는 기록만 골라 반환한다 - 원본 리스트는 손대지 않는다.</summary>
        public List<NpcDecisionTraceRecord> Query(TraceFilter filter)
        {
            IEnumerable<NpcDecisionTraceRecord> query = records;

            if (!string.IsNullOrEmpty(filter.NpcId))
                query = query.Where(r => r.NpcId == filter.NpcId);
            if (filter.StageTurn.HasValue)
                query = query.Where(r => r.StageTurn == filter.StageTurn.Value);
            if (!string.IsNullOrEmpty(filter.MissionId))
                query = query.Where(r => r.MissionId == filter.MissionId);
            if (!string.IsNullOrEmpty(filter.ThinkerMode))
                query = query.Where(r => r.ThinkerMode == filter.ThinkerMode);
            if (filter.ResultSourceFilter != TraceResultSourceFilter.Any)
                query = query.Where(r => MatchesResultSource(r, filter.ResultSourceFilter));
            if (!string.IsNullOrEmpty(filter.DecisionType))
                query = query.Where(r => r.DecisionType == filter.DecisionType);
            if (!string.IsNullOrEmpty(filter.CardId))
                query = query.Where(r => r.CardId == filter.CardId);
            if (filter.ErrorOnly)
                query = query.Where(r => r.HasError);

            if (!string.IsNullOrEmpty(filter.SearchText))
            {
                var needle = filter.SearchText.Trim();
                query = query.Where(r => MatchesSearch(r, needle));
            }

            return query.ToList();
        }

        static bool MatchesResultSource(NpcDecisionTraceRecord r, TraceResultSourceFilter f)
        {
            switch (f)
            {
                case TraceResultSourceFilter.RuleOnly: return !r.UsedLlm;
                case TraceResultSourceFilter.Llm: return r.UsedLlm && !r.FallbackOccurred;
                case TraceResultSourceFilter.Fallback: return r.UsedLlm && r.FallbackOccurred;
                default: return true;
            }
        }

        static bool MatchesSearch(NpcDecisionTraceRecord r, string needle)
        {
            return Contains(r.NpcDisplayName, needle) || Contains(r.NpcId, needle)
                || Contains(r.CardTitle, needle) || Contains(r.CardId, needle)
                || Contains(r.CurrentLocationId, needle) || Contains(r.CurrentLocationDisplayName, needle)
                || Contains(r.SelectedDialogueText, needle) || Contains(r.FallbackReason, needle)
                || Contains(r.SelectedDestinationId, needle);
        }

        static bool Contains(string haystack, string needle)
            => !string.IsNullOrEmpty(haystack) && haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>Library/BeliefLogs/ 아래에 JSONL로 저장한다(Assets 밖 - 프로젝트 에셋으로 취급되지
        /// 않음). 반환값은 저장된 절대 경로.</summary>
        public static string ExportJsonl(IEnumerable<NpcDecisionTraceRecord> toExport, string fileNameTimestamp)
        {
            var dir = Path.Combine(Application.dataPath, "..", "Library", "BeliefLogs");
            Directory.CreateDirectory(dir);
            var fileName = $"NpcDecisionTrace_{fileNameTimestamp}.jsonl";
            var path = Path.Combine(dir, fileName);

            var sb = new StringBuilder();
            foreach (var record in toExport)
                sb.AppendLine(JsonUtility.ToJson(record));

            File.WriteAllText(path, sb.ToString());
            return Path.GetFullPath(path);
        }
    }

    public struct TraceFilter
    {
        public string NpcId;
        public int? StageTurn;
        public string MissionId;
        public string ThinkerMode;
        public TraceResultSourceFilter ResultSourceFilter;
        public string DecisionType;
        public string CardId;
        public bool ErrorOnly;
        public string SearchText;
    }

    public enum TraceResultSourceFilter { Any, RuleOnly, Llm, Fallback }
}
