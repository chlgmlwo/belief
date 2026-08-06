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
        int attemptCounter;

        public MissionState State => state;

        /// <summary>현재 시도의 기준점. TurnSystem이 미션 시작·재시작 시점에 BeginAttempt로 만든다.
        /// null이면 완료 판정은 기존 동작(현재 상태만 검사)으로 흐른다.</summary>
        public MissionStartBaseline Baseline { get; private set; }

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
            Baseline = null; // 곧바로 이어지는 BeginAttempt가 새 미션 기준으로 다시 만든다.
            eventBus.Publish(new MissionChangedEvent(data, state));
        }

        /// <summary>새 미션 시도의 기준점을 만든다. <b>반드시 이 미션의 첫 Evaluate보다 먼저</b>
        /// 호출되어야 한다 - 그러지 않으면 이전 미션이 남긴 상태만으로 첫 평가가 통과한다.
        /// 호출 지점은 TurnSystem의 StartGame / ResetForNewMission / RestartMissionAttempt 셋뿐이다.</summary>
        public void BeginAttempt(MissionEvaluationContext context)
        {
            Baseline = MissionStartBaseline.Capture(state.Data, context, ++attemptCounter);
        }

        /// <summary>완료 판정의 단일 진입점. MissionSystem 자신의 Evaluate와 ProgressionController의
        /// 최종 확정이 <b>같은 함수</b>를 거치게 해서, 한쪽만 신선도를 반영해 "HUD는 완료인데 확정은
        /// 안 되는" 상태로 갈라지지 않게 한다. 지금 추적 중인 미션이 아니면 기존 동작으로 계산한다.</summary>
        public int EvaluateSuccessProgress(MissionData mission, MissionEvaluationContext context)
        {
            if (mission == null) return 0;
            if (mission != state.Data)
            {
                // 정상 흐름에서는 도달하지 않는다 - ProgressionController가 확인 대기 구간에서 재평가를
                // 건너뛰므로 "현재 목표"와 "추적 중인 미션"이 어긋난 채 평가되는 순간이 없다. 여기 걸렸다면
                // 그 불변식이 깨진 것이고, 기준점 없이 평가되어 즉시 클리어가 샐 수 있으므로 알린다.
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                UnityEngine.Debug.LogWarning(
                    $"[MissionSystem] 추적 중인 미션({state.Data?.missionId})과 평가 대상({mission.missionId})이 " +
                    "달라 기준점 없이 기존 방식으로 판정했습니다 - 미션 전환 순서를 확인하세요.");
#endif
                return mission.GetSuccessProgress(context);
            }

            var baseline = Baseline;
            return mission.GetSuccessProgress(context,
                c => MissionFreshnessEvaluator.CountsForCompletion(c, baseline, context));
        }

        public void Evaluate(MissionEvaluationContext context)
        {
            int progress = EvaluateSuccessProgress(state.Data, context);
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
