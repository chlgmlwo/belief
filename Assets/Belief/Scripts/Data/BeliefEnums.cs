namespace Belief.Data
{
    public enum BeliefState
    {
        Unknown,
        NeedsVerification,
        Doubtful,
        Plausible,
        Trusted,
        Denied
    }

    public enum InfoCardType
    {
        Spread,
        Deliver
    }

    // NpcRank(Major/Minor)는 제거됐다. NPC를 등급으로 나누던 시절에는 Minor가 기억 없이 판단하고
    // 이동도 무작위 배회로 처리됐는데, "AI가 정보를 받고 각 NPC가 해석해서 행동한다"는 이 게임의
    // 핵심에 어긋나서 전원을 같은 경로로 통일했다. 등급으로 분기하고 싶어지면 그 판단 차이를
    // 데이터(성향 태그/신뢰 편향)로 표현할 것.

    /// <summary>장소·NPC 기획서 2.2의 장소 특성 항목. LocationData의 콘텐츠 필드 전용이며
    /// 아직 어떤 시스템도 이 값을 읽어 수치를 계산하지 않는다(순수 데이터).</summary>
    public enum LocationSpreadSpeed
    {
        Unspecified = 0,
        Low,
        Medium,
        High
    }

    public enum LocationNpcDensity
    {
        Unspecified = 0,
        Low,
        Medium,
        High
    }

    public enum LocationSensitiveInfoType
    {
        Unspecified = 0,
        Rumor,
        Intelligence,
        FactualInformation,
        OrderDocument,
        CriminalDeal,
        ForgedDocument
    }

    public enum LocationAccessType
    {
        Unspecified = 0,
        Public,
        GuardRestricted,
        StewardRestricted,
        Restricted
    }

    public enum LocationCredibilityModifier
    {
        Unspecified = 0,
        Low,
        Neutral,
        High,
        VeryHigh
    }

    /// <summary>InformationData가 담고 있는 정보의 성격(무엇을 어떻게 전하는 정보인가) - LocationData의
    /// sensitiveInformationType(장소가 어떤 정보에 민감한가)과는 별개 enum이지만, 값 이름을 1:1로
    /// 맞춰서 관리한다(LocationMechanicsSettings.IsSensitiveTypeMatch가 이름으로 비교). 스테이지가
    /// 늘어나 새 유형이 필요해지면 두 enum에 같은 이름을 동시에 추가한다 - 기존 값의 정수는 절대
    /// 바꾸지 않는다(이미 저장된 카드 자산이 조용히 다른 값으로 바뀌는 것을 막기 위함).
    /// EconomicIntelligence/ForgedDocument/CriminalDeal은 실제 1스테이지 카드 30장을 내용 기준으로
    /// 재분류하며 필요해 뒤에 추가되었다(기존 4개 값의 정수는 그대로 유지) - LocationSensitiveInfoType
    /// 쪽은 이 3개를 sensitiveInformationType으로 쓰는 장소가 아직 없어 동기화하지 않았다.</summary>
    public enum InformationType
    {
        Unspecified = 0,
        Rumor,
        Intelligence,
        FactualInformation,
        OrderDocument,
        EconomicIntelligence,
        ForgedDocument,
        CriminalDeal
    }
}
