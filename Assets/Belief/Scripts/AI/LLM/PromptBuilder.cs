using System.Text;
using Belief.AI;
using Belief.Data;

namespace Belief.AI.LLM
{
    /// <summary>
    /// Prompt 생성은 이 클래스 하나에서만 한다. 게임 객체를 그대로 Serialize하지 않고,
    /// NpcThinkContext/NpcMoveContext에서 필요한 값만 뽑아 사람이 읽을 수 있는 자연어 요약 + LLM에게
    /// 요구하는 JSON 응답 형식 지시문으로 조립한다.
    /// </summary>
    public static class PromptBuilder
    {
        public static string Build(NpcThinkContext context)
        {
            var npcData = context.Npc.Data;
            var sb = new StringBuilder();

            sb.AppendLine("[NPC]");
            // "주요 인물 / 일반 시민" 구분은 뺐다 - NPC 등급을 없앴을 뿐 아니라, 그 라벨 자체가
            // AI에게 "이 인물은 덜 중요하다"는 편향을 주어 판단을 납작하게 만들 소지가 있다.
            // 인물의 차이는 아래 직업·성향 태그·목표로 충분히 드러난다.
            sb.AppendLine($"이름: {npcData.displayName}");
            if (!string.IsNullOrEmpty(npcData.job)) sb.AppendLine($"직업: {npcData.job}");
            sb.AppendLine($"신뢰경향: {npcData.trustBias:F2}, 의심도: {npcData.skepticism:F2}");
            if (npcData is MajorNpcData major && !string.IsNullOrEmpty(major.goal))
                sb.AppendLine($"목표: {major.goal}");

            sb.AppendLine();
            sb.AppendLine("[상황]");
            sb.AppendLine($"현재 위치: {(context.CurrentLocation != null ? context.CurrentLocation.Data.displayName : "알 수 없음")}");
            sb.AppendLine($"현재 턴: {context.CurrentTurn}");
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

            sb.AppendLine();
            sb.AppendLine("[응답 형식]");
            sb.AppendLine("반드시 아래 JSON 형식으로만, 다른 텍스트 없이 응답하세요.");
            sb.AppendLine("action은 위 [선택 가능한 행동] 목록의 id 중 하나여야 합니다. 새로운 행동을 만들지 마세요.");
            sb.AppendLine("{\"action\":\"<행동 id>\",\"dialogue\":\"<이 인물이 할 법한 짧은 대사>\"}");

            return sb.ToString();
        }

        /// <summary>이동 판단 전용 Prompt. 특정 카드 노출과 무관하게 매 턴 전원에 대해 호출되므로
        /// Build(NpcThinkContext)와 달리 카드/기억 섹션이 없다.</summary>
        public static string BuildMove(NpcMoveContext context)
        {
            var npcData = context.Npc.Data;
            var sb = new StringBuilder();

            sb.AppendLine("[NPC]");
            sb.AppendLine($"이름: {npcData.displayName}");
            if (npcData is MajorNpcData major && !string.IsNullOrEmpty(major.goal))
                sb.AppendLine($"목표: {major.goal}");

            sb.AppendLine();
            sb.AppendLine("[상황]");
            sb.AppendLine($"현재 위치: {(context.CurrentLocation != null ? context.CurrentLocation.displayName : "알 수 없음")}");
            sb.AppendLine($"현재 턴: {context.CurrentTurn}");

            sb.AppendLine();
            sb.AppendLine("[이동 후보]");
            if (context.Candidates != null)
            {
                foreach (var loc in context.Candidates)
                    if (loc != null) sb.AppendLine($"- {loc.locationId}: {loc.displayName}");
            }

            sb.AppendLine();
            sb.AppendLine("[응답 형식]");
            sb.AppendLine("반드시 아래 JSON 형식으로만, 다른 텍스트 없이 응답하세요.");
            sb.AppendLine("destination은 위 [이동 후보] 목록의 locationId 중 하나이거나, 이동할 필요가 없다면 \"stay\"여야 합니다.");
            sb.AppendLine("{\"destination\":\"<locationId 또는 stay>\"}");

            return sb.ToString();
        }
    }
}
