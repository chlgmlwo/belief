using System.Collections.Generic;
using Belief.AI;
using Belief.AI.LLM;

namespace Belief.Core
{
    /// <summary>
    /// 파일럿 한 세션에서 <b>실제로 판단된 카드</b>의 표본을 모은다.
    ///
    /// <b>이 클래스는 게임을 바꾸지 않는다</b> - 카드 드로우도, 카드 풀도, 전달 순서도 건드리지
    /// 않고 이미 일어난 판단을 받아 적기만 한다. 그래서 "등급이 하나 빠졌다"는 것은 실패가
    /// 아니라 <b>이번 실행의 표본이 부족했다</b>는 사실이고, 그 판정은 <see cref="CoverageComplete"/>가
    /// 보고할 뿐 어떤 흐름도 중단시키지 않는다.
    ///
    /// 표본 단위가 "전달"이 아니라 "판단"인 이유: 대상 NPC가 없는 장소에 카드를 뿌리면 판단이
    /// 생기지 않고, 그때는 기록할 대상 NPC도 없다. 판단 단위로 세면 카드·대상·경로가 항상 함께 남는다.
    /// </summary>
    public sealed class IntegratedLlmPilotCoverage : IIntegratedJudgmentObserver
    {
        /// <summary>신뢰도 등급 경계 - 파일럿 계획과 결과 보고가 같은 기준을 쓰도록 여기 하나만 둔다.</summary>
        public const float HighThreshold = 0.60f;
        public const float MediumThreshold = 0.45f;

        public readonly struct Sample
        {
            public readonly string CardId;
            public readonly float Credibility;
            public readonly string Tier;

            /// <summary>true면 다른 NPC를 통해 번져 온 것(재확산), false면 플레이어의 직접 전달.</summary>
            public readonly bool ViaRespread;

            public readonly string NpcId;
            public readonly int Turn;

            public Sample(string cardId, float credibility, string tier, bool viaRespread, string npcId, int turn)
            {
                CardId = cardId; Credibility = credibility; Tier = tier;
                ViaRespread = viaRespread; NpcId = npcId; Turn = turn;
            }
        }

        readonly List<Sample> samples = new List<Sample>();

        public IReadOnlyList<Sample> Samples => samples;
        public int Count => samples.Count;

        public bool HasHigh => HasTier("High");
        public bool HasMedium => HasTier("Medium");
        public bool HasLow => HasTier("Low");

        /// <summary>세 등급이 모두 한 번 이상 판단됐는가. false면 표본이 부족한 것이지
        /// 실행이 실패한 것이 아니다 - 호출자는 "추가 실행 필요"로만 보고한다.</summary>
        public bool CoverageComplete => HasHigh && HasMedium && HasLow;

        public int DirectCount => CountWhere(false);
        public int RespreadCount => CountWhere(true);

        public void OnJudgmentRequested(NpcJudgmentContext context, JudgmentRequestIdentity identity)
        {
            var card = context.Card;
            if (card == null || card.information == null) return;

            float cred = card.information.baseCredibility;
            samples.Add(new Sample(
                card.cardId, cred, TierOf(cred),
                context.Propagator != null,
                context.Npc != null && context.Npc.Data != null ? context.Npc.Data.npcId : "?",
                context.Turn));
        }

        public static string TierOf(float credibility) =>
            credibility >= HighThreshold ? "High" : credibility >= MediumThreshold ? "Medium" : "Low";

        public int CountOfTier(string tier)
        {
            int n = 0;
            foreach (var s in samples) if (s.Tier == tier) n++;
            return n;
        }

        bool HasTier(string tier)
        {
            foreach (var s in samples) if (s.Tier == tier) return true;
            return false;
        }

        int CountWhere(bool respread)
        {
            int n = 0;
            foreach (var s in samples) if (s.ViaRespread == respread) n++;
            return n;
        }
    }
}
