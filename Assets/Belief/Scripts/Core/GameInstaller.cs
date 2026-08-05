using System.Collections.Generic;
using UnityEngine;
using Belief.AI;
using Belief.AI.LLM;
using Belief.Data;
using Belief.Debugging;
using Belief.Domain;
using Belief.Events;
using Belief.Systems;
using Belief.Systems.BeliefEvaluators;

namespace Belief.Core
{
    /// <summary>
    /// 합성 루트. Data 에셋으로부터 Domain 상태를 만들고 전체 System 그래프를 조립한다.
    /// InfoDeliverySystem은 "현재 턴"을 알아야 하고 TurnSystem은 InfoDeliverySystem을 갖고 있어야 하는
    /// 상호 의존은 지연 캡처(turnSystemRef)로 끊는다 - 람다는 호출 시점에만 평가되므로 안전하다.
    /// </summary>
    public class GameInstaller : MonoBehaviour
    {
        [Header("Stage Data (지정하면 아래 raw 필드보다 우선 - 비워두면 기존 raw 필드로 하위 호환 동작)")]
        [SerializeField] StageData stageData;

        [Header("World Data (raw - stageData 미지정 시 사용)")]
        [SerializeField] LocationData[] allLocations;
        [SerializeField] NpcData[] allNpcs;

        [Header("Information / Mission (raw - stageData 미지정 시 사용)")]
        [SerializeField] InformationCardPoolData informationPool;
        [SerializeField] int maxTurns = 6;
        [SerializeField] MissionData mission;
        [SerializeField] MissionConditionData instantFailCondition;

        /// <summary>선택 항목 - 지정하면 이 씬이 최종 스테이지로 승리(GameOverEvent(true))할 때만
        /// 순수 결과 연출(예: 최종 보스 NPC의 Belief를 Denied로)을 적용한다. 미판정 로직에는 전혀
        /// 관여하지 않는다(StageFinalResultSystem 참고). 비워두면(null) 기존 씬과 동일하게 아무 일도
        /// 하지 않는다 - 하위 호환.</summary>
        [Header("Final Result (선택 - 최종 승리 결과 연출)")]
        [SerializeField] StageFinalResultData finalResultData;

        [Header("Belief Tuning")]
        [SerializeField] BeliefTuningData beliefTuning;
        [SerializeField] MemoryTuningData memoryTuning;
        [SerializeField] MemoryCategoryData repeatedLiesCategory;

        /// <summary>Location Mechanics V1의 유일한 수치 자산 - 모든 스테이지(씬)가 이 자산 하나를
        /// 공유해야 한다(스테이지별로 다른 자산을 물리지 않는다).</summary>
        [SerializeField] LocationMechanicsSettings locationMechanics;

        [Header("AI (LLM Layer)")]
        /// <summary>RuleOnly = AI 호출 없음(자동 테스트 기본값, 토큰 안 듦) / FakeLlm = 가짜 응답으로
        /// 배선 확인 / Llm = 실제 호출. Llm으로 두면 아래 llmProviderConfig가 반드시 필요하다.</summary>
        [SerializeField] ThinkerMode thinkerMode = ThinkerMode.RuleOnly;
        [SerializeField] FakeTransportMode fakeTransportMode = FakeTransportMode.AlwaysSuccess;

        /// <summary>ThinkerMode.Llm일 때만 쓰인다. 어떤 모델을 어느 주소로 부를지가 여기 들어간다 -
        /// API 키는 여기에 절대 넣지 않는다(ApiKeyProvider 또는 중계 서버가 담당).</summary>
        [SerializeField] LlmProviderConfig llmProviderConfig;

        /// <summary>LLM 요청 Timeout의 유일한 설정 위치 - 여기 값 하나만 ThinkerFactory를 거쳐
        /// LlmMajorThinker로 전달된다. 이 요청이 이 시간(ms) 안에 끝나지 않으면 그 판단 1회만
        /// RuleBased 결과로 즉시 대체한다(늦게 도착하는 원래 응답은 폐기).</summary>
        [SerializeField, Min(1)] int llmTimeoutMs = LlmMajorThinker.DefaultTimeoutMs;

        [Header("Shadow Mode (개발·관찰 전용)")]
        /// <summary>켜면 실제 판단이 끝난 뒤 같은 스냅샷으로 LLM 통합 판단을 따로 돌려 규칙 기반
        /// 결과와 비교만 한다. <b>결과는 월드와 미션에 절대 적용되지 않는다.</b> thinkerMode와 독립이라
        /// RuleOnly로 게임을 돌리면서도 관찰할 수 있다 - 그래서 <b>토큰이 나간다</b>. 기본값은 꺼짐.</summary>
        [SerializeField] bool shadowMode = false;

        /// <summary>Shadow 요청의 전체 프롬프트와 원문 응답을 비교 기록에 담는다. 로그가 매우 커지므로
        /// 필요할 때만 켠다 - 꺼져 있으면 어떤 파일도 만들지 않는다.</summary>
        [SerializeField] bool shadowPromptLogging = false;

        readonly Dictionary<LocationData, LocationState> locationStates = new Dictionary<LocationData, LocationState>();
        readonly Dictionary<NpcData, NpcState> npcStates = new Dictionary<NpcData, NpcState>();

        public IReadOnlyDictionary<LocationData, LocationState> Locations => locationStates;
        public IReadOnlyDictionary<NpcData, NpcState> Npcs => npcStates;

        /// <summary>이 씬에 지정된 StageData 원본(정적 데이터) - 없으면 null. ProgressionController 등
        /// 다른 시스템이 "이 구역이 StageData 기반인지"를 판단할 때 참조한다.</summary>
        public StageData StageAsset => stageData;

        /// <summary>Awake에서 구성한 런타임 상태 컨테이너(Locations/Npcs/Missions/Turn을 묶음). 게임 규칙은
        /// 갖지 않는다 - 읽기 전용 참조 모음.</summary>
        public StageState Stage { get; private set; }

        public IGameEventBus EventBus { get; private set; }

        /// <summary>Shadow Mode가 꺼져 있으면 null. 관찰 전용이라 게임 로직은 이 값을 읽지 않는다
        /// (검증 하네스와 관찰 창에서만 참조).</summary>
        public ShadowJudgmentSystem ShadowJudgment { get; private set; }

        /// <summary>IntegratedLlm 파일럿이 꺼져 있으면 null. 검증 하네스와 관찰 창이 참조한다.</summary>
        public JudgmentApplicationSystem JudgmentApplication { get; private set; }
        public EventLogSystem Log { get; private set; }
        public TurnSystem Turns { get; private set; }
        public InfoDeliverySystem Delivery { get; private set; }
        public MissionSystem Mission { get; private set; }
        public IBeliefDebugRepository BeliefDebug { get; private set; }
        public IPromptRepository PromptRepo { get; private set; }
        public LocationMechanicsSettings LocationMechanics => locationMechanics;

        /// <summary>Awake에서 확정된 실제 판단 모드. 씬에 저장된 thinkerMode와 다를 수 있다 -
        /// 정책 거부로 강등됐거나, 파일럿 opt-in으로 IntegratedLlm이 켜진 경우다. Awake 이전에는
        /// 씬 값 그대로 보고한다.</summary>
        ThinkerMode? effectiveThinkerMode;
        public ThinkerMode CurrentThinkerMode => effectiveThinkerMode ?? thinkerMode;

        /// <summary>통합 판단이 실제로 쓰는 설정. 씬 값이 비어 있으면 파일럿이 넘겨준 자산이 들어온다 -
        /// 씬에 저장된 llmProviderConfig 필드는 어느 경우에도 바뀌지 않는다.</summary>
        LlmProviderConfig activeIntegratedConfig;

        /// <summary>Debug Overlay가 지금 무엇으로 판단하고 있는지 한눈에 보여주기 위한 설명 문자열.</summary>
        public string CurrentTransportDescription => CurrentThinkerMode switch
        {
            ThinkerMode.RuleOnly => "없음 (RuleOnly)",
            ThinkerMode.FakeLlm => $"FakeTransport ({fakeTransportMode})",
            ThinkerMode.IntegratedLlm =>
                "통합 판단 "
                + (activeIntegratedConfig != null
                    ? $"{activeIntegratedConfig.modelId} @ {activeIntegratedConfig.endpoint}" : "(설정 없음)")
                + $" [파일럿 {IntegratedLlmPilotSession.CallsUsed}/{IntegratedLlmPilotSession.MaxCalls}]",
            ThinkerMode.Llm when llmProviderConfig != null =>
                $"{llmProviderConfig.modelId} @ {llmProviderConfig.endpoint}"
                + (llmProviderConfig.useProxy ? " (중계 서버)" : " (직접 호출)"),
            _ => "Llm (설정 없음 - RuleOnly로 동작)"
        };

        // Awake()에서 전부 조립한다 - Unity는 씬의 모든 Awake()가 모든 Start()보다 먼저 끝나는 것을
        // 보장하므로, WorldPresenter/HudPresenter/TargetingController가 자신의 Start()에서
        // installer.EventBus/Turns 등을 안전하게 참조할 수 있다(Start() 간 순서는 보장되지 않는다).
        void Awake()
        {
            EventBus = new GameEventBus();
            Log = new EventLogSystem(EventBus);

            if (stageData != null)
                StageDataValidator.LogIssues(stageData, StageDataValidator.Validate(stageData));

            var effectiveLocations = stageData != null && stageData.locations != null && stageData.locations.Length > 0
                ? stageData.locations : allLocations;
            var effectivePlacements = stageData != null ? stageData.npcPlacements : null;
            var effectiveNpcs = effectivePlacements != null && effectivePlacements.Length > 0
                ? PlacementNpcs(effectivePlacements) : allNpcs;
            var effectiveCardPool = stageData != null && stageData.cardPool != null ? stageData.cardPool : informationPool;
            var effectiveMaxTurns = stageData != null && stageData.maxTurns > 0 ? stageData.maxTurns : maxTurns;
            var effectiveMission = stageData != null && stageData.startMission != null ? stageData.startMission : mission;

            BuildDomainState(effectiveLocations, effectiveNpcs, effectivePlacements);

            var debugRepository = new BeliefDebugRepository();
            BeliefDebug = debugRepository;
            var beliefSystem = BuildBeliefSystem(debugRepository);
            var memorySelector = new MemorySelector();
            var memorySystem = new MemorySystem(EventBus, npcStates, repeatedLiesCategory);

            var actionResolution = new ActionResolutionSystem(locationStates, EventBus);

            var promptRepository = new PromptRepository();
            PromptRepo = promptRepository;

            // ── IntegratedLlm 파일럿 판정 ────────────────────────────────────────
            // 씬의 thinkerMode가 IntegratedLlm이어도 정책이 허용하지 않으면 RuleOnly로 강등한다.
            // 강등되면 아래 조립은 기존 RuleOnly와 완전히 동일해지고 Transport 호출도 0이다.
            // 파일럿 opt-in은 씬에 저장되지 않고 이 Awake 1회만 소비된다 - 그래서 씬을 IntegratedLlm으로
            // 바꿔 저장할 필요가 없고, 다음 실행이나 다음 씬은 자동으로 RuleOnly로 돌아온다.
            var effectiveMode = thinkerMode;
            IJudgmentCallBudget pilotBudget = null;
            bool pilotLogPrompts = false;

            // 통합 판단 전용 설정 - 씬 값이 비어 있을 때만 파일럿이 넘겨준 자산을 쓴다. 씬에 저장된
            // llmProviderConfig 자체는 절대 바꾸지 않으므로 다른 모드의 동작은 그대로다.
            var integratedProviderConfig = llmProviderConfig;

            if (IntegratedLlmPilotSession.TryConsumeOptIn(out string pilotSessionId, out pilotLogPrompts,
                    out var pilotProviderConfig))
            {
                if (integratedProviderConfig == null) integratedProviderConfig = pilotProviderConfig;

                if (thinkerMode != ThinkerMode.RuleOnly)
                {
                    // 이미 Transport를 쓰는 모드라면 두 경로가 같은 판단에 겹쳐 이중 비용이 된다.
                    IntegratedLlmPilotSession.End("SceneModeConflict");
                    Debug.LogWarning($"[GameInstaller] 씬 thinkerMode가 {thinkerMode}라 파일럿 opt-in을 무시했습니다 - 씬 값을 RuleOnly로 두고 다시 실행하세요.");
                }
                else
                {
                    effectiveMode = ThinkerMode.IntegratedLlm;
                    pilotBudget = new IntegratedLlmPilotCallBudget(pilotSessionId);
                }
            }

            IntegratedLlmThinker integratedThinker = null;
            if (effectiveMode == ThinkerMode.IntegratedLlm)
            {
                // 씬 값으로 직접 켠 경우(파일럿 도구를 거치지 않음)에도 호출 상한은 반드시 걸린다 -
                // "20회"는 파일럿 도구의 정책이 아니라 이 경로 전체의 안전장치이기 때문이다.
                if (pilotBudget == null)
                {
                    Debug.LogWarning($"[GameInstaller] 씬 값으로 IntegratedLlm이 켜져 있습니다 - 상한 {IntegratedLlmPilotSession.MaxCalls}회를 적용합니다. "
                                   + "권장 방식은 씬을 RuleOnly로 두고 BELIEF/Diagnostics의 파일럿 도구로 여는 것입니다.");
                    IntegratedLlmPilotSession.BeginSession("scene-" + name);
                    pilotBudget = new IntegratedLlmPilotCallBudget(IntegratedLlmPilotSession.ActiveSessionId);
                }

                string stageId = stageData != null ? stageData.stageId : null;
                if (!IntegratedLlmPilotPolicy.IsAllowed(stageId, out string denyReason))
                {
                    Debug.LogWarning($"[GameInstaller] IntegratedLlm이 허용되지 않아 RuleOnly로 동작합니다 - {denyReason}");
                    effectiveMode = ThinkerMode.RuleOnly;
                    IntegratedLlmPilotSession.End("StageNotAllowed");
                }
                else
                {
                    integratedThinker = ThinkerFactory.CreateIntegrated(
                        beliefSystem, integratedProviderConfig, llmTimeoutMs, pilotBudget, pilotLogPrompts,
                        IntegratedLlmPilotSession.Coverage);
                    if (integratedThinker == null)
                    {
                        Debug.LogWarning("[GameInstaller] IntegratedLlm 조립에 실패해 RuleOnly로 동작합니다.");
                        effectiveMode = ThinkerMode.RuleOnly;
                        IntegratedLlmPilotSession.End("AssemblyFailed");
                    }
                }
            }
            effectiveThinkerMode = effectiveMode;
            activeIntegratedConfig = integratedThinker != null ? integratedProviderConfig : null;

            // IntegratedLlm은 기존 IMajorNpcThinker 경로를 쓰지 않는다 - 그 경로는 RuleOnly로 만들어
            // 두고(이동 판단 등 공용 사용), 정보 판단만 통합 경로가 가져간다.
            IMajorNpcThinker thinker = ThinkerFactory.Create(
                integratedThinker != null ? ThinkerMode.RuleOnly : effectiveMode,
                promptRepository, fakeTransportMode, llmTimeoutMs, llmProviderConfig);

            // Shadow Mode - 명시적으로 켰을 때만 만들어진다. thinkerMode와 독립이라 게임이 RuleOnly로
            // 도는 동안에도 관찰할 수 있고, 그래서 토큰이 나간다(기본값 꺼짐). 결과는 비교 로그로만
            // 흘러가며 ShadowJudgmentSystem은 월드를 바꿀 수단 자체를 참조하지 않는다.
            ShadowJudgment = null;
            if (shadowMode && integratedThinker != null)
            {
                // 같은 판단에 실제 LLM과 Shadow LLM을 둘 다 부르면 비용이 두 배가 된다.
                // 실제 적용이 우선이므로 Shadow를 끈다.
                Debug.LogWarning("[GameInstaller] IntegratedLlm이 활성화되어 Shadow Mode를 비활성화합니다 - 같은 판단에 대한 중복 호출과 이중 비용을 막습니다.");
            }
            else if (shadowMode)
            {
                var shadowTransport = ThinkerFactory.CreateShadowTransport(llmProviderConfig);
                if (shadowTransport != null)
                {
                    ShadowJudgment = new ShadowJudgmentSystem(
                        shadowTransport, llmTimeoutMs, shadowPromptLogging, EventBus,
                        () => stageData != null ? stageData.stageId : "",
                        () =>
                        {
                            var pc = ProgressionController.Instance;
                            var objective = pc != null ? pc.CurrentObjective() : null;
                            return objective != null ? objective.missionId : "";
                        });
                    Debug.LogWarning("[GameInstaller] Shadow Mode가 켜져 있습니다 - 게임 판단과 별개로 관찰용 LLM 호출이 발생하며 토큰이 소모됩니다(결과는 월드에 적용되지 않습니다).");
                }
            }

            // IntegratedLlm 전용 - 이동 예약과 적용 시스템. 통합 모드가 아니면 둘 다 null이라
            // 아래 시스템들의 동작이 지금까지와 완전히 같다.
            DestinationReservation reservations = null;
            JudgmentApplication = null;
            if (integratedThinker != null)
            {
                reservations = new DestinationReservation(locationStates, EventBus);
                JudgmentApplication = new JudgmentApplicationSystem(
                    actionResolution, reservations, EventBus,
                    () => stageData != null ? stageData.stageId : "",
                    () =>
                    {
                        var pc = ProgressionController.Instance;
                        var objective = pc != null ? pc.CurrentObjective() : null;
                        return objective != null ? objective.missionId : "";
                    });
                Debug.LogWarning("[GameInstaller] IntegratedLlm 파일럿이 활성화되었습니다 - 통합 판단 결과가 실제 월드에 적용되며 토큰이 소모됩니다.\n"
                               + $"  호출 상한 : {IntegratedLlmPilotSession.MaxCalls}회 (초과 요청은 Transport를 부르지 않고 RuleBased 전체 폴백)\n"
                               + $"  프롬프트 원문 기록 : {(pilotLogPrompts ? "켜짐" : "꺼짐")}\n"
                               + $"  {IntegratedLlmPilotSession.Describe()}");
            }

            // NPC 등급 구분 없음 - 모든 NPC가 같은 판단/이동 시스템을 탄다.
            var thinking = new NpcThinkingSystem(memorySelector, beliefSystem, thinker, actionResolution, memoryTuning, EventBus,
                ShadowJudgment, locationStates, integratedThinker, JudgmentApplication);

            // 이동 판단은 두 경로를 쓴다: 이번 턴에 판단이 새로 필요해진 NPC만 thinker(LLM 모드면
            // LLM)로 보내고, 나머지는 이 RuleBased 인스턴스로 보낸다. 별도 인스턴스를 하나 더 만드는
            // 이유는 LlmMajorThinker가 자기 fallback을 private으로 갖고 있어 꺼낼 수 없기 때문인데,
            // RuleBasedMajorThinker는 필드가 하나도 없는 완전 무상태라 인스턴스가 둘이어도 동작이
            // 똑같다(RuleOnly 모드에서는 thinker 자체가 RuleBased라 어느 쪽으로 가든 결과가 같다).
            var movement = new NpcMovementSystem(actionResolution, thinker, new RuleBasedMajorThinker(), reservations);

            TurnSystem turnSystemRef = null;
            var delivery = new InfoDeliverySystem(locationStates, thinking, EventBus, () => turnSystemRef.CurrentTurn, locationMechanics);
            Delivery = delivery;

            var informationCards = new InformationCardSystem(effectiveCardPool, EventBus);
            Mission = new MissionSystem(effectiveMission, EventBus);

            Turns = new TurnSystem(informationCards, delivery, movement, Mission, npcStates, locationStates, effectiveMaxTurns, instantFailCondition, EventBus, locationMechanics, memorySystem);
            turnSystemRef = Turns;

            if (finalResultData != null)
                new StageFinalResultSystem(finalResultData, npcStates, EventBus);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // NPC Decision Trace(Editor 전용 관찰 창)가 읽는 상위 문맥값 - 델리게이트만 등록할 뿐
            // 게임 판단에는 전혀 관여하지 않는다. GameInstaller가 Belief.Debugging을 참조하는 것은
            // 순수 데이터 홀더(UnityEditor 네임스페이스 아님)라 플레이어 빌드/DEVELOPMENT_BUILD에서도 안전하다.
            NpcDecisionTraceContext.StageIdProvider = () => stageData != null ? stageData.stageId : "";
            NpcDecisionTraceContext.StageTurnProvider = () => turnSystemRef.StageTurn;
            NpcDecisionTraceContext.MissionIdProvider = () =>
            {
                var pc = ProgressionController.Instance;
                var objective = pc != null ? pc.CurrentObjective() : null;
                return objective != null ? objective.missionId : "";
            };
            NpcDecisionTraceContext.MissionTurnProvider = () => turnSystemRef.CurrentTurn;
            // 씬 값이 아니라 실제로 확정된 모드를 보고한다 - 강등되거나 파일럿으로 켜진 경우
            // 씬 값을 그대로 적으면 기록이 사실과 달라진다.
            NpcDecisionTraceContext.ThinkerModeProvider = () => effectiveMode.ToString();
#endif

            var turnState = new TurnState(effectiveMaxTurns);
            var missionStates = BuildMissionStates(stageData);
            Stage = new StageState(stageData, locationStates, npcStates, missionStates, turnState);

            EventBus.Publish(new GameInitializedEvent(effectiveLocations.Length, effectiveNpcs.Length, effectiveCardPool.cards.Length));

            Turns.StartGame();
        }

        /// <summary>씬이 내려가면 이 구역의 Shadow 관찰도 끝난다 - 이후에는 새 요청을 발사하지 않고,
        /// 이미 떠 있던 요청이 늦게 돌아와도 이 인스턴스와 함께 사라지므로 다음 씬에 섞이지 않는다.</summary>
        void OnDestroy()
        {
            ShadowJudgment?.Disable();

            // 이 구역이 내려가면 파일럿도 끝난다 - 다음 씬(Zone2 등)에서 남은 예산이 되살아나
            // 허용되지 않은 스테이지의 판단이 Transport를 부르는 일이 없도록 여기서 닫는다.
            if (JudgmentApplication != null) IntegratedLlmPilotSession.End("SceneUnloaded");
        }

        void BuildDomainState(LocationData[] locations, NpcData[] npcs, NpcPlacementEntry[] placements)
        {
            foreach (var location in locations)
                locationStates[location] = new LocationState(location);

            var placementByNpc = BuildPlacementLookup(placements);

            foreach (var npc in npcs)
            {
                var state = new NpcState(npc);

                if (placementByNpc.TryGetValue(npc, out var placement))
                {
                    var startLocation = placement.EffectiveStartLocation;
                    if (startLocation != null) state.CurrentLocation = startLocation;

                    if (placement.initialBeliefs != null)
                        foreach (var belief in placement.initialBeliefs)
                            if (belief.card != null) state.SetBelief(belief.card, belief.belief);
                }

                npcStates[npc] = state;

                if (state.CurrentLocation != null && locationStates.TryGetValue(state.CurrentLocation, out var homeState))
                    homeState.PresentNpcs.Add(state);
            }
        }

        static Dictionary<NpcData, NpcPlacementEntry> BuildPlacementLookup(NpcPlacementEntry[] placements)
        {
            var map = new Dictionary<NpcData, NpcPlacementEntry>();
            if (placements == null) return map;
            foreach (var p in placements)
                if (p.npc != null) map[p.npc] = p;
            return map;
        }

        static NpcData[] PlacementNpcs(NpcPlacementEntry[] placements)
        {
            var list = new List<NpcData>(placements.Length);
            foreach (var p in placements)
                if (p.npc != null) list.Add(p.npc);
            return list.ToArray();
        }

        /// <summary>StageData.missions 각각을 기존 MissionState로 감싼다 - 새 미션 판정 시스템이 아니라
        /// StageState에 담아 둘 데이터 스냅샷일 뿐이다(실제 완료 판정은 지금까지와 동일하게
        /// ProgressionController/MissionSystem이 각자 담당한다).</summary>
        static List<MissionState> BuildMissionStates(StageData stage)
        {
            var list = new List<MissionState>();
            if (stage == null || stage.missions == null) return list;
            foreach (var m in stage.missions)
                if (m != null) list.Add(new MissionState(m));
            return list;
        }

        BeliefSystem BuildBeliefSystem(BeliefDebugRepository debugRepository)
        {
            var evaluators = new IBeliefEvaluator[]
            {
                new PersonalityEvaluator(),
                new ExistingBeliefEvaluator(),
                new CredibilityEvaluator(),
                new SourceEvaluator(),
                new GoalEvaluator(),
                new SituationEvaluator(),
                new MemoryEvaluator(memoryTuning),
            };
            return new BeliefSystem(evaluators, beliefTuning, debugRepository, locationMechanics);
        }
    }
}
