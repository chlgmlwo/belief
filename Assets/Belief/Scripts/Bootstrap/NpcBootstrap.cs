using System.Threading.Tasks;
using UnityEngine;
using Belief.Systems.NPC;

namespace Belief.Bootstrap
{
    /// <summary>씬 전환 간 유지되는 NPC JSON 초기화 오브젝트(DontDestroyOnLoad) - 기존
    /// ProgressionController와 동일한 [RuntimeInitializeOnLoadMethod(BeforeSceneLoad)] +
    /// DontDestroyOnLoad 싱글턴 패턴을 그대로 따른다. StreamingAssets 로딩은 비동기이므로
    /// Awake에서 곧바로 완료를 보장할 수 없다 - IsReady/InitializationFailed로 완료 여부를
    /// 노출하고, 다른 시스템은 이 값을 확인한 뒤에만 NPC 상태를 사용해야 한다.</summary>
    public class NpcBootstrap : MonoBehaviour
    {
        public static NpcBootstrap Instance { get; private set; }
        public static NpcManager Manager { get; private set; }

        public static bool IsReady => Manager != null && Manager.IsInitialized;
        public static bool InitializationFailed { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Bootstrap()
        {
            if (Instance != null) return;
            var go = new GameObject("NpcBootstrap");
            DontDestroyOnLoad(go);
            go.AddComponent<NpcBootstrap>();
        }

        async void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            Manager = new NpcManager();

            await InitializeAsync();
        }

        async Task InitializeAsync()
        {
            var loader = new NpcJsonLoader();

            // 1-2. Profile manifest + Profile 16개 로드
            var profiles = await loader.LoadAllProfilesAsync();
            // 3. npc_runtime_initial.json 로드
            var runtimeDatabase = await loader.LoadInitialRuntimeDatabaseAsync();

            // 4-6. 전체 정합성 검사 + NpcState 생성 + NpcManager 등록
            Manager.ResetToInitialState(profiles, runtimeDatabase, out var errors);

            if (Manager.IsInitialized)
            {
                // 7. NpcManager.IsInitialized = true (ResetToInitialState 내부에서 설정됨)
                int runtimeCount = runtimeDatabase?.npcRuntimeStates?.Count ?? 0;
                Debug.Log($"[NPC] Initialization completed. Profiles: {profiles.Count}, " +
                    $"Runtime states: {runtimeCount}, Created states: {Manager.Npcs.Count}");
                InitializationFailed = false;

                // 8. 이후 Belief/AI/Movement/Mission 시스템 초기화 - 이 JSON 기반 NPC 파이프라인은
                // 아직 기존 GameInstaller/TurnSystem(ScriptableObject 기반 Zone NPC)과 연결되어
                // 있지 않다. 연결 시점이 정해지면 여기서 해당 시스템 초기화를 이어서 호출한다.
            }
            else
            {
                InitializationFailed = true;
                foreach (var error in errors)
                    Debug.LogError($"[NPC] Initialization failed: {error}");

                if (errors.Count == 0)
                    Debug.LogError("[NPC] Initialization failed: 원인 불명(생성된 NpcState 수가 Profile 수와 다름).");
            }
        }
    }
}
