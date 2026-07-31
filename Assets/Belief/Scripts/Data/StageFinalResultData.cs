using UnityEngine;

namespace Belief.Data
{
    /// <summary>스테이지 최종 승리(GameOverEvent(true)) 확정 이후에만 참조되는 순수 결과 연출 데이터.
    /// 미션 판정에는 전혀 관여하지 않는다 - ProgressionController가 승리를 이미 확정한 뒤
    /// StageFinalResultSystem이 이 데이터를 읽어 지정된 NPC의 지정된 카드 Belief를 Denied로
    /// 바꾸는 결과 연출에만 쓰인다. NPC ID를 코드에 하드코딩하지 않기 위한 자산 참조 지점.</summary>
    [CreateAssetMenu(fileName = "FinalResult_", menuName = "Belief/Stage Final Result", order = 11)]
    public class StageFinalResultData : ScriptableObject
    {
        public NpcData targetNpc;

        [Tooltip("최종 승리 연출로 Denied 처리할 카드 목록 - GameOverEvent(true) 확정 이후에만 적용되므로 판정에 영향을 주지 않는다.")]
        public InformationCardData[] denialCards;
    }
}
