using Belief.Data;
using Belief.Domain;
using Belief.Events;

namespace Belief.Systems
{
    /// <summary>MissionState의 유일한 쓰기 지점. 승리 판정 로직은 MissionConditionData(데이터)에 있다.
    /// 역할은 "현재 로드된 MissionData 하나"의 진행률 계산과 UI/로그용 이벤트 제공으로 한정된다 -
    /// 최종 판정 권한(성공/실패/턴소진/전환/승리 확정, GameOverEvent 발행)은 전부
    /// Belief.Core.ProgressionController에 있다. 아래 MissionCompletedEvent는 게임 흐름을 바꾸지
    /// 않는다 - EventLogSystem이 로그 문구("임무 성공!")로만 소비하는 알림용이다.</summary>
    public class MissionSystem
    {
        MissionState state;
        readonly IGameEventBus eventBus;
        bool completedAnnounced;

        public MissionState State => state;

        public MissionSystem(MissionData data, IGameEventBus eventBus)
        {
            state = new MissionState(data);
            this.eventBus = eventBus;
        }

        /// <summary>추적 중인 미션을 다음 미션으로 교체한다(같은 구역 안에서의 미션 전환 전용).
        /// 진행도/완료 통지 상태를 새 미션 기준으로 초기화한다 - 판정 로직(Evaluate/GetSuccessProgress)은
        /// 건드리지 않는다.</summary>
        public void LoadMission(MissionData data)
        {
            state = new MissionState(data);
            completedAnnounced = false;
            eventBus.Publish(new MissionChangedEvent(data, state));
        }

        public void Evaluate(MissionEvaluationContext context)
        {
            int progress = state.Data.GetSuccessProgress(context);
            state.UpdateProgress(progress);

            eventBus.Publish(new MissionProgressChangedEvent(progress, state.Data.SuccessTarget));

            if (state.IsComplete && !completedAnnounced)
            {
                completedAnnounced = true;
                eventBus.Publish(new MissionCompletedEvent());
            }
        }
    }
}
