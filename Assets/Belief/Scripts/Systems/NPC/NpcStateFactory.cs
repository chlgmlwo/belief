using System.Collections.Generic;
using System.Linq;
using Belief.Data.NPC;
using Belief.Domain.NPC;

namespace Belief.Systems.NPC
{
    /// <summary>Profile DTO + RuntimeInitial DTO를 짝지어 NpcState를 생성한다. 정합성 검사를
    /// 통과하지 못하면 생성하지 않고 오류만 반환한다 - 호출자(NpcBootstrap)가 실패를 판단해
    /// AI 턴을 시작하지 않도록 한다.</summary>
    public class NpcStateFactory
    {
        public bool TryCreate(NpcProfileDto profile, NpcRuntimeDto runtime, out NpcState state, out string error)
        {
            state = null;

            if (profile.npcId != runtime.npcId)
            {
                error = $"npcId 불일치: profile.npcId={profile.npcId}, runtime.npcId={runtime.npcId}";
                return false;
            }

            var profileBeliefIds = (profile.initialBeliefs ?? new List<NpcInitialBeliefDto>())
                .Select(b => b.informationId).OrderBy(x => x).ToList();
            var runtimeBeliefIds = (runtime.beliefStates ?? new List<NpcBeliefStateDto>())
                .Select(b => b.informationId).OrderBy(x => x).ToList();

            if (!profileBeliefIds.SequenceEqual(runtimeBeliefIds))
            {
                error = $"{profile.npcId}: initialBeliefs informationId 집합 불일치 " +
                    $"(profile=[{string.Join(",", profileBeliefIds)}], runtime=[{string.Join(",", runtimeBeliefIds)}])";
                return false;
            }

            foreach (var pb in profile.initialBeliefs)
            {
                var rb = runtime.beliefStates.First(b => b.informationId == pb.informationId);
                if (pb.initialLevel != rb.currentLevel)
                {
                    error = $"{profile.npcId}: {pb.informationId}의 initialLevel({pb.initialLevel}) != " +
                        $"runtime currentLevel({rb.currentLevel})";
                    return false;
                }
            }

            string profileLocation = profile.basicInfo?.defaultLocationId;
            string runtimeLocation = runtime.runtimeStatus?.currentLocationId;
            if (profileLocation != runtimeLocation)
            {
                error = $"{profile.npcId}: defaultLocationId({profileLocation}) != " +
                    $"runtimeStatus.currentLocationId({runtimeLocation})";
                return false;
            }

            state = new NpcState
            {
                NpcId = profile.npcId,
                Profile = profile,
                RuntimeStatus = CopyRuntimeStatus(runtime.runtimeStatus),
                CurrentGoal = CopyGoal(runtime.currentGoal),
                CurrentMove = CopyMove(runtime.currentMove),
                BeliefStates = CopyBeliefStates(runtime.beliefStates),
                WorkingMemory = CopyWorkingMemory(runtime.workingMemory),
                RelationshipStates = CopyRelationshipStates(runtime.relationshipStates),
            };

            error = null;
            return true;
        }

        static NpcRuntimeStatus CopyRuntimeStatus(NpcRuntimeStatusDto dto)
        {
            if (dto == null) return new NpcRuntimeStatus();
            return new NpcRuntimeStatus
            {
                CurrentStageId = dto.currentStageId,
                CurrentLocationId = dto.currentLocationId,
                IsActive = dto.isActive,
                IsAvailable = dto.isAvailable,
                LastUpdatedTurn = dto.lastUpdatedTurn,
            };
        }

        static NpcGoalState CopyGoal(NpcGoalDto dto)
        {
            if (dto == null) return new NpcGoalState();
            return new NpcGoalState
            {
                GoalId = dto.goalId,
                GoalType = dto.goalType,
                ReasonInformationId = dto.reasonInformationId,
                TargetLocationId = dto.targetLocationId,
                Status = dto.status,
            };
        }

        static NpcMoveState CopyMove(NpcMoveDto dto)
        {
            if (dto == null) return new NpcMoveState();
            return new NpcMoveState
            {
                TargetLocationId = dto.targetLocationId,
                ReasonGoalId = dto.reasonGoalId,
                Status = dto.status,
                StartedTurn = dto.startedTurn,
            };
        }

        static List<BeliefState> CopyBeliefStates(List<NpcBeliefStateDto> list)
        {
            var result = new List<BeliefState>();
            if (list == null) return result;
            foreach (var dto in list)
            {
                result.Add(new BeliefState
                {
                    InformationId = dto.informationId,
                    CurrentLevel = dto.currentLevel,
                    PreviousLevel = dto.previousLevel,
                    SourceId = dto.sourceId,
                    EvidenceIds = new List<string>(dto.evidenceIds ?? new List<string>()),
                    LastUpdatedTurn = dto.lastUpdatedTurn,
                    IsInitialBelief = dto.isInitialBelief,
                });
            }
            return result;
        }

        static WorkingMemory CopyWorkingMemory(NpcWorkingMemoryDto dto)
        {
            var result = new WorkingMemory();
            if (dto == null) return result;
            result.KnownInformationIds = new List<string>(dto.knownInformationIds ?? new List<string>());
            result.RecentInformationIds = new List<string>(dto.recentInformationIds ?? new List<string>());
            result.RecentEventIds = new List<string>(dto.recentEventIds ?? new List<string>());
            result.ObservedNpcIds = new List<string>(dto.observedNpcIds ?? new List<string>());
            return result;
        }

        static List<RelationshipState> CopyRelationshipStates(List<NpcRelationshipStateDto> list)
        {
            var result = new List<RelationshipState>();
            if (list == null) return result;
            foreach (var dto in list)
            {
                result.Add(new RelationshipState
                {
                    TargetType = dto.targetType,
                    TargetId = dto.targetId,
                    Trust = dto.trust,
                    Intimacy = dto.intimacy,
                    Influence = dto.influence,
                    TrustDelta = dto.trustDelta,
                    IntimacyDelta = dto.intimacyDelta,
                    InfluenceDelta = dto.influenceDelta,
                    InitializedFromProfile = dto.initializedFromProfile,
                });
            }
            return result;
        }
    }
}
