using System.Collections.Generic;
using System.Threading.Tasks;
using Belief.AI;
using Belief.Data;
using Belief.Domain;
using UnityEngine;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using Belief.Debugging;
#endif

namespace Belief.Systems
{
    /// <summary>
    /// NPC 이동(등급 구분 없음 - 전원 동일 경로). 목적지 후보(movementCandidates)는 v4 destinationCandidateIds를 그대로 반영한
    /// 실제 데이터이고, 실제 판단(LLM 우선, 실패 시 RuleBased 대체)은 IMajorNpcThinker.DecideMoveAsync에
    /// 위임한다 - 이 클래스는 매 턴 전체 NPC를 순회하며 판단을 요청하고 최종 Move를 실행하는
    /// 오케스트레이션만 담당한다("8. Unity가 최종 Move를 실행"). NPC별로 순차 await하므로(Task.WhenAll
    /// 같은 병렬 실행 없음) 같은 턴 안에서 서로 다른 NPC의 판단이 겹치지 않는다 - 기존 이동 순서 규칙도
    /// 그대로 유지된다.
    /// </summary>
    public class NpcMovementSystem
    {
        readonly ActionResolutionSystem actionResolution;
        readonly IMajorNpcThinker thinker;
        int currentTurn;

        public NpcMovementSystem(ActionResolutionSystem actionResolution, IMajorNpcThinker thinker)
        {
            this.actionResolution = actionResolution;
            this.thinker = thinker;
        }

        public async Task MoveNpcsAsync(IEnumerable<NpcState> allNpcs, int currentTurn)
        {
            this.currentTurn = currentTurn;

            // 필수 구현 6(중복 적용 방지)의 방어적 검증 - 이 호출(=이번 턴) 동안 NPC 하나당 이동
            // 적용은 최대 한 번만 허용한다. 정상 흐름에서는 아래 foreach가 NPC마다 정확히 한 번씩만
            // thinker를 호출하므로 항상 통과하지만, 실수로 같은 NPC를 두 번 처리하는 회귀가 생기면
            // 여기서 걸러진다(구조적으로도 발생할 수 없음 - LlmMajorThinker의 늦은 응답 관찰 경로는
            // ActionResolutionSystem을 아예 참조하지 않는다).
            var appliedThisCall = new HashSet<string>();

            foreach (var npc in new List<NpcState>(allNpcs))
            {
                if (!(npc.Data is MajorNpcData major)) continue;
                if (major.movementCandidates == null || major.movementCandidates.Length == 0) continue;

                object traceParam = null;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                NpcDecisionTraceBuilder trace = null;
                if (NpcDecisionTraceHub.HasListeners)
                {
                    trace = new NpcDecisionTraceBuilder(
                        "TurnMove", npc,
                        NpcDecisionTraceContext.StageId, NpcDecisionTraceContext.StageTurn,
                        NpcDecisionTraceContext.MissionId, NpcDecisionTraceContext.MissionTurn,
                        NpcDecisionTraceContext.ThinkerMode);
                    trace.WithReceivedInformation(null);
                    trace.WithStateBefore(npc, null);
                }
                traceParam = trace;
#endif

                var beforeLocation = npc.CurrentLocation;
                var context = new NpcMoveContext(npc, beforeLocation, major.movementCandidates, this.currentTurn);
                var result = await thinker.DecideMoveAsync(context, traceParam);

                bool willMove = result.Destination != null && result.Destination != beforeLocation;
                string npcKey = npc.Data.npcId;
                int actionResolutionCount = 0;

                if (willMove)
                {
                    if (appliedThisCall.Add(npcKey))
                    {
                        actionResolution.MoveNpc(npc, result.Destination);
                        actionResolutionCount = 1;
                    }
                    else
                    {
                        Debug.LogWarning($"[NpcMovementSystem] Turn {this.currentTurn}에 {npcKey}의 중복 이동 적용을 차단했습니다.");
                    }
                }
                else
                {
                    appliedThisCall.Add(npcKey);
                }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (trace != null)
                {
                    string positionBeforeId = beforeLocation != null ? beforeLocation.locationId : null;
                    string positionAfterId = actionResolutionCount == 1 && result.Destination != null
                        ? result.Destination.locationId : positionBeforeId;

                    trace.WithFinalResolution(
                        finalIntent: null,
                        finalActionId: null,
                        finalDialogue: null,
                        finalGoal: npc.CurrentGoal,
                        finalMoveDestinationId: result.Destination != null ? result.Destination.locationId : null,
                        actionResolutionCount: actionResolutionCount,
                        positionBefore: positionBeforeId,
                        positionAfter: positionAfterId);
                    trace.Publish();
                }
#endif
            }
        }
    }
}
