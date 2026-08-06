using System.Collections.Generic;
using System.Text;
using Belief.Data;
using Belief.Domain;

namespace Belief.AI.LLM
{
    /// <summary>
    /// 통합 판단(Interpretation/Belief/Goal/Action/Destination/Dialogue) 전용 프롬프트.
    /// 기존 PromptBuilder와 분리해 둔 이유는, 실제 게임 판단이 쓰는 프롬프트를 Shadow Mode 실험이
    /// 건드리지 않게 하기 위해서다 - 두 경로가 같은 파일을 공유하면 관찰용 수정이 실제 판단에
    /// 새어 나간다.
    ///
    /// 기존 프롬프트와 결정적으로 다른 점: <b>현재 믿음을 확정값으로 주지 않는다.</b> "직전 믿음"만
    /// 알려주고 이번 정보를 반영한 믿음은 AI가 직접 고르게 한다. 기존 프롬프트가 믿음을 확정값으로
    /// 넘겨줬기 때문에 근거가 belief로 쏠렸다.
    /// </summary>
    public static class UnifiedPromptBuilder
    {
        static readonly string[] BeliefValues =
            { "Trusted", "Plausible", "NeedsVerification", "Doubtful", "Denied" };

        public const int MaxInterpretationLength = 200;
        public const int MaxGoalLength = 120;
        public const int MaxDialogueLength = 200;

        public static string Build(NpcJudgmentContext ctx)
        {
            var npcData = ctx.Npc.Data;
            var sb = new StringBuilder();

            sb.AppendLine("[NPC]");
            sb.AppendLine($"이름: {npcData.displayName}");
            if (!string.IsNullOrEmpty(npcData.job)) sb.AppendLine($"직업: {npcData.job}");
            if (!string.IsNullOrEmpty(npcData.affiliation)) sb.AppendLine($"소속: {npcData.affiliation}");
            sb.Append($"신뢰경향: {npcData.trustBias:F2}, 의심도: {npcData.skepticism:F2}");
            if (npcData is MajorNpcData m) sb.Append($", 충성도: {m.loyalty:F2}");
            sb.AppendLine();
            if (npcData is MajorNpcData major && !string.IsNullOrEmpty(ctx.GoalBefore))
                sb.AppendLine($"지금까지의 목표: {ctx.GoalBefore}");

            AppendProfileTags(sb, npcData);
            AppendNotesAndBackstory(sb, npcData);
            AppendRelationships(sb, ctx);

            sb.AppendLine();
            sb.AppendLine("[상황]");
            sb.AppendLine($"현재 위치: {(ctx.Where != null ? ctx.Where.Data.displayName : "알 수 없음")}");
            sb.AppendLine($"현재 턴: {ctx.Turn}");
            AppendPresentNpcs(sb, ctx);
            sb.AppendLine(ctx.Propagator != null
                ? $"이 정보를 전달한 인물: {ctx.Propagator.Data.npcId} ({ctx.Propagator.Data.displayName})"
                : "이 정보를 전달한 인물: none (정보원을 통한 전달이라 전달한 인물이 없음)");
            sb.AppendLine($"이 주장에 대해 지금까지 갖고 있던 믿음: {ctx.BeliefBefore}");

            sb.AppendLine();
            sb.AppendLine("[이번에 접한 주장]");
            var info = ctx.Card.information;
            sb.AppendLine($"제목: {(info != null ? info.title : "알 수 없음")}");
            sb.AppendLine($"내용: {(info != null ? info.description : "알 수 없음")}");
            // 주장 자체의 그럴듯함(0~1). 출처 신뢰도와는 별개의 축이라 반드시 따로 제시한다 -
            // 재확산으로 넘어와도 카드 객체가 그대로 전달되므로 이 값에 감쇠가 붙지 않는다(원본 = 유효값).
            // 정보 유형은 장소의 민감 정보 유형과 맞춰 보기 위한 값이다 - 이게 없으면 LLM이
            // "이 자리가 이 주제에 민감한가"를 판정할 재료 자체가 없다.
            if (info != null) sb.AppendLine($"정보 유형: {info.informationType}");
            sb.AppendLine(info != null
                ? $"내용 신뢰도: {info.baseCredibility:0.00}  (0~1, 높을수록 주장 자체가 그럴듯함)"
                : "내용 신뢰도: 알 수 없음");

            sb.AppendLine();
            sb.AppendLine("[정보 출처]");
            sb.AppendLine($"선언된 출처: {(ctx.Card.source != null ? ctx.Card.source.displayName : "알 수 없음")}");
            sb.AppendLine($"출처 Id: {(ctx.Card.source != null ? ctx.Card.source.sourceId : "알 수 없음")}");
            sb.AppendLine(ctx.Card.source != null
                ? $"출처 신뢰도: {ctx.Card.source.baseTrustModifier:0.00}  (0~1, 높을수록 출처가 믿을 만함)"
                : "출처 신뢰도: 알 수 없음");

            AppendHearingPlace(sb, ctx);

            sb.AppendLine();
            sb.AppendLine("[관련 기억]");
            if (ctx.Memory != null && !ctx.Memory.IsEmpty)
                foreach (var e in ctx.Memory.Entries) sb.AppendLine($"- {e.Description}");
            else
                sb.AppendLine("(관련된 특별한 기억 없음)");

            sb.AppendLine();
            sb.AppendLine("[선택 가능한 행동]");
            if (ctx.ActionCandidates != null)
                foreach (var a in ctx.ActionCandidates)
                    if (a != null) sb.AppendLine($"- {a.actionId}: {a.displayLabel}");

            AppendMoveCandidates(sb, ctx);

            AppendPrinciples(sb);
            AppendResponseSpec(sb, ctx);
            return sb.ToString();
        }

        static void AppendProfileTags(StringBuilder sb, NpcData npcData)
        {
            var tags = JudgmentGroundsValidator.ProfileTagsOf(npcData);
            if (tags.Count == 0) return;
            sb.AppendLine();
            sb.AppendLine("[성향 태그]  (profileInfluence로는 아래 값 중 하나만 반환할 수 있습니다)");
            if (!string.IsNullOrWhiteSpace(npcData.judgmentTendencyTag)) sb.AppendLine($"판단경향: {npcData.judgmentTendencyTag.Trim()}");
            if (!string.IsNullOrWhiteSpace(npcData.priorityTag)) sb.AppendLine($"우선순위: {npcData.priorityTag.Trim()}");
            if (!string.IsNullOrWhiteSpace(npcData.sensitiveInfoTag)) sb.AppendLine($"민감주제: {npcData.sensitiveInfoTag.Trim()}");
            if (!string.IsNullOrWhiteSpace(npcData.relationTendencyTag)) sb.AppendLine($"관계처리: {npcData.relationTendencyTag.Trim()}");
            if (!string.IsNullOrWhiteSpace(npcData.trustJudgmentTag)) sb.AppendLine($"신뢰판단: {npcData.trustJudgmentTag.Trim()}");
        }

        static void AppendNotesAndBackstory(StringBuilder sb, NpcData npcData)
        {
            if (npcData.aiNotes != null && npcData.aiNotes.Length > 0)
            {
                sb.AppendLine();
                sb.AppendLine("[인물 메모]");
                foreach (var n in npcData.aiNotes)
                    if (!string.IsNullOrWhiteSpace(n)) sb.AppendLine($"- {n.Trim()}");
            }
            if (!string.IsNullOrWhiteSpace(npcData.backstory))
            {
                sb.AppendLine();
                sb.AppendLine("[배경]");
                sb.AppendLine(npcData.backstory.Trim());
            }
        }

        static void AppendRelationships(StringBuilder sb, NpcJudgmentContext ctx)
        {
            if (!(ctx.Npc.Data is MajorNpcData major) || major.relationships == null || major.relationships.Length == 0) return;
            var usable = JudgmentGroundsValidator.UsableRelationships(ctx.Npc, ctx.PresentNpcs, ctx.Propagator);

            sb.AppendLine();
            sb.AppendLine("[관계]");
            if (usable.Count > 0)
            {
                sb.AppendLine("▶ 지금 이 자리/이번 정보에 실제로 관련된 인물 (relationshipInfluence로 반환 가능):");
                foreach (var r in usable) AppendRelationshipLine(sb, r, ctx.Propagator);
            }
            else
            {
                sb.AppendLine("▶ 지금 이 자리/이번 정보에 관련된 인물: 없음");
                sb.AppendLine("  (따라서 relationshipInfluence는 반드시 none이어야 합니다)");
            }

            var background = new List<RelationshipEntry>();
            foreach (var r in major.relationships)
                if (r.other != null && !usable.Contains(r)) background.Add(r);
            if (background.Count > 0)
            {
                sb.AppendLine("▷ 참고용 배경 관계 (지금 자리에 없으므로 근거로 반환할 수 없음):");
                foreach (var r in background) AppendRelationshipLine(sb, r, null);
            }
        }

        static void AppendRelationshipLine(StringBuilder sb, RelationshipEntry rel, NpcState propagator)
        {
            bool isProp = propagator != null && propagator.Data == rel.other;
            string label = !string.IsNullOrWhiteSpace(rel.relationshipTypeLabel) ? rel.relationshipTypeLabel.Trim() : "관계 유형 미지정";
            sb.AppendLine($"  - {rel.other.npcId} ({rel.other.displayName}) | {label} | "
                        + $"strength={rel.strength:F2} ({JudgmentGroundsValidator.DescribeStrength(rel.strength)})"
                        + (isProp ? " | ★이 정보를 전달한 당사자" : ""));
            if (!string.IsNullOrWhiteSpace(rel.relationshipDescription))
                sb.AppendLine($"      {rel.relationshipDescription.Trim()}");
        }

        static void AppendPresentNpcs(StringBuilder sb, NpcJudgmentContext ctx)
        {
            var parts = new List<string>();
            if (ctx.PresentNpcs != null)
                foreach (var n in ctx.PresentNpcs)
                    if (n != null && n != ctx.Npc && n.Data != null) parts.Add($"{n.Data.npcId} ({n.Data.displayName})");
            sb.AppendLine(parts.Count > 0 ? $"같은 장소에 있는 인물: {string.Join(", ", parts)}" : "같은 장소에 있는 인물: 없음");
        }

        /// <summary>
        /// 이동 후보를 <b>판단 가능한 형태</b>로 제시한다. 예전에는 locationId와 이름만 나열해서
        /// LLM 입장에서는 맥락 없는 id 목록이었고, 실측 12건이 전부 명시적 stay였다(현재 위치를
        /// 골라 정규화된 것이 아니라 진짜로 "stay"라고 답했다).
        ///
        /// 여기서 보여주는 값은 전부 <b>이미 저작돼 있는 실제 데이터</b>다 - LocationData의 설명·
        /// 민감 정보 유형·확산 속도·밀집도·출입 제한과, LocationState의 현재 경계 상태·재실 인물.
        /// 장소의 역할을 지어내거나 "조사 장소" 같은 새 개념을 만들지 않는다.
        /// </summary>
        static void AppendMoveCandidates(StringBuilder sb, NpcJudgmentContext ctx)
        {
            sb.AppendLine();
            sb.AppendLine("[현재 위치]");
            if (ctx.Where != null)
            {
                sb.AppendLine($"- {ctx.Where.Data.locationId}: {ctx.Where.Data.displayName}");
                // 신뢰도 보정은 위의 [이 주장을 듣고 있는 자리]가 전담한다 - 여기에도 넣으면
                // 같은 값이 프롬프트에 두 번 등장한다.
                AppendLocationDetail(sb, ctx.Where.Data, ctx.Where, ctx.Npc, "    ");
            }
            else sb.AppendLine("- 알 수 없음");

            sb.AppendLine();
            sb.AppendLine("[이동 후보]");
            if (ctx.MoveCandidates == null || ctx.MoveCandidates.Count == 0)
            {
                sb.AppendLine("(이동 가능한 곳 없음 - destinationId는 stay여야 합니다)");
                return;
            }

            foreach (var l in ctx.MoveCandidates)
            {
                if (l == null) continue;
                bool isCurrent = ctx.Where != null && l == ctx.Where.Data;
                sb.AppendLine($"- {l.locationId}: {l.displayName}{(isCurrent ? "   ※ 현재 위치" : "")}");

                LocationState state = null;
                if (ctx.AllLocations != null) ctx.AllLocations.TryGetValue(l, out state);
                AppendLocationDetail(sb, l, state, ctx.Npc, "    ");
            }
        }

        static void AppendLocationDetail(StringBuilder sb, LocationData data, LocationState state, NpcState self, string indent,
            bool includeCredibility = false)
        {
            if (!string.IsNullOrWhiteSpace(data.description))
                sb.AppendLine($"{indent}설명: {data.description.Trim().Replace("\n", " ")}");
            sb.AppendLine($"{indent}민감 정보 유형: {data.sensitiveInformationType} / 정보 확산 속도: {data.spreadSpeed} / 인구 밀집도: {data.npcDensity}");
            sb.AppendLine($"{indent}출입: {data.accessType}");
            if (includeCredibility)
                sb.AppendLine($"{indent}장소 신뢰도 보정: {data.credibilityModifier} ({CredibilityModifierMeaning(data.credibilityModifier)})");

            if (state != null)
            {
                sb.AppendLine($"{indent}현재 장소 상태: {state.SiteState}");
                var here = new List<string>();
                foreach (var n in state.PresentNpcs)
                    if (n != null && n != self && n.Data != null) here.Add($"{n.Data.npcId}({n.Data.displayName})");
                sb.AppendLine($"{indent}현재 인물: {(here.Count > 0 ? string.Join(", ", here) : "없음")}");
            }
        }

        /// <summary>
        /// 이 주장을 <b>듣고 있는 자리</b>가 신빙성에 주는 영향만 따로 모은 섹션.
        ///
        /// 왜 독립 섹션인가: 같은 값을 [현재 위치] 안에 한 줄로 넣었을 때는 48회 실측에서 판단에
        /// 전혀 반영되지 않았다(Low/High 평균이 소수점까지 동일). 출처는 [정보 출처]라는 독립
        /// 섹션을 갖고 있어 primaryReason이 source로 쏠렸는데, 장소만 다른 속성들 사이에 묻혀
        /// 있었던 것이 원인으로 보여 <b>같은 위상</b>으로 올린다.
        ///
        /// [현재 위치]는 이동 판단용으로 그대로 두고, 신뢰도 보정 줄만 이쪽으로 옮겨 중복을 없앤다.
        /// </summary>
        static void AppendHearingPlace(StringBuilder sb, NpcJudgmentContext ctx)
        {
            if (ctx.Where == null || ctx.Where.Data == null) return;
            var d = ctx.Where.Data;

            sb.AppendLine();
            sb.AppendLine("[이 주장을 듣고 있는 자리]");
            sb.AppendLine($"장소: {d.displayName}");
            sb.AppendLine($"장소 신뢰도 보정: {d.credibilityModifier} ({CredibilityModifierMeaning(d.credibilityModifier)})");

            // 민감 정보 유형은 "이 자리가 이 주제에 예민한가"를 뜻한다. 일치 여부를 미리 계산해
            // 알려 주되, 그것이 믿음을 얼마나 움직여야 하는지는 정하지 않는다.
            var info = ctx.Card != null ? ctx.Card.information : null;
            string cardType = info != null ? info.informationType.ToString() : null;
            string placeType = d.sensitiveInformationType.ToString();
            bool matched = !string.IsNullOrEmpty(cardType)
                           && cardType != "Unspecified" && placeType != "Unspecified"
                           && cardType == placeType;
            sb.AppendLine($"이 자리가 민감하게 다루는 주제: {placeType}"
                          + (matched ? "  → 이번 주장의 유형과 일치한다" : "  → 이번 주장의 유형과는 다르다"));
        }

        /// <summary>LocationCredibilityModifier의 실제 값(Unspecified/Low/Neutral/High/VeryHigh)을
        /// 사람이 읽는 한 줄 의미로 옮긴다. 여기 없는 값을 지어내지 않는다 - enum이 늘어나면
        /// default가 "별도 정보 없음"으로 안전하게 떨어진다.</summary>
        static string CredibilityModifierMeaning(LocationCredibilityModifier value) => value switch
        {
            LocationCredibilityModifier.Low => "이 장소에서는 정보가 덜 신뢰받기 쉬움",
            LocationCredibilityModifier.Neutral => "장소로 인한 추가 보정 없음",
            LocationCredibilityModifier.High => "이 장소에서는 정보가 더 신뢰받기 쉬움",
            LocationCredibilityModifier.VeryHigh => "이 장소에서는 정보가 훨씬 더 신뢰받기 쉬움",
            _ => "별도 장소 신뢰도 정보 없음"
        };

        static void AppendPrinciples(StringBuilder sb)
        {
            sb.AppendLine();
            sb.AppendLine("[판단 원칙]");
            sb.AppendLine("이 인물의 입장에서 이번 주장을 해석하고, 믿음·목표·행동·이동·대사를 한 번에 정하세요.");
            sb.AppendLine("성향 태그는 기본 경향일 뿐 반드시 따라야 하는 규칙이 아닙니다. 관계, 정보 출처,");
            sb.AppendLine("지금 이 자리에 있는 인물과의 관계가 충분히 강하다면 평소와 다른 선택을 해도 됩니다.");
            sb.AppendLine("다만 그렇게 했다면 위에 실제로 제시된 성향 태그나 관계 npcId를 근거로 반환해야 합니다.");
            sb.AppendLine("위에 제시되지 않은 인물, 관계, 기억, 과거 사건을 지어내지 마세요. 지어내면 응답 전체가 무효입니다.");
            sb.AppendLine("정보의 내용 신뢰도와 출처 신뢰도는 서로 독립적인 단서이며, NPC의 성향·관계·기억·현재 장소와");
            sb.AppendLine("함께 종합적으로 판단하세요. 이 수치들은 판단 근거 중 하나일 뿐이며, 정해진 공식으로 계산하거나");
            sb.AppendLine("특정 값을 기준으로 믿음 단계를 기계적으로 정하지 마세요.");
            sb.AppendLine("[이 주장을 듣고 있는 자리]도 같은 무게의 단서입니다 - 어디서 들었는지가 그 말을 얼마나");
            sb.AppendLine("믿을 만하게 만드는지, 이 자리가 그 주제에 예민한 곳인지를 함께 따져 보세요.");

            sb.AppendLine();
            sb.AppendLine("action과 destinationId는 하나의 계획으로 함께 판단하세요.");
            sb.AppendLine("- 확인하는 행동이라면, 확인에 도움이 되는 실제 장소나 그곳의 인물이 있는지 보세요.");
            sb.AppendLine("- 보고하는 행동이라면, 보고할 상대가 실제로 있는 장소인지 보세요.");
            sb.AppendLine("- 정보를 퍼뜨리는 행동이라면, 확산 속도와 인구 밀집도가 그에 맞는 장소인지 보세요.");
            sb.AppendLine("현재 위치에서 그 행동을 수행할 수 있으면 stay를 선택할 수 있습니다.");
            sb.AppendLine("다른 장소나 그곳의 인물이 행동·목표 달성에 실질적으로 도움이 된다면 해당 이동 후보를 선택하세요.");
            sb.AppendLine("이동할 이유가 분명하지 않은데 억지로 움직이지 말고, 반대로 이유가 충분한데 습관적으로 stay를 고르지도 마세요.");
            sb.AppendLine("위에 제시된 장소 정보만 사용하고, 장소의 기능이나 그곳에 있는 인물을 지어내지 마세요.");
        }

        static void AppendResponseSpec(StringBuilder sb, NpcJudgmentContext ctx)
        {
            sb.AppendLine();
            sb.AppendLine("[응답 형식]");
            sb.AppendLine("반드시 아래 JSON 형식으로만, 다른 텍스트 없이 응답하세요.");
            sb.AppendLine($"belief는 다음 중 하나여야 합니다: {string.Join(" / ", BeliefValues)}");
            sb.AppendLine("action은 [선택 가능한 행동]의 id 중 하나, destinationId는 [이동 후보]의 locationId 중 하나이거나 \"stay\"여야 합니다.");
            sb.AppendLine($"interpretation은 {MaxInterpretationLength}자, goal은 {MaxGoalLength}자, dialogue는 {MaxDialogueLength}자 이내입니다.");

            sb.AppendLine($"primaryReason은 아래 정의에 따라 하나만 고르세요.");
            sb.AppendLine("  relationship : 특정 인물과의 관계, 또는 전달자·동석 인물과의 관계 때문에 판단이 달라진 경우");
            sb.AppendLine("  situation    : 인물 관계가 아니라 장소 상태·봉쇄·경계 태세 같은 비인격적 상황이 근거인 경우");
            sb.AppendLine("  location     : 지금 있는 장소의 성격(장소 신뢰도 보정 등)이 이 주장을 얼마나 믿을지에 직접 영향을 준 경우");
            sb.AppendLine("  belief       : 이 주장에 대해 갖고 있던 기존 믿음 자체가 가장 직접적인 근거인 경우");
            sb.AppendLine("  profile      : 성향 태그와 기본 성격이 가장 직접적인 근거인 경우");
            sb.AppendLine("  goal         : 목표를 유지하거나 달성하는 것이 직접적인 근거인 경우");
            sb.AppendLine("  source       : 정보 출처의 신뢰도나 종류가 직접적인 근거인 경우");

            var tags = JudgmentGroundsValidator.ProfileTagsOf(ctx.Npc.Data);
            sb.AppendLine(tags.Count > 0
                ? $"profileInfluence는 다음 중 하나이거나 none: {string.Join(" / ", tags)}"
                : "profileInfluence는 none이어야 합니다.");

            var usable = JudgmentGroundsValidator.UsableRelationships(ctx.Npc, ctx.PresentNpcs, ctx.Propagator);
            if (usable.Count > 0)
            {
                var ids = new List<string>();
                foreach (var r in usable) ids.Add(r.other.npcId);
                sb.AppendLine($"relationshipInfluence는 다음 중 하나이거나 none: {string.Join(" / ", ids)}");
            }
            else sb.AppendLine("relationshipInfluence는 none이어야 합니다.");

            sb.AppendLine("{\"interpretation\":\"<이 인물이 이번 주장을 어떻게 받아들였는지>\","
                        + "\"belief\":\"<위 5개 중 하나>\",\"goal\":\"<지금부터의 목표>\","
                        + "\"action\":\"<행동 id>\",\"destinationId\":\"<locationId 또는 stay>\","
                        + "\"dialogue\":\"<짧은 대사>\",\"primaryReason\":\"<위 6개 중 하나>\","
                        + "\"profileInfluence\":\"<성향 태그 또는 none>\",\"relationshipInfluence\":\"<npcId 또는 none>\"}");
        }
    }
}
