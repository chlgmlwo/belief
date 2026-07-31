using System;
using System.Collections.Generic;

namespace Belief.Data.NPC
{
    /// <summary>NPC_*_Runtime.json 및 npc_runtime_initial.json 배열 원소의 1:1 직렬화 대상.
    /// 필드명은 JSON 키와 동일하게 맞춘다. 읽기 전용 초기 데이터 - 게임 중 이 인스턴스를
    /// 직접 수정하지 않고 NpcState로 깊은 복사해서 사용한다.</summary>
    [Serializable]
    public class NpcRuntimeDto
    {
        public int schemaVersion;
        public string npcId;
        public NpcRuntimeStatusDto runtimeStatus;
        public NpcGoalDto currentGoal;
        public NpcMoveDto currentMove;
        public List<NpcBeliefStateDto> beliefStates;
        public NpcWorkingMemoryDto workingMemory;
        public List<NpcRelationshipStateDto> relationshipStates;
    }

    [Serializable]
    public class NpcRuntimeStatusDto
    {
        public string currentStageId;
        public string currentLocationId;
        public bool isActive;
        public bool isAvailable;
        public int lastUpdatedTurn;
    }

    [Serializable]
    public class NpcGoalDto
    {
        public string goalId;
        public string goalType;
        public string reasonInformationId;
        public string targetLocationId;
        public string status;
    }

    [Serializable]
    public class NpcMoveDto
    {
        public string targetLocationId;
        public string reasonGoalId;
        public string status;
        public int startedTurn;
    }

    [Serializable]
    public class NpcBeliefStateDto
    {
        public string informationId;
        public string currentLevel;
        public string previousLevel;
        public string sourceId;
        public List<string> evidenceIds;
        public int lastUpdatedTurn;
        public bool isInitialBelief;
    }

    [Serializable]
    public class NpcWorkingMemoryDto
    {
        public List<string> knownInformationIds;
        public List<string> recentInformationIds;
        public List<string> recentEventIds;
        public List<string> observedNpcIds;
    }

    [Serializable]
    public class NpcRelationshipStateDto
    {
        public string targetType;
        public string targetId;
        public string trust;
        public string intimacy;
        public string influence;
        public int trustDelta;
        public int intimacyDelta;
        public int influenceDelta;
        public bool initializedFromProfile;
    }
}
