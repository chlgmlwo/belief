using System;

namespace Belief.Debugging
{
    /// <summary>NPC 판단 관찰 기록의 유일한 발행 지점. 게임 판단 코드(Systems/AI)는 이 클래스만 알고,
    /// Editor Window는 이 클래스만 구독한다 - 서로를 직접 참조하지 않는다. UnityEditor 네임스페이스를
    /// 전혀 참조하지 않으므로 플레이어 빌드에도 안전하게 포함될 수 있지만, 실제 기록 생성 자체는
    /// 호출부(#if UNITY_EDITOR || DEVELOPMENT_BUILD)에서 걸러지므로 출시 빌드에서는 이 클래스가
    /// 사실상 호출되지 않는다(방어적으로 Enabled/구독자 없음 체크도 유지).</summary>
    public static class NpcDecisionTraceHub
    {
        /// <summary>Editor Window의 "Record On/Off" 토글이 조작한다 - 기본값은 켜짐이지만, 이 값이
        /// 게임 판단 결과 자체에는 전혀 영향을 주지 않는다(기록 발행 여부만 결정).</summary>
        public static bool Enabled = true;

        public static event Action<NpcDecisionTraceRecord> RecordPublished;

        public static bool HasListeners => RecordPublished != null;

        public static void Publish(NpcDecisionTraceRecord record)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!Enabled || record == null) return;
            RecordPublished?.Invoke(record);
#endif
        }
    }
}
