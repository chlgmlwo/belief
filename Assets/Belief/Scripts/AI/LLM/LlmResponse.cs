using System;

namespace Belief.AI.LLM
{
    /// <summary>
    /// 고정 JSON 응답 형식을 그대로 반영한 DTO. UnityEngine.JsonUtility로 역직렬화하므로
    /// 필드명이 JSON 키와 정확히 일치해야 한다. reason/confidence 등은 이후 이 클래스에
    /// 필드만 추가하면 확장된다(ResponseParser의 필수 검증 대상만 아니면 하위 호환 유지).
    /// </summary>
    [Serializable]
    public class LlmResponse
    {
        public string action;
        public string dialogue;

        // ── 판단 근거(1단계) ──────────────────────────────────────────────────────
        // 자유 장문이 아니라 Unity가 제공한 값만 되돌려 받는다 - 지어낸 인물/태그를 걸러내기 위함.
        // JsonUtility는 없는 키를 조용히 무시하고 빈 문자열로 두므로, 구버전 응답이 와도 예외는
        // 나지 않고 검증 단계에서 "MissingPrimaryReason"으로 걸린다.
        public string primaryReason;
        public string profileInfluence;
        public string relationshipInfluence;
    }

    /// <summary>이동 판단 전용 응답 DTO. destination은 [이동 후보] 목록의 locationId 중 하나이거나,
    /// 이동하지 않겠다는 명시적 의사표시인 "stay"여야 한다. 근거 3필드는 행동 판단과 같은 규칙으로
    /// 검증하지만, 별도 호출이라 같은 턴에도 행동 쪽과 primaryReason이 다를 수 있다(정상).</summary>
    [Serializable]
    public class LlmMoveResponse
    {
        public string destination;
        public string primaryReason;
        public string profileInfluence;
        public string relationshipInfluence;
    }
}
