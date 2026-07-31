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
    }

    /// <summary>이동 판단 전용 응답 DTO. destination은 [이동 후보] 목록의 locationId 중 하나이거나,
    /// 이동하지 않겠다는 명시적 의사표시인 "stay"여야 한다.</summary>
    [Serializable]
    public class LlmMoveResponse
    {
        public string destination;
    }
}
