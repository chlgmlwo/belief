using System.IO;
using UnityEditor;
using UnityEngine;
using Belief.Data;

namespace Belief.EditorTools
{
    /// <summary>
    /// "정보 카드 콘텐츠 기획서 (30장)" 기준으로 InformationData/InfoSourceData/InformationCardData를
    /// 생성하고 CardPool_Default를 기획서의 30장으로 재구성하는 반복 실행 가능한 Editor 전용 도구.
    /// ID(informationId/sourceId/cardId) 기준으로 이미 존재하는 자산은 재사용하고 덮어쓰지 않는다 -
    /// 여러 번 실행해도 자산이 중복 생성되지 않는다. 런타임 빌드에는 포함되지 않는다(Editor 폴더).
    /// </summary>
    public static class InformationCardGenerator
    {
        const string InformationFolder = "Assets/Belief/Data/Information";
        const string SourcesFolder = "Assets/Belief/Data/Sources";
        const string CardsFolder = "Assets/Belief/Data/Cards";
        const string DefaultPoolPath = "Assets/Belief/Data/CardPool_Default.asset";

        struct SourceDef
        {
            public string Key;
            public string Id;
            public string DisplayName;

            public SourceDef(string key, string id, string displayName)
            {
                Key = key;
                Id = id;
                DisplayName = displayName;
            }
        }

        struct CardDef
        {
            public string Id;
            public string Title;
            public InfoCardType Type;
            public string CategoryCode;
            public string CategoryName;
            public string SourceKey;
            public string Text;
            public string[] Tags;

            public CardDef(string id, string title, InfoCardType type, string categoryCode, string categoryName,
                string sourceKey, string text, string[] tags)
            {
                Id = id;
                Title = title;
                Type = type;
                CategoryCode = categoryCode;
                CategoryName = categoryName;
                SourceKey = sourceKey;
                Text = text;
                Tags = tags;
            }
        }

        static readonly SourceDef[] Sources =
        {
            new SourceDef("Patrol", "src_patrol", "순찰대"),
            new SourceDef("AnonymousTip", "src_anonymous_tip", "익명 제보"),
            new SourceDef("Traveler", "src_traveler", "여행자"),
            new SourceDef("MerchantGuild", "src_merchant_guild", "상인 길드"),
            new SourceDef("AdminOffice", "src_admin_office", "행정기관"),
            new SourceDef("Market", "src_market", "시장"),
            new SourceDef("NobleHouse", "src_noble_house", "귀족가"),
            new SourceDef("Tavern", "src_tavern", "주점"),
            new SourceDef("Religious", "src_religious_facility", "종교시설"),
        };

        static readonly CardDef[] Cards =
        {
            new CardDef("C-SEC-01", "야간 순찰 인원 감소", InfoCardType.Spread, "SEC", "SECURITY", "Patrol",
                "상업지구 인근에서 야간 순찰 인원이 줄었다는 이야기가 돈다.", new[] { "SECURITY", "PATROL", "NIGHT" }),
            new CardDef("C-SEC-02", "뒷조사 정황", InfoCardType.Deliver, "SEC", "SECURITY", "AnonymousTip",
                "누군가 특정 인물의 뒷조사를 하고 있다는 제보가 들어왔다.", new[] { "SECURITY", "SURVEILLANCE", "THREAT" }),
            new CardDef("C-SEC-03", "검문소 경비 허술", InfoCardType.Spread, "SEC", "SECURITY", "Traveler",
                "도시 외곽 검문소의 경비가 허술해졌다는 이야기가 퍼진다.", new[] { "SECURITY", "CHECKPOINT", "OUTSKIRTS" }),

            new CardDef("C-MIL-01", "국경 병력 이동", InfoCardType.Spread, "MIL", "MILITARY", "Patrol",
                "국경 인근에서 병력이 이동 중이라는 소식이 전해진다.", new[] { "MILITARY", "TROOP_MOVEMENT", "BORDER" }),
            new CardDef("C-MIL-02", "무기 거래 급증", InfoCardType.Deliver, "MIL", "MILITARY", "MerchantGuild",
                "무기 거래량이 최근 급증했다는 정보가 있다.", new[] { "MILITARY", "WEAPON_TRADE", "ECONOMY_LINK" }),
            new CardDef("C-MIL-03", "예비군 소집설", InfoCardType.Spread, "MIL", "MILITARY", "AdminOffice",
                "예비군 소집 명령이 곧 내려질 것이라는 이야기가 돈다.", new[] { "MILITARY", "CONSCRIPTION", "MOBILIZATION" }),

            new CardDef("C-ECO-01", "곡물 가격 폭등", InfoCardType.Spread, "ECO", "ECONOMY", "MerchantGuild",
                "상업지구의 곡물 가격이 폭등했다는 소식이 퍼진다.", new[] { "ECONOMY", "PRICE_HIKE", "GRAIN" }),
            new CardDef("C-ECO-02", "상인의 대규모 부채", InfoCardType.Deliver, "ECO", "ECONOMY", "Market",
                "특정 상인이 대규모 부채를 지고 있다는 이야기가 들린다.", new[] { "ECONOMY", "DEBT", "MERCHANT" }),
            new CardDef("C-ECO-03", "교역로 단절 위기", InfoCardType.Spread, "ECO", "ECONOMY", "Traveler",
                "인근 지역과의 교역로가 곧 끊길 수 있다는 소문이 돈다.", new[] { "ECONOMY", "TRADE_ROUTE", "DISRUPTION" }),

            new CardDef("C-POL-01", "행정기관 권력 다툼", InfoCardType.Spread, "POL", "POLITICS", "NobleHouse",
                "행정기관 내부에서 권력 다툼이 벌어지고 있다는 이야기가 퍼진다.", new[] { "POLITICS", "POWER_STRUGGLE", "ADMIN" }),
            new CardDef("C-POL-02", "반역 혐의 감시", InfoCardType.Deliver, "POL", "POLITICS", "AnonymousTip",
                "특정 인물이 반역 혐의로 감시받고 있다는 제보가 있다.", new[] { "POLITICS", "TREASON", "SURVEILLANCE" }),
            new CardDef("C-POL-03", "세금 정책 불만", InfoCardType.Spread, "POL", "POLITICS", "Patrol",
                "새로운 세금 정책에 대한 불만이 곳곳에서 터져나온다.", new[] { "POLITICS", "TAX_POLICY", "UNREST" }),

            new CardDef("C-CRI-01", "소매치기 증가", InfoCardType.Spread, "CRI", "CRIME", "Tavern",
                "상업지구에서 소매치기가 늘었다는 소문이 돈다.", new[] { "CRIME", "THEFT", "MARKET" }),
            new CardDef("C-CRI-02", "밀수 연루 제보", InfoCardType.Deliver, "CRI", "CRIME", "AnonymousTip",
                "특정 인물이 밀수에 연루되어 있다는 제보가 들어왔다.", new[] { "CRIME", "SMUGGLING", "INFORMANT" }),
            new CardDef("C-CRI-03", "정체불명 범죄 조직", InfoCardType.Spread, "CRI", "CRIME", "Traveler",
                "도시 외곽에 정체불명의 범죄 조직이 자리 잡았다는 이야기가 퍼진다.", new[] { "CRIME", "GANG", "OUTSKIRTS" }),

            new CardDef("C-REL-01", "이단 의심 집회", InfoCardType.Spread, "REL", "RELIGION", "Religious",
                "종교시설에서 이단으로 의심되는 집회가 열렸다는 소문이 돈다.", new[] { "RELIGION", "HERESY", "GATHERING" }),
            new CardDef("C-REL-02", "교리 위반 발언", InfoCardType.Deliver, "REL", "RELIGION", "AnonymousTip",
                "특정 성직자가 교리에 어긋나는 발언을 했다는 제보가 있다.", new[] { "RELIGION", "DOCTRINE", "CLERGY" }),
            new CardDef("C-REL-03", "대규모 기부설", InfoCardType.Spread, "REL", "RELIGION", "Market",
                "종교시설이 곧 대규모 기부를 받을 것이라는 이야기가 퍼진다.", new[] { "RELIGION", "DONATION", "INFLUENCE" }),

            new CardDef("C-PUB-01", "주민 불안감 확산", InfoCardType.Spread, "PUB", "PUBLIC", "Tavern",
                "상업지구 주민들 사이에서 불안감이 커지고 있다는 이야기가 돈다.", new[] { "PUBLIC", "ANXIETY", "MARKET" }),
            new CardDef("C-PUB-02", "인물에 대한 신망", InfoCardType.Deliver, "PUB", "PUBLIC", "Traveler",
                "특정 인물이 시민들 사이에서 신망을 얻고 있다는 이야기가 들린다.", new[] { "PUBLIC", "REPUTATION", "TRUST" }),
            new CardDef("C-PUB-03", "통행금지 불만", InfoCardType.Spread, "PUB", "PUBLIC", "Patrol",
                "도시 곳곳에서 통행금지에 대한 불만이 커지고 있다.", new[] { "PUBLIC", "CURFEW", "DISCONTENT" }),

            new CardDef("C-NOB-01", "은밀한 혼담", InfoCardType.Spread, "NOB", "NOBILITY", "NobleHouse",
                "귀족가 사이에서 은밀한 혼담이 오가고 있다는 소문이 돈다.", new[] { "NOBILITY", "MARRIAGE", "ALLIANCE" }),
            new CardDef("C-NOB-02", "귀족의 재정난", InfoCardType.Deliver, "NOB", "NOBILITY", "AnonymousTip",
                "특정 귀족이 재정난에 시달리고 있다는 제보가 있다.", new[] { "NOBILITY", "FINANCE", "SCANDAL" }),
            new CardDef("C-NOB-03", "사치품 소비 급증", InfoCardType.Spread, "NOB", "NOBILITY", "MerchantGuild",
                "귀족가에서 사치품 소비가 급증했다는 이야기가 퍼진다.", new[] { "NOBILITY", "LUXURY", "SPENDING" }),

            new CardDef("C-ADM-01", "민원 처리 지연", InfoCardType.Spread, "ADM", "ADMIN", "AdminOffice",
                "행정기관의 인력 부족으로 민원 처리가 지연되고 있다는 소문이 돈다.", new[] { "ADMIN", "DELAY", "STAFFING" }),
            new CardDef("C-ADM-02", "관리의 뇌물 수수", InfoCardType.Deliver, "ADM", "ADMIN", "AnonymousTip",
                "특정 관리가 뇌물을 받았다는 제보가 들어왔다.", new[] { "ADMIN", "BRIBERY", "CORRUPTION" }),
            new CardDef("C-ADM-03", "새 통행증 제도", InfoCardType.Spread, "ADM", "ADMIN", "Traveler",
                "새로운 통행증 제도가 곧 시행될 것이라는 이야기가 퍼진다.", new[] { "ADMIN", "PERMIT_SYSTEM", "POLICY" }),

            new CardDef("C-DIS-01", "전염병 징후", InfoCardType.Spread, "DIS", "DISASTER", "Traveler",
                "도시 외곽에서 원인 모를 전염병 징후가 나타났다는 소문이 돈다.", new[] { "DISASTER", "EPIDEMIC", "OUTSKIRTS" }),
            new CardDef("C-DIS-02", "건물 붕괴 위험", InfoCardType.Deliver, "DIS", "DISASTER", "Patrol",
                "특정 지역의 건물이 붕괴 위험에 처했다는 제보가 있다.", new[] { "DISASTER", "COLLAPSE", "HAZARD" }),
            new CardDef("C-DIS-03", "화재 위험 증가", InfoCardType.Spread, "DIS", "DISASTER", "Tavern",
                "상업지구 인근에서 화재 위험이 커지고 있다는 이야기가 퍼진다.", new[] { "DISASTER", "FIRE_RISK", "MARKET" }),
        };

        [MenuItem("Belief/Content/Generate Information Cards (30)")]
        public static void Generate()
        {
            Directory.CreateDirectory(InformationFolder);
            Directory.CreateDirectory(SourcesFolder);
            Directory.CreateDirectory(CardsFolder);

            int sourcesCreated = 0, sourcesReused = 0;
            int infosCreated = 0, infosReused = 0;
            int cardsCreated = 0, cardsReused = 0;

            var sourceAssets = new System.Collections.Generic.Dictionary<string, InfoSourceData>();
            foreach (var def in Sources)
            {
                string path = $"{SourcesFolder}/Source_{def.Key}.asset";
                var existing = AssetDatabase.LoadAssetAtPath<InfoSourceData>(path);
                if (existing != null)
                {
                    sourceAssets[def.Key] = existing;
                    sourcesReused++;
                    continue;
                }

                var source = ScriptableObject.CreateInstance<InfoSourceData>();
                source.sourceId = def.Id;
                source.displayName = def.DisplayName;
                AssetDatabase.CreateAsset(source, path);
                sourceAssets[def.Key] = source;
                sourcesCreated++;
            }

            var cardAssets = new InformationCardData[Cards.Length];
            for (int i = 0; i < Cards.Length; i++)
            {
                var def = Cards[i];
                string fileSafeId = def.Id.Replace("-", "_");

                string infoPath = $"{InformationFolder}/Info_{fileSafeId}.asset";
                var info = AssetDatabase.LoadAssetAtPath<InformationData>(infoPath);
                if (info == null)
                {
                    info = ScriptableObject.CreateInstance<InformationData>();
                    info.informationId = $"info_{fileSafeId.ToLowerInvariant()}";
                    info.title = def.Title;
                    info.description = def.Text;
                    info.tags = def.Tags;
                    info.categoryId = def.CategoryName;
                    AssetDatabase.CreateAsset(info, infoPath);
                    infosCreated++;
                }
                else
                {
                    infosReused++;
                }

                string cardPath = $"{CardsFolder}/Card_{fileSafeId}.asset";
                var card = AssetDatabase.LoadAssetAtPath<InformationCardData>(cardPath);
                if (card == null)
                {
                    card = ScriptableObject.CreateInstance<InformationCardData>();
                    card.cardId = def.Id;
                    card.information = info;
                    card.source = sourceAssets[def.SourceKey];
                    card.cardType = def.Type;
                    AssetDatabase.CreateAsset(card, cardPath);
                    cardsCreated++;
                }
                else
                {
                    cardsReused++;
                }

                cardAssets[i] = card;
            }

            var pool = AssetDatabase.LoadAssetAtPath<InformationCardPoolData>(DefaultPoolPath);
            if (pool == null)
            {
                pool = ScriptableObject.CreateInstance<InformationCardPoolData>();
                AssetDatabase.CreateAsset(pool, DefaultPoolPath);
            }
            pool.cards = cardAssets;
            EditorUtility.SetDirty(pool);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[InformationCardGenerator] 완료 - Source 생성 {sourcesCreated}/재사용 {sourcesReused}, " +
                $"Information 생성 {infosCreated}/재사용 {infosReused}, Card 생성 {cardsCreated}/재사용 {cardsReused}, " +
                $"CardPool_Default.cards = {pool.cards.Length}장");
        }
    }
}
