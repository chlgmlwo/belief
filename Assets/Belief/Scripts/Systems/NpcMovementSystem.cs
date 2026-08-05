using System;
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
    /// NPC 이동(등급 구분 없음 - 전원 동일 경로). 목적지 후보(movementCandidates)는 v4 destinationCandidateIds를
    /// 그대로 반영한 실제 데이터이고, 실제 판단은 IMajorNpcThinker에 위임한다 - 이 클래스는 매 턴
    /// "누가 새로 판단해야 하는가"를 고르고, 요청을 한꺼번에 띄우고, 최종 Move를 실행하는 오케스트레이션만
    /// 담당한다("8. Unity가 최종 Move를 실행").
    ///
    /// 한 턴은 정확히 세 단계로 나뉘고, 세 단계 모두 <b>같은 순서 리스트</b>를 공유한다:
    ///
    /// <list type="number">
    /// <item><b>선별</b> - NpcState.NeedsFreshDecision인 NPC만 LLM 경로(thinker)로, 나머지는 전부
    ///   RuleBased 경로로 보낸다. 이미 하던 대로 움직이면 되는 NPC에 토큰을 쓰지 않는 것이 목적이다.</item>
    /// <item><b>발사</b> - await 없이 요청만 전부 띄운다. 이 루프 안에 대기 지점이 하나도 없으므로
    ///   그 사이 세계가 바뀔 수 없고, 결과적으로 모든 요청이 <b>동일한 턴 스냅샷</b>을 본다
    ///   (별도의 스냅샷 자료구조가 필요 없는 이유다). NpcMoveContext는 현재 위치를 값으로 캡처해
    ///   담으므로, 나중에 다른 NPC가 이동해도 이미 띄운 요청의 입력은 변하지 않는다.</item>
    /// <item><b>적용</b> - 응답이 도착한 순서가 아니라 1단계에서 확정한 순서대로 await하고 적용한다.
    ///   전부 이미 동시에 진행 중이므로 순서대로 기다려도 총 대기 시간은 "가장 느린 하나"다.</item>
    /// </list>
    ///
    /// 순서의 출처(중요): Dictionary 열거 순서를 "고정 순서"로 가정하지 않는다 - 삽입 순서로 도는 것은
    /// 구현 세부이지 보장이 아니다. npcId를 StringComparer.Ordinal로 정렬해 매번 같은 리스트를 만들고
    /// 세 단계가 그 하나만 쓴다. 그래서 응답 도착 순서나 딕셔너리 내부 상태가 어떻든 실행 결과가 같다.
    /// </summary>
    public class NpcMovementSystem
    {
        readonly ActionResolutionSystem actionResolution;

        /// <summary>선별된 NPC(NeedsFreshDecision)의 판단 경로. LLM 모드면 LlmMajorThinker,
        /// RuleOnly 모드면 ThinkerFactory가 RuleBased를 넘겨준다 - 즉 RuleOnly에서는 이 필드로
        /// 가든 아래 ruleBased로 가든 결과가 같고, Transport는 애초에 존재하지 않는다.</summary>
        readonly IMajorNpcThinker thinker;

        /// <summary>비대상 NPC의 판단 경로. thinker가 LLM이어도 이쪽은 절대 Transport를 타지 않는다 -
        /// "판단이 필요 없는 NPC는 호출하지 않는다"를 코드 구조로 보장하는 지점이다.</summary>
        readonly RuleBasedMajorThinker ruleBased;

        int currentTurn;

        public NpcMovementSystem(ActionResolutionSystem actionResolution, IMajorNpcThinker thinker, RuleBasedMajorThinker ruleBased)
        {
            this.actionResolution = actionResolution;
            this.thinker = thinker;
            this.ruleBased = ruleBased;
        }

        /// <summary>발사 단계에서 만들어 적용 단계까지 들고 가는 한 NPC분의 진행 중 판단.</summary>
        readonly struct PendingMove
        {
            public readonly NpcState Npc;
            public readonly LocationData BeforeLocation;
            public readonly Task<NpcMoveResult> Request;
            public readonly bool UsedThinkerPath;
            public readonly object Trace;

            public PendingMove(NpcState npc, LocationData beforeLocation, Task<NpcMoveResult> request, bool usedThinkerPath, object trace)
            {
                Npc = npc;
                BeforeLocation = beforeLocation;
                Request = request;
                UsedThinkerPath = usedThinkerPath;
                Trace = trace;
            }
        }

        public async Task MoveNpcsAsync(IEnumerable<NpcState> allNpcs, int currentTurn)
        {
            this.currentTurn = currentTurn;

            var ordered = BuildStableOrder(allNpcs);

            try
            {
                var pending = Dispatch(ordered);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                LogSelection(pending);
#endif
                await ApplyInOrder(pending);
            }
            finally
            {
                // 마커는 여기서만 내린다. Dispatch가 이미 끝난 뒤이므로 선별보다 먼저 초기화될 수
                // 없고, 적용 도중 예외가 나도 다음 턴으로 잔류하지 않는다(잔류하면 그 NPC가 이유
                // 없이 LLM 대상이 되어 토큰을 쓰고 선별 기준이 무너진다).
                foreach (var npc in ordered) npc.ClearDecisionMarkers();
            }
        }

        /// <summary>세 단계가 공유하는 유일한 순서 원본. npcId Ordinal 정렬이라 입력 컬렉션의 열거
        /// 순서가 무엇이든 결과가 같다(npcId는 애셋마다 고유하므로 동률이 없어 정렬이 결정론적이다).</summary>
        static List<NpcState> BuildStableOrder(IEnumerable<NpcState> allNpcs)
        {
            var list = new List<NpcState>();
            foreach (var npc in allNpcs)
                if (npc != null && npc.Data != null) list.Add(npc);

            list.Sort((a, b) => StringComparer.Ordinal.Compare(a.Data.npcId, b.Data.npcId));
            return list;
        }

        /// <summary>1+2단계. 이 메서드 안에는 await가 하나도 없다 - 반드시 그래야 모든 요청이 같은
        /// 세계를 본다. 나중에 여기에 await를 추가하면 "동일 턴 스냅샷" 보장이 조용히 깨진다.</summary>
        List<PendingMove> Dispatch(List<NpcState> ordered)
        {
            var pending = new List<PendingMove>(ordered.Count);

            foreach (var npc in ordered)
            {
                if (!(npc.Data is MajorNpcData major)) continue;
                if (major.movementCandidates == null || major.movementCandidates.Length == 0) continue;

                bool needsFreshDecision = npc.NeedsFreshDecision;

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

                // 여기서 await하지 않는다 - Task만 받아 두고 다음 NPC로 넘어간다. RuleBased는
                // Task.FromResult라 이 자리에서 이미 완료되고, LLM은 백그라운드로 진행된다.
                Task<NpcMoveResult> request;
                try
                {
                    request = needsFreshDecision
                        ? thinker.DecideMoveAsync(context, traceParam)
                        : ruleBased.DecideMoveAsync(context, traceParam);
                }
                catch (Exception ex)
                {
                    // 계약상 구현체는 던지지 않지만, 한 NPC의 사고가 나머지 전원의 이동을 막으면 안 된다.
                    Debug.LogWarning($"[NpcMovementSystem] Turn {this.currentTurn} {npc.Data.npcId} 이동 판단 요청이 실패했습니다: {ex.Message}");
                    request = Task.FromResult(new NpcMoveResult(null));
                }

                pending.Add(new PendingMove(npc, beforeLocation, request, needsFreshDecision, traceParam));
            }

            return pending;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>이번 턴에 실제로 몇 명이 새 판단 경로를 탔는지만 남긴다(관찰 전용 - 판단에
        /// 아무 영향도 주지 않는다). LLM 모드를 켰을 때 "정말 선별이 되고 있는가"를 이 한 줄로
        /// 확인할 수 있어야 한다 - 이것이 없으면 토큰이 새는지 알 방법이 로그 뒤짐뿐이다.</summary>
        void LogSelection(List<PendingMove> pending)
        {
            int selected = 0;
            var names = new List<string>();
            foreach (var p in pending)
                if (p.UsedThinkerPath) { selected++; names.Add(p.Npc.Data.npcId); }

            Debug.Log($"[NpcMovementSystem] Turn {this.currentTurn} 이동 판단 - 전체 {pending.Count}명 중 "
                      + $"새 판단 필요 {selected}명"
                      + (selected > 0 ? $" ({string.Join(", ", names)})" : ""));
        }
#endif

        /// <summary>3단계. pending은 이미 고정 순서이므로 이 루프가 곧 적용 순서다 - 응답이 어떤
        /// 순서로 도착하든 세계에 반영되는 순서는 항상 같다.</summary>
        async Task ApplyInOrder(List<PendingMove> pending)
        {
            // 필수 구현 6(중복 적용 방지). 판단 시점이 아니라 <b>적용 시점</b>을 지킨다 - 병렬에서는
            // "판단을 한 번만 했다"가 "적용을 한 번만 했다"를 더 이상 함의하지 않기 때문이다.
            // 정상 흐름에서는 Dispatch가 NPC당 요청을 정확히 하나만 만들므로 항상 통과한다.
            var appliedThisTurn = new HashSet<string>();

            foreach (var p in pending)
            {
                var npc = p.Npc;
                string npcKey = npc.Data.npcId;

                NpcMoveResult result;
                try
                {
                    result = await p.Request;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[NpcMovementSystem] Turn {this.currentTurn} {npcKey} 이동 판단이 예외로 끝나 이동 없음으로 처리합니다: {ex.Message}");
                    result = new NpcMoveResult(null);
                }

                bool willMove = result.Destination != null && result.Destination != p.BeforeLocation;
                int actionResolutionCount = 0;

                if (!appliedThisTurn.Add(npcKey))
                {
                    Debug.LogWarning($"[NpcMovementSystem] Turn {this.currentTurn}에 {npcKey}의 중복 이동 적용을 차단했습니다.");
                    continue;
                }

                if (willMove)
                {
                    actionResolution.MoveNpc(npc, result.Destination);
                    actionResolutionCount = 1;
                }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                var trace = p.Trace as NpcDecisionTraceBuilder;
                if (trace != null)
                {
                    string positionBeforeId = p.BeforeLocation != null ? p.BeforeLocation.locationId : null;
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

                    // Publish도 적용과 같은 고정 순서로 나간다 - 관찰 창의 기록 순서가 실제 적용
                    // 순서와 어긋나면 병렬 동작을 읽을 수 없게 된다.
                    trace.Publish();
                }
#endif
            }
        }
    }
}
