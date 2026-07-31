using UnityEngine;

namespace Belief.Data
{
    /// <summary>
    /// "무엇을 주장하는가"만 담는다. 출처(InfoSourceData)와는 완전히 분리되어 있어,
    /// 같은 InformationData를 여러 InfoSourceData와 자유롭게 조합해 InformationCardData를 만들 수 있다.
    /// 판단 관련 수치(baseCredibility/spreadPower/isActuallyTrue)는 "이 주장 자체가 얼마나
    /// 그럴듯하고 잘 퍼지고 실제로 참인가"이므로 출처가 아니라 여기 속한다.
    /// </summary>
    [CreateAssetMenu(fileName = "Info_", menuName = "Belief/Information", order = 0)]
    public class InformationData : ScriptableObject
    {
        [Header("Identity")]
        public string informationId;
        public string title;
        [TextArea(2, 5)] public string description;

        [Header("Classification (구조만 준비 - SituationEvaluator/GoalEvaluator 고도화용, 현재 미사용)")]
        [Range(0f, 1f)] public float importance = 0.5f;
        public string[] tags;
        public string categoryId;

        /// <summary>Location Mechanics V1 - 장소의 sensitiveInformationType과 일치 여부를 판정하는 데만
        /// 쓰인다(Belief 판단의 다른 항목엔 영향 없음). Unspecified(기본값)면 어떤 장소와도 일치하지 않는다.</summary>
        public InformationType informationType;

        [Header("Judgement Inputs (BeliefSystem 전용, 플레이어 비공개)")]
        [Tooltip("이 주장 자체의 기본 그럴듯함. 출처 신뢰도(InfoSourceData.baseTrustModifier)와는 별개.")]
        [Range(0f, 1f)] public float baseCredibility = 0.5f;
        [Range(0f, 1f)] public float spreadPower = 0.5f;
        public bool isActuallyTrue = true;
    }
}
