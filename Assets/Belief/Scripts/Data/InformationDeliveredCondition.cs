using UnityEngine;

namespace Belief.Data
{
    /// <summary>
    /// "이 정보를 전달했는가"를 판정하는 조건. 어떤 필드를 채웠는지로 요구 수준이 정해진다 -
    /// 코드 분기가 아니라 데이터 저작으로 표현한다.
    /// - requiredInformation만 채움: 정보 내용만 요구 (예: "왕 독살 정보를 전달한다")
    /// - + requiredSource도 채움: 특정 출처까지 요구 (예: "왕실 문서를 출처로 전달한다")
    /// 비워둔 필드는 와일드카드(아무 값이나 허용)로 취급한다.
    /// </summary>
    [CreateAssetMenu(fileName = "Condition_InformationDelivered", menuName = "Belief/Missions/Information Delivered Condition")]
    public class InformationDeliveredCondition : MissionConditionData
    {
        public InformationData requiredInformation;
        public InfoSourceData requiredSource;

        public override int GetCurrentProgress(MissionEvaluationContext context)
        {
            if (requiredInformation == null || context.DeliveredCards == null) return 0;

            int count = 0;
            foreach (var record in context.DeliveredCards)
            {
                if (record.Card == null || record.Card.information != requiredInformation) continue;
                if (requiredSource != null && record.Card.source != requiredSource) continue;
                count++;
            }
            return count;
        }
    }
}
