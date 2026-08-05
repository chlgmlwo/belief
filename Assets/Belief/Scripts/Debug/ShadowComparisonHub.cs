using System;

namespace Belief.Debugging
{
    /// <summary>Shadow 비교 기록의 유일한 발행 지점. NpcDecisionTraceHub와 같은 패턴으로,
    /// 게임 로직은 이 클래스만 알고 관찰 도구는 이 클래스만 구독한다.
    /// 구독자가 하나도 없어도 게임은 완전히 동일하게 동작한다.</summary>
    public static class ShadowComparisonHub
    {
        public static event Action<ShadowComparisonRecord> RecordPublished;

        public static bool HasListeners => RecordPublished != null;

        public static void Publish(ShadowComparisonRecord record)
        {
            if (record == null) return;
            RecordPublished?.Invoke(record);
        }
    }
}
