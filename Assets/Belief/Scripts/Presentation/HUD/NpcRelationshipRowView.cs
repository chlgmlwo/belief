using TMPro;
using UnityEngine;

namespace Belief.Presentation.HUD
{
    /// <summary>NPC 관계도 한 줄(대상명 Bold 15 헤더 + 설명 Regular 10, 세로 배치)의 View -
    /// CardTileView와 같은 패턴. 두 줄의 폰트/크기가 달라 하나의 TMP_Text로는 표현할 수 없어
    /// 헤더/본문 두 자식으로 나눈다(기존 AddNpcRelationshipRow와 동일한 구조, 이제 프리팹에 굽는다).</summary>
    public class NpcRelationshipRowView : MonoBehaviour
    {
        [SerializeField] TMP_Text header;
        [SerializeField] TMP_Text desc;

        public void Bind(string headerText, string descText)
        {
            if (header != null) header.text = headerText;
            if (desc != null) desc.text = descText;
        }
    }
}
