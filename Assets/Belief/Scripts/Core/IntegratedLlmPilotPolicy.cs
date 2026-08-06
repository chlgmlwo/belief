namespace Belief.Core
{
    /// <summary>
    /// IntegratedLlm(통합 판단 실제 적용)을 어디서 허용할지 정한다.
    ///
    /// 경로가 둘로 갈린다:
    /// <list type="bullet">
    /// <item><b>파일럿</b>(SingleZone / FullPlaythrough) - 에디터 도구가 opt-in 토큰으로 여는
    ///   실험 경로. "1스테이지에서만, 개발 환경에서만"이라는 안전벽이 그대로 남는다.</item>
    /// <item><b>출시</b>(<see cref="PilotRunMode.Release"/>) - 씬의 thinkerMode가 IntegratedLlm이라
    ///   게임 자체가 통합 판단으로 도는 경우. 전 구역·출시 빌드에서 허용된다(2026-08-06 결정).
    ///   여기서 막으면 배포된 게임의 NPC가 전부 규칙 기반으로 퇴화하므로 스테이지 제한을 두지 않는다.
    ///   <b>비용 통제는 게임이 아니라 중계 서버(useProxy)가 맡는다</b> - 클라이언트 측 상한은
    ///   어차피 우회 가능해서 방어선이 되지 못한다.</item>
    /// </list>
    ///
    /// 씬이나 StageData에 직렬화 필드를 만들지 않고 코드 상수로만 제한한다 - 필드를 추가하면
    /// 씬 파일이 바뀌고, 그 변경은 되돌리기도 검증하기도 번거롭다.
    /// </summary>
    public static class IntegratedLlmPilotPolicy
    {
        /// <summary>파일럿(실험) 경로를 허용하는 유일한 스테이지. 출시 경로에는 적용되지 않는다.</summary>
        public const string PilotStageId = "STAGE_01";

        /// <summary>이 빌드에서 <b>파일럿</b>이 가능한가. 에디터와 개발 빌드에서만 참이다 -
        /// 실험 경로가 출시 빌드에서 요금을 내지 않도록 막는다.
        /// 출시 경로(Release)는 이 값을 보지 않는다.</summary>
        public static bool IsPilotBuild =>
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            true;
#else
            false;
#endif

        /// <summary>
        /// 이 스테이지에서 IntegratedLlm을 허용할지 판정한다.
        /// 허용되지 않으면 호출자는 RuleOnly로 강등하고 <paramref name="denyReason"/>을 경고로 남긴다 -
        /// 예외를 던지지 않는 것은 기존 ThinkerFactory 폴백 정책과 같은 방향이다.
        /// </summary>
        public static bool IsAllowed(string stageId, out string denyReason) =>
            IsAllowed(stageId, PilotRunMode.SingleZone, out denyReason);

        /// <summary>
        /// <paramref name="mode"/>가 FullPlaythrough면 스테이지 제한만 풀린다 -
        /// <b>출시 빌드 차단과 StageData 필수 조건은 어느 모드에서도 그대로다</b>.
        /// 전 구역 연속 플레이는 "1구역만"이라는 제약의 목적(요금이 새는 범위를 좁힌다)을
        /// 스테이지가 아니라 구역당 호출 예산으로 대신 지킨다.
        /// </summary>
        public static bool IsAllowed(string stageId, PilotRunMode mode, out string denyReason)
        {
            // StageData 필수 조건만 모든 모드에 공통이다 - 스테이지를 특정하지 못하면 판단 맥락도
            // 만들 수 없으므로 출시 경로에서도 켤 수 없다.
            if (string.IsNullOrEmpty(stageId))
            {
                denyReason = "StageData가 없는 씬에서는 IntegratedLlm을 켤 수 없습니다.";
                return false;
            }

            // 출시 경로 - 빌드 종류와 스테이지를 가리지 않는다. 여기서 막으면 배포된 게임의
            // NPC 판단이 통째로 규칙 기반으로 내려앉는다.
            if (mode == PilotRunMode.Release)
            {
                denyReason = null;
                return true;
            }

            if (!IsPilotBuild)
            {
                denyReason = "출시 빌드에서는 IntegratedLlm 파일럿(실험 경로)이 허용되지 않습니다.";
                return false;
            }

            if (mode != PilotRunMode.FullPlaythrough && stageId != PilotStageId)
            {
                denyReason = $"IntegratedLlm 파일럿은 {PilotStageId}에서만 허용됩니다 (요청: {stageId}).";
                return false;
            }

            denyReason = null;
            return true;
        }
    }
}
