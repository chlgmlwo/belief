using System.Collections.Generic;
using Belief.Data;
using Belief.Domain;

namespace Belief.AI.LLM
{
    /// <summary>LLM이 반환한 "왜 그렇게 판단했는가"를 담는 검증 완료 값. 검증을 통과한 것만
    /// 이 구조체가 되므로, 여기 들어 있는 태그/npcId는 반드시 Unity가 제공한 실제 데이터다.
    /// None에 해당하는 값은 null로 정규화해 둔다.</summary>
    public readonly struct JudgmentGrounds
    {
        /// <summary>profile / relationship / belief / goal / source / situation 중 하나.</summary>
        public readonly string PrimaryReason;

        /// <summary>이 NPC의 5개 성향 태그 중 하나(원본 표기 그대로). 근거로 쓰지 않았으면 null.</summary>
        public readonly string ProfileInfluence;

        /// <summary>이 NPC의 relationships에 실재하면서 이번 문맥에도 등장하는 NPC의 npcId.
        /// 근거로 쓰지 않았으면 null.</summary>
        public readonly string RelationshipInfluence;

        public JudgmentGrounds(string primaryReason, string profileInfluence, string relationshipInfluence)
        {
            PrimaryReason = primaryReason;
            ProfileInfluence = profileInfluence;
            RelationshipInfluence = relationshipInfluence;
        }

        public static JudgmentGrounds None => new JudgmentGrounds(null, null, null);

        public override string ToString() =>
            $"reason={PrimaryReason ?? "-"} / profile={ProfileInfluence ?? "none"} / relationship={RelationshipInfluence ?? "none"}";
    }

    /// <summary>
    /// LLM 응답의 근거 3필드를 "Unity가 실제로 제공한 값인가"로만 검증한다. 판단의 옳고 그름은
    /// 전혀 보지 않는다 - 지어낸 인물/태그/관계를 걸러내는 것이 유일한 목적이다.
    ///
    /// 관계 근거에는 조건이 둘이다:
    /// <list type="number">
    /// <item>그 NPC가 이 NPC의 relationships 배열에 실재할 것</item>
    /// <item>그 NPC가 <b>이번 판단 문맥에 실제로 등장</b>할 것(같은 장소에 있거나, 이 정보를 전달한
    ///   당사자거나) - 프로필에 관계가 적혀 있다는 이유만으로 이번 정보와 무관한 인물을 근거로
    ///   가져다 붙이는 것을 막는다.</item>
    /// </list>
    /// </summary>
    public static class JudgmentGroundsValidator
    {
        public const string NoneToken = "none";

        /// <summary>
        /// primaryReason 허용값. <b>location은 2026-08-06에 추가</b>됐다 - 장소 신뢰도 보정을
        /// 프롬프트에 넣었는데도 판단에 반영되지 않아 24회 실측으로 확인해 보니, 장소를 근거로
        /// 삼아도 <b>반환할 카테고리가 없어서</b> source/belief로 흘러가고 있었다.
        ///
        /// situation과 구분된다 - situation은 봉쇄·경계 태세 같은 <b>그때그때의 상태</b>,
        /// location은 그 장소가 원래 가진 <b>성격</b>(신뢰도 보정)이 근거인 경우다.
        /// </summary>
        static readonly string[] AllowedPrimaryReasons =
            { "profile", "relationship", "belief", "goal", "source", "situation", "location" };

        public static IReadOnlyList<string> PrimaryReasons => AllowedPrimaryReasons;

        /// <summary>태그 비교용 정규화 - 앞뒤 공백 제거 + 선행 '#' 제거. 원본 Profile 데이터는
        /// 절대 건드리지 않고, 비교할 때만 양쪽을 같은 방식으로 정규화한다.</summary>
        public static string NormalizeTag(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return null;
            var t = tag.Trim();
            while (t.Length > 0 && t[0] == '#') t = t.Substring(1);
            return t.Trim();
        }

        static bool IsNone(string value) =>
            string.IsNullOrWhiteSpace(value) || string.Equals(value.Trim(), NoneToken, System.StringComparison.OrdinalIgnoreCase);

        /// <summary>이 NPC가 실제로 가진 5개 성향 태그(빈 값 제외, 원본 표기 그대로).</summary>
        public static List<string> ProfileTagsOf(NpcData data)
        {
            var list = new List<string>(5);
            if (data == null) return list;
            void Add(string s) { if (!string.IsNullOrWhiteSpace(s)) list.Add(s.Trim()); }
            Add(data.judgmentTendencyTag);
            Add(data.priorityTag);
            Add(data.sensitiveInfoTag);
            Add(data.relationTendencyTag);
            Add(data.trustJudgmentTag);
            return list;
        }

        /// <summary>이번 판단에서 관계 근거로 쓸 수 있는 NPC들 - 같은 장소에 있는 인물 + 실제 전달자.
        /// 둘 다 없으면 빈 목록이고, 그때는 relationshipInfluence가 none이어야만 유효하다.</summary>
        public static List<NpcData> ContextualNpcs(
            NpcState self, IReadOnlyList<NpcState> presentNpcs, NpcState propagator)
        {
            var list = new List<NpcData>();
            if (presentNpcs != null)
                foreach (var n in presentNpcs)
                    if (n != null && n != self && n.Data != null && !list.Contains(n.Data)) list.Add(n.Data);
            if (propagator != null && propagator != self && propagator.Data != null && !list.Contains(propagator.Data))
                list.Add(propagator.Data);
            return list;
        }

        /// <summary>실재하는 관계 중 이번 문맥에도 등장하는 것만 추린다 - 프롬프트에 "근거로 쓸 수
        /// 있음"으로 표시할 대상이자, 검증 시 허용 목록이기도 하다(둘이 같은 함수를 쓰므로 어긋날 수 없다).</summary>
        public static List<RelationshipEntry> UsableRelationships(
            NpcState self, IReadOnlyList<NpcState> presentNpcs, NpcState propagator)
        {
            var usable = new List<RelationshipEntry>();
            if (!(self?.Data is MajorNpcData major) || major.relationships == null) return usable;

            var contextual = ContextualNpcs(self, presentNpcs, propagator);
            foreach (var rel in major.relationships)
                if (rel.other != null && contextual.Contains(rel.other)) usable.Add(rel);
            return usable;
        }

        /// <summary>프롬프트 표기 전용 강도 해석. 게임 수치 계산에는 절대 쓰지 않는다(원본 float도
        /// 함께 제공하므로 이 라벨은 어디까지나 사람이 읽는 설명이다).</summary>
        public static string DescribeStrength(float s) =>
            s >= 0.6f ? "Strong Positive" :
            s >= 0.2f ? "Positive" :
            s > -0.2f ? "Neutral" :
            s > -0.6f ? "Negative" : "Strong Negative";

        /// <summary>검증 본체. 실패하면 failureReason에 분류 라벨(관찰 창이 쓰는 영문 키)을 담는다.</summary>
        public static bool TryValidate(
            string primaryReason, string profileInfluence, string relationshipInfluence,
            NpcState self, IReadOnlyList<NpcState> presentNpcs, NpcState propagator,
            out JudgmentGrounds grounds, out string failureReason)
        {
            grounds = JudgmentGrounds.None;
            failureReason = null;

            // 1) primaryReason - 6개 enum 중 하나여야 하고 생략할 수 없다.
            string reason = primaryReason != null ? primaryReason.Trim().ToLowerInvariant() : null;
            if (string.IsNullOrEmpty(reason))
            {
                failureReason = "MissingPrimaryReason";
                return false;
            }
            bool reasonOk = false;
            foreach (var r in AllowedPrimaryReasons) if (r == reason) { reasonOk = true; break; }
            if (!reasonOk)
            {
                failureReason = "InvalidPrimaryReason";
                return false;
            }

            // 2) profileInfluence - none이거나, 이 NPC가 실제로 가진 5개 태그 중 하나.
            string profileResolved = null;
            if (!IsNone(profileInfluence))
            {
                var wanted = NormalizeTag(profileInfluence);
                foreach (var tag in ProfileTagsOf(self?.Data))
                {
                    if (NormalizeTag(tag) == wanted) { profileResolved = tag; break; }
                }
                if (profileResolved == null)
                {
                    failureReason = "InvalidProfileInfluence";
                    return false;
                }
            }

            // 3) relationshipInfluence - none이거나, 실재하면서 이번 문맥에도 등장하는 관계.
            string relationshipResolved = null;
            if (!IsNone(relationshipInfluence))
            {
                var wanted = relationshipInfluence.Trim();

                bool existsInProfile = false;
                if (self?.Data is MajorNpcData major && major.relationships != null)
                    foreach (var rel in major.relationships)
                        if (rel.other != null && rel.other.npcId == wanted) { existsInProfile = true; break; }

                if (!existsInProfile)
                {
                    // 프로필에 아예 없는 인물 = 지어낸 관계.
                    failureReason = "UnknownRelationship";
                    return false;
                }

                bool inContext = false;
                foreach (var rel in UsableRelationships(self, presentNpcs, propagator))
                    if (rel.other != null && rel.other.npcId == wanted) { inContext = true; break; }

                if (!inContext)
                {
                    // 관계는 실재하지만 이번 정보/장소와 아무 접점이 없다.
                    failureReason = "IrrelevantRelationship";
                    return false;
                }

                relationshipResolved = wanted;
            }

            // 4) 일관성 - "관계 때문에 그렇게 했다"고 해 놓고 어떤 관계인지 대지 않으면 근거가 아니다.
            // 성향도 마찬가지다. 이 검사가 없으면 primaryReason이 아무 의미 없는 라벨로 전락한다.
            if (reason == "relationship" && relationshipResolved == null)
            {
                failureReason = "RelationshipReasonWithoutTarget";
                return false;
            }
            if (reason == "profile" && profileResolved == null)
            {
                failureReason = "ProfileReasonWithoutTag";
                return false;
            }

            grounds = new JudgmentGrounds(reason, profileResolved, relationshipResolved);
            return true;
        }
    }
}
