using System;
using UnityEngine;

namespace Belief.Data
{
    /// <summary>
    /// "게임 방법" 설명 페이지 모음 - 메인 메뉴의 [게임 방법]과 게임 중 [?] 버튼이 동일한 데이터를
    /// 읽어 같은 내용을 보여준다(설명을 두 곳에 중복 작성하지 않기 위함). 단일 Resources 에셋으로 관리.
    /// </summary>
    [CreateAssetMenu(fileName = "HowToPlayData", menuName = "Belief/How To Play Data", order = 21)]
    public class HowToPlayData : ScriptableObject
    {
        public HowToPlayPage[] pages;
    }

    [Serializable]
    public class HowToPlayPage
    {
        public string title;
        [TextArea(3, 8)] public string body;
    }
}
