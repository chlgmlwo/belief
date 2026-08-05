namespace Belief.Core
{
    /// <summary>
    /// IntegratedLlm(통합 판단 실제 적용)을 어디서 허용할지 정하는 <b>임시 파일럿 안전장치</b>.
    ///
    /// 씬이나 StageData에 직렬화 필드를 만들지 않고 코드 상수로만 제한한다 - 기존 4개 씬의 저장
    /// 상태를 건드리지 않기 위해서다. 필드를 추가하면 씬 파일이 바뀌고, 그 변경은 되돌리기도
    /// 검증하기도 번거롭다.
    ///
    /// 정식 출시 범위가 정해지면 설정 데이터 방식으로 교체할 수 있다 - 그때까지는 "1스테이지에서만,
    /// 개발 환경에서만"이라는 사실을 코드에 못 박아 두는 편이 안전하다.
    /// </summary>
    public static class IntegratedLlmPilotPolicy
    {
        /// <summary>파일럿을 허용하는 유일한 스테이지.</summary>
        public const string PilotStageId = "STAGE_01";

        /// <summary>이 빌드에서 파일럿이 가능한가. 에디터와 개발 빌드에서만 참이다 -
        /// 출시 빌드에서 요금이 나가는 경로를 열지 않는다.</summary>
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
        public static bool IsAllowed(string stageId, out string denyReason)
        {
            if (!IsPilotBuild)
            {
                denyReason = "출시 빌드에서는 IntegratedLlm 파일럿이 허용되지 않습니다.";
                return false;
            }

            if (string.IsNullOrEmpty(stageId))
            {
                denyReason = "StageData가 없는 씬에서는 IntegratedLlm 파일럿을 켤 수 없습니다.";
                return false;
            }

            if (stageId != PilotStageId)
            {
                denyReason = $"IntegratedLlm 파일럿은 {PilotStageId}에서만 허용됩니다 (요청: {stageId}).";
                return false;
            }

            denyReason = null;
            return true;
        }
    }
}
