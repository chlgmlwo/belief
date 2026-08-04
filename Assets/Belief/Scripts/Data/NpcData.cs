using UnityEngine;

namespace Belief.Data
{
    public abstract class NpcData : ScriptableObject
    {
        [Header("Identity")]
        public string npcId;
        public string displayName;
        public LocationData homeLocation;

        [Header("Profile (조사 파일 표시용, Frozen - 읽기 전용 원문)")]
        public string gender;
        public string job;
        public string affiliation;
        [TextArea(1, 3)] public string gameplayRoleSummary;
        [TextArea(1, 2)] public string[] aiNotes;

        /// <summary>조사 파일(Profile 패널) 하단 History 칸에 보여줄 인물 배경 서사 -
        /// NPC 기획서 "2. 백스토리·가치관" 절을 History 칸 크기(280×125)에 맞게 간추린 문단이다.
        /// 예전에는 이 칸을 gameplayRoleSummary + aiNotes를 빈 줄로 이어 붙여 채웠는데,
        /// (1) gameplayRoleSummary는 "1스테이지 클리어 목표 대상" 같은 개발용 메모라 플레이어에게
        /// 보일 내용이 아니고 (2) 빈 줄 문단 구분만으로 칸 높이의 절반 이상을 잡아먹어 넘쳤다.
        /// 시안(`UI/Guides/[배치가이드] ... 프로필.jpg`)의 History도 줄바꿈 없는 문단형 산문이다.</summary>
        [TextArea(3, 8)] public string backstory;

        /// <summary>NPC 기획서 "1.2 특성 태그" 표를 그대로 옮긴 값 - # 포함 원문 그대로 저장한다.
        /// BeliefSystem 판정 로직은 건드리지 않고 조사 파일(Profile 패널) 표시 전용이다.</summary>
        [Header("특성 태그 (조사 파일 '성격 태그' 표시용, Frozen 원문)")]
        public string judgmentTendencyTag;
        public string priorityTag;
        public string sensitiveInfoTag;
        public string relationTendencyTag;
        public string trustJudgmentTag;

        [Header("Personality (BeliefSystem 전용, 플레이어 비공개)")]
        [Range(0f, 1f)] public float trustBias = 0.5f;
        [Range(0f, 1f)] public float skepticism = 0.5f;

        [Header("Visual")]
        [Tooltip("NpcActorView의 사진 프레임 안에 표시할 실제 캐릭터 스프라이트 - 비어 있으면 기존 placeholder 단색으로 대체된다.")]
        public Sprite characterPhoto;
        [Tooltip("이동(AnimateTo) 중 재생할 걷기 사이클 프레임들 - 순서대로 재생 후 반복한다. 비어 있으면 이동 중에도 characterPhoto를 그대로 유지한다(하위 호환).")]
        public Sprite[] walkFrames;

        public abstract NpcRank Rank { get; }

        /// <summary>Frozen AI Profile에 정의된 장기 목표(있는 경우). NpcState가 타입 분기 없이
        /// 공통으로 읽을 수 있도록 base에 가상 프로퍼티로 둔다 - 목표가 없는 NPC 유형은 null을 반환한다.</summary>
        public virtual string InitialGoal => null;
    }
}
