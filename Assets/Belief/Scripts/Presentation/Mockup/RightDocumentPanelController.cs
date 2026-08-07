using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Belief.Presentation.Mockup
{
    public enum RightPanelState
    {
        Closed,
        Profile,
        Log
    }

    /// <summary>UI_PlayHudMockup 우측 Profile/Log 문서를 담당한다. 탭(SharedTabRoot)은 두 패널과
    /// 완전히 분리된 항상-활성 오브젝트이며, 열리고 닫힐 때만 문서와 같은 이동값을 공유해 함께
    /// 슬라이드한다. Profile↔Log처럼 이미 열린 상태끼리 전환할 때는 SharedTabRoot와 패널 모두
    /// 위치를 바꾸지 않고 콘텐츠/배경만 교체한다. 실제 데이터/게임 시스템과는 연결하지 않는다.</summary>
    public class RightDocumentPanelController : MonoBehaviour
    {
        [SerializeField] RectTransform profilePanelRoot;
        [SerializeField] RectTransform logPanelRoot;
        [SerializeField] RectTransform sharedTabRoot;

        [SerializeField] Button profileTabButton;
        [SerializeField] Button logTabButton;

        [SerializeField] float hiddenXOffset = 500f;
        [SerializeField] float animationDuration = 0.30f;

        public RightPanelState CurrentState { get; private set; } = RightPanelState.Closed;

        Vector2 openPosition, closedPosition;

        GameObject profileContent, logContent;

        Coroutine activeTransition;

        void Awake()
        {
            // ProfilePanelRoot의 현재 Inspector Open 위치를 공통 기준으로 캡처한다 -
            // LogPanelRoot와 SharedTabRoot 모두 반드시 이 값을 그대로 공유한다.
            openPosition = profilePanelRoot.anchoredPosition;
            closedPosition = openPosition + new Vector2(hiddenXOffset, 0f);

            profileContent = profilePanelRoot.Find("ProfileContent")?.gameObject;
            logContent = logPanelRoot.Find("LogContent")?.gameObject;

            // 초기 상태 Closed - ProfilePanelRoot를 기본 활성 패널로 둔다.
            profilePanelRoot.anchoredPosition = closedPosition;
            profilePanelRoot.gameObject.SetActive(true);
            if (profileContent != null) profileContent.SetActive(false);

            logPanelRoot.anchoredPosition = closedPosition;
            logPanelRoot.gameObject.SetActive(false);
            if (logContent != null) logContent.SetActive(false);

            // SharedTabRoot는 항상 활성 - 절대 SetActive(false) 하지 않는다.
            sharedTabRoot.gameObject.SetActive(true);
            sharedTabRoot.anchoredPosition = closedPosition;

            Wire(profileTabButton, HandleProfileTabClicked);
            Wire(logTabButton, HandleLogTabClicked);

            // 공용 클릭음을 끄고 소리는 아래에서 직접 낸다 - 펼치는 press에만 문서 소리를 주고
            // 나머지 press에는 클릭음을 줘야 하는데, 자동으로 걸리는 클릭음은 둘을 구분하지 못한다.
            Belief.Core.SfxPlayer.Mute(profileTabButton);
            Belief.Core.SfxPlayer.Mute(logTabButton);
        }

        static void Wire(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button == null)
            {
                Debug.LogWarning("[RightPanel] 탭 버튼 참조가 비어 있습니다.");
                return;
            }

            button.onClick.RemoveListener(action);
            button.onClick.AddListener(action);
        }

        void OnDestroy()
        {
            if (profileTabButton != null) profileTabButton.onClick.RemoveListener(HandleProfileTabClicked);
            if (logTabButton != null) logTabButton.onClick.RemoveListener(HandleLogTabClicked);
        }

        void HandleProfileTabClicked() => HandleTabClicked(true);
        void HandleLogTabClicked() => HandleTabClicked(false);

        /// <summary>탭을 누르지 않고도 Profile 문서를 연다 - 지도에서 NPC에 커서를 올리면 조사 파일이
        /// 저절로 열려야 하기 때문이다. 이미 열려 있으면 아무것도 하지 않는다(같은 탭 재클릭은
        /// <see cref="HandleTabClicked"/>에서 "닫기"로 처리되므로 그대로 부르면 도로 닫힌다).</summary>
        public void OpenProfile()
        {
            if (CurrentState == RightPanelState.Profile) return;
            HandleTabClicked(true);
        }

        /// <summary>펼쳐져 있던 문서를 도로 집어넣는다 - 진행 완료처럼 화면이 다음 단계로 넘어가는
        /// 순간에 문서가 펼쳐진 채로 남아 있으면 지도를 가린 채 턴이 흘러간다.</summary>
        public void CloseIfOpen()
        {
            if (CurrentState == RightPanelState.Closed) return;
            var panel = CurrentState == RightPanelState.Profile ? profilePanelRoot : logPanelRoot;
            var content = CurrentState == RightPanelState.Profile ? profileContent : logContent;
            Belief.Core.SfxPlayer.Play(Belief.Core.Sfx.DocumentClose);
            Restart(AnimateClose(panel, content));
            CurrentState = RightPanelState.Closed;
        }

        void HandleTabClicked(bool wantProfile)
        {
            var target = wantProfile ? profilePanelRoot : logPanelRoot;
            var targetContent = wantProfile ? profileContent : logContent;
            var other = wantProfile ? logPanelRoot : profilePanelRoot;
            var otherContent = wantProfile ? logContent : profileContent;

            var targetState = wantProfile ? RightPanelState.Profile : RightPanelState.Log;

            if (CurrentState == targetState)
            {
                // 같은 탭 재클릭 - 문서와 SharedTabRoot를 함께 Open -> Closed로 슬라이드(활성 상태 유지).
                CloseIfOpen();
                return;
            }

            // 닫혀 있던 문서를 펼치는 순간에만 종이 소리를 낸다 - Profile↔Log 전환은 종이가 이미
            // 펼쳐진 채로 내용만 바뀌는 것이라 여닫는 소리가 어울리지 않는다. 그쪽은 클릭음으로 받는다.
            Belief.Core.SfxPlayer.Play(CurrentState == RightPanelState.Closed
                ? Belief.Core.Sfx.DocumentOpen
                : Belief.Core.Sfx.Click);

            if (CurrentState == RightPanelState.Closed)
            {
                if (other.gameObject.activeSelf)
                {
                    // 다른 패널이 Closed 위치에서 peek 중이었다면 애니메이션 없이 즉시 교체.
                    other.gameObject.SetActive(false);
                    target.gameObject.SetActive(true);
                    target.anchoredPosition = closedPosition;
                }

                // 문서와 SharedTabRoot를 함께 Closed -> Open으로 슬라이드.
                Restart(AnimateOpen(target, targetContent));
            }
            else
            {
                // 이미 열린 다른 문서에서 전환 - SharedTabRoot 위치는 그대로, 문서만 같은 Open 좌표에서 즉시 교체.
                if (activeTransition != null)
                {
                    StopCoroutine(activeTransition);
                    activeTransition = null;
                }

                if (otherContent != null) otherContent.SetActive(false);
                other.gameObject.SetActive(false);

                target.gameObject.SetActive(true);
                target.anchoredPosition = openPosition;
                if (targetContent != null) targetContent.SetActive(true);

                // 직전 Closed->Open 애니메이션이 완료되기 전에 끼어든 경우를 대비해
                // sharedTabRoot도 Open 위치로 명시적으로 강제한다(진행 중이던 코루틴은 위에서 이미 정지됨).
                sharedTabRoot.anchoredPosition = openPosition;
            }

            CurrentState = targetState;
        }

        void Restart(IEnumerator routine)
        {
            if (activeTransition != null) StopCoroutine(activeTransition);
            activeTransition = StartCoroutine(routine);
        }

        IEnumerator AnimateOpen(RectTransform panel, GameObject content)
        {
            yield return MoveTogether(panel, sharedTabRoot, openPosition);
            if (content != null) content.SetActive(true);
            activeTransition = null;
        }

        IEnumerator AnimateClose(RectTransform panel, GameObject content)
        {
            if (content != null) content.SetActive(false);
            yield return MoveTogether(panel, sharedTabRoot, closedPosition);
            activeTransition = null;
        }

        /// <summary>문서 패널과 SharedTabRoot를 같은 목표 좌표로 동시에 슬라이드한다 - 항상 같은
        /// Open/Closed 좌표를 공유하므로 둘 다 같은 시작점에서 같은 도착점까지 이동한다.</summary>
        IEnumerator MoveTogether(RectTransform panel, RectTransform tabRoot, Vector2 destination)
        {
            Vector2 panelStart = panel.anchoredPosition;
            Vector2 tabStart = tabRoot.anchoredPosition;
            float t = 0f;
            while (t < 1f)
            {
                t += animationDuration > 0f ? Time.unscaledDeltaTime / animationDuration : 1f;
                float e = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
                panel.anchoredPosition = Vector2.Lerp(panelStart, destination, e);
                tabRoot.anchoredPosition = Vector2.Lerp(tabStart, destination, e);
                yield return null;
            }

            panel.anchoredPosition = destination;
            tabRoot.anchoredPosition = destination;
        }
    }
}
