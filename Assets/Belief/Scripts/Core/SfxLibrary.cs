using UnityEngine;

namespace Belief.Core
{
    /// <summary>효과음을 "파일 이름"이 아니라 "언제 나는 소리인지"로 부른다 - 나중에 소리를 바꿔도
    /// 부르는 쪽은 손대지 않는다.</summary>
    public enum Sfx
    {
        None,
        /// <summary>버튼을 눌렀을 때(모든 버튼 공통).</summary>
        Click,
        /// <summary>손패에서 카드를 골랐을 때.</summary>
        CardSelect,
        /// <summary>정보를 실제로 내보냈을 때.</summary>
        CardPlay,
        /// <summary>NPC의 믿음이 갱신됐을 때.</summary>
        BeliefChange,
        /// <summary>NPC가 다른 장소로 옮겨 갈 때.</summary>
        NpcWalk,
        /// <summary>지도의 장소에 커서를 올려 정보 쪽지가 뜰 때.</summary>
        LocationHover,
        /// <summary>Log/Profile 문서를 펼칠 때.</summary>
        DocumentOpen,
        /// <summary>일시정지 메뉴를 여닫을 때.</summary>
        PauseToggle,
        /// <summary>브리핑에서 작전을 개시할 때.</summary>
        OperationStart,
        /// <summary>작전 성공 리포트가 뜰 때.</summary>
        ResultSuccess,
        /// <summary>작전 실패 리포트가 뜰 때.</summary>
        ResultFailure,
        /// <summary>리포트를 넘길 때.</summary>
        PageTurn,
        /// <summary>Log/Profile 문서를 도로 집어넣을 때. 새 값은 반드시 끝에 붙인다 -
        /// 중간에 끼우면 이미 저장된 라이브러리의 항목들이 통째로 다른 소리를 가리킨다.</summary>
        DocumentClose,
    }

    /// <summary>어느 소리를 쓸지만 담는 자료. <see cref="SfxPlayer"/>가 Resources에서 이름으로 읽으므로
    /// 반드시 Resources 폴더 안에 `SfxLibrary` 이름으로 있어야 한다(BgmLibrary와 같은 방식).</summary>
    [CreateAssetMenu(fileName = "SfxLibrary", menuName = "Belief/Audio/SFX Library")]
    public class SfxLibrary : ScriptableObject
    {
        [System.Serializable]
        public class Entry
        {
            public Sfx kind;
            public AudioClip clip;

            /// <summary>소리마다 녹음 크기가 제각각이라(실측 24배 차이) 한 곳에서 균형을 잡는다.</summary>
            [Range(0f, 4f)] public float volume = 1f;

            /// <summary>클립의 어디부터 재생할지. 앞에 붙은 무음을 건너뛰기 위한 값이다 - 버튼 소리가
            /// 0.15초 늦게 울리면 클릭이 씹힌 것처럼 느껴진다.</summary>
            public float startTime;

            /// <summary>몇 초만 재생할지. 0이면 끝까지. 원본이 통짜 녹음이라 54초짜리도 있어서
            /// 한 번 쓰고 마는 소리는 여기서 잘라 준다.</summary>
            public float maxLength;
        }

        public Entry[] entries;

        public Entry Find(Sfx kind)
        {
            if (entries != null)
                foreach (var e in entries)
                    if (e != null && e.kind == kind && e.clip != null)
                        return e;
            return null;
        }
    }
}
