using System;
using System.Collections.Generic;

namespace Belief.Data.NPC
{
    /// <summary>NPC_*.json(Profile)의 1:1 직렬화 대상. 필드명은 JSON 키와 대소문자까지 동일하게
    /// 맞춰 JsonUtility가 별도 매핑 설정 없이 그대로 읽을 수 있게 한다. 읽기 전용 원본 데이터.</summary>
    [Serializable]
    public class NpcProfileDto
    {
        public int schemaVersion;
        public string npcId;
        public string displayName;
        public string stageId;
        public NpcBasicInfoDto basicInfo;
        public NpcGameplayRoleDto gameplayRole;
        public List<string> dailyRoles;
        public List<string> defaultGoals;
        public List<string> values;
        public NpcTraitsDto traits;
        public List<NpcInformationSensitivityDto> informationSensitivities;
        public List<NpcInitialBeliefDto> initialBeliefs;
        public NpcLocationPreferenceDto locationPreference;
        public NpcMovementDto movement;
        public List<NpcBeliefDialogueDto> beliefDialogues;
        public List<NpcRelationshipDto> relationships;
        public List<string> aiNotes;
        public NpcRuleOnlyDto ruleOnly;
        public string sourceDocument;
    }

    [Serializable]
    public class NpcBasicInfoDto
    {
        public string gender;
        public string job;
        public string affiliation;
        public string defaultLocationId;
    }

    [Serializable]
    public class NpcGameplayRoleDto
    {
        public string role;
        public List<string> functions;
        public string playerRelationship;
    }

    [Serializable]
    public class NpcTraitsDto
    {
        public string caution;
        public string curiosity;
        public string altruism;
        public string suspicion;
        public string responsibility;
    }

    [Serializable]
    public class NpcInformationSensitivityDto
    {
        public string level;
        public List<string> categories;
    }

    [Serializable]
    public class NpcInitialBeliefDto
    {
        public string informationId;
        public string statement;
        public string initialLevel;
        public string displayLevel;
        public string description;
    }

    [Serializable]
    public class NpcLocationPreferenceDto
    {
        public List<string> preferredLocationIds;
        public List<string> avoidedLocationIds;
    }

    [Serializable]
    public class NpcMovementDto
    {
        public List<string> priorityLocationIds;
        public List<string> rules;
    }

    [Serializable]
    public class NpcBeliefDialogueDto
    {
        public string level;
        public string displayLevel;
        public string line;
    }

    [Serializable]
    public class NpcRelationshipDto
    {
        public string targetType;
        public string targetId;
        public string targetName;
        public string relationshipType;
        public string trust;
        public string intimacy;
        public string influence;
        public string description;
    }

    [Serializable]
    public class NpcRuleOnlyDto
    {
        public string judgmentTag;
        public string beliefChangeRule;
        public string offlineBehaviorRule;
        public List<string> offlineFixedLines;
        public string winLossRule;
    }
}
