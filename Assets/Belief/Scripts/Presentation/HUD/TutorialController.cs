using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Belief.Core;
using Belief.Data;
using Belief.Events;
using Belief.Presentation.World;

namespace Belief.Presentation.HUD
{
    /// <summary>
    /// 처음 플레이하는 사용자만을 위한 4단계 최소 안내. 전체 화면을 가리지 않는 작은 안내창과,
    /// 현재 필요한 UI 요소를 반복 Highlight하는 것만으로 구성된다 - 새 하이라이트 시스템을 만들지
    /// 않고 LocationSiteView/NpcActorView/CardTileView에 이미 있는 Highlight()를 주기적으로
    /// 다시 호출하는 방식으로 "은은하게 계속 빛나는" 느낌을 낸다.
    /// 각 단계에서 실제로 필요한 행동만 가능하도록 TargetingController의 선택적 필터 훅을 사용하고,
    /// 튜토리얼이 아닌 평소에는 그 훅이 전부 null이라 기존 자유 선택 동작이 전혀 바뀌지 않는다.
    /// 완료 여부는 PlayerPrefs에 저장해 두 번째 플레이부터는 다시 표시하지 않는다.
    /// </summary>
    public class TutorialController : MonoBehaviour
    {
        public const string CompletedPrefKey = "Belief_TutorialCompleted";
        public static bool IsCompleted => PlayerPrefs.GetInt(CompletedPrefKey, 0) == 1;

        static readonly Color PanelColor = new Color(0.09f, 0.12f, 0.10f, 0.95f);
        static readonly Color AccentColor = new Color(0.30f, 0.85f, 0.55f);
        static readonly Color MutedText = new Color(0.72f, 0.78f, 0.74f);
        static readonly Color FlashColor = new Color(0.95f, 0.85f, 0.30f);

        const float FadeDuration = 0.2f;
        const float PulseInterval = 0.8f;
        const float Step4LingerDuration = 2.4f;

        GameInstaller installer;
        TargetingController targeting;
        HudPresenter hud;
        WorldPresenter worldPresenter;

        GameObject guideGo;
        CanvasGroup guideCanvasGroup;
        TMP_Text stepLabelText;
        TMP_Text guideText;

        int step;
        Coroutine pulseRoutine;
        Action<CardSelectedEvent> cardSelectedHandler;
        Action<CardPlayedEvent> cardPlayedHandler;

        public void Begin(GameInstaller installer, TargetingController targeting, HudPresenter hud, Transform canvasParent, TMP_FontAsset font)
        {
            this.installer = installer;
            this.targeting = targeting;
            this.hud = hud;
            worldPresenter = FindFirstObjectByType<WorldPresenter>();

            BuildGuidePanel(canvasParent, font);

            cardSelectedHandler = _ => { if (step == 1) AdvanceToStep2(); };
            cardPlayedHandler = _ => { if (step == 3) AdvanceToStep4(); };
            installer.EventBus.Subscribe(cardSelectedHandler);
            installer.EventBus.Subscribe(cardPlayedHandler);
            targeting.PhaseChanged += OnTargetingPhaseChanged;

            EnterStep1();
        }

        void OnTargetingPhaseChanged()
        {
            if (step == 2 && targeting.Phase == TargetingPhase.AwaitingConfirm)
                AdvanceToStep3();
        }

        // ------------------------------------------------------------ 단계 진행

        void EnterStep1()
        {
            step = 1;
            targeting.CardSelectionAllowed = card => card.cardType == InfoCardType.Spread;
            targeting.LocationSelectionAllowed = _ => false;
            targeting.NpcSelectionAllowed = _ => false;
            targeting.DeliverAllowed = () => false;

            ShowGuide("STEP 1 / 4", "먼저 퍼뜨릴 정보를 선택해 보세요.\n정보는 사람들의 믿음을 바꾸는 시작점입니다.");
            RestartPulse(PulseCardTiles());
        }

        void AdvanceToStep2()
        {
            step = 2;
            targeting.CardSelectionAllowed = _ => false;
            targeting.LocationSelectionAllowed = _ => true;
            targeting.NpcSelectionAllowed = _ => false;
            targeting.DeliverAllowed = () => false;

            ShowGuide("STEP 2 / 4", "정보를 퍼뜨릴 장소를 선택해 보세요.\n같은 정보라도 어디에서 퍼지는지가 중요합니다.");
            RestartPulse(PulseLocations());
        }

        void AdvanceToStep3()
        {
            step = 3;
            targeting.CardSelectionAllowed = _ => false;
            targeting.LocationSelectionAllowed = _ => false;
            targeting.NpcSelectionAllowed = _ => false;
            targeting.DeliverAllowed = () => true;

            ShowGuide("STEP 3 / 4", "정보원에게 전달해 확산을 확정해 보세요.\n정보는 항상 정보원을 통해 유통됩니다.");
            RestartPulse(PulseDeliverButton());
        }

        /// <summary>실제 게임에서는 "정보원에게 전달" 버튼을 누르는 순간 전달이 곧바로 실행된다 -
        /// STEP 4는 별도 입력을 더 요구하지 않고 완료를 알리는 마무리 문구만 잠깐 보여준 뒤
        /// 튜토리얼을 끝낸다.</summary>
        void AdvanceToStep4()
        {
            step = 4;
            ClearFilters();
            StopPulse();

            ShowGuide("STEP 4 / 4", "준비가 끝났습니다.\n정보를 전달하고 사람들이 어떻게 행동하는지 지켜보세요.");
            StartCoroutine(EndAfterDelay(Step4LingerDuration));
        }

        /// <summary>안내창 Fade Out이 끝난 뒤에야 컴포넌트를 파괴한다 - Destroy(this)를 먼저 호출하면
        /// 그 위에서 막 시작한 Fade Out 코루틴이 끝나기도 전에 함께 중단되어, 안내창이 사라지지 않고
        /// 화면에 그대로 남는 문제가 있었다.</summary>
        IEnumerator EndAfterDelay(float seconds)
        {
            yield return new WaitForSeconds(seconds);
            yield return FadeOutGuideRoutine();
            FinalizeTutorial();
        }

        void FinalizeTutorial()
        {
            ClearFilters();
            StopPulse();

            targeting.PhaseChanged -= OnTargetingPhaseChanged;
            installer.EventBus.Unsubscribe(cardSelectedHandler);
            installer.EventBus.Unsubscribe(cardPlayedHandler);

            PlayerPrefs.SetInt(CompletedPrefKey, 1);
            PlayerPrefs.Save();

            Destroy(this);
        }

        void ClearFilters()
        {
            targeting.CardSelectionAllowed = null;
            targeting.LocationSelectionAllowed = null;
            targeting.NpcSelectionAllowed = null;
            targeting.DeliverAllowed = null;
        }

        void OnDestroy()
        {
            // 튜토리얼 도중 씬이 통째로 바뀌는 경우(비정상 종료 등) 필터가 걸린 채로 남지 않도록 방어.
            if (targeting != null) ClearFilters();
        }

        // ------------------------------------------------------------ Highlight 반복 재생 (기존 Highlight() 재사용)

        void RestartPulse(IEnumerator routine)
        {
            StopPulse();
            pulseRoutine = StartCoroutine(routine);
        }

        void StopPulse()
        {
            if (pulseRoutine == null) return;
            StopCoroutine(pulseRoutine);
            pulseRoutine = null;
        }

        IEnumerator PulseCardTiles()
        {
            while (true)
            {
                foreach (var tile in hud.OwnedCardTiles) tile.Highlight();
                yield return new WaitForSeconds(PulseInterval);
            }
        }

        IEnumerator PulseLocations()
        {
            while (true)
            {
                if (worldPresenter != null)
                    foreach (var view in worldPresenter.LocationViews.Values) view.Highlight();
                yield return new WaitForSeconds(PulseInterval);
            }
        }

        IEnumerator PulseDeliverButton()
        {
            while (true)
            {
                // 전달 확정 자리는 스테이지에 접선 지점이 있으면 지도 위 "전달" 태그로 옮겨간다 -
                // 그쪽을 먼저 보고, 없을 때만 예전 하단 버튼을 깜빡인다.
                var contact = hud.ContactPointView;
                if (contact != null)
                {
                    contact.FlashContactTag();
                }
                else
                {
                    var go = hud.DeliverButtonGo;
                    if (go != null && go.activeInHierarchy)
                    {
                        var img = go.GetComponent<Image>();
                        if (img != null) StartCoroutine(FlashImage(img, AccentColor));
                    }
                }
                yield return new WaitForSeconds(PulseInterval);
            }
        }

        IEnumerator FlashImage(Image img, Color baseColor)
        {
            img.color = FlashColor;
            const float duration = 0.3f;
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                img.color = Color.Lerp(FlashColor, baseColor, t / duration);
                yield return null;
            }
            img.color = baseColor;
        }

        // ------------------------------------------------------------ 작은 안내창 (전체 화면을 가리지 않음)

        void BuildGuidePanel(Transform canvasParent, TMP_FontAsset font)
        {
            guideGo = new GameObject("TutorialGuide", typeof(RectTransform));
            guideGo.transform.SetParent(canvasParent, false);
            var rt = (RectTransform)guideGo.transform;
            rt.anchorMin = new Vector2(0.24f, 0.845f);
            rt.anchorMax = new Vector2(0.76f, 0.925f);
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;

            var img = guideGo.AddComponent<Image>();
            img.color = PanelColor;
            img.raycastTarget = false;
            guideCanvasGroup = guideGo.AddComponent<CanvasGroup>();

            stepLabelText = CreateText(guideGo.transform, "StepLabel", "", 11, TextAlignmentOptions.TopLeft, font);
            stepLabelText.color = MutedText;
            stepLabelText.fontStyle = FontStyles.Bold;
            stepLabelText.rectTransform.anchorMin = new Vector2(0f, 0.62f);
            stepLabelText.rectTransform.anchorMax = new Vector2(1f, 0.98f);
            stepLabelText.rectTransform.offsetMin = new Vector2(14, 0);
            stepLabelText.rectTransform.offsetMax = new Vector2(-14, 0);

            guideText = CreateText(guideGo.transform, "Text", "", 14, TextAlignmentOptions.TopLeft, font);
            guideText.color = Color.white;
            guideText.textWrappingMode = TextWrappingModes.Normal;
            guideText.rectTransform.anchorMin = new Vector2(0f, 0.02f);
            guideText.rectTransform.anchorMax = new Vector2(1f, 0.64f);
            guideText.rectTransform.offsetMin = new Vector2(14, 0);
            guideText.rectTransform.offsetMax = new Vector2(-14, 0);

            guideCanvasGroup.alpha = 0f;
            guideGo.SetActive(false);
        }

        void ShowGuide(string stepLabel, string text)
        {
            stepLabelText.text = stepLabel;
            guideText.text = text;

            if (!guideGo.activeSelf)
            {
                guideGo.SetActive(true);
                guideCanvasGroup.alpha = 0f;
            }
            StartCoroutine(FadeCanvasGroup(guideCanvasGroup, guideCanvasGroup.alpha, 1f));
        }

        IEnumerator FadeOutGuideRoutine()
        {
            yield return FadeCanvasGroup(guideCanvasGroup, guideCanvasGroup.alpha, 0f);
            guideGo.SetActive(false);
        }

        IEnumerator FadeCanvasGroup(CanvasGroup cg, float from, float to)
        {
            float t = 0f;
            while (t < FadeDuration)
            {
                t += Time.deltaTime;
                cg.alpha = Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / FadeDuration));
                yield return null;
            }
            cg.alpha = to;
        }

        TMP_Text CreateText(Transform parent, string name, string content, int size, TextAlignmentOptions align, TMP_FontAsset font)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<TextMeshProUGUI>();
            if (font != null) text.font = font;
            text.text = content;
            text.fontSize = size;
            text.alignment = align;
            text.color = Color.white;
            text.raycastTarget = false;
            var rt = text.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return text;
        }
    }
}
