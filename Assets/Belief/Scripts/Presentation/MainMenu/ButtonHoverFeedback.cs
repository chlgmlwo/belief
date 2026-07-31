using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Belief.Presentation.MainMenu
{
    /// <summary>버튼에 마우스를 올렸을 때 배경색을 살짝 밝게 트윈하는 최소한의 Hover 연출 -
    /// 새 Animation 시스템이 아니라 Color.Lerp 코루틴 하나로 처리한다.</summary>
    public class ButtonHoverFeedback : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        const float TweenDuration = 0.15f;

        Image target;
        Color normalColor;
        Color hoverColor;
        Coroutine routine;

        public void Init(Image target, Color normalColor, Color hoverColor)
        {
            this.target = target;
            this.normalColor = normalColor;
            this.hoverColor = hoverColor;
        }

        public void OnPointerEnter(PointerEventData eventData) => StartTween(hoverColor);
        public void OnPointerExit(PointerEventData eventData) => StartTween(normalColor);

        void StartTween(Color to)
        {
            if (routine != null) StopCoroutine(routine);
            routine = StartCoroutine(TweenRoutine(to));
        }

        IEnumerator TweenRoutine(Color to)
        {
            Color from = target.color;
            float t = 0f;
            while (t < TweenDuration)
            {
                t += Time.deltaTime;
                target.color = Color.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / TweenDuration));
                yield return null;
            }
            target.color = to;
        }
    }
}
