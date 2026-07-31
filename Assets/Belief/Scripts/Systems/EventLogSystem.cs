using System;
using System.Collections.Generic;
using UnityEngine;
using Belief.Data;
using Belief.Events;

namespace Belief.Systems
{
    /// <summary>
    /// GameEventBus를 구독해 플레이어에게 공개 가능한 이벤트만 사람이 읽는
    /// 로그 문자열로 변환한다. 점수/보정치/관계·기억 수치 등 숨겨진 내부 값은 여기로
    /// 절대 흘러들어오지 않는다 - 자연어 결과(행동/대화/이동/사건)만 다룬다.
    /// </summary>
    public class EventLogSystem
    {
        readonly List<string> entries = new List<string>();
        public IReadOnlyList<string> Entries => entries;

        int lastMissionProgress = -1;

        /// <summary>Presentation이 매 프레임 폴링하지 않고 새 로그 한 줄만 받아 갱신할 수 있도록.</summary>
        public event Action<string> OnLogAdded;

        public EventLogSystem(IGameEventBus bus)
        {
            bus.Subscribe<GameInitializedEvent>(OnGameInitialized);
            bus.Subscribe<TurnStartedEvent>(OnTurnStarted);
            bus.Subscribe<TurnEndedEvent>(OnTurnEnded);
            bus.Subscribe<InfoSpreadEvent>(OnInfoSpread);
            bus.Subscribe<InfoDeliveredEvent>(OnInfoDelivered);
            bus.Subscribe<CardJudgedEvent>(OnCardJudged);
            bus.Subscribe<InformationAcquiredEvent>(OnInformationAcquired);
            bus.Subscribe<NpcRelocatedEvent>(OnNpcRelocated);
            bus.Subscribe<NpcSpokeEvent>(OnNpcSpoke);
            bus.Subscribe<WorldEventOccurred>(OnWorldEvent);
            bus.Subscribe<MissionProgressChangedEvent>(OnMissionProgressChanged);
            bus.Subscribe<MissionCompletedEvent>(OnMissionCompleted);
            bus.Subscribe<GameOverEvent>(OnGameOver);
        }

        void OnGameInitialized(GameInitializedEvent e) =>
            Log($"BELIEF 초기화 완료 - 장소 {e.LocationCount}곳, NPC {e.NpcCount}명, 카드 {e.CardCount}장 로드됨.");

        void OnTurnStarted(TurnStartedEvent e) => Log($"===== 턴 {e.Turn}/{e.MaxTurns} 시작 =====");

        void OnTurnEnded(TurnEndedEvent e) => Log($"===== 턴 {e.Turn} 종료 =====");

        void OnInfoSpread(InfoSpreadEvent e) => Log($"'{e.Card.information.title}' 정보가 {e.Location.displayName}에 퍼졌다.");

        void OnInfoDelivered(InfoDeliveredEvent e) => Log($"'{e.Card.information.title}' 정보가 {e.Target.displayName}에게 전달되었다.");

        void OnInformationAcquired(InformationAcquiredEvent e)
        {
            if (e.Cards.Count == 0) return;
            Log(e.IsInitialSupply
                ? $"정보 {e.Cards.Count}개를 지급받았다."
                : $"보유 정보가 부족해 새로운 정보 {e.Cards.Count}개를 획득했다.");
        }

        void OnNpcRelocated(NpcRelocatedEvent e) =>
            Log($"{e.Npc.displayName}가(이) {(e.From != null ? e.From.displayName : "?")}에서 {e.To.displayName}(으)로 이동했다.");

        void OnNpcSpoke(NpcSpokeEvent e)
        {
            string text = e.Dialogue.IsGenerated ? e.Dialogue.GeneratedText : e.Dialogue.PredefinedLine?.text;
            if (!string.IsNullOrEmpty(text))
                Log($"{e.Npc.displayName}: \"{text}\"");
        }

        void OnWorldEvent(WorldEventOccurred e) => Log(e.Message);

        void OnCardJudged(CardJudgedEvent e)
        {
            string reaction = e.ResultBelief switch
            {
                BeliefState.Trusted => "믿었다",
                BeliefState.Plausible => "그럴듯하다고 여겼다",
                BeliefState.NeedsVerification => "확인이 필요하다고 여겼다",
                BeliefState.Doubtful => "의심했다",
                BeliefState.Denied => "믿지 않았다",
                _ => null
            };
            if (reaction == null) return;

            Log($"{e.Npc.displayName}가(이) '{e.Card.information.title}' 정보를 {reaction}.");
        }

        void OnMissionProgressChanged(MissionProgressChangedEvent e)
        {
            if (e.CurrentProgress == lastMissionProgress) return;
            if (e.CurrentProgress > lastMissionProgress) Log("임무 진행도가 증가했다.");
            lastMissionProgress = e.CurrentProgress;
        }

        void OnMissionCompleted(MissionCompletedEvent e) => Log("임무 성공!");

        void OnGameOver(GameOverEvent e) => Log(e.Won ? "게임 종료 - 승리" : "게임 종료 - 턴 소진");

        void Log(string message)
        {
            entries.Add(message);
            Debug.Log("[BELIEF] " + message);
            OnLogAdded?.Invoke(message);
        }
    }
}
