using UnityEngine;
using Belief.Domain;
using Belief.Events;

namespace Belief.Data
{
    /// <summary>
    /// 확인(Verify) 행동이 <b>실제 확인 결과</b>를 기억으로 남긴다. 이 효과가 붙기 전까지 Verify는
    /// 로그 한 줄과 미션 판정용 기록만 남기고 다음 턴 판단에 아무 영향도 주지 못했다 - NPC가 매 턴
    /// "확인해보겠다"만 반복하고 아무것도 확인되지 않던 원인이다.
    ///
    /// <b>Verify했다고 무조건 믿음이 올라가지 않는다.</b> 확인 결과가 주장의 진위를 그대로 따른다:
    /// <list type="bullet">
    /// <item>사실인 정보를 확인 → 긍정 근거(confirmedTrueCategory, valence +)</item>
    /// <item>거짓인 정보를 확인 → 부정 근거(confirmedFalseCategory, valence −)</item>
    /// <item>진위를 가릴 수 없음 → 기억을 남기지 않음(확정적 보정 없음)</item>
    /// </list>
    ///
    /// 쓰기는 직접 하지 않고 MemoryWorthyEventOccurred만 발행한다 - LongMemory의 유일한 쓰기 지점은
    /// 계속 MemorySystem이다. 같은 NPC가 같은 주장을 다시 확인해도 보정이 중첩되지 않도록 하는
    /// 중복 방지도 MemorySystem이 informationId 기준으로 처리한다.
    /// </summary>
    [CreateAssetMenu(fileName = "Effect_RecordVerificationResult", menuName = "Belief/Actions/Record Verification Result Effect")]
    public class RecordVerificationResultEffect : NpcActionEffect
    {
        [Tooltip("확인해 보니 사실이었을 때 남길 기억 종류(valence는 양수여야 한다).")]
        public MemoryCategoryData confirmedTrueCategory;

        [Tooltip("확인해 보니 거짓이었을 때 남길 기억 종류(valence는 음수여야 한다).")]
        public MemoryCategoryData confirmedFalseCategory;

        [Tooltip("기억의 중요도. Belief 보정 크기는 Importance x MemoryTuning.maxSingleMemoryModifier로 계산된다 "
               + "(Core 기억이면 그대로, 아니면 절반). Frozen 공식과 ±0.35 상한은 건드리지 않는다.")]
        [Range(0f, 1f)] public float importance = 0.8f;

        [Tooltip("이 행동은 진위를 가릴 수 없는 확인이다 - 켜면 기억을 남기지 않아 확정적 보정이 생기지 않는다.")]
        public bool inconclusive;

        public override void Apply(NpcState actor, ActionEffectContext context)
        {
            if (inconclusive) return;                       // 확인 불가 - 확정적 보정 없음
            if (context.JudgedCard == null) return;         // 이동 등 카드 없는 판단에서는 아무것도 하지 않는다

            var information = context.JudgedCard.information;
            if (information == null) return;                // 확인할 주장 자체가 없음 - 확인 불가와 동일 취급

            var category = information.isActuallyTrue ? confirmedTrueCategory : confirmedFalseCategory;
            if (category == null) return;                   // 해당 결과 종류가 저작되지 않았으면 조용히 넘어간다

            string verdict = information.isActuallyTrue ? "사실로 확인됨" : "거짓으로 확인됨";
            string title = !string.IsNullOrEmpty(information.title) ? information.title : information.informationId;

            var entry = new MemoryEntry(
                description: $"'{title}'을(를) 직접 확인한 결과 {verdict}",
                turnRecorded: context.CurrentTurn,
                importance: importance,
                relatedLocationId: actor.CurrentLocation != null ? actor.CurrentLocation.locationId : null,
                relatedSourceId: context.JudgedCard.source != null ? context.JudgedCard.source.sourceId : null,
                relatedInformationCategoryId: information.categoryId,
                memoryCategoryId: category.memoryCategoryId,
                valence: category.valence,
                relatedInformationId: information.informationId);

            context.EventBus.Publish(new MemoryWorthyEventOccurred(actor.Data, entry));
        }
    }
}
