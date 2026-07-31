using UnityEngine;

namespace Belief.Data
{
    /// <summary>게임에 등장 가능한 정보 카드 전체 목록(설계 데이터). 런타임에 아직 획득하지 않은
    /// 나머지("정보 풀")는 InformationCardSystem이 이 목록을 복사해 별도로 관리한다.</summary>
    [CreateAssetMenu(fileName = "CardPool_", menuName = "Belief/Information Card Pool", order = 13)]
    public class InformationCardPoolData : ScriptableObject
    {
        public InformationCardData[] cards;
    }
}
