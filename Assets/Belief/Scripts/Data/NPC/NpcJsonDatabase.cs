using System;
using System.Collections.Generic;

namespace Belief.Data.NPC
{
    /// <summary>npc_runtime_initial.json(통합 런타임 파일) 루트의 1:1 직렬화 대상.</summary>
    [Serializable]
    public class NpcRuntimeDatabaseDto
    {
        public int schemaVersion;
        public List<NpcRuntimeDto> npcRuntimeStates;
    }

    /// <summary>npc_profile_manifest.json 루트의 1:1 직렬화 대상. Profile 폴더 파일 열거가
    /// 빌드 플랫폼에서 불안정할 수 있어, NPC ID 목록을 코드에 하드코딩하지 않고 이 manifest를
    /// 통해 로드 대상 파일명을 얻는다.</summary>
    [Serializable]
    public class NpcProfileManifestDto
    {
        public int schemaVersion;
        public List<string> profileFiles;
    }
}
