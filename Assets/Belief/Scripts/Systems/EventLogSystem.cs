using System;
using System.Collections.Generic;
using UnityEngine;
using Belief.Core;
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

        /// <summary>턴 구분선 - 예전엔 `===== 턴 1 시작 =====`처럼 일반 로그와 완전히 같은 크기·색으로
        /// 나와서 수사 파일 톤과 어울리지 않고 시각적 위계도 없었다(2026-08-05 사용자 지적).
        /// TMP 리치 텍스트로 한 단계 작고 흐리게 눌러 배경 구분선처럼 보이게 한다.
        /// 턴 종료 로그는 바로 다음 줄의 "턴 N+1" 시작 구분선과 의미가 겹쳐 지웠다.</summary>
        void OnTurnStarted(TurnStartedEvent e) =>
            Log($"<size=85%><color=#8A857EFF>――  턴 {e.Turn} / {e.MaxTurns}  ――</color></size>");

        void OnTurnEnded(TurnEndedEvent e) { }

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
            Log($"{KoreanParticle.WithSubject(e.Npc.displayName)} " +
                $"{(e.From != null ? e.From.displayName : "?")}에서 " +
                $"{KoreanParticle.WithDirection(e.To.displayName)} 이동했다.");

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

            Log($"{KoreanParticle.WithSubject(e.Npc.displayName)} '{e.Card.information.title}' 정보를 {reaction}.");
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
