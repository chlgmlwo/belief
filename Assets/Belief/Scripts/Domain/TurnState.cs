namespace Belief.Domain
{
    /// <summary>
    /// 턴 진행의 순수 상태값(지금 몇 턴/최대 몇 턴)만 담는다. 카드 재생, NPC 이동/판단 트리거,
    /// 미션 재평가 같은 턴 루프 오케스트레이션은 여기 두지 않는다 - 그건 System 계층(TurnSystem)의
    /// 몫이다.
    /// </summary>
    public class TurnState
    {
        public int CurrentTurn { get; private set; } = 1;
        public int MaxTurns { get; private set; }
        public bool TurnsExhausted => CurrentTurn > MaxTurns;

        public TurnState(int maxTurns)
        {
            MaxTurns = maxTurns;
        }

        public void AdvanceTurn() => CurrentTurn++;

        public void ResetForNewMission(int newMaxTurns)
        {
            CurrentTurn = 1;
            MaxTurns = newMaxTurns;
        }
    }
}
