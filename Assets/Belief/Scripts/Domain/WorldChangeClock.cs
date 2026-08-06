using UnityEngine;

namespace Belief.Domain
{
    /// <summary>
    /// 세계 상태가 바뀔 때마다 하나씩 올라가는 단조 증가 스탬프. "이 변화가 현재 미션이 시작된 뒤에
    /// 일어난 것인가"를 판정하는 유일한 기준이다(MissionStartBaseline이 미션 시작 시점의 값을 하나
    /// 들고 있고, 각 상태 레코드는 마지막으로 바뀐 시점의 값을 들고 있다).
    ///
    /// <b>턴 번호를 쓰지 않는 이유</b>: TurnSystem.CurrentTurn은 미션마다 1로 리셋되므로 이전 미션의
    /// 1턴과 새 미션의 1턴이 같은 값이라 구분할 수 없고, StageTurn은 미션이 완료·실패하는 턴에
    /// FreezeTurnAdvance 때문에 증가하지 않아 단조롭지 않다. 그래서 턴과 무관한 별도 축을 둔다.
    ///
    /// <b>정적인 이유</b>: 스탬프를 찍는 지점이 NpcState·RumorState·InformationWorldState처럼
    /// 시스템 주입 통로가 없는 도메인 값 객체 내부라, 클럭을 인스턴스로 만들면 생성 지점 전부에
    /// 배선을 뚫어야 한다. 대신 Play 진입마다 0으로 되돌려 세션 안에서는 항상 결정적이게 한다.
    /// </summary>
    public static class WorldChangeClock
    {
        static long current;

        /// <summary>지금까지 발급된 마지막 스탬프. 아직 아무 변화도 없었으면 0.</summary>
        public static long Current => current;

        /// <summary>새 스탬프를 발급한다. 0은 "한 번도 바뀐 적 없음"을 뜻하므로 항상 1 이상이다.</summary>
        public static long Next() => ++current;

        /// <summary>Play 진입마다 0으로 되돌린다. Domain Reload 설정과 무관하게 실행되므로,
        /// 재생을 반복해도 같은 플레이가 같은 스탬프 순서를 갖는다.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetForNewSession() => current = 0;
    }
}
