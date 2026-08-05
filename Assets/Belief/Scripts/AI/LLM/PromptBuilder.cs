using System.Collections.Generic;
using System.Text;
using Belief.AI;
using Belief.Data;
using Belief.Domain;

namespace Belief.AI.LLM
{
    /// <summary>
    /// Prompt 생성은 이 클래스 하나에서만 한다. 게임 객체를 그대로 Serialize하지 않고,
    /// NpcThinkContext/NpcMoveContext에서 필요한 값만 뽑아 사람이 읽을 수 있는 자연어 요약 + LLM에게
    /// 요구하는 JSON 응답 형식 지시문으로 조립한다.
    ///
    /// 1단계 확장(성향·관계 반영): 이미 저작돼 있으면서 판단에 전혀 쓰이지 않던 값들 - 성향 태그 5개,
    /// aiNotes, backstory, 소속, 충성도, 관계 배열 - 을 문맥으로 넘긴다. 다만 <b>관계 근거로 쓸 수 있는
    /// 대상은 이번 문맥에 실제로 등장하는 인물</b>(같은 장소 NPC 또는 실제 전달자)로 한정해서 표시한다 -
    /// 그렇게 하지 않으면 프로필에 관계가 적혀 있다는 이유만으로 이번 정보와 무관한 인물을 근거로
    /// 가져다 붙인다. 표시 대상과 검증 허용 목록은 JudgmentGroundsValidator.UsableRelationships
    /// 하나에서 나오므로 둘이 어긋날 수 없다.
    /// </summary>
    public static class PromptBuilder
    {
        public static string Build(NpcThinkContext context)
        {
            var npcData = context.Npc.Data;
            var sb = new StringBuilder();

            AppendIdentity(sb, npcData);
            AppendProfileTags(sb, npcData);
            AppendNotesAndBackstory(sb, npcData);
            AppendRelationships(sb, context.Npc, context.PresentNpcs, context.Propagator);

            sb.AppendLine();
            sb.AppendLine("[상황]");
            sb.AppendLine($"현재 위치: {(context.CurrentLocation != null ? context.CurrentLocation.Data.displayName : "알 수 없음")}");
            sb.AppendLine($"현재 턴: {context.CurrentTurn}");
            AppendPresentNpcs(sb, context.Npc, context.PresentNpcs);
            sb.AppendLine(context.Propagator != null
                ? $"이 정보를 전달한 인물: {context.Propagator.Data.npcId} ({context.Propagator.Data.displayName})"
                : "이 정보를 전달한 인물: none (정보원을 통한 전달이라 전달한 인물이 없음)");
            sb.AppendLine($"이 정보에 대한 현재 믿음: {context.CurrentBelief}");

            sb.AppendLine();
            sb.AppendLine("[주장]");
            sb.AppendLine($"정보 Id: {(context.Card.information != null ? context.Card.information.informationId : "알 수 없음")}");
            sb.AppendLine($"제목: {(context.Card.information != null ? context.Card.information.title : "알 수 없음")}");
            sb.AppendLine($"내용: {(context.Card.information != null ? context.Card.information.description : "알 수 없음")}");

            sb.AppendLine();
            sb.AppendLine("[정보 출처]");
            sb.AppendLine($"출처 Id: {(context.Card.source != null ? context.Card.source.sourceId : "알 수 없음")}");
            sb.AppendLine($"선언된 출처: {(context.Card.source != null ? context.Card.source.displayName : "알 수 없음")}");

            sb.AppendLine();
            sb.AppendLine("[관련 기억]");
            if (context.WorkingMemory != null && !context.WorkingMemory.IsEmpty)
            {
                foreach (var entry in context.WorkingMemory.Entries)
                    sb.AppendLine($"- {entry.Description}");
            }
            else
            {
                sb.AppendLine("(관련된 특별한 기억 없음)");
            }

            sb.AppendLine();
            sb.AppendLine("[선택 가능한 행동]");
            if (context.CandidateActions != null)
            {
                foreach (var action in context.CandidateActions)
                    sb.AppendLine($"- {action.actionId}: {action.displayLabel}");
            }

            AppendJudgmentPrinciples(sb);

            sb.AppendLine();
            sb.AppendLine("[응답 형식]");
            sb.AppendLine("반드시 아래 JSON 형식으로만, 다른 텍스트 없이 응답하세요.");
            sb.AppendLine("action은 위 [선택 가능한 행동] 목록의 id 중 하나여야 합니다. 새로운 행동을 만들지 마세요.");
            AppendGroundsFieldSpec(sb, context.Npc, context.PresentNpcs, context.Propagator);
            sb.AppendLine("{\"action\":\"<행동 id>\",\"dialogue\":\"<이 인물이 할 법한 짧은 대사>\","
                        + "\"primaryReason\":\"<위 6개 중 하나>\",\"profileInfluence\":\"<성향 태그 또는 none>\","
                        + "\"relationshipInfluence\":\"<npcId 또는 none>\"}");

            return sb.ToString();
        }

        /// <summary>이동 판단 전용 Prompt. 특정 카드 노출과 무관하게 매 턴 전원에 대해 호출되므로
        /// Build(NpcThinkContext)와 달리 카드/기억/전달자 섹션이 없다 - 관계 근거는 같은 장소에 있는
        /// 인물로만 성립한다.</summary>
        public static string BuildMove(NpcMoveContext context)
        {
            var npcData = context.Npc.Data;
            var sb = new StringBuilder();

            AppendIdentity(sb, npcData);
            AppendProfileTags(sb, npcData);
            AppendNotesAndBackstory(sb, npcData);
            AppendRelationships(sb, context.Npc, context.PresentNpcs, null);

            sb.AppendLine();
            sb.AppendLine("[상황]");
            sb.AppendLine($"현재 위치: {(context.CurrentLocation != null ? context.CurrentLocation.displayName : "알 수 없음")}");
            sb.AppendLine($"현재 턴: {context.CurrentTurn}");
            AppendPresentNpcs(sb, context.Npc, context.PresentNpcs);

            sb.AppendLine();
            sb.AppendLine("[이동 후보]");
            if (context.Candidates != null)
            {
                foreach (var loc in context.Candidates)
                    if (loc != null) sb.AppendLine($"- {loc.locationId}: {loc.displayName}");
            }

            AppendJudgmentPrinciples(sb);

            sb.AppendLine();
            sb.AppendLine("[응답 형식]");
            sb.AppendLine("반드시 아래 JSON 형식으로만, 다른 텍스트 없이 응답하세요.");
            sb.AppendLine("destination은 위 [이동 후보] 목록의 locationId 중 하나이거나, 이동할 필요가 없다면 \"stay\"여야 합니다.");
            AppendGroundsFieldSpec(sb, context.Npc, context.PresentNpcs, null);
            sb.AppendLine("{\"destination\":\"<locationId 또는 stay>\","
                        + "\"primaryReason\":\"<위 6개 중 하나>\",\"profileInfluence\":\"<성향 태그 또는 none>\","
                        + "\"relationshipInfluence\":\"<npcId 또는 none>\"}");

            return sb.ToString();
        }

        // ── 공통 섹션 ────────────────────────────────────────────────────────────────

        static void AppendIdentity(StringBuilder sb, NpcData npcData)
        {
            sb.AppendLine("[NPC]");
            // "주요 인물 / 일반 시민" 구분은 뺐다 - NPC 등급을 없앴을 뿐 아니라, 그 라벨 자체가
            // AI에게 "이 인물은 덜 중요하다"는 편향을 주어 판단을 납작하게 만들 소지가 있다.
            // 인물의 차이는 아래 직업·성향 태그·목표로 충분히 드러난다.
            sb.AppendLine($"이름: {npcData.displayName}");
            if (!string.IsNullOrEmpty(npcData.job)) sb.AppendLine($"직업: {npcData.job}");
            if (!string.IsNullOrEmpty(npcData.affiliation)) sb.AppendLine($"소속: {npcData.affiliation}");
            sb.Append($"신뢰경향: {npcData.trustBias:F2}, 의심도: {npcData.skepticism:F2}");
            if (npcData is MajorNpcData m) sb.Append($", 충성도: {m.loyalty:F2}");
            sb.AppendLine();
            if (npcData is MajorNpcData major && !string.IsNullOrEmpty(major.goal))
                sb.AppendLine($"목표: {major.goal}");
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
                foreach (var note in npcData.aiNotes)
                    if (!string.IsNullOrWhiteSpace(note)) sb.AppendLine($"- {note.Trim()}");
            }

            if (!string.IsNullOrWhiteSpace(npcData.backstory))
            {
                sb.AppendLine();
                sb.AppendLine("[배경]");
                sb.AppendLine(npcData.backstory.Trim());
            }
        }

        /// <summary>관계는 두 묶음으로 나눠 보여준다. "지금 근거로 쓸 수 있는 관계"만 relationshipInfluence
        /// 후보이고, 나머지는 인물 이해를 돕는 배경으로만 제공한다 - 근거 후보와 배경을 섞어 놓으면
        /// 이번 정보와 아무 접점 없는 인물을 근거로 반환해 응답이 통째로 무효가 된다.</summary>
        static void AppendRelationships(StringBuilder sb, NpcState self, IReadOnlyList<NpcState> presentNpcs, NpcState propagator)
        {
            if (!(self.Data is MajorNpcData major) || major.relationships == null || major.relationships.Length == 0) return;

            var usable = JudgmentGroundsValidator.UsableRelationships(self, presentNpcs, propagator);

            sb.AppendLine();
            sb.AppendLine("[관계]");
            if (usable.Count > 0)
            {
                sb.AppendLine("▶ 지금 이 자리/이번 정보에 실제로 관련된 인물 (relationshipInfluence로 반환 가능):");
                foreach (var rel in usable) AppendRelationshipLine(sb, rel, propagator);
            }
            else
            {
                sb.AppendLine("▶ 지금 이 자리/이번 정보에 관련된 인물: 없음");
                sb.AppendLine("  (따라서 relationshipInfluence는 반드시 none이어야 합니다)");
            }

            var background = new List<RelationshipEntry>();
            foreach (var rel in major.relationships)
                if (rel.other != null && !usable.Contains(rel)) background.Add(rel);

            if (background.Count > 0)
            {
                sb.AppendLine("▷ 참고용 배경 관계 (지금 자리에 없으므로 근거로 반환할 수 없음):");
                foreach (var rel in background) AppendRelationshipLine(sb, rel, null);
            }
        }

        static void AppendRelationshipLine(StringBuilder sb, RelationshipEntry rel, NpcState propagator)
        {
            bool isPropagator = propagator != null && propagator.Data == rel.other;
            string label = !string.IsNullOrWhiteSpace(rel.relationshipTypeLabel) ? rel.relationshipTypeLabel.Trim() : "관계 유형 미지정";

            // strength는 원본 float와 구간 해석을 함께 준다 - 구간 라벨은 설명용일 뿐 게임 수치가 아니다.
            sb.AppendLine($"  - {rel.other.npcId} ({rel.other.displayName}) | {label} | "
                        + $"strength={rel.strength:F2} ({JudgmentGroundsValidator.DescribeStrength(rel.strength)})"
                        + (isPropagator ? " | ★이 정보를 전달한 당사자" : ""));
            if (!string.IsNullOrWhiteSpace(rel.relationshipDescription))
                sb.AppendLine($"      {rel.relationshipDescription.Trim()}");
        }

        static void AppendPresentNpcs(StringBuilder sb, NpcState self, IReadOnlyList<NpcState> presentNpcs)
        {
            if (presentNpcs == null || presentNpcs.Count == 0)
            {
                sb.AppendLine("같은 장소에 있는 인물: 없음");
                return;
            }

            var parts = new List<string>();
            foreach (var n in presentNpcs)
                if (n != null && n != self && n.Data != null)
                    parts.Add($"{n.Data.npcId} ({n.Data.displayName})");

            sb.AppendLine(parts.Count > 0 ? $"같은 장소에 있는 인물: {string.Join(", ", parts)}" : "같은 장소에 있는 인물: 없음");
        }

        static void AppendJudgmentPrinciples(StringBuilder sb)
        {
            sb.AppendLine();
            sb.AppendLine("[판단 원칙]");
            sb.AppendLine("성향 태그는 이 인물의 기본 경향일 뿐이며 반드시 따라야 하는 규칙이 아닙니다.");
            sb.AppendLine("관계, 정보 출처, 지금 이 자리에 있는 인물과의 관계가 충분히 강하다면 평소 성향과 다른 선택을 해도 됩니다.");
            sb.AppendLine("다만 평소와 다른 선택을 했다면, 위에 실제로 제시된 성향 태그나 관계 npcId를 근거로 반드시 반환해야 합니다.");
            sb.AppendLine("위에 제시되지 않은 인물, 관계, 기억, 과거 사건을 지어내지 마세요. 지어내면 응답 전체가 무효 처리됩니다.");
        }

        static void AppendGroundsFieldSpec(StringBuilder sb, NpcState self, IReadOnlyList<NpcState> presentNpcs, NpcState propagator)
        {
            sb.AppendLine($"primaryReason은 다음 중 하나여야 합니다: {string.Join(" / ", JudgmentGroundsValidator.PrimaryReasons)}");

            var tags = JudgmentGroundsValidator.ProfileTagsOf(self.Data);
            sb.AppendLine(tags.Count > 0
                ? $"profileInfluence는 다음 중 하나이거나 none이어야 합니다: {string.Join(" / ", tags)}"
                : "profileInfluence는 none이어야 합니다.");

            var usable = JudgmentGroundsValidator.UsableRelationships(self, presentNpcs, propagator);
            if (usable.Count > 0)
            {
                var ids = new List<string>();
                foreach (var rel in usable) ids.Add(rel.other.npcId);
                sb.AppendLine($"relationshipInfluence는 다음 중 하나이거나 none이어야 합니다: {string.Join(" / ", ids)}");
            }
            else
            {
                sb.AppendLine("relationshipInfluence는 none이어야 합니다.");
            }
        }
    }
}
