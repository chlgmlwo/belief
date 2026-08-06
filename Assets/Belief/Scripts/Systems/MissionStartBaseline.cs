using System.Collections.Generic;
using Belief.Data;
using Belief.Domain;

namespace Belief.Systems
{
    /// <summary>
    /// 현재 미션 시도가 시작된 순간의 기준점. 미션 사이에 세계 상태를 초기화하지 않는 대신,
    /// "이전 미션에서 이미 만족 중이던 조건은 새 미션의 성과로 즉시 인정하지 않는다"를 판정하기 위한
    /// 최소 정보만 들고 있다.
    ///
    /// 세계 상태 전체를 다시 복사하지 않는다 - 시작 시점의 <b>스탬프 하나</b>와 <b>조건별 시작 만족
    /// 여부</b>만 있으면 충분하다. 개별 상태가 그 뒤에 바뀌었는지는 각 레코드가 들고 있는
    /// LastChangedStamp/LocationChangeStamp를 이 StartStamp와 비교해서 알 수 있다.
    /// (재시작 복원용 전체 스냅샷은 TurnSystem이 따로 들고 있다 - 역할이 다르다.)
    /// </summary>
    public sealed class MissionStartBaseline
    {
        public readonly string MissionId;

        /// <summary>이 미션의 몇 번째 시도인지. 재시작마다 증가하며, 이전 시도의 진행이 새 시도에
        /// 인정되지 않는다는 것을 로그에서 구분하기 위한 값이다.</summary>
        public readonly int AttemptId;

        /// <summary>미션 시작 시점의 WorldChangeClock 값. 이보다 큰 스탬프를 가진 변화만
        /// "이번 미션에서 새로 일어난 일"이다.</summary>
        public readonly long StartStamp;

        readonly HashSet<MissionConditionData> satisfiedAtStart;

        MissionStartBaseline(string missionId, int attemptId, long startStamp,
            HashSet<MissionConditionData> satisfiedAtStart)
        {
            MissionId = missionId;
            AttemptId = attemptId;
            StartStamp = startStamp;
            this.satisfiedAtStart = satisfiedAtStart;
        }

        /// <summary>이 조건이 미션 시작 시점에 <b>이미</b> 만족 중이었는지. 참이면 그 조건은 이번
        /// 미션의 성과로 그냥 인정할 수 없고, 관련 상태가 새로 변했다는 근거가 따로 필요하다.</summary>
        public bool WasSatisfiedAtStart(MissionConditionData condition) =>
            condition != null && satisfiedAtStart.Contains(condition);

        /// <summary>미션이 활성화된 직후, 그 미션의 첫 평가보다 <b>먼저</b> 호출되어야 한다.
        /// 합성 조건(AllOf/AnyOf)은 자기 자신과 하위 조건을 모두 기록한다 - clearMode=Any에서
        /// 조건 단위로 판정하려면 말단까지 시작 상태를 알아야 한다.</summary>
        public static MissionStartBaseline Capture(MissionData mission, MissionEvaluationContext context, int attemptId)
        {
            var satisfied = new HashSet<MissionConditionData>();
            if (mission != null && mission.successConditions != null)
                foreach (var c in mission.successConditions)
                    Walk(c, context, satisfied);

            return new MissionStartBaseline(
                mission != null ? mission.missionId : null, attemptId, WorldChangeClock.Current, satisfied);
        }

        static void Walk(MissionConditionData condition, MissionEvaluationContext context, HashSet<MissionConditionData> satisfied)
        {
            if (condition == null) return;

            if (condition.GetCurrentProgress(context) >= condition.TargetCount)
                satisfied.Add(condition);

            if (condition is AllOfConditions all && all.subConditions != null)
                foreach (var sub in all.subConditions) Walk(sub, context, satisfied);
            else if (condition is AnyOfConditions any && any.subConditions != null)
                foreach (var sub in any.subConditions) Walk(sub, context, satisfied);
        }
    }
}
