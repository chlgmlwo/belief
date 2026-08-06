using System;
using System.Collections.Generic;
using UnityEngine;
using Belief.Data;
using Belief.Domain;

namespace Belief.Core
{
    // ---------------------------------------------------------------- 저장 형식
    // 에셋 참조는 전부 ID 문자열로 바꿔 담는다. 직접 참조를 담으면 JsonUtility가 인스턴스 ID를
    // 저장해 다음 실행에서 아무 의미도 없는 값이 된다.

    [Serializable]
    public class NpcWorldSaveDto
    {
        public string npcId;
        public string locationId;
        public string goal;
        public string behaviorModifier;
        public long locationChangeStamp;
        public long latestBeliefStamp;

        /// <summary>believeCardIds[i]에 대한 판단이 beliefStates[i], 그 시점 스탬프가 beliefStamps[i].
        /// 셋은 항상 같은 길이다 - Dictionary를 JsonUtility가 직렬화하지 못해 나란한 배열로 편다.</summary>
        public string[] beliefCardIds;
        public int[] beliefStates;
        public long[] beliefStamps;

        public string[] receivedCardIds;
        public int[] receivedTurns;
    }

    [Serializable]
    public class RumorWorldSaveDto
    {
        public string informationId;
        public string sourceCardId;
        public string propagatedByNpcId;
        public int activatedTurn;
        public bool isActive;
        public long lastChangedStamp;
    }

    [Serializable]
    public class InvestigationWorldSaveDto
    {
        public string informationId;
        public string categoryId;
        public string actorNpcId;
        public int resultType;
        public int activatedTurn;
        public bool isActive;
        public long lastChangedStamp;
    }

    [Serializable]
    public class LocationWorldSaveDto
    {
        public string locationId;
        public int siteState;
        public RumorWorldSaveDto[] rumors;
        public InvestigationWorldSaveDto[] investigations;
    }

    /// <summary>
    /// 오토세이브에 함께 담기는 세계 상태.
    ///
    /// <b>담는 것</b> - NPC의 위치·믿음·받은 정보·목표·행동 모드와 각종 스탬프, 장소의 경보 단계·
    /// 소문·조사 기록, 그리고 세계 시계. 미션 완료 판정(MissionEvaluationContext)이 읽는 값이
    /// 전부 여기 들어 있어야 복원 후 판정이 저장 시점과 같아진다.
    ///
    /// <b>담지 않는 것</b> - 손패/카드 풀과 NPC의 장기 기억. 손패는 미션을 다시 시작하면 어차피
    /// 새로 뽑히는 값이라(세션 안에서 미션을 재시작할 때와 같다) 저장 단위인 "미션 처음부터"와
    /// 오히려 어긋나지 않고, 장기 기억은 완료 판정에 쓰이지 않는다.
    /// </summary>
    [Serializable]
    public class WorldSaveDto
    {
        public long worldClock;
        public NpcWorldSaveDto[] npcs;
        public LocationWorldSaveDto[] locations;
    }

    // ---------------------------------------------------------------- 캡처 / 복원

    public static class WorldSaveMapper
    {
        public static WorldSaveDto Capture(GameInstaller installer)
        {
            if (installer == null) return null;

            var npcs = new List<NpcWorldSaveDto>();
            foreach (var kv in installer.Npcs)
            {
                var data = kv.Key;
                var s = kv.Value;
                if (data == null || string.IsNullOrEmpty(data.npcId)) continue;

                var cardIds = new List<string>();
                var states = new List<int>();
                var stamps = new List<long>();
                foreach (var b in s.Beliefs)
                {
                    if (b.Key == null || string.IsNullOrEmpty(b.Key.cardId)) continue;
                    cardIds.Add(b.Key.cardId);
                    states.Add((int)b.Value);
                    stamps.Add(s.GetBeliefStamp(b.Key));
                }

                var recvIds = new List<string>();
                var recvTurns = new List<int>();
                foreach (var r in s.ReceivedInformation)
                {
                    if (r.Card == null || string.IsNullOrEmpty(r.Card.cardId)) continue;
                    recvIds.Add(r.Card.cardId);
                    recvTurns.Add(r.Turn);
                }

                npcs.Add(new NpcWorldSaveDto
                {
                    npcId = data.npcId,
                    locationId = s.CurrentLocation != null ? s.CurrentLocation.locationId : null,
                    goal = s.CurrentGoal,
                    behaviorModifier = s.CurrentBehaviorModifier,
                    locationChangeStamp = s.LocationChangeStamp,
                    latestBeliefStamp = s.LatestBeliefStamp,
                    beliefCardIds = cardIds.ToArray(),
                    beliefStates = states.ToArray(),
                    beliefStamps = stamps.ToArray(),
                    receivedCardIds = recvIds.ToArray(),
                    receivedTurns = recvTurns.ToArray(),
                });
            }

            var locations = new List<LocationWorldSaveDto>();
            foreach (var kv in installer.Locations)
            {
                var data = kv.Key;
                var s = kv.Value;
                if (data == null || string.IsNullOrEmpty(data.locationId)) continue;

                var rumors = new List<RumorWorldSaveDto>();
                foreach (var r in s.ActiveRumors)
                {
                    if (r.Information == null || string.IsNullOrEmpty(r.Information.informationId)) continue;
                    rumors.Add(new RumorWorldSaveDto
                    {
                        informationId = r.Information.informationId,
                        sourceCardId = r.SourceCard != null ? r.SourceCard.cardId : null,
                        propagatedByNpcId = r.PropagatedBy != null ? r.PropagatedBy.npcId : null,
                        activatedTurn = r.ActivatedTurn,
                        isActive = r.IsActive,
                        lastChangedStamp = r.LastChangedStamp,
                    });
                }

                var investigations = new List<InvestigationWorldSaveDto>();
                foreach (var w in s.InvestigationStates)
                {
                    if (w.Information == null || string.IsNullOrEmpty(w.Information.informationId)) continue;
                    investigations.Add(new InvestigationWorldSaveDto
                    {
                        informationId = w.Information.informationId,
                        categoryId = w.CategoryId,
                        actorNpcId = w.Actor != null ? w.Actor.npcId : null,
                        resultType = (int)w.ResultType,
                        activatedTurn = w.ActivatedTurn,
                        isActive = w.IsActive,
                        lastChangedStamp = w.LastChangedStamp,
                    });
                }

                locations.Add(new LocationWorldSaveDto
                {
                    locationId = data.locationId,
                    siteState = (int)s.SiteState,
                    rumors = rumors.ToArray(),
                    investigations = investigations.ToArray(),
                });
            }

            return new WorldSaveDto
            {
                worldClock = WorldChangeClock.Current,
                npcs = npcs.ToArray(),
                locations = locations.ToArray(),
            };
        }

        /// <summary>저장본을 지금 씬의 상태에 덮어쓴다. 저장본에 없는 NPC/장소(스테이지 구성이 바뀐
        /// 경우)는 건드리지 않고 초기 상태로 남긴다 - 없는 대상 때문에 복원 전체를 포기하지 않는다.
        /// 돌려주는 값은 실제로 덮어쓴 NPC 수 / 장소 수.</summary>
        public static (int npcs, int locations) Apply(GameInstaller installer, WorldSaveDto dto)
        {
            if (installer == null || dto == null) return (0, 0);

            // 스탬프를 먼저 맞춘다 - 복원 도중 생성되는 값이 과거 스탬프와 겹치지 않게 한다.
            WorldChangeClock.RestoreAtLeast(dto.worldClock);

            var npcById = new Dictionary<string, NpcData>();
            foreach (var kv in installer.Npcs)
                if (kv.Key != null && !string.IsNullOrEmpty(kv.Key.npcId)) npcById[kv.Key.npcId] = kv.Key;

            var locById = new Dictionary<string, LocationData>();
            foreach (var kv in installer.Locations)
                if (kv.Key != null && !string.IsNullOrEmpty(kv.Key.locationId)) locById[kv.Key.locationId] = kv.Key;

            var cardById = BuildCardMap(installer);
            var infoById = BuildInformationMap(cardById, installer);

            int npcCount = 0;
            if (dto.npcs != null)
            {
                foreach (var n in dto.npcs)
                {
                    if (n == null || !npcById.TryGetValue(n.npcId, out var npcData)) continue;
                    if (!installer.Npcs.TryGetValue(npcData, out var state)) continue;

                    var beliefs = new Dictionary<InformationCardData, BeliefState>();
                    var beliefStamps = new Dictionary<InformationCardData, long>();
                    if (n.beliefCardIds != null)
                    {
                        for (int i = 0; i < n.beliefCardIds.Length; i++)
                        {
                            if (!cardById.TryGetValue(n.beliefCardIds[i], out var card)) continue;
                            beliefs[card] = (BeliefState)n.beliefStates[i];
                            beliefStamps[card] = n.beliefStamps[i];
                        }
                    }

                    var received = new List<ReceivedInformationEntry>();
                    if (n.receivedCardIds != null)
                    {
                        for (int i = 0; i < n.receivedCardIds.Length; i++)
                            if (cardById.TryGetValue(n.receivedCardIds[i], out var card))
                                received.Add(new ReceivedInformationEntry(card, n.receivedTurns[i]));
                    }

                    LocationData loc = null;
                    if (!string.IsNullOrEmpty(n.locationId)) locById.TryGetValue(n.locationId, out loc);
                    if (loc == null) loc = state.CurrentLocation;   // 저장된 장소가 사라졌으면 지금 자리를 유지

                    // 장기 기억과 마지막 행동은 저장 대상이 아니라 빈 값으로 넣는다 - 완료 판정이
                    // 읽지 않는 값이고, 그 둘까지 담으려면 저장 형식이 크게 늘어난다.
                    state.RestoreSnapshot(new NpcState.NpcStateSnapshot(
                        loc, n.goal, n.behaviorModifier, null,
                        beliefs, new List<MemoryEntry>(), received,
                        n.locationChangeStamp, n.latestBeliefStamp, beliefStamps));
                    npcCount++;
                }
            }

            int locCount = 0;
            if (dto.locations != null)
            {
                foreach (var l in dto.locations)
                {
                    if (l == null || !locById.TryGetValue(l.locationId, out var locData)) continue;
                    if (!installer.Locations.TryGetValue(locData, out var state)) continue;

                    var rumors = new List<RumorState>();
                    if (l.rumors != null)
                    {
                        foreach (var r in l.rumors)
                        {
                            if (!infoById.TryGetValue(r.informationId, out var info)) continue;
                            cardById.TryGetValue(r.sourceCardId ?? "", out var sourceCard);
                            NpcData by = null;
                            if (!string.IsNullOrEmpty(r.propagatedByNpcId)) npcById.TryGetValue(r.propagatedByNpcId, out by);
                            rumors.Add(RumorState.Restore(info, sourceCard, locData, by, r.activatedTurn, r.isActive, r.lastChangedStamp));
                        }
                    }

                    var investigations = new List<InformationWorldState>();
                    if (l.investigations != null)
                    {
                        foreach (var w in l.investigations)
                        {
                            if (!infoById.TryGetValue(w.informationId, out var info)) continue;
                            NpcData actor = null;
                            if (!string.IsNullOrEmpty(w.actorNpcId)) npcById.TryGetValue(w.actorNpcId, out actor);
                            investigations.Add(InformationWorldState.Restore(info, w.categoryId, locData, actor,
                                (InformationResultType)w.resultType, w.activatedTurn, w.isActive, w.lastChangedStamp));
                        }
                    }

                    state.RestoreSnapshot(new LocationState.LocationStateSnapshot(
                        rumors, investigations, (LocationSiteState)l.siteState));
                    locCount++;
                }
            }

            // NpcState.RestoreSnapshot은 CurrentLocation만 되돌리고 장소별 PresentNpcs는 건드리지
            // 않는다(주석 참고) - 전원 위치가 확정된 지금 한 번에 다시 짠다.
            RebuildPresentNpcs(installer);

            return (npcCount, locCount);
        }

        static void RebuildPresentNpcs(GameInstaller installer)
        {
            foreach (var kv in installer.Locations) kv.Value.PresentNpcs.Clear();
            foreach (var kv in installer.Npcs)
            {
                var loc = kv.Value.CurrentLocation;
                if (loc != null && installer.Locations.TryGetValue(loc, out var ls)) ls.PresentNpcs.Add(kv.Value);
            }
        }

        /// <summary>cardId -> 카드. 카드 풀이 이 스테이지가 쓰는 카드의 유일한 출처다.</summary>
        static Dictionary<string, InformationCardData> BuildCardMap(GameInstaller installer)
        {
            var map = new Dictionary<string, InformationCardData>();
            var pool = installer.InformationPool;
            if (pool == null || pool.cards == null) return map;
            foreach (var c in pool.cards)
                if (c != null && !string.IsNullOrEmpty(c.cardId)) map[c.cardId] = c;
            return map;
        }

        /// <summary>informationId -> 정보. 소문/조사 기록은 카드가 아니라 정보 단위로 식별되므로
        /// 카드에 물려 있는 information을 훑어 만든다.</summary>
        static Dictionary<string, InformationData> BuildInformationMap(
            Dictionary<string, InformationCardData> cardById, GameInstaller installer)
        {
            var map = new Dictionary<string, InformationData>();
            foreach (var kv in cardById)
            {
                var info = kv.Value.information;
                if (info != null && !string.IsNullOrEmpty(info.informationId)) map[info.informationId] = info;
            }
            return map;
        }
    }
}
