using Belief.Data;
using Belief.Domain;
using Belief.Events;

namespace Belief.Systems
{
    /// <summary>MissionState의 유일한 쓰기 지점. 승리 판정 로직은 MissionConditionData(데이터)에 있다.</summary>
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
