using Belief.Data;
using Belief.Domain;

namespace Belief.Systems
{
    /// <summary>
    /// "현재 상태가 조건을 만족한다"에 "그 성립이 이번 미션 안에서 새로 일어났다"를 덧붙여 판정한다.
    /// 조건 클래스(MissionConditionData 파생)의 데이터와 GetCurrentProgress 본문은 전혀 건드리지 않고,
    /// 조건이 어떤 상태에 의존하는지만 여기서 한 곳에 모아 읽는다.
    ///
    /// <b>핵심 규칙</b>: 미션 시작 시점에 만족하지 <b>않던</b> 조건은 지금까지와 완전히 동일하게 다룬다
    /// (지금 만족하면 그대로 인정). 추가 검사는 <b>시작부터 이미 만족 중이던 조건</b>에만 걸린다.
    /// 그래서 무회귀 범위가 구조적으로 좁다 - 정상적으로 플레이해서 미션 안에서 조건을 새로 달성하는
    /// 경로는 아무 영향을 받지 않는다.
    ///
    /// 모르는 조건 종류는 기존 동작(인정)으로 흘려보낸다 - 새 조건 타입이 추가됐을 때 미션이 조용히
    /// 클리어 불가가 되는 것보다, 즉시 클리어가 한 번 새는 쪽이 덜 위험하다.
    /// </summary>
    public static class MissionFreshnessEvaluator
    {
        /// <summary>이 조건을 미션 완료 집계에 넣어도 되는지. MissionData.GetSuccessProgress가
        /// "현재 만족" 판정과 AND로 묶어 쓴다.</summary>
        public static bool CountsForCompletion(MissionConditionData condition, MissionStartBaseline baseline,
            MissionEvaluationContext context)
        {
            if (condition == null) return false;
            if (baseline == null) return true;              // 기준점이 없으면 기존 동작 그대로.
            if (!baseline.WasSatisfiedAtStart(condition)) return true; // 이번 미션에서 새로 성립한 조건.

            return HasFreshProgress(condition, baseline, context);
        }

        /// <summary>이 조건이 의존하는 상태 중 하나라도 미션 시작 이후에 실제로 변했는지.
        /// 조건이 지목한 대상만 본다 - 무관한 NPC가 움직였다고 성공 처리되지 않는다.</summary>
        static bool HasFreshProgress(MissionConditionData condition, MissionStartBaseline baseline,
            MissionEvaluationContext context)
        {
            long since = baseline.StartStamp;

            switch (condition)
            {
                // 합성 조건은 자기 자신이 아니라 하위 조건의 변화로만 새로 성립할 수 있다.
                case AllOfConditions all:
                    if (all.subConditions != null)
                        foreach (var sub in all.subConditions)
                            if (sub != null && HasFreshProgress(sub, baseline, context)) return true;
                    return false;

                case AnyOfConditions any:
                    if (any.subConditions != null)
                        foreach (var sub in any.subConditions)
                            if (sub != null && HasFreshProgress(sub, baseline, context)) return true;
                    return false;

                // ---- 위치 조건: 감시 대상 NPC가 실제로 움직였는가 ----
                case NpcsLeaveLocationCondition leave:
                    return AnyNpcMoved(leave.watchedNpcs, context, since);

                case NpcsGatherAtLocationCondition gather:
                    return AnyNpcMoved(gather.watchedNpcs, context, since);

                case NpcAtLocationCondition at:
                    return NpcMoved(at.targetNpc, context, since);

                // ---- Belief 조건: 대상 NPC가 새로 판단했는가 ----
                case NpcAnyBeliefReachedCondition anyBelief:
                    return anyBelief.targetNpc != null
                        && context.Npcs.TryGetValue(anyBelief.targetNpc, out var anyState)
                        && anyState.LatestBeliefStamp > since;

                case NpcBeliefReachedCondition belief:
                    return belief.targetNpc != null
                        && context.Npcs.TryGetValue(belief.targetNpc, out var beliefState)
                        && beliefState.GetBeliefStamp(belief.referenceCard) > since;

                // ---- 조사 기록: 조건에 맞는 기록이 새로 생기거나 갱신됐는가 ----
                case InformationWorldStateCondition investigation:
                    return HasFreshInvestigation(investigation, context, since);

                // ---- 소문·확산: 조건에 맞는 소문이 새로 생기거나 갱신됐는가 ----
                case LocationRumorActiveCondition rumor:
                    return HasFreshRumor(rumor, context, since);

                default:
                    return true;
            }
        }

        static bool AnyNpcMoved(NpcData[] npcs, MissionEvaluationContext context, long since)
        {
            if (npcs == null) return false;
            foreach (var n in npcs)
                if (NpcMoved(n, context, since)) return true;
            return false;
        }

        static bool NpcMoved(NpcData npc, MissionEvaluationContext context, long since) =>
            npc != null && context.Npcs.TryGetValue(npc, out var state) && state.LocationChangeStamp > since;

        /// <summary>조건이 인정하는 기록(장소·행위자·결과·카테고리가 전부 맞는 것)만 골라 스탬프를 본다.
        /// 같은 장소의 무관한 조사 기록이 갱신된 것으로는 성립하지 않는다.</summary>
        static bool HasFreshInvestigation(InformationWorldStateCondition c, MissionEvaluationContext context, long since)
        {
            if (c.targetLocation == null || !context.Locations.TryGetValue(c.targetLocation, out var loc)) return false;

            foreach (var s in loc.InvestigationStates)
            {
                if (!s.IsActive) continue;
                if (s.Actor != c.requiredActor) continue;
                if (s.ResultType != c.requiredResultType) continue;
                if (!string.IsNullOrEmpty(c.requiredCategoryId) && s.CategoryId != c.requiredCategoryId) continue;
                if (s.LastChangedStamp > since) return true;
            }
            return false;
        }

        /// <summary>조건이 인정하는 소문만 골라 스탬프를 본다 - 필터는 LocationRumorActiveCondition의
        /// 판정과 같은 순서·같은 기준을 쓴다(빈 문자열을 미설정으로 보는 규칙 포함).</summary>
        static bool HasFreshRumor(LocationRumorActiveCondition c, MissionEvaluationContext context, long since)
        {
            if (c.targetLocation == null || !context.Locations.TryGetValue(c.targetLocation, out var loc)) return false;

            foreach (var r in loc.ActiveRumors)
            {
                if (!r.IsActive) continue;
                if (r.PropagatedBy != c.requiredPropagator) continue;
                if (c.requiredInformation != null && r.Information != c.requiredInformation) continue;
                if (!string.IsNullOrEmpty(c.requiredCategoryId) &&
                    (r.Information == null || r.Information.categoryId != c.requiredCategoryId)) continue;
                if (r.LastChangedStamp > since) return true;
            }
            return false;
        }
    }
}
