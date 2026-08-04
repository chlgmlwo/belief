using System;
using UnityEngine;

namespace Belief.Presentation.HUD
{
    /// <summary>작전 결과 화면은 성공/실패 아트(작전 성공UI.png / 작전 실패 UI.png)의 구성이 서로
    /// 완전히 다르다 - 리포트 카드가 오른쪽/왼쪽, 태그가 중앙우측/우상단, 진행 버튼이 우하단(NEXT)/
    /// 좌하단(RETRY)으로 뒤집힌다. 그래서 성공용·실패용 하이어라키를 따로 두는 대신 **같은 텍스트
    /// 오브젝트를 결과에 따라 옮겨 쓴다**(텍스트 배선이 두 벌로 갈라지지 않는다).
    ///
    /// 좌표는 전부 아트 원본 픽셀을 실측해 캔버스 좌표로 옮긴 값이다 - 아트는 원본 크기
    /// 1607x1057 그대로 캔버스(1920x1080) 정중앙에 놓이므로
    /// <c>캔버스좌표 = (픽셀x - 803.5, 528.5 - 픽셀y)</c> 로 변환된다.</summary>
    public class ResultScreenLayout : MonoBehaviour
    {
        [Serializable]
        public struct Slot
        {
            public RectTransform target;
            public Vector2 successPos;
            public Vector2 failurePos;

            /// <summary>해당 결과에서는 아예 숨긴다 - 예: 실패 아트는 우상단이 태그로 차 있어
            /// "NO. 001" 자리가 없다.</summary>
            public bool hideOnSuccess;
            public bool hideOnFailure;
        }

        [SerializeField] Slot[] slots;

        public void Apply(bool won)
        {
            if (slots == null) return;
            foreach (var slot in slots)
            {
                if (slot.target == null) continue;
                bool hidden = won ? slot.hideOnSuccess : slot.hideOnFailure;
                slot.target.gameObject.SetActive(!hidden);
                if (hidden) continue;
                slot.target.anchoredPosition = won ? slot.successPos : slot.failurePos;
            }
        }
    }
}
