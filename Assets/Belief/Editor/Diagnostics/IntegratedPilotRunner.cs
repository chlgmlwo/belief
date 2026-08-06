using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Belief.AI.LLM;
using Belief.Core;
using Belief.Data;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Belief.EditorTools.Diagnostics
{
    /// <summary>
    /// Zone1 IntegratedLlm 파일럿의 <b>유일한 실행 진입점</b>. Editor 폴더에 있으므로 플레이어
    /// 빌드에는 포함되지 않는다.
    ///
    /// <b>이 도구는 실제 OpenAI API를 호출해 요금이 발생한다.</b> 그래서 실행 전 확인 대화상자로
    /// 상한과 예상 비용을 보여주고, 취소하면 무장조차 하지 않는다.
    ///
    /// 안전 설계:
    /// <list type="bullet">
    /// <item><b>씬을 수정하지 않는다.</b> thinkerMode를 IntegratedLlm으로 바꿔 저장하는 대신
    ///   <see cref="IntegratedLlmPilotSession"/>의 일회성 토큰만 남긴다 - 씬은 RuleOnly인 채로
    ///   남고 Dirty가 되지 않는다. 그래서 "되돌리는 것을 잊어 다음 실행에 요금이 나가는" 사고가
    ///   구조적으로 불가능하다.</item>
    /// <item>토큰은 GameInstaller.Awake가 <b>한 번</b> 소비하면 사라진다 - 미션 재시작이나 Zone2
    ///   전환으로 다시 Awake돼도 파일럿은 켜지지 않는다.</item>
    /// <item>호출은 <see cref="IntegratedLlmPilotSession.MaxCalls"/>회를 넘지 못한다. 초과 요청은
    ///   Transport를 부르지 않고 RuleBased 전체 폴백으로 확정된다(부분 적용 없음).</item>
    /// <item>Play 종료·중단·Console Error 어느 경우든 세션을 닫는다 - 닫힌 뒤의 요청은
    ///   전부 RuleBased로 내려가므로 늦은 응답이 월드에 닿지 못한다.</item>
    /// <item>실행 전 사전 점검에서 조건이 하나라도 어긋나면 <b>씬을 고치지 않고 거부</b>한다.</item>
    /// </list>
    ///
    /// 실제 API를 쓰기 전에 <see cref="IntegratedPilotFakeCheck"/>(가짜 Transport)로 결정적
    /// 검증을 먼저 통과시키는 것을 전제로 한다.
    /// </summary>
    [InitializeOnLoad]
    public static class IntegratedPilotRunner
    {
        public const string PilotScenePath = "Assets/Belief/Scenes/Zone1.unity";

        /// <summary>씬 GameInstaller의 llmProviderConfig가 비어 있을 때 파일럿에만 임시로 쓸 자산.
        /// Shadow 하네스가 쓰던 것과 같은 자산이라 호출 방식이 갈리지 않는다.</summary>
        public const string DefaultProviderConfigPath = "Assets/Belief/Data/AI/LlmProviderConfig_Proxy.asset";

        /// <summary>
        /// 파일럿이 진행할 최대 턴 수. <b>StageData.maxTurns는 건드리지 않는다</b> - 이 상한은
        /// 게임 규칙이 아니라 도구의 관측 창이므로, 여기서 세어 세션을 닫고 Play를 끄는 것으로만
        /// 지킨다(TurnSystem·미션 조건 무변경).
        ///
        /// 호출 상한(20회)과 <b>완전히 독립</b>이다 - 카드를 적게 써서 호출이 남아도 4턴이면 끝나고,
        /// 4턴이 되기 전에 호출을 다 써도 그 시점부터 전부 RuleBased 폴백이 된다.
        /// </summary>
        public const int MaxPilotTurns = 4;

        /// <summary>계획 안내 문구에서 쓰는 이름(값은 <see cref="MaxPilotTurns"/>와 같다).</summary>
        public const int ExpectedMaxTurns = MaxPilotTurns;

        /// <summary>턴 상한 도달 후 진행 중이던 요청이 정리될 때까지 기다리는 여유(ms).
        /// 어떤 요청도 llmTimeoutMs 안에 성공이나 폴백으로 끝나므로, 그 위에 이만큼만 더 준다.</summary>
        const int DrainSlackMs = 2000;

        public const int MinPlannedCards = 4;
        public const int MaxPlannedCards = 6;

        /// <summary>비용 안내용 실측 평균(2026-08-06 Shadow 표본 14건). gpt-4o-mini 단가로 환산한다.</summary>
        const int AvgInputTokens = 1363;
        const int AvgOutputTokens = 149;
        const double InputPricePerMillion = 0.15;
        const double OutputPricePerMillion = 0.60;

        static double EstimatedCost =>
            IntegratedLlmPilotSession.MaxCalls
            * (AvgInputTokens / 1_000_000.0 * InputPricePerMillion
             + AvgOutputTokens / 1_000_000.0 * OutputPricePerMillion);

        /// <summary>이 파일럿 실행에서 Console Error가 하나라도 났는지 - 중단 기준 중 하나다.</summary>
        static bool sawConsoleError;
        static string firstConsoleError;

        // ── 턴 감시 상태 ────────────────────────────────────────────────────────
        // 런타임 이벤트 버스를 구독하지 않고 에디터 idle에서 TurnSystem을 읽기만 한다 - 그래서
        // 게임 쪽에 남는 구독자가 0이고, Play가 어떻게 끝나든 정리할 것이 에디터 쪽밖에 없다.
        static GameInstaller monitored;
        static int startStageTurn = -1;
        static int startMissionTurn = -1;
        static int lastStageTurn = -1;
        static bool turnLimitReached;
        static double drainDeadline = -1;

        /// <summary>턴 감시나 정리 대기가 걸려 있는 상태인지 - 종료 후 잔존 확인용.</summary>
        internal static bool IsMonitoring => startStageTurn >= 0 || drainDeadline > 0;

        /// <summary>감시가 붙잡은 시작 누적 턴. 아직 안 붙었으면 -1.</summary>
        internal static int MonitorStartStageTurn => startStageTurn;

        internal static bool TurnLimitReached => turnLimitReached;

        /// <summary>결정적 검증이 감시 상태를 초기화하기 위한 진입점. 실제 흐름에서는
        /// Play 종료·EditMode 복귀가 같은 일을 한다.</summary>
        internal static void ResetMonitorForCheck() => ResetMonitor();

        // Domain Reload가 켜져 있든 꺼져 있든 도메인이 올라올 때마다 다시 등록된다 -
        // 훅이 사라진 채로 파일럿이 도는 상태가 생기지 않는다.
        static IntegratedPilotRunner()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            UnityEngine.Application.logMessageReceived -= OnLogMessage;
            UnityEngine.Application.logMessageReceived += OnLogMessage;
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.update += OnEditorUpdate;
        }

        // ── 실행 ────────────────────────────────────────────────────────────────

        [MenuItem("BELIEF/Diagnostics/Integrated Pilot - 실제 API 실행", priority = 110)]
        static void RunPilot()
        {
            if (UnityEngine.Application.isPlaying)
            {
                EditorUtility.DisplayDialog("Integrated Pilot",
                    "Play Mode를 먼저 끄세요.\n\n파일럿 opt-in은 GameInstaller.Awake에서 소비되므로 "
                    + "Play에 들어가기 전에 무장해야 합니다.", "확인");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var scene = EditorSceneManager.OpenScene(PilotScenePath, OpenSceneMode.Single);
            if (!scene.IsValid())
            {
                EditorUtility.DisplayDialog("Integrated Pilot", $"{PilotScenePath}를 열 수 없습니다.", "확인");
                return;
            }

            // 사전 점검 - 씬을 고치지 않고 읽기만 한다(SerializedObject 읽기는 Dirty를 만들지 않는다).
            if (!Preflight(out string blocker, out string plan, out string providerConfigPath))
            {
                EditorUtility.DisplayDialog("Integrated Pilot - 실행할 수 없습니다",
                    blocker + "\n\n씬은 수정하지 않았습니다.", "확인");
                Debug.LogWarning("[IntegratedPilot] 사전 점검 실패 - API 호출 0회.\n" + blocker);
                return;
            }

            bool go = EditorUtility.DisplayDialog(
                "Integrated Pilot - 실제 API를 호출합니다",
                "Zone1에서 통합 판단(IntegratedLlm) 결과가 <실제 월드에> 적용됩니다. 요금이 발생합니다.\n\n"
                + $"• 최대 호출 수 : {IntegratedLlmPilotSession.MaxCalls}회 (초과분은 RuleBased 전체 폴백)\n"
                + $"• 예상 최대 비용 : 약 ${EstimatedCost:F4} (gpt-4o-mini, 실측 평균 기준)\n"
                + $"• 최대 턴 : {MaxPilotTurns}턴 - 도달하면 자동으로 세션을 닫고 Play Mode를 종료합니다\n"
                + $"• 권장 카드 : {MinPlannedCards}~{MaxPlannedCards}장 (드로우는 조작하지 않습니다)\n"
                + "• 프롬프트 원문 기록 : 꺼짐\n\n"
                + plan + "\n"
                + "확인을 누르면 씬을 수정하지 않은 채 Play Mode로 들어갑니다.\n"
                + "중단하려면 Play를 끄거나 BELIEF/Diagnostics/Integrated Pilot - 즉시 중단을 쓰세요.",
                "실행", "취소");

            if (!go)
            {
                IntegratedLlmPilotSession.Disarm();
                Debug.Log("[IntegratedPilot] 취소했습니다 - 무장하지 않았고 API 호출 0회입니다.");
                return;
            }

            sawConsoleError = false;
            firstConsoleError = null;

            string sessionId = "pilot-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            IntegratedLlmPilotSession.Arm(sessionId, logPrompts: false, providerConfigPath: providerConfigPath);
            Debug.LogWarning($"[IntegratedPilot] 무장했습니다 ({sessionId}). Play 진입 시 1회만 소비됩니다.\n" + plan);

            EditorApplication.isPlaying = true;
        }

        /// <summary>
        /// Zone1부터 대도시까지 씬 전환을 넘어 이어서 플레이한다. 단일 구역 파일럿과 다른 점은
        /// 셋뿐이다 - 스테이지 제한 해제, 예산이 <b>구역당</b> 다시 채워짐, 4턴 자동 중단 없음.
        /// 출시 빌드 차단·씬 무변경·중복/Stale 차단·전체 폴백은 그대로다.
        /// </summary>
        [MenuItem("BELIEF/Diagnostics/Integrated Pilot - 전 구역 연속 플레이 (실제 API)", priority = 114)]
        static void RunFullPlaythrough()
        {
            if (UnityEngine.Application.isPlaying)
            {
                EditorUtility.DisplayDialog("Integrated Pilot", "Play Mode를 먼저 끄세요.", "확인");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(PilotScenePath, OpenSceneMode.Single);

            if (!Preflight(out string blocker, out string plan, out string providerConfigPath))
            {
                EditorUtility.DisplayDialog("Integrated Pilot - 실행할 수 없습니다",
                    blocker + "\n\n씬은 수정하지 않았습니다.", "확인");
                return;
            }

            int perZone = IntegratedLlmPilotSession.FullPlaythroughMaxCallsPerZone;
            double worstCost = perZone * 4
                * (AvgInputTokens / 1_000_000.0 * InputPricePerMillion
                 + AvgOutputTokens / 1_000_000.0 * OutputPricePerMillion);

            bool go = EditorUtility.DisplayDialog(
                "Integrated Pilot - 전 구역 연속 플레이 (실제 API)",
                "Zone1 → Zone2 → Zone3 → 대도시를 통합 판단(IntegratedLlm)으로 이어서 플레이합니다.\n"
                + "결과는 실제 월드에 적용되고 요금이 발생합니다.\n\n"
                + $"• 호출 상한 : 구역당 {perZone}회 (구역이 바뀔 때마다 다시 채워짐)\n"
                + $"• 최악 비용 : 약 ${worstCost:F3} (4구역 전부 상한까지 쓴 경우)\n"
                + "• 턴 자동 중단 : 없음 (끝까지 진행)\n"
                + "• 프롬프트 원문 기록 : 꺼짐\n"
                + "• 씬 · StageData · 카드 · 미션 데이터 : 수정하지 않음\n\n"
                + plan + "\n"
                + "중단하려면 Play를 끄거나 'Integrated Pilot - 즉시 중단'을 쓰세요.",
                "실행", "취소");

            if (!go)
            {
                IntegratedLlmPilotSession.Disarm();
                Debug.Log("[IntegratedPilot] 취소했습니다 - 무장하지 않았고 API 호출 0회입니다.");
                return;
            }

            sawConsoleError = false;
            firstConsoleError = null;

            string sessionId = "full-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            IntegratedLlmPilotSession.Arm(sessionId, logPrompts: false,
                providerConfigPath: providerConfigPath, mode: PilotRunMode.FullPlaythrough);
            Debug.LogWarning($"[IntegratedPilot] 전 구역 연속 플레이로 무장했습니다 ({sessionId}). 구역당 {perZone}회.");

            EditorApplication.isPlaying = true;
        }

        /// <summary>무장하지 않고 사전 점검과 권장 표본 계획만 본다 - API 호출 0회, 씬 무변경.
        /// 실제 실행 전에 조건이 맞는지 확인할 때 쓴다.</summary>
        [MenuItem("BELIEF/Diagnostics/Integrated Pilot - 사전 점검만 (호출 0회)", priority = 111)]
        static void PreflightOnly()
        {
            if (Preflight(out string blocker, out string plan))
                Debug.Log($"[IntegratedPilot] 사전 점검 통과 - 실행하면 최대 {IntegratedLlmPilotSession.MaxCalls}회, 약 ${EstimatedCost:F4}.\n" + plan);
            else
                Debug.LogWarning("[IntegratedPilot] 사전 점검 실패 - 씬은 수정하지 않았습니다.\n" + blocker);
        }

        [MenuItem("BELIEF/Diagnostics/Integrated Pilot - 상태 보기", priority = 112)]
        static void ShowStatus()
        {
            Debug.Log("[IntegratedPilot] " + IntegratedLlmPilotSession.Describe()
                    + (sawConsoleError ? $"\n  Console Error 발생: {firstConsoleError}" : ""));
        }

        /// <summary>Play를 끄지 않고 파일럿만 닫는다. 이후 판단은 전부 RuleBased 전체 폴백이 되므로
        /// 게임은 그대로 계속 굴러간다 - 중단이 곧 게임 정지는 아니다.</summary>
        [MenuItem("BELIEF/Diagnostics/Integrated Pilot - 즉시 중단", priority = 113)]
        static void AbortNow()
        {
            IntegratedLlmPilotSession.End("ManualAbort");
            IntegratedLlmPilotSession.Disarm();
            Debug.LogWarning("[IntegratedPilot] 수동 중단했습니다 - 이후 판단은 RuleBased 전체 폴백으로 처리됩니다.\n"
                           + IntegratedLlmPilotSession.Describe());
        }

        // ── 사전 점검 ───────────────────────────────────────────────────────────

        /// <summary>씬을 <b>읽기만</b> 해서 파일럿을 켜도 되는 상태인지 확인하고, 동시에
        /// 권장 표본 계획을 만든다. 어긋난 것이 있으면 고치지 않고 사유만 돌려준다 -
        /// 이 도구가 씬을 대신 고치기 시작하면 "씬 무변경" 보장이 무너진다.</summary>
        internal static bool Preflight(out string blocker, out string plan) =>
            Preflight(out blocker, out plan, out _);

        static bool Preflight(out string blocker, out string plan, out string providerConfigPath)
        {
            plan = "";
            providerConfigPath = null;
            var problems = new List<string>();

            var installer = UnityEngine.Object.FindFirstObjectByType<GameInstaller>();
            if (installer == null)
            {
                blocker = "Zone1 씬에서 GameInstaller를 찾을 수 없습니다.";
                return false;
            }

            var so = new SerializedObject(installer);
            var mode = (ThinkerMode)so.FindProperty("thinkerMode").enumValueIndex;
            bool shadow = so.FindProperty("shadowMode").boolValue;
            var config = so.FindProperty("llmProviderConfig").objectReferenceValue as LlmProviderConfig;
            var stage = installer.StageAsset;

            if (mode != ThinkerMode.RuleOnly)
                problems.Add($"• 씬 thinkerMode가 {mode}입니다 - RuleOnly로 두세요. "
                           + "파일럿은 씬을 바꾸지 않고 opt-in으로만 켭니다.");

            if (shadow)
                problems.Add("• shadowMode가 켜져 있습니다 - 같은 판단에 관찰용 호출이 겹쳐 비용이 두 배가 됩니다. 끄고 실행하세요.");

            // 씬에 설정이 없어도 씬을 고치지 않는다 - 기본 자산 경로를 opt-in 토큰에 실어 보내면
            // GameInstaller가 그것으로 통합 판단만 조립한다(씬의 llmProviderConfig 필드는 그대로 빈 채다).
            if (config == null)
            {
                config = AssetDatabase.LoadAssetAtPath<LlmProviderConfig>(DefaultProviderConfigPath);
                if (config != null)
                {
                    providerConfigPath = DefaultProviderConfigPath;
                    Debug.Log($"[IntegratedPilot] 씬에 LlmProviderConfig가 없어 {DefaultProviderConfigPath}를 파일럿에만 임시로 씁니다 - 씬은 수정하지 않습니다.");
                }
            }

            if (config == null)
                problems.Add($"• LlmProviderConfig를 찾을 수 없습니다 (씬 필드도 비었고 {DefaultProviderConfigPath}도 없습니다).");
            else if (!config.useProxy && !ApiKeyProvider.TryGetApiKey(config.provider.ToString(), out _))
                problems.Add($"• API 키를 찾을 수 없습니다 (BELIEF_LLM_API_KEY_{config.provider.ToString().ToUpperInvariant()}). "
                           + "중계 서버를 쓸 계획이면 useProxy를 켜세요.");

            if (stage == null)
                problems.Add("• StageData가 지정되지 않았습니다 - 파일럿 정책이 스테이지를 판정할 수 없습니다.");
            else if (!IntegratedLlmPilotPolicy.IsAllowed(stage.stageId, out string denyReason))
                problems.Add("• " + denyReason);

            if (problems.Count > 0)
            {
                blocker = string.Join("\n", problems);
                return false;
            }

            blocker = null;
            plan = BuildPlan(stage);
            return true;
        }

        /// <summary>
        /// 권장 표본 계획. <b>강제하지 않는다</b> - 카드는 턴마다 무작위로 뽑히므로 이 목록이
        /// 그대로 손에 들어온다는 보장이 없다. 실제 상한은 호출 예산 하나뿐이고, 이 계획은
        /// "뽑히면 우선 쓰라"는 안내다.
        /// </summary>
        static string BuildPlan(StageData stage)
        {
            var sb = new StringBuilder();

            int maxTurns = stage.maxTurns;
            sb.AppendLine($"[스테이지] {stage.stageId} / StageData.maxTurns {maxTurns}턴 (수정하지 않음)"
                        + (maxTurns > MaxPilotTurns
                            ? $" → 도구가 {MaxPilotTurns}턴에서 자동 중단합니다"
                            : " → 미션이 먼저 끝납니다"));

            // ── 카드: 신뢰도 등급별로 최소 1장씩 ─────────────────────────────────
            var cards = stage.cardPool != null && stage.cardPool.cards != null
                ? stage.cardPool.cards.Where(c => c != null && c.information != null).ToList()
                : new List<InformationCardData>();

            var byTier = new Dictionary<string, List<InformationCardData>>
            {
                { "High", new List<InformationCardData>() },
                { "Medium", new List<InformationCardData>() },
                { "Low", new List<InformationCardData>() },
            };
            foreach (var c in cards) byTier[Tier(c)].Add(c);

            var picks = new List<InformationCardData>();
            foreach (var tier in new[] { "High", "Medium", "Low" })
            {
                // 등급 안에서는 출처 신뢰도가 가장 뚜렷한 것부터 - 등급 차이가 결과에 드러나야 표본이 된다.
                var ordered = byTier[tier]
                    .OrderByDescending(c => tier == "Low" ? -SourceTrust(c) : SourceTrust(c))
                    .ThenBy(c => c.cardId, StringComparer.Ordinal).ToList();
                if (ordered.Count > 0) picks.Add(ordered[0]);
                if (ordered.Count > 1 && picks.Count < MaxPlannedCards) picks.Add(ordered[1]);
            }

            sb.AppendLine($"[권장 카드] {picks.Count}장 (풀 {cards.Count}장 중, High/Medium/Low 각 1장 이상)");
            foreach (var c in picks)
                sb.AppendLine($"   {Tier(c),-6} {c.cardId,-10} cred {c.information.baseCredibility:F2} / "
                            + $"출처 {(c.source != null ? c.source.sourceId : "-")} {SourceTrust(c):F2} / {c.cardType}");

            foreach (var tier in new[] { "High", "Medium", "Low" })
                if (byTier[tier].Count == 0)
                    sb.AppendLine($"   ! {tier} 등급 카드가 이 풀에 없습니다 - 등급 차이를 관찰할 수 없습니다.");

            // ── NPC: Verify 가능 / 관계 판단 가능 ────────────────────────────────
            var npcs = stage.npcPlacements != null
                ? stage.npcPlacements.Select(p => p.npc).OfType<MajorNpcData>().ToList()
                : new List<MajorNpcData>();

            var verifyNpcs = npcs.Where(n => n.availableActions != null
                && n.availableActions.Any(a => a != null && a.intent == NpcActionIntent.Verify)).ToList();
            var relationNpcs = npcs.Where(n => n.relationships != null && n.relationships.Length > 0).ToList();

            sb.AppendLine($"[NPC] 배치 {npcs.Count}명 / Verify 가능 {verifyNpcs.Count}명 / 관계 보유 {relationNpcs.Count}명");
            sb.AppendLine("   Verify : " + (verifyNpcs.Count > 0
                ? string.Join(", ", verifyNpcs.Take(6).Select(n => n.npcId)) : "없음 - 기억 되먹임을 관찰할 수 없습니다"));
            sb.AppendLine("   관계   : " + (relationNpcs.Count > 0
                ? string.Join(", ", relationNpcs.Take(6).Select(n => n.npcId)) : "없음 - 관계 근거를 관찰할 수 없습니다"));

            return sb.ToString();
        }

        static float SourceTrust(InformationCardData c) => c.source != null ? c.source.baseTrustModifier : 0f;

        // 등급 경계는 결과 계측(IntegratedLlmPilotCoverage)과 반드시 같아야 한다 - 계획과 보고가
        // 다른 기준을 쓰면 "Low를 넣었는데 Low가 없다"는 보고가 나온다.
        static string Tier(InformationCardData c) =>
            IntegratedLlmPilotCoverage.TierOf(c.information.baseCredibility);

        // ── 자동 정리 ───────────────────────────────────────────────────────────

        /// <summary>
        /// 에디터 idle에서 파일럿 턴 수를 센다. 게임 코드는 한 줄도 실행하지 않고
        /// <see cref="TurnSystem.StageTurn"/>을 읽기만 한다 - 미션 재시작으로 미션 턴이 1로
        /// 돌아가도 누적 턴은 이어지므로, "실제로 몇 턴을 굴렸는가"를 정확히 센다.
        ///
        /// 상한에 닿으면 곧바로 세션을 닫아 새 Transport 호출을 0으로 만들고, 진행 중이던 요청이
        /// 스스로 끝날 시간을 준 뒤에 Play를 끈다 - 요청을 중간에 잘라내지 않는다.
        /// </summary>
        static void OnEditorUpdate()
        {
            if (!UnityEngine.Application.isPlaying)
            {
                if (IsMonitoring) ResetMonitor();
                return;
            }

            // 정리 대기 중 - 세션은 이미 닫혔고 남은 것은 진행 중이던 요청뿐이다.
            if (drainDeadline > 0)
            {
                if (EditorApplication.timeSinceStartup < drainDeadline) return;
                drainDeadline = -1;
                Debug.LogWarning("[IntegratedPilot] 진행 중이던 요청 정리 시간이 끝나 Play Mode를 종료합니다.");
                EditorApplication.isPlaying = false;   // ExitingPlayMode에서 보고와 초기화가 이어진다
                return;
            }

            if (!IntegratedLlmPilotSession.IsActive) return;

            // 전 구역 연속 플레이는 끝까지 도는 것이 목적이라 턴 상한을 적용하지 않는다 -
            // 그 모드의 방어선은 구역당 호출 예산이다.
            if (IntegratedLlmPilotSession.RunMode == PilotRunMode.FullPlaythrough) return;

            // Release도 같은 이유로 면제한다. 씬 thinkerMode가 IntegratedLlm이 된 뒤로는
            // 에디터에서 그냥 Play를 눌러도 RunMode가 Release가 되는데, 여기서 4턴 만에 Play를
            // 꺼버리면 전체 플레이 검증이 아예 불가능하다(5턴째에 매번 종료됐다).
            // Release의 비용 방어선은 이 감시기가 아니라 중계 서버다 - GameInstaller도 Release일 때
            // "호출 상한: 없음 (중계 서버에서 통제)"라고 보고한다. 감시기가 그 설계와 어긋나 있었다.
            if (IntegratedLlmPilotSession.RunMode == PilotRunMode.Release) return;

            if (monitored == null) monitored = UnityEngine.Object.FindFirstObjectByType<GameInstaller>();
            if (monitored == null || monitored.Turns == null) return;

            int stageTurn = monitored.Turns.StageTurn;
            if (startStageTurn < 0)
            {
                startStageTurn = stageTurn;
                startMissionTurn = monitored.Turns.CurrentTurn;
                Debug.LogWarning($"[IntegratedPilot] 턴 감시 시작 - 누적 턴 {startStageTurn}(미션 턴 {startMissionTurn})부터 최대 {MaxPilotTurns}턴.");
            }
            lastStageTurn = stageTurn;

            if (stageTurn - startStageTurn + 1 <= MaxPilotTurns) return;

            // ── 턴 상한 도달 ────────────────────────────────────────────────────
            turnLimitReached = true;
            IntegratedLlmPilotSession.End(IntegratedLlmPilotSession.TurnLimitReason);

            int timeoutMs = ReadTimeoutMs(monitored);
            drainDeadline = EditorApplication.timeSinceStartup + (timeoutMs + DrainSlackMs) / 1000.0;

            Debug.LogWarning(
                $"[IntegratedPilot] {IntegratedLlmPilotSession.TurnLimitReason} - {MaxPilotTurns}턴을 채워 파일럿을 닫았습니다.\n"
                + "  지금부터 새 Transport 호출은 0이고, 남은 판단은 RuleBased 전체 폴백으로 처리됩니다.\n"
                + $"  진행 중이던 요청을 최대 {(timeoutMs + DrainSlackMs) / 1000.0:F1}초 기다린 뒤 Play Mode를 종료합니다.");
        }

        /// <summary>씬을 읽기만 해서 이 실행의 Timeout 값을 가져온다 - 정리 대기 시간의 근거다.</summary>
        static int ReadTimeoutMs(GameInstaller installer)
        {
            try
            {
                var p = new SerializedObject(installer).FindProperty("llmTimeoutMs");
                if (p != null && p.intValue > 0) return p.intValue;
            }
            catch (Exception) { /* 값을 못 읽으면 아래 기본값으로 간다 */ }
            return 15000;
        }

        static void ResetMonitor()
        {
            monitored = null;
            startStageTurn = -1;
            startMissionTurn = -1;
            lastStageTurn = -1;
            drainDeadline = -1;
        }

        static void OnPlayModeChanged(PlayModeStateChange change)
        {
            switch (change)
            {
                case PlayModeStateChange.ExitingPlayMode:
                    if (IntegratedLlmPilotSession.IsActive || IntegratedLlmPilotSession.CallsUsed > 0
                        || turnLimitReached)
                        Report();
                    IntegratedLlmPilotSession.End("PlayModeExited");
                    IntegratedLlmPilotSession.Disarm();
                    IntegratedLlmPilotSession.Clear();   // 보고가 끝난 뒤에 예산·표본을 지운다
                    ResetMonitor();
                    turnLimitReached = false;
                    break;

                case PlayModeStateChange.EnteredEditMode:
                    // Play가 실패로 끝났거나 무장만 하고 들어가지 못한 경우까지 여기서 확실히 턴다.
                    IntegratedLlmPilotSession.End("EnteredEditMode");
                    IntegratedLlmPilotSession.Disarm();
                    IntegratedLlmPilotSession.Clear();
                    ResetMonitor();
                    turnLimitReached = false;
                    break;
            }
        }

        /// <summary>Console Error는 파일럿 중단 기준이다. 게임을 멈추지는 않고 파일럿만 닫는다 -
        /// 이후 판단은 RuleBased 전체 폴백이 되므로 오류 상태 위에서 LLM 결과를 더 쌓지 않는다.</summary>
        static void OnLogMessage(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            if (!IntegratedLlmPilotSession.IsActive) return;

            // 2026-08-06 실측: Unity 패키지(MCP relay)의 연결 오류가 파일럿을 조기 종료시켰다.
            // 중단 기준의 의도는 "게임이 깨졌는가"이므로, 이 프로젝트 코드에서 나온 오류만 중단으로
            // 본다. 나머지는 기록만 남긴다 - 남의 오류로 실행이 죽으면 관측 자체가 불가능해진다.
            bool fromGameCode = stackTrace != null && stackTrace.Contains("Assets/Belief");

            if (!sawConsoleError)
            {
                sawConsoleError = true;
                firstConsoleError = condition;
            }

            if (!fromGameCode)
            {
                Debug.LogWarning("[IntegratedPilot] 프로젝트 밖(패키지/툴링)에서 Console Error가 났습니다 - "
                               + "파일럿은 계속 진행합니다.\n  " + condition);
                return;
            }

            IntegratedLlmPilotSession.End("ConsoleError");
            Debug.LogWarning("[IntegratedPilot] 게임 코드에서 Console Error가 발생해 파일럿을 중단했습니다 - "
                           + "이후 판단은 RuleBased 전체 폴백으로 처리됩니다.\n  " + condition);
        }

        static void Report()
        {
            int used = IntegratedLlmPilotSession.CallsUsed;
            double cost = used * (AvgInputTokens / 1_000_000.0 * InputPricePerMillion
                                + AvgOutputTokens / 1_000_000.0 * OutputPricePerMillion);

            // 종료 턴은 "마지막으로 굴린 턴"이다. 상한으로 닫은 경우 감시가 본 턴은 이미 상한을
            // 넘긴 턴이므로, 실제로 플레이된 마지막 턴은 그 직전이다.
            int endStageTurn = turnLimitReached
                ? startStageTurn + MaxPilotTurns - 1
                : lastStageTurn;
            int progressed = startStageTurn >= 0 && endStageTurn >= startStageTurn
                ? endStageTurn - startStageTurn + 1 : 0;

            var sb = new StringBuilder();
            sb.AppendLine("[IntegratedPilot] 파일럿 종료");
            sb.AppendLine($"  LLM 호출 : {used}/{IntegratedLlmPilotSession.CurrentMaxCalls}회"
                        + $"  (모드 {IntegratedLlmPilotSession.RunMode}, 진입 구역 {IntegratedLlmPilotSession.ZonesEntered})");
            sb.AppendLine($"  거부된 요청 : {IntegratedLlmPilotSession.CallsDenied}회 (전부 RuleBased 전체 폴백)");
            sb.AppendLine($"  예상 비용 : 약 ${cost:F5} (실측 평균 환산 - 정확한 값은 Decision Trace의 토큰 수를 보세요)");

            sb.AppendLine($"  시작 턴 : 누적 {(startStageTurn >= 0 ? startStageTurn.ToString() : "-")}"
                        + $" (미션 턴 {(startMissionTurn >= 0 ? startMissionTurn.ToString() : "-")})");
            sb.AppendLine($"  종료 턴 : 누적 {(endStageTurn >= 0 ? endStageTurn.ToString() : "-")}");
            sb.AppendLine($"  진행 턴 수 : {progressed}턴 (상한 {MaxPilotTurns}턴)");
            sb.AppendLine($"  {IntegratedLlmPilotSession.TurnLimitReason} : {(turnLimitReached ? "예" : "아니오")}");

            AppendCoverage(sb);

            sb.AppendLine($"  Console Error : {(sawConsoleError ? "발생 - " + firstConsoleError : "없음")}");
            sb.AppendLine($"  종료 사유 : {IntegratedLlmPilotSession.EndReason ?? "PlayModeExited"}");
            sb.AppendLine("  상세 판단 기록 : BELIEF/NPC Decision Trace 창");

            if (sawConsoleError) Debug.LogWarning(sb.ToString());
            else Debug.Log(sb.ToString());
        }

        /// <summary>실제로 판단된 카드의 신뢰도 등급 분포. 등급이 빠져도 <b>실패가 아니다</b> -
        /// 카드 드로우를 조작하지 않기로 한 이상 표본이 치우칠 수 있고, 그건 추가 실행으로 메울 일이다.</summary>
        static void AppendCoverage(StringBuilder sb)
        {
            var cov = IntegratedLlmPilotSession.Coverage;
            if (cov == null || cov.Count == 0)
            {
                sb.AppendLine("  카드 표본 : 0건 - CoverageIncomplete (추가 실행 필요)");
                return;
            }

            sb.AppendLine($"  카드 표본 : {cov.Count}건 (직접 {cov.DirectCount} / 재확산 {cov.RespreadCount})");
            sb.AppendLine($"    High {cov.CountOfTier("High")}건 [{(cov.HasHigh ? "포함" : "없음")}]"
                        + $" / Medium {cov.CountOfTier("Medium")}건 [{(cov.HasMedium ? "포함" : "없음")}]"
                        + $" / Low {cov.CountOfTier("Low")}건 [{(cov.HasLow ? "포함" : "없음")}]");
            sb.AppendLine(cov.CoverageComplete
                ? "    Coverage : 완전 (High/Medium/Low 모두 관측)"
                : "    CoverageIncomplete : 빠진 등급이 있습니다 - 실패가 아니라 표본 부족이며 추가 실행이 필요합니다.");

            foreach (var s in cov.Samples)
                sb.AppendLine($"    T{s.Turn} {s.Tier,-6} {s.CardId,-10} cred {s.Credibility:F2}"
                            + $" / {(s.ViaRespread ? "재확산" : "직접")} → {s.NpcId}");
        }
    }
}
