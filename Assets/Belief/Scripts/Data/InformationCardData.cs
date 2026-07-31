using UnityEngine;

namespace Belief.Data
{
    /// <summary>
    /// 정보 카드 = InformationData(무엇을) + InfoSourceData(어디서 주장하는지)의 조합.
    /// 내용을 복사하지 않고 참조만 조합하므로, 같은 InformationData를 여러 InfoSourceData와
    /// 엮은 카드를 자유롭게 만들 수 있다(예: "왕이 독살당했다" + 왕실 문서/시장 소문/익명 제보).
    /// </summary>
    /// <summary>SPREAD 카드는 항상 PLACE, DELIVER 카드는 항상 NPC를 대상으로 한다(기획서 스키마) -
    /// cardType과 1:1로 대응되는 파생값이라 별도 필드로 중복 저장하지 않는다.</summary>
    public enum InformationTargetType
    {
        Place,
        Npc
    }

    [CreateAssetMenu(fileName = "Card_", menuName = "Belief/Information Card", order = 1)]
    public class InformationCardData : ScriptableObject
    {
        [Header("Identity")]
        public string cardId;

        [Header("Composition")]
        public InformationData information;
        public InfoSourceData source;

        [Header("Delivery")]
        public InfoCardType cardType = InfoCardType.Spread;

        public InformationTargetType TargetType =>
            cardType == InfoCardType.Spread ? InformationTargetType.Place : InformationTargetType.Npc;
    }
}
