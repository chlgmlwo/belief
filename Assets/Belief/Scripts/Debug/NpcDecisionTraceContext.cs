using System;

namespace Belief.Debugging
{
    /// <summary>StageId/StageTurn/MissionId/MissionTurn/ThinkerMode처럼 판단 시스템(NpcThinkingSystem,
    /// NpcMovementSystem 등) 안에서는 직접 알 수 없는 상위 문맥값을 트레이스 기록에 채우기 위한
    /// 정적 홀더. GameInstaller.Awake()가 한 번 델리게이트를 등록해 두면, 판단 시스템들은 생성자 인자를
    /// 추가로 받지 않고도 이 값을 읽을 수 있다(ProgressionController.Instance/PlaybackDirector.Instance와
    /// 같은 기존 정적 싱글턴 패턴을 그대로 따른다). 델리게이트가 비어 있으면 빈 값을 반환할 뿐 예외를
    /// 던지지 않는다 - Editor Window가 열려있지 않거나 배선이 안 된 상태에서도 게임은 그대로 동작한다.</summary>
    public static class NpcDecisionTraceContext
    {
        public static Func<string> StageIdProvider;
        public static Func<int> StageTurnProvider;
        public static Func<string> MissionIdProvider;
        public static Func<int> MissionTurnProvider;
        public static Func<string> ThinkerModeProvider;
        // 예전에는 여기에 "진행 중인 판단의 NpcDecisionTraceBuilder"를 담는 정적 CurrentBuilder
        // 필드가 있었다 - LLM 요청이 비동기(Task 기반, 실제로는 여러 프레임에 걸쳐 대기)로 바뀌면서
        // 폐기했다: 요청이 겹치거나 타임아웃 이후 응답이 늦게 도착하면 이 하나의 static 필드를 서로
        // 다른 NPC/판단이 덮어써 로그가 섞일 위험이 있었다. 지금은 각 판단의 TraceBuilder를
        // IMajorNpcThinker.DecideAsync/DecideMoveAsync의 trace 매개변수로 명시적으로 전달한다 -
        // 매 호출마다 독립된 지역 변수/클로저이므로 비동기 요청끼리 서로의 기록을 건드릴 수 없다.

        public static string StageId => StageIdProvider != null ? StageIdProvider() : "";
        public static int StageTurn => StageTurnProvider != null ? StageTurnProvider() : 0;
        public static string MissionId => MissionIdProvider != null ? MissionIdProvider() : "";
        public static int MissionTurn => MissionTurnProvider != null ? MissionTurnProvider() : 0;
        public static string ThinkerMode => ThinkerModeProvider != null ? ThinkerModeProvider() : "";
    }
}
