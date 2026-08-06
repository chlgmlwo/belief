using Belief.AI.LLM;
using UnityEngine;

namespace Belief.Core
{
    /// <summary>
    /// IntegratedLlm 파일럿의 <b>일회성 opt-in</b>과 <b>호출 예산</b>을 함께 관리하는 단일 지점.
    ///
    /// 왜 씬에 저장하지 않는가: thinkerMode를 IntegratedLlm으로 바꿔 저장하면 Zone1 씬 파일이
    /// 바뀌고, 되돌리는 것을 잊으면 다음 실행에서 의도치 않게 요금이 나간다. 그래서 파일럿은
    /// <b>씬을 전혀 건드리지 않고</b> 이 세션 토큰 하나로만 켜진다 - 토큰은
    /// <see cref="TryConsumeOptIn"/>이 읽는 즉시 사라지므로 두 번째 Awake(씬 전환·재시작)는
    /// 절대 파일럿을 켜지 못한다.
    ///
    /// 잔존 방지 3중 장치:
    /// <list type="number">
    /// <item>토큰 저장소가 SessionState라 <b>디스크에 남지 않고</b> Unity를 끄면 사라진다.</item>
    /// <item>Play 시작마다 <see cref="ResetRuntimeState"/>가 활성 세션 static을 지운다 -
    ///   Domain Reload를 꺼 두어 static이 살아남아도 이전 Play의 세션이 이어지지 않는다.</item>
    /// <item>Play 종료·중단 시 에디터 도구가 <see cref="End"/>와 <see cref="Disarm"/>을 호출한다.</item>
    /// </list>
    ///
    /// 에디터가 아닌 빌드에서는 모든 진입점이 아무 일도 하지 않는다 - 파일럿을 켤 수단 자체가
    /// 플레이어 빌드에 존재하지 않는다.
    /// </summary>
    /// <summary>파일럿이 무엇을 관측하려는 실행인가. 안전벽의 강도가 이 값 하나로 갈린다.</summary>
    public enum PilotRunMode
    {
        /// <summary>기본값. Zone1에서만, 20회, 4턴, 토큰은 Awake 1회로 소멸한다.</summary>
        SingleZone,

        /// <summary>Zone1~대도시를 씬 전환까지 이어서 플레이한다. 구역이 바뀔 때마다 새
        /// GameInstaller가 같은 토큰을 다시 소비해야 하므로 <b>토큰이 소멸하지 않는다</b> -
        /// 대신 Play가 끝나면(Disarm) 반드시 사라진다. 예산은 <b>구역당</b> 다시 채워진다.</summary>
        FullPlaythrough,

        /// <summary>
        /// <b>출시 경로.</b> 씬의 thinkerMode가 IntegratedLlm이라 게임 자체가 통합 판단으로 도는
        /// 경우다 - 위 두 모드가 "실험을 좁게 열어 둔다"면 이것은 "정상 플레이가 곧 통합 판단"이다.
        ///
        /// 그래서 파일럿용 안전벽 셋이 전부 풀린다 - 호출 상한 없음, 스테이지 제한 없음,
        /// 출시 빌드에서도 동작. 비용은 게임 안이 아니라 <b>중계 서버에서</b> 통제한다(2026-08-06 결정).
        /// 파일럿 도구로 여는 경로는 이 값을 쓰지 않으므로 실험용 안전벽은 그대로 남는다.
        /// </summary>
        Release,
    }

    public static class IntegratedLlmPilotSession
    {
        /// <summary>단일 구역 파일럿의 최대 LLM 호출 수.
        /// 21번째 요청은 Transport를 부르지 않고 <see cref="CallLimitExceededReason"/>로 거부된다.</summary>
        public const int MaxCalls = 20;

        /// <summary>전 구역 연속 플레이에서 <b>구역 하나당</b> 허용하는 최대 호출 수.
        /// 구역이 바뀌면 다시 이 값만큼 채워지므로, 한 구역이 폭주해도 다음 구역을 굶기지 않는다.</summary>
        public const int FullPlaythroughMaxCallsPerZone = 150;

        /// <summary>지금 실행에 적용되는 실제 상한. Release(출시 경로)는 상한이 없다 -
        /// 정상 플레이가 곧 통합 판단이므로 게임 안에서 판단 횟수를 깎으면 그만큼 NPC가
        /// 규칙 기반으로 퇴화한다. 비용 통제는 중계 서버가 맡는다.</summary>
        public static int CurrentMaxCalls => runMode switch
        {
            PilotRunMode.Release => int.MaxValue,
            PilotRunMode.FullPlaythrough => FullPlaythroughMaxCallsPerZone,
            _ => MaxCalls
        };

        public static PilotRunMode RunMode => runMode;

        /// <summary>상한 초과 시 FallbackReason에 그대로 실리는 값.</summary>
        public const string CallLimitExceededReason = "PilotCallLimitExceeded";

        /// <summary>세션이 이미 끝난 뒤(Play 종료·중단·오류)에 도착한 요청의 거부 사유.
        /// 종료 사유가 구체적으로 남아 있으면 그 값이 대신 실린다 - 예: <see cref="TurnLimitReason"/>.</summary>
        public const string SessionEndedReason = "PilotSessionEnded";

        /// <summary>파일럿 턴 상한(도구가 정한 4턴)에 도달해 세션을 닫았을 때의 사유.
        /// 호출 상한(<see cref="CallLimitExceededReason"/>)과 <b>독립</b>이다 - 둘 중 무엇이 먼저
        /// 걸리든 나머지 하나와 무관하게 새 Transport 호출을 0으로 만든다.</summary>
        public const string TurnLimitReason = "PilotTurnLimitReached";

        const string ArmedTokenKey = "Belief.IntegratedLlmPilot.ArmedSessionId";
        const string ArmedPromptLoggingKey = "Belief.IntegratedLlmPilot.ArmedPromptLogging";
        const string ArmedProviderConfigKey = "Belief.IntegratedLlmPilot.ArmedProviderConfigPath";
        const string ArmedRunModeKey = "Belief.IntegratedLlmPilot.ArmedRunMode";

        // ── 활성 세션(Play 중에만 의미가 있다) ──────────────────────────────────
        static PilotRunMode runMode;
        static int zonesEntered;

        /// <summary>이 실행에서 파일럿이 켜진 구역 수 - 연속 플레이에서 몇 구역을 지났는지.</summary>
        public static int ZonesEntered => zonesEntered;

        static string activeSessionId;
        static int callsUsed;
        static int callsDenied;
        static string endReason;
        static IntegratedLlmPilotCoverage coverage;

        /// <summary>이 세션에서 실제로 판단된 카드 표본. 세션이 열리면 만들어지고 <see cref="Clear"/>
        /// 전까지 유지된다 - <see cref="End"/> 후에도 살아 있어야 종료 보고를 쓸 수 있다.</summary>
        public static IntegratedLlmPilotCoverage Coverage => coverage;

        public static bool IsActive => !string.IsNullOrEmpty(activeSessionId);
        public static string ActiveSessionId => activeSessionId;
        public static int CallsUsed => callsUsed;

        /// <summary>상한 초과나 세션 종료로 거부된 요청 수 - 전부 RuleBased 전체 폴백으로 처리됐다.</summary>
        public static int CallsDenied => callsDenied;

        /// <summary>세션이 끝난 이유. 아직 진행 중이면 null.</summary>
        public static string EndReason => endReason;

        public static int CallsRemaining => IsActive ? Mathf.Max(0, CurrentMaxCalls - callsUsed) : 0;

        // ── 무장(Arm) - 에디터 도구만 호출한다 ──────────────────────────────────

        /// <summary>다음 Play 진입 <b>1회</b>에 한해 파일럿을 켜도록 토큰을 남긴다.</summary>
        /// <param name="logPrompts">프롬프트 원문 기록 - 기본은 꺼짐.</param>
        /// <param name="providerConfigPath">씬의 GameInstaller에 LlmProviderConfig가 비어 있을 때
        /// 대신 쓸 자산 경로. 씬을 수정하지 않고 파일럿을 켜기 위한 통로다 - 비워 두면 씬 값만 쓴다.</param>
        public static void Arm(string sessionId, bool logPrompts = false, string providerConfigPath = null,
            PilotRunMode mode = PilotRunMode.SingleZone)
        {
#if UNITY_EDITOR
            if (string.IsNullOrEmpty(sessionId)) return;
            UnityEditor.SessionState.SetString(ArmedTokenKey, sessionId);
            UnityEditor.SessionState.SetBool(ArmedPromptLoggingKey, logPrompts);
            UnityEditor.SessionState.SetString(ArmedProviderConfigKey, providerConfigPath ?? "");
            UnityEditor.SessionState.SetInt(ArmedRunModeKey, (int)mode);
#endif
        }

        public static bool IsArmed
        {
            get
            {
#if UNITY_EDITOR
                return !string.IsNullOrEmpty(UnityEditor.SessionState.GetString(ArmedTokenKey, ""));
#else
                return false;
#endif
            }
        }

        /// <summary>토큰을 버린다. 무장한 뒤 Play에 들어가지 않았거나, Play가 끝났을 때 호출한다.</summary>
        public static void Disarm()
        {
#if UNITY_EDITOR
            UnityEditor.SessionState.EraseString(ArmedTokenKey);
            UnityEditor.SessionState.EraseBool(ArmedPromptLoggingKey);
            UnityEditor.SessionState.EraseString(ArmedProviderConfigKey);
            UnityEditor.SessionState.EraseInt(ArmedRunModeKey);
#endif
        }

        /// <summary>
        /// 토큰을 <b>소비</b>한다. 성공하면 토큰은 즉시 사라지므로 같은 Play 안의 두 번째 호출
        /// (씬 전환으로 GameInstaller가 다시 Awake되는 경우 등)은 반드시 false다.
        /// </summary>
        /// <param name="providerConfig">무장 시 지정한 대체 설정. 없으면 null이고, 호출자는
        /// 씬에 지정된 값을 그대로 쓴다.</param>
        public static bool TryConsumeOptIn(out string sessionId, out bool logPrompts,
            out Belief.Data.LlmProviderConfig providerConfig)
        {
            sessionId = null;
            logPrompts = false;
            providerConfig = null;
#if UNITY_EDITOR
            string token = UnityEditor.SessionState.GetString(ArmedTokenKey, "");
            if (string.IsNullOrEmpty(token)) return false;

            logPrompts = UnityEditor.SessionState.GetBool(ArmedPromptLoggingKey, false);
            runMode = (PilotRunMode)UnityEditor.SessionState.GetInt(ArmedRunModeKey, (int)PilotRunMode.SingleZone);

            string configPath = UnityEditor.SessionState.GetString(ArmedProviderConfigKey, "");
            if (!string.IsNullOrEmpty(configPath))
                providerConfig = UnityEditor.AssetDatabase.LoadAssetAtPath<Belief.Data.LlmProviderConfig>(configPath);

            // 단일 구역 파일럿은 여기서 토큰을 버린다 - 두 번째 Awake가 절대 파일럿을 못 켠다.
            // 전 구역 연속 플레이는 반대로 <b>다음 구역의 Awake가 다시 소비해야</b> 이어지므로
            // 토큰을 남긴다. 남겨도 Play가 끝나면 러너가 Disarm하므로 편집 모드로 새지 않는다.
            if (runMode != PilotRunMode.FullPlaythrough) Disarm();

            activeSessionId = token;
            callsUsed = 0;      // 예산은 구역 단위로 다시 채워진다
            callsDenied = 0;
            endReason = null;
            coverage = new IntegratedLlmPilotCoverage();
            zonesEntered++;
            sessionId = token;
            return true;
#else
            return false;
#endif
        }

        /// <summary>세션을 끝낸다. 이후 도착하는 요청은 Transport를 타지 못하고 전부 RuleBased로
        /// 내려간다 - 늦은 응답이나 중단 이후 요청이 월드에 닿는 경로를 여기서 끊는다.</summary>
        public static void End(string reason)
        {
            if (!IsActive) return;
            endReason = string.IsNullOrEmpty(reason) ? "Ended" : reason;
            activeSessionId = null;
        }

        /// <summary>Play가 시작될 때마다 활성 세션 static을 지운다. Domain Reload 설정과 무관하게
        /// 실행되므로, 이전 Play의 세션·카운터가 다음 Play로 이어지지 않는다.
        /// 무장 토큰(SessionState)은 여기서 건드리지 않는다 - 그 토큰은 <b>이번</b> Play를 위해
        /// 방금 남긴 것이므로 지우면 파일럿이 켜지지 않는다.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetRuntimeState() => ClearAll();

        /// <summary>세션·카운터·표본을 전부 지운다. <see cref="End"/>는 이것들을 남기는데
        /// (종료 보고에 필요하다), 보고가 끝난 뒤에도 그대로 두면 다음 실행이나 결정적 검증의
        /// 수치가 지난 실행과 섞인다. 그래서 보고 직후에 이 메서드를 부른다.</summary>
        public static void Clear() => ClearAll();

        static void ClearAll()
        {
            activeSessionId = null;
            callsUsed = 0;
            callsDenied = 0;
            endReason = null;
            coverage = null;
            runMode = PilotRunMode.SingleZone;   // 기본값 = 가장 강한 안전벽
            zonesEntered = 0;
        }

        /// <summary>호출 1회 소비 판정. <see cref="IntegratedLlmPilotCallBudget"/>만 사용한다.</summary>
        internal static bool TryConsumeCall(string sessionId, out string denyReason)
        {
            if (!IsActive || sessionId != activeSessionId)
            {
                // 왜 닫혔는지가 남아 있으면 그것을 그대로 사유로 쓴다 - 그래야 판단 기록의
                // FallbackReason만 보고 "턴 상한 때문인지, 오류 중단 때문인지"를 구분할 수 있다.
                denyReason = string.IsNullOrEmpty(endReason) ? SessionEndedReason : endReason;
                callsDenied++;
                return false;
            }

            if (callsUsed >= CurrentMaxCalls)
            {
                denyReason = CallLimitExceededReason;
                callsDenied++;
                return false;
            }

            callsUsed++;
            denyReason = null;
            return true;
        }

        /// <summary>
        /// opt-in 토큰 없이 세션을 직접 연다. 쓰는 곳은 두 군데뿐이다 -
        /// 결정적 검증(가짜 Transport)과, 씬 값으로 IntegratedLlm이 켜진 경우 상한을 강제로
        /// 걸어 주는 GameInstaller의 방어 경로. <b>정상 파일럿은 <see cref="TryConsumeOptIn"/>만
        /// 쓴다</b> - 이 메서드는 일회성 보장을 우회하므로 게임 진행 코드에서 부르지 않는다.
        /// </summary>
        public static void BeginSession(string sessionId) => BeginSession(sessionId, PilotRunMode.SingleZone);

        /// <summary><paramref name="mode"/>로 세션을 연다. 출시 경로(<see cref="PilotRunMode.Release"/>)는
        /// 씬 값으로 IntegratedLlm이 켜졌을 때 GameInstaller가 이 오버로드로 시작한다.</summary>
        public static void BeginSession(string sessionId, PilotRunMode mode)
        {
            activeSessionId = sessionId;
            runMode = mode;
            callsUsed = 0;
            callsDenied = 0;
            endReason = null;
            coverage = new IntegratedLlmPilotCoverage();
        }

        public static string Describe() =>
            IsActive
                ? $"세션 {activeSessionId} [{runMode}] - 호출 {callsUsed}/{CurrentMaxCalls}, 거부 {callsDenied}, 구역 {zonesEntered}번째"
                : $"세션 없음 (무장 {(IsArmed ? "됨" : "안 됨")}, 마지막 종료 사유 {endReason ?? "-"}, 호출 {callsUsed}, 거부 {callsDenied})";
    }

    /// <summary>
    /// 파일럿 세션의 호출 예산을 <see cref="IJudgmentCallBudget"/>로 노출하는 어댑터.
    /// 자신이 발급된 세션이 아직 살아 있을 때만 호출을 허용하므로, Play가 끝난 뒤 남아 있던
    /// 판단이 뒤늦게 Transport를 부르는 일이 없다.
    /// </summary>
    public sealed class IntegratedLlmPilotCallBudget : IJudgmentCallBudget
    {
        readonly string sessionId;

        public IntegratedLlmPilotCallBudget(string sessionId) { this.sessionId = sessionId; }

        public bool TryConsume(out string denyReason) =>
            IntegratedLlmPilotSession.TryConsumeCall(sessionId, out denyReason);
    }
}
