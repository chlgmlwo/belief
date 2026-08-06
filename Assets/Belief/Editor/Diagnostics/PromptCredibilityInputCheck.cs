using System;
using System.Collections.Generic;
using System.Text;
using Belief.AI;
using Belief.AI.LLM;
using Belief.Data;
using Belief.Domain;
using UnityEditor;
using UnityEngine;

namespace Belief.EditorTools.Diagnostics
{
    /// <summary>
    /// 카드 내용 신뢰도 · 출처 신뢰도 · 장소 신뢰도 보정이 IntegratedLlm 프롬프트에 실제로
    /// 실리는지 <b>문자열 수준에서</b> 결정적으로 검증한다. <b>API 호출 0회 · 요금 0원</b>이며
    /// 실제 표적 검증(BELIEF/Diagnostics/IntegratedLlm ...) 전에 먼저 통과시키는 것을 전제로 한다.
    ///
    /// 세 값은 서로 독립적인 축이라, 값이 "들어갔는지"만이 아니라 <b>서로 뒤바뀌거나 합쳐지지
    /// 않는지</b>까지 본다 - 엇갈린 조합(내용 높음/출처 낮음)에서 두 숫자가 각자 자리에 남아야 한다.
    /// </summary>
    public static class PromptCredibilityInputCheck
    {
        static int passed, failed;
        static readonly StringBuilder Report = new StringBuilder();

        [MenuItem("BELIEF/Diagnostics/프롬프트 신뢰도 입력 검증 (호출 0회)", priority = 140)]
        public static void Run()
        {
            passed = 0; failed = 0; Report.Length = 0;

            SectionA_DirectDelivery();
            SectionB_CrossedValues();
            SectionC_Respread();
            SectionD_LocationModifier();
            SectionE_Regression();

            var head = $"프롬프트 신뢰도 입력 검증: {passed}/{passed + failed} PASS"
                       + (failed == 0 ? "  — 전부 통과" : $"  — {failed}건 실패");
            if (failed == 0) Debug.Log(head + "\n" + Report);
            else Debug.LogError(head + "\n" + Report);
        }

        // ── A. 직접 전달 ────────────────────────────────────────────────────────
        static void SectionA_DirectDelivery()
        {
            Report.AppendLine("[A] 직접 전달");
            string p = Build(cred: 0.55f, trust: 0.65f, modifier: LocationCredibilityModifier.Neutral);

            Check("A1 내용 신뢰도가 실린다", p.Contains("내용 신뢰도: 0.55"));
            Check("A2 출처 신뢰도가 실린다", p.Contains("출처 신뢰도: 0.65"));
            Check("A3 장소 신뢰도 보정이 실린다", p.Contains("장소 신뢰도 보정: Neutral"));
            Check("A4 내용 신뢰도가 주장 섹션에 있다", IsAfter(p, "[이번에 접한 주장]", "내용 신뢰도: 0.55", "[정보 출처]"));
            Check("A5 출처 신뢰도가 출처 섹션에 있다", IsAfter(p, "[정보 출처]", "출처 신뢰도: 0.65", "[관련 기억]"));
        }

        // ── B. 서로 엇갈린 값 ───────────────────────────────────────────────────
        static void SectionB_CrossedValues()
        {
            Report.AppendLine("[B] 엇갈린 신뢰도 조합");

            string high = Build(cred: 0.90f, trust: 0.10f, modifier: LocationCredibilityModifier.Neutral);
            Check("B1 내용 높음/출처 낮음 - 내용 0.90", high.Contains("내용 신뢰도: 0.90"));
            Check("B2 내용 높음/출처 낮음 - 출처 0.10", high.Contains("출처 신뢰도: 0.10"));
            Check("B3 두 값이 뒤바뀌지 않음", !high.Contains("내용 신뢰도: 0.10") && !high.Contains("출처 신뢰도: 0.90"));

            string low = Build(cred: 0.10f, trust: 0.90f, modifier: LocationCredibilityModifier.Neutral);
            Check("B4 내용 낮음/출처 높음 - 내용 0.10", low.Contains("내용 신뢰도: 0.10"));
            Check("B5 내용 낮음/출처 높음 - 출처 0.90", low.Contains("출처 신뢰도: 0.90"));
            Check("B6 두 값이 뒤바뀌지 않음", !low.Contains("내용 신뢰도: 0.90") && !low.Contains("출처 신뢰도: 0.10"));

            // 합쳐진 단일 값(평균 0.50 등)이 새로 등장하지 않는지 - 두 축이 하나로 뭉개지면 안 된다.
            Check("B7 두 값을 합친 단일 수치가 없다",
                !high.Contains("종합 신뢰도") && !high.Contains("평균 신뢰도") && !high.Contains("최종 신뢰도"));
        }

        // ── C. 재확산 ───────────────────────────────────────────────────────────
        static void SectionC_Respread()
        {
            Report.AppendLine("[C] 재확산");

            // 재확산은 같은 카드 객체를 그대로 넘긴다(InfoDeliverySystem.TryReSpread → ExposeCardAtLocationAsync).
            // 따라서 두 경로의 신뢰도 숫자가 동일해야 한다 - 다르면 어딘가에서 감쇠가 중복 적용된 것이다.
            var card = MakeCard(0.70f, 0.40f);
            var propagator = MakeNpc("npc_witness", "목격자");

            string direct = Build(card, LocationCredibilityModifier.Neutral, propagator: null);
            string respread = Build(card, LocationCredibilityModifier.Neutral, propagator: propagator);

            Check("C1 재확산에서도 내용 신뢰도 동일", respread.Contains("내용 신뢰도: 0.70"));
            Check("C2 재확산에서도 출처 신뢰도 동일", respread.Contains("출처 신뢰도: 0.40"));
            Check("C3 직접 전달과 재확산의 신뢰도가 같다 (감쇠 중복 없음)",
                Extract(direct, "내용 신뢰도: ") == Extract(respread, "내용 신뢰도: ")
                && Extract(direct, "출처 신뢰도: ") == Extract(respread, "출처 신뢰도: "));
            Check("C4 재확산이면 전달 인물이 표기된다", respread.Contains("npc_witness"));
            Check("C5 직접 전달이면 전달 인물이 none", direct.Contains("이 정보를 전달한 인물: none"));
        }

        // ── D. 장소 보정 ────────────────────────────────────────────────────────
        static void SectionD_LocationModifier()
        {
            Report.AppendLine("[D] 장소 신뢰도 보정");

            var expected = new (LocationCredibilityModifier v, string meaning)[]
            {
                (LocationCredibilityModifier.Unspecified, "별도 장소 신뢰도 정보 없음"),
                (LocationCredibilityModifier.Low,         "덜 신뢰받기 쉬움"),
                (LocationCredibilityModifier.Neutral,     "추가 보정 없음"),
                (LocationCredibilityModifier.High,        "더 신뢰받기 쉬움"),
                (LocationCredibilityModifier.VeryHigh,    "훨씬 더 신뢰받기 쉬움"),
            };

            foreach (var (v, meaning) in expected)
            {
                string p = Build(cred: 0.50f, trust: 0.50f, modifier: v);
                Check($"D:{v} 이름 출력", p.Contains($"장소 신뢰도 보정: {v}"));
                Check($"D:{v} 의미 출력", p.Contains(meaning));
            }

            // 장소가 없을 때(Where == null) 터지지 않고 안전한 표현으로 떨어지는지.
            string noWhere = BuildWithoutLocation();
            Check("D6 장소가 없어도 예외 없이 생성된다", !string.IsNullOrEmpty(noWhere));
            Check("D7 장소가 없으면 신뢰도 보정 줄이 없다", !noWhere.Contains("장소 신뢰도 보정:"));

            // 프롬프트 전체에 정확히 한 번만 나와야 한다 - [이 주장을 듣고 있는 자리]로 옮긴 뒤
            // [현재 위치]에도 남아 있으면 같은 값이 두 번 등장한다.
            string p2 = Build(cred: 0.50f, trust: 0.50f, modifier: LocationCredibilityModifier.High);
            int first = p2.IndexOf("장소 신뢰도 보정:", StringComparison.Ordinal);
            int last = p2.LastIndexOf("장소 신뢰도 보정:", StringComparison.Ordinal);
            Check("D8 신뢰도 보정이 프롬프트에 정확히 1회", first >= 0 && first == last);

            // 독립 섹션으로 승격됐는지 - 출처와 같은 위상이어야 한다는 것이 이번 변경의 요지다.
            Check("D9 듣고 있는 자리 섹션이 있다", p2.Contains("[이 주장을 듣고 있는 자리]"));
            Check("D10 보정이 그 섹션 안에 있다",
                IsAfter(p2, "[이 주장을 듣고 있는 자리]", "장소 신뢰도 보정:", "[관련 기억]"));
            Check("D11 민감 주제 일치 여부를 알려준다",
                p2.Contains("이 자리가 민감하게 다루는 주제:")
                && (p2.Contains("유형과 일치한다") || p2.Contains("유형과는 다르다")));
            Check("D12 카드 정보 유형이 실린다", p2.Contains("정보 유형:"));
        }

        // ── E. 회귀 ─────────────────────────────────────────────────────────────
        static void SectionE_Regression()
        {
            Report.AppendLine("[E] 회귀 - 기존 섹션 보존");
            string p = Build(cred: 0.55f, trust: 0.65f, modifier: LocationCredibilityModifier.High);

            foreach (var section in new[]
                     { "[NPC]", "[성향 태그]", "[상황]", "[이번에 접한 주장]", "[정보 출처]",
                       "[이 주장을 듣고 있는 자리]", "[관련 기억]", "[선택 가능한 행동]",
                       "[현재 위치]", "[이동 후보]", "[판단 원칙]", "[응답 형식]" })
                Check($"E 섹션 유지 {section}", p.Contains(section));

            Check("E1 NPC 성향 수치가 그대로 있다", p.Contains("신뢰경향:") && p.Contains("의심도:"));
            Check("E2 공식 계산을 지시하지 않는다",
                p.Contains("정해진 공식으로 계산하거나") && !p.Contains("평균내"));

            // primaryReason에 location을 추가한 뒤 - 프롬프트 정의와 검증기 허용값이 어긋나면
            // LLM이 location을 골라도 InvalidPrimaryReason으로 전체 폴백된다.
            // 이 파일은 System.Linq를 쓰지 않으므로 확장 메서드 대신 직접 순회한다.
            Check("E3 프롬프트에 location 정의가 있다", p.Contains("location     :"));
            Check("E4 검증기가 location을 허용한다", ReasonAllowed("location"));
            bool allKept = true;
            foreach (var r in new[] { "profile", "relationship", "belief", "goal", "source", "situation" })
                if (!ReasonAllowed(r)) allKept = false;
            Check("E5 기존 6개 카테고리가 그대로 있다", allKept);
            Check("E6 situation 정의가 location과 구분된다",
                p.Contains("경계 태세 같은 비인격적 상황") && p.Contains("장소의 성격"));

            // 프롬프트 증가량 - 토큰 비용에 직접 연결되므로 수치를 기록해 둔다.
            string before = BuildLegacyApproximation();
            int delta = p.Length - before.Length;
            Report.AppendLine($"    프롬프트 길이: {before.Length} → {p.Length} 자 (+{delta}자, 약 +{delta / 2}~{delta}토큰 추정)");
        }

        // ── 빌더 ────────────────────────────────────────────────────────────────

        static string Build(float cred, float trust, LocationCredibilityModifier modifier) =>
            Build(MakeCard(cred, trust), modifier, null);

        static string Build(InformationCardData card, LocationCredibilityModifier modifier, NpcState propagator)
        {
            var npc = MakeNpc("npc_guard_captain", "경비대장");
            var where = MakeLocation("LOC_GUARD_POST", "경비 초소", modifier);
            var dest = MakeLocation("LOC_INN", "여관", LocationCredibilityModifier.Low);

            var ctx = new NpcJudgmentContext(
                npc, card, where, turn: 3,
                beliefBefore: BeliefState.Unknown, goalBefore: null,
                memory: null,
                actionCandidates: new List<NpcActionData>(),
                moveCandidates: new List<LocationData> { dest.Data },
                presentNpcs: new List<NpcState>(),
                propagator: propagator,
                allLocations: new Dictionary<LocationData, LocationState> { { dest.Data, dest } });

            return UnifiedPromptBuilder.Build(ctx);
        }

        static string BuildWithoutLocation()
        {
            var npc = MakeNpc("npc_guard_captain", "경비대장");
            var ctx = new NpcJudgmentContext(
                npc, MakeCard(0.5f, 0.5f), null, 1,
                BeliefState.Unknown, null, null,
                new List<NpcActionData>(), new List<LocationData>(), new List<NpcState>(), null);
            return UnifiedPromptBuilder.Build(ctx);
        }

        /// <summary>이번에 추가한 네 줄을 뺀 길이 근사 - 증가량 보고에만 쓴다.</summary>
        static string BuildLegacyApproximation()
        {
            string p = Build(0.55f, 0.65f, LocationCredibilityModifier.High);
            var sb = new StringBuilder();
            foreach (var line in p.Split('\n'))
            {
                string t = line.Trim();
                if (t.StartsWith("내용 신뢰도:") || t.StartsWith("출처 신뢰도:")
                    || t.StartsWith("장소 신뢰도 보정:") || t.StartsWith("정보의 내용 신뢰도와")
                    || t.StartsWith("함께 종합적으로") || t.StartsWith("특정 값을 기준으로")) continue;
                sb.Append(line).Append('\n');
            }
            return sb.ToString();
        }

        static InformationCardData MakeCard(float cred, float trust)
        {
            var info = ScriptableObject.CreateInstance<InformationData>();
            info.informationId = "INFO_TEST";
            info.title = "북문 순찰 축소";
            info.description = "북문 야간 순찰이 줄었다는 이야기.";
            info.baseCredibility = cred;

            var source = ScriptableObject.CreateInstance<InfoSourceData>();
            source.sourceId = "SRC_PATROL";
            source.displayName = "순찰대";
            source.baseTrustModifier = trust;

            var card = ScriptableObject.CreateInstance<InformationCardData>();
            card.cardId = "C-TEST-01";
            card.information = info;
            card.source = source;
            card.cardType = InfoCardType.Deliver;
            return card;
        }

        static NpcState MakeNpc(string id, string name)
        {
            var data = ScriptableObject.CreateInstance<MajorNpcData>();
            data.npcId = id;
            data.displayName = name;
            data.trustBias = 0.6f;
            data.skepticism = 0.35f;
            data.loyalty = 0.8f;
            // 실제 NPC 에셋처럼 성향 태그를 채운다 - 비워 두면 [성향 태그] 섹션 자체가 생성되지 않아
            // 회귀 검사가 "섹션이 사라졌다"고 잘못 판정한다(프롬프트 문제가 아니라 표본 문제).
            data.judgmentTendencyTag = "증거를 우선한다";
            data.priorityTag = "질서 유지";
            data.sensitiveInfoTag = "경비 배치";
            data.relationTendencyTag = "상관에게 보고한다";
            data.trustJudgmentTag = "출처를 먼저 본다";
            return new NpcState(data);
        }

        static LocationState MakeLocation(string id, string name, LocationCredibilityModifier modifier)
        {
            var data = ScriptableObject.CreateInstance<LocationData>();
            data.locationId = id;
            data.displayName = name;
            data.credibilityModifier = modifier;
            return new LocationState(data);
        }

        // ── 헬퍼 ────────────────────────────────────────────────────────────────

        /// <summary>검증기의 primaryReason 허용 목록에 값이 있는지 - Linq 없이 직접 순회한다.</summary>
        static bool ReasonAllowed(string reason)
        {
            foreach (var r in JudgmentGroundsValidator.PrimaryReasons)
                if (r == reason) return true;
            return false;
        }

        static void Check(string label, bool ok)
        {
            if (ok) { passed++; Report.AppendLine($"    PASS  {label}"); }
            else { failed++; Report.AppendLine($"    FAIL  {label}"); }
        }

        /// <summary>needle이 start와 end 사이 구간에 있는지 - 값이 엉뚱한 섹션에 붙는 것을 잡는다.</summary>
        static bool IsAfter(string text, string start, string needle, string end)
        {
            int s = text.IndexOf(start, StringComparison.Ordinal);
            int n = text.IndexOf(needle, StringComparison.Ordinal);
            int e = text.IndexOf(end, StringComparison.Ordinal);
            if (s < 0 || n < 0) return false;
            if (e < 0) return n > s;
            return n > s && n < e;
        }

        static string Extract(string text, string key)
        {
            int i = text.IndexOf(key, StringComparison.Ordinal);
            if (i < 0) return null;
            int start = i + key.Length;
            int end = text.IndexOfAny(new[] { ' ', '\r', '\n' }, start);
            return end < 0 ? text.Substring(start) : text.Substring(start, end - start);
        }
    }
}
