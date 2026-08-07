using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
using Belief.Core;

namespace Belief.Presentation.HUD
{
    /// <summary>인게임 일시정지 메뉴 - 우측 상단 돋보기 옆의 일시정지 아이콘을 누르면 열리고,
    /// 계속 / 사운드 / 메인 메뉴 세 갈래를 준다. 아이콘은 도움말(돋보기)과 같은 규격(45x45 흰 선)이라
    /// 두 개가 한 줄로 붙어 하나의 도구 묶음처럼 읽힌다.
    ///
    /// <b>멈추는 방법은 Time.timeScale = 0 하나뿐이다.</b> 턴 진행이나 LLM 호출을 따로 붙잡지
    /// 않는다 - 그쪽은 이미 진행 중인 비동기 작업이라 중간에 끊으면 상태가 어긋난다. 대신 화면
    /// 전체를 덮는 막이 클릭을 삼켜서 새 행동이 들어가지 못하게 막는다(월드 클릭도 EventSystem을
    /// 거치므로 이 막 하나로 함께 막힌다).
    ///
    /// 멈춘 동안에도 도는 연출이 있어야 하므로 이 메뉴의 트윈은 전부 unscaledDeltaTime을 쓴다.</summary>
    public class PauseMenuController : MonoBehaviour
    {
        [Header("여닫기")]
        [SerializeField] GameObject pauseButtonGo;
        [SerializeField] Button pauseButton;
        [SerializeField] GameObject panelRoot;
        [SerializeField] CanvasGroup panelGroup;

        [Header("페이지")]
        [SerializeField] GameObject menuPage;
        [SerializeField] GameObject soundPage;

        [Header("버튼")]
        [SerializeField] Button resumeButton;
        [SerializeField] Button soundButton;
        [SerializeField] Button mainMenuButton;
        [SerializeField] Button soundBackButton;

        [Header("사운드")]
        [SerializeField] Slider bgmSlider;
        [SerializeField] Slider sfxSlider;
        [SerializeField] TMP_Text bgmValueText;
        [SerializeField] TMP_Text sfxValueText;

        const float FadeDuration = 0.18f;

        bool open;
        float fadeT;
        bool available = true;

        public bool IsOpen => open;

        void Awake()
        {
            if (pauseButton != null) pauseButton.onClick.AddListener(Open);
            if (resumeButton != null) resumeButton.onClick.AddListener(Close);
            if (soundButton != null) soundButton.onClick.AddListener(() => ShowPage(false));
            if (soundBackButton != null) soundBackButton.onClick.AddListener(() => ShowPage(true));
            if (mainMenuButton != null) mainMenuButton.onClick.AddListener(GoToMainMenu);

            if (bgmSlider != null) bgmSlider.onValueChanged.AddListener(v => { SoundSettings.Bgm = v; RefreshSoundLabels(); });
            if (sfxSlider != null) sfxSlider.onValueChanged.AddListener(v => { SoundSettings.Sfx = v; RefreshSoundLabels(); });

            if (panelRoot != null) panelRoot.SetActive(false);
            if (panelGroup != null) panelGroup.alpha = 0f;
        }

        void OnDestroy()
        {
            // 씬을 떠날 때 멈춘 채로 남으면 다음 씬이 통째로 얼어붙는다.
            if (open) Time.timeScale = 1f;
        }

        /// <summary>결과 리포트처럼 이 메뉴가 끼어들면 안 되는 화면에서 잠근다.</summary>
        public void SetAvailable(bool value)
        {
            available = value;
            if (!value && open) Close();
            if (pauseButtonGo != null) pauseButtonGo.SetActive(value);
        }

        void Update()
        {
            // ESC로도 여닫는다 - 일시정지 메뉴에서 가장 흔히 기대되는 조작이다.
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                if (open) Close();
                else if (available) Open();
            }

            if (panelGroup == null) return;
            float target = open ? 1f : 0f;
            if (!Mathf.Approximately(fadeT, target))
            {
                // 멈춘 동안에도 흘러야 하므로 unscaled를 쓴다.
                fadeT = Mathf.MoveTowards(fadeT, target, Time.unscaledDeltaTime / FadeDuration);
                panelGroup.alpha = Mathf.SmoothStep(0f, 1f, fadeT);
                if (!open && Mathf.Approximately(fadeT, 0f) && panelRoot != null) panelRoot.SetActive(false);
            }
        }

        public void Open()
        {
            if (open || !available) return;
            open = true;
            SfxPlayer.Play(Sfx.PauseToggle);
            Time.timeScale = 0f;
            if (panelRoot != null) panelRoot.SetActive(true);
            ShowPage(true);
            SyncSlidersFromSettings();
            if (pauseButtonGo != null) pauseButtonGo.SetActive(false);
            if (panelGroup != null) panelGroup.blocksRaycasts = true;
            // 방금 누른 버튼이 선택된 채로 남아 스페이스/엔터에 다시 반응하지 않게 한다.
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        }

        public void Close()
        {
            if (!open) return;
            open = false;
            SfxPlayer.Play(Sfx.PauseToggle);
            Time.timeScale = 1f;
            if (pauseButtonGo != null) pauseButtonGo.SetActive(available);
            if (panelGroup != null) panelGroup.blocksRaycasts = false;
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        }

        void ShowPage(bool menu)
        {
            if (menuPage != null) menuPage.SetActive(menu);
            if (soundPage != null) soundPage.SetActive(!menu);
            if (!menu) RefreshSoundLabels();
        }

        void SyncSlidersFromSettings()
        {
            // 값을 넣는 동안 onValueChanged가 되돌아와 저장을 다시 부르지 않게 잠깐 떼어 둔다.
            if (bgmSlider != null) { bgmSlider.SetValueWithoutNotify(SoundSettings.Bgm); }
            if (sfxSlider != null) { sfxSlider.SetValueWithoutNotify(SoundSettings.Sfx); }
            RefreshSoundLabels();
        }

        void RefreshSoundLabels()
        {
            if (bgmValueText != null) bgmValueText.text = Mathf.RoundToInt(SoundSettings.Bgm * 100f) + "%";
            if (sfxValueText != null) sfxValueText.text = Mathf.RoundToInt(SoundSettings.Sfx * 100f) + "%";
        }

        void GoToMainMenu()
        {
            // 멈춰 둔 시간을 반드시 먼저 되돌린다 - 안 그러면 메인 메뉴가 정지 상태로 뜬다.
            Time.timeScale = 1f;
            open = false;
            var fader = ScreenFader.Instance;
            if (fader != null) fader.LoadScene("MainMenu");
            else UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
        }
    }
}
