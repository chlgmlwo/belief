using System;
using System.IO;
using Belief.Debugging;
using UnityEditor;
using UnityEngine;

namespace Belief.EditorTools.Diagnostics
{
    /// <summary>
    /// 판단 기록을 <b>발생 즉시 파일로 흘려보내는</b> 장시간 검증용 보조 기록기.
    ///
    /// 기존 <c>NpcDecisionTraceStore</c>(관찰 창 소유)는 그대로 두고 건드리지 않는다 - 그쪽의
    /// 800건 메모리 상한도 변경하지 않는다. 다만 그 구조로는 자동 진행 도구로 돌린 전체 플레이의
    /// 통계를 낼 수 없었다: 창이 열려 있어야 하고, 풀 플레이 1회가 800건을 넘기며(실측 881건),
    /// Play Mode를 나가는 순간 도메인 리로드로 전부 사라진다.
    ///
    /// 그래서 여기서는 세 가지만 다르게 한다:
    /// <list type="bullet">
    /// <item>구독을 <see cref="InitializeOnLoadAttribute"/>로 붙여 도메인 리로드마다 되살아난다.</item>
    /// <item>켜짐 여부·경로·건수를 EditorPrefs에 둔다 - static 필드는 리로드 때 초기화된다.</item>
    /// <item>메모리에 쌓지 않고 한 줄씩 append한다 - 매 줄이 곧바로 flush·close되므로 Play가
    ///   어떻게 끝나든 그때까지의 기록이 파일에 남는다.</item>
    /// </list>
    ///
    /// <b>안전 경계</b>
    /// <list type="bullet">
    /// <item>Editor 어셈블리에만 존재한다 - 어떤 플레이어 빌드에도 포함되지 않으므로 출시 빌드에서는
    ///   코드 자체가 없다. 추가로 런타임 가드를 한 번 더 둔다.</item>
    /// <item>출력은 <c>Library/BeliefLogs</c> 아래로 강제한다 - Assets 아래에 런타임 로그를 만들면
    ///   에셋으로 임포트되고 git에 섞인다.</item>
    /// <item>세션마다 타임스탬프가 붙은 별도 파일을 만든다 - 이전 세션을 덮어쓰지 않는다.</item>
    /// <item>파일 크기 상한과 보관 세션 수 상한을 둔다.</item>
    /// <item>기록 실패는 게임 진행을 막지 않는다 - 한 번 알리고 스스로 꺼진다.</item>
    /// </list>
    ///
    /// 스키마는 기존 Decision Log(<see cref="NpcDecisionTraceRecord"/> + JsonUtility)를 그대로 쓴다 -
    /// 별도 포맷을 만들지 않으므로 기존 분석 도구가 같은 방식으로 읽는다. 이 스키마에는 API 키·
    /// Authorization 헤더·프록시 주소 같은 비밀값 필드가 존재하지 않는다(게임 판단 데이터만 담긴다).
    /// </summary>
    [InitializeOnLoad]
    public static class TraceStreamRecorder
    {
        const string ActiveKey = "Belief.TraceStream.Active";
        const string PathKey = "Belief.TraceStream.Path";
        const string CountKey = "Belief.TraceStream.Count";

        /// <summary>출력 폴더 - Assets 밖이라 에셋으로 임포트되지 않는다.</summary>
        const string OutDirRelative = "Library/BeliefLogs";

        /// <summary>파일 하나가 이 크기를 넘으면 스스로 멈춘다. 실측 풀 플레이 1회가 약 0.6MB라
        /// 정상 사용에서는 닿지 않고, 폭주했을 때만 걸리는 안전판이다.</summary>
        const long MaxFileBytes = 64L * 1024 * 1024;

        /// <summary>보관할 최대 세션 파일 수 - 초과하면 가장 오래된 것부터 지운다.</summary>
        const int MaxSessionFiles = 20;

        const string FilePrefix = "trace_";

        public static bool IsActive => EditorPrefs.GetBool(ActiveKey, false);
        public static string CurrentPath => EditorPrefs.GetString(PathKey, "");
        public static int Written => EditorPrefs.GetInt(CountKey, 0);

        static TraceStreamRecorder()
        {
            // 중복 구독 방지 - 리로드마다 이 생성자가 다시 돈다.
            NpcDecisionTraceHub.RecordPublished -= OnRecordPublished;
            NpcDecisionTraceHub.RecordPublished += OnRecordPublished;
        }

        static string OutDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", OutDirRelative));

        /// <summary>세션 이름으로 기록을 시작한다. 실제 경로는 이 클래스가 정한다 -
        /// 호출자가 Assets 아래나 임의 위치를 지정할 수 없게 하기 위해서다.
        /// 반환값은 만들어진 파일의 절대 경로(실패 시 null).</summary>
        public static string Begin(string sessionName)
        {
            var safe = Sanitize(sessionName);
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var dir = OutDir;

            try
            {
                Directory.CreateDirectory(dir);
                TrimOldSessions(dir);

                var path = Path.Combine(dir, $"{FilePrefix}{safe}_{stamp}.jsonl");
                File.WriteAllText(path, string.Empty);

                EditorPrefs.SetString(PathKey, path);
                EditorPrefs.SetInt(CountKey, 0);
                EditorPrefs.SetBool(ActiveKey, true);
                Debug.Log($"[TraceStream] 기록 시작 → {path}");
                return path;
            }
            catch (Exception e)
            {
                EditorPrefs.SetBool(ActiveKey, false);
                Debug.LogWarning($"[TraceStream] 기록을 시작하지 못했습니다: {e.Message}");
                return null;
            }
        }

        public static void End()
        {
            if (!IsActive) return;
            EditorPrefs.SetBool(ActiveKey, false);
            Debug.Log($"[TraceStream] 기록 종료 - {Written}건 → {CurrentPath}");
        }

        static void OnRecordPublished(NpcDecisionTraceRecord record)
        {
            if (record == null || !IsActive) return;

            // Editor 어셈블리라 빌드에 포함되지 않지만, 플레이어에서 도는 경로가 생기더라도
            // 기록이 남지 않도록 한 겹 더 막는다.
            if (!Application.isEditor) return;

            var path = CurrentPath;
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                var info = new FileInfo(path);
                if (info.Exists && info.Length >= MaxFileBytes)
                {
                    EditorPrefs.SetBool(ActiveKey, false);
                    Debug.LogWarning($"[TraceStream] 파일이 상한({MaxFileBytes / 1024 / 1024}MB)에 도달해 기록을 멈춥니다: {path}");
                    return;
                }

                // AppendAllText는 매 호출마다 열고 닫으므로 항상 flush된 상태다 -
                // Play가 강제 종료돼도 그때까지의 줄이 파일에 남는다.
                File.AppendAllText(path, JsonUtility.ToJson(record) + Environment.NewLine);
                EditorPrefs.SetInt(CountKey, Written + 1);
            }
            catch (Exception e)
            {
                // 기록 실패가 게임 진행을 막아서는 안 된다 - 한 번 알리고 스스로 꺼진다.
                EditorPrefs.SetBool(ActiveKey, false);
                Debug.LogWarning($"[TraceStream] 기록 실패로 중단합니다: {e.Message}");
            }
        }

        /// <summary>보관 상한을 넘은 오래된 세션 파일을 지운다 - 이 클래스가 만든 파일만 대상이다.</summary>
        static void TrimOldSessions(string dir)
        {
            var files = new DirectoryInfo(dir).GetFiles(FilePrefix + "*.jsonl");
            if (files.Length < MaxSessionFiles) return;

            Array.Sort(files, (a, b) => a.LastWriteTimeUtc.CompareTo(b.LastWriteTimeUtc));
            for (int i = 0; i <= files.Length - MaxSessionFiles; i++)
            {
                try { files[i].Delete(); }
                catch (Exception e) { Debug.LogWarning($"[TraceStream] 오래된 기록 삭제 실패({files[i].Name}): {e.Message}"); }
            }
        }

        static string Sanitize(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "session";
            var invalid = Path.GetInvalidFileNameChars();
            var chars = s.Trim().ToCharArray();
            for (int i = 0; i < chars.Length; i++)
                if (Array.IndexOf(invalid, chars[i]) >= 0) chars[i] = '_';
            return new string(chars);
        }
    }
}
