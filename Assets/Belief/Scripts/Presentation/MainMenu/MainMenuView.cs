using UnityEngine;
using UnityEngine.UI;

namespace Belief.Presentation.MainMenu
{
    /// <summary>MainMenuCanvas.prefab의 자식 참조 테이블 - StageBriefingView/HudView와 동일한 패턴.
    /// 텍스트가 전부 고정 문구라 Bind는 필요 없고, 버튼 참조만 노출한다(리스너는 Presenter가 붙인다).</summary>
    public class MainMenuView : MonoBehaviour
    {
        [SerializeField] CanvasGroup rootCanvasGroup;
        [SerializeField] Button startButton;
        [SerializeField] Button howToPlayButton;
        [SerializeField] Button quitButton;

        public CanvasGroup RootCanvasGroup => rootCanvasGroup;
        public Button StartButton => startButton;
        public Button HowToPlayButton => howToPlayButton;
        public Button QuitButton => quitButton;
    }
}
