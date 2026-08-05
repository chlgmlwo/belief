using System.Collections.Generic;
using Belief.Data;
using Belief.Domain;
using Belief.Events;
using UnityEngine;

namespace Belief.Systems
{
    /// <summary>이번 턴에 이 NPC의 이동이 이미 정해졌는지, 정해졌다면 어떻게 정해졌는지.</summary>
    public enum DestinationReservationType
    {
        /// <summary>예약이 없다 - 기존 이동 판단 경로를 그대로 탄다.</summary>
        None,
        /// <summary>이번 턴은 머문다고 <b>확정</b>됐다. 기존 이동 판단도 실행하지 않는다.</summary>
        Stay,
        /// <summary>특정 장소로 이동한다고 확정됐다.</summary>
        MoveTo,
    }

    /// <summary>
    /// 통합 판단이 정한 목적지를 <b>이번 턴의 이동 결정</b>으로 예약해 두는 저장소.
    ///
    /// 왜 즉시 이동시키지 않는가: 이동은 턴 구조상 모든 카드 판단이 끝난 뒤
    /// (TurnSystem.FinishTurnAsync → NpcMovementSystem) 한 번에 처리된다. 판단 시점에 바로
    /// 옮기면 그 턴의 재확산 대상 NPC 구성과 미션 판정이 보는 위치가 달라진다.
    ///
    /// <b>Stay를 명시적으로 기록하는 이유</b>: "예약 없음"과 "머물기로 확정"을 구분하지 않으면,
    /// LLM이 stay를 골랐는데 이동 단계에서 규칙 기반이 다른 곳으로 옮겨 버리는 혼합 결과가 된다.
    /// 그래서 Stay도 하나의 예약이며, 예약된 NPC는 기존 이동 판단을 아예 실행하지 않는다.
    ///
    /// 내부 컬렉션은 외부에 노출하지 않는다 - 예약을 만들고 소비하는 경로를 이 클래스 밖에서
    /// 임의로 늘리면 "한 턴에 한 번만"이라는 보장이 무너진다.
    /// </summary>
    public class DestinationReservation
    {
        readonly struct Entry
        {
            public readonly DestinationReservationType Type;
            public readonly LocationData Destination;
            public readonly string ApplicationKey;
            public readonly int MissionAttemptId;
            public readonly int Turn;

            public Entry(DestinationReservationType type, LocationData destination, string applicationKey,
                int missionAttemptId, int turn)
            {
                Type = type; Destination = destination; ApplicationKey = applicationKey;
                MissionAttemptId = missionAttemptId; Turn = turn;
            }
        }

        readonly Dictionary<NpcData, Entry> entries = new Dictionary<NpcData, Entry>();
        readonly IReadOnlyDictionary<LocationData, LocationState> locations;

        public DestinationReservation(IReadOnlyDictionary<LocationData, LocationState> locations, IGameEventBus eventBus)
        {
            this.locations = locations;

            // 새 시도가 시작되면(턴 1) 지난 시도의 예약은 전부 무효다. 미션 재시작·씬 전환
            // 모두 여기로 들어온다(씬 전환은 인스턴스 자체가 새로 만들어진다).
            eventBus?.Subscribe<TurnStartedEvent>(e => { if (e.Turn == 1) entries.Clear(); });
        }

        public int Count => entries.Count;

        /// <summary>
        /// 목적지를 예약한다. destination이 null이면 Stay 확정이다.
        /// 이미 이번 턴에 예약이 있으면 <b>덮어쓰지 않고 거부</b>한다 - 같은 NPC가 한 턴에 두 번
        /// 이동 결정을 받는 상황을 여기서 차단한다.
        /// </summary>
        public bool TryReserve(NpcState npc, LocationData destination, string applicationKey,
            int missionAttemptId, int turn, out string failureReason)
        {
            failureReason = null;

            if (npc?.Data == null) { failureReason = "NpcMissing"; return false; }

            if (entries.TryGetValue(npc.Data, out var existing)
                && existing.MissionAttemptId == missionAttemptId && existing.Turn == turn)
            {
                failureReason = $"AlreadyReserved:{existing.ApplicationKey}";
                return false;
            }

            // 등록되지 않은 장소는 예약 단계에서 막는다 - 이동 단계까지 흘려보내면
            // NpcMovementService가 CurrentLocation만 바꾸고 PresentNpcs는 갱신하지 못해
            // 장소별 재실 목록이 어긋난다.
            if (destination != null && (locations == null || !locations.ContainsKey(destination)))
            {
                failureReason = $"DestinationNotRegistered:{destination.locationId}";
                return false;
            }

            entries[npc.Data] = new Entry(
                destination == null ? DestinationReservationType.Stay : DestinationReservationType.MoveTo,
                destination, applicationKey, missionAttemptId, turn);
            return true;
        }

        /// <summary>
        /// 이번 턴의 예약을 <b>한 번만</b> 꺼낸다. 꺼낸 예약은 즉시 제거되므로 두 번 소비할 수 없다.
        /// 턴이 다르면 없는 것으로 취급한다(지난 턴 예약이 이번 턴 이동을 바꾸지 못한다).
        /// </summary>
        public DestinationReservationType TryConsume(NpcState npc, int turn, out LocationData destination)
        {
            destination = null;
            if (npc?.Data == null) return DestinationReservationType.None;
            if (!entries.TryGetValue(npc.Data, out var e)) return DestinationReservationType.None;
            if (e.Turn != turn) return DestinationReservationType.None;

            entries.Remove(npc.Data);

            if (e.Type == DestinationReservationType.MoveTo)
            {
                // 예약 시점에 검증했지만, 그 사이 장소 구성이 바뀌었을 가능성까지 여기서 한 번 더 막는다.
                if (e.Destination == null || locations == null || !locations.ContainsKey(e.Destination))
                {
                    Debug.LogError($"[DestinationReservation] {npc.Data.npcId}의 예약 목적지가 등록되지 않아 이동을 취소했습니다 "
                                 + $"({(e.Destination != null ? e.Destination.locationId : "null")}, key={e.ApplicationKey}). "
                                 + "이번 턴에는 이동하지 않습니다 - 규칙 기반 판단을 즉석에서 다시 실행하지 않습니다.");
                    return DestinationReservationType.Stay;
                }
                destination = e.Destination;
            }
            return e.Type;
        }

        /// <summary>턴 종료 등에서 남은 예약을 정리한다. 정상 흐름에서는 이동 단계가 전부 소비하므로
        /// 비어 있고, 남아 있다면 어딘가에서 판단만 하고 이동 단계를 타지 않은 것이다.</summary>
        public void ClearRemaining(string context = null)
        {
            if (entries.Count == 0) return;
            Debug.LogWarning($"[DestinationReservation] 소비되지 않은 예약 {entries.Count}건을 정리했습니다{(context != null ? " (" + context + ")" : "")}.");
            entries.Clear();
        }
    }
}
