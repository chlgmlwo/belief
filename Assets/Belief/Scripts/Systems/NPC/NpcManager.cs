using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Belief.Data.NPC;
using Belief.Domain.NPC;

namespace Belief.Systems.NPC
{
    /// <summary>게임 중 NPC 상태(Belief.Domain.NPC.NpcState)의 단일 소유자. Profile/RuntimeInitial
    /// JSON을 직접 읽지 않는다 - 생성은 NpcJsonLoader+NpcStateFactory가 담당하고, 이 클래스는
    /// 완성된 NpcState를 보관·조회하는 역할만 한다.</summary>
    public class NpcManager
    {
        readonly Dictionary<string, NpcState> npcStates = new Dictionary<string, NpcState>();
        readonly NpcStateFactory factory = new NpcStateFactory();

        public IReadOnlyDictionary<string, NpcState> Npcs => npcStates;
        public bool IsInitialized { get; private set; }

        public NpcState GetNpc(string npcId)
        {
            if (npcStates.TryGetValue(npcId, out var state)) return state;
            Debug.LogError($"[NpcManager] 등록되지 않은 npcId 조회: {npcId}");
            return null;
        }

        public bool TryGetNpc(string npcId, out NpcState state) => npcStates.TryGetValue(npcId, out state);

        public IReadOnlyList<NpcState> GetNpcsByStage(string stageId) =>
            npcStates.Values.Where(s => s.RuntimeStatus?.CurrentStageId == stageId).ToList();

        /// <summary>Profile 목록과 초기 Runtime 데이터베이스로부터 NpcState 전체를 새로 만들어
        /// 등록한다. 정합성 검사에 실패한 NPC는 등록하지 않고 errors에 사유를 남긴다 - 하나라도
        /// 실패하면 IsInitialized는 false로 남아 호출자(NpcBootstrap)가 AI 턴 시작을 막을 수 있다.</summary>
        public void ResetToInitialState(IReadOnlyList<NpcProfileDto> profiles, NpcRuntimeDatabaseDto initialRuntimeDatabase, out List<string> errors)
        {
            npcStates.Clear();
            IsInitialized = false;
            errors = new List<string>();

            if (profiles == null || profiles.Count == 0)
            {
                errors.Add("Profile 목록이 비어 있습니다.");
                return;
            }
            if (initialRuntimeDatabase?.npcRuntimeStates == null || initialRuntimeDatabase.npcRuntimeStates.Count == 0)
            {
                errors.Add("RuntimeInitial 데이터가 비어 있습니다.");
                return;
            }

            var runtimeByNpcId = new Dictionary<string, NpcRuntimeDto>();
            foreach (var runtime in initialRuntimeDatabase.npcRuntimeStates)
            {
                if (runtimeByNpcId.ContainsKey(runtime.npcId))
                {
                    errors.Add($"runtime npcId 중복: {runtime.npcId}");
                    continue;
                }
                runtimeByNpcId[runtime.npcId] = runtime;
            }

            var seenProfileIds = new HashSet<string>();
            foreach (var profile in profiles)
            {
                if (!seenProfileIds.Add(profile.npcId))
                {
                    errors.Add($"profile npcId 중복: {profile.npcId}");
                    continue;
                }

                if (!runtimeByNpcId.TryGetValue(profile.npcId, out var runtime))
                {
                    errors.Add($"{profile.npcId}: Profile은 있으나 RuntimeInitial이 없습니다.");
                    continue;
                }

                if (!factory.TryCreate(profile, runtime, out var state, out var createError))
                {
                    errors.Add(createError);
                    continue;
                }

                if (npcStates.ContainsKey(state.NpcId))
                {
                    errors.Add($"동일 npcId 중복 등록 시도: {state.NpcId}");
                    continue;
                }

                npcStates[state.NpcId] = state;
            }

            foreach (var runtimeNpcId in runtimeByNpcId.Keys)
            {
                if (!seenProfileIds.Contains(runtimeNpcId))
                    errors.Add($"{runtimeNpcId}: RuntimeInitial은 있으나 Profile이 없습니다.");
            }

            IsInitialized = errors.Count == 0 && npcStates.Count == profiles.Count;
        }
    }
}
