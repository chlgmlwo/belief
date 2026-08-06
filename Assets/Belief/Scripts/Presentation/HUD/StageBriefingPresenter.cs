using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using Belief.Core;
using Belief.Data;

namespace Belief.Presentation.HUD
{
    /// <summary>스테이지 진입 직후, 실제 입력이 가능해지기 전에 한 번 표시되는 브리핑/미니맵 화면
    /// (section 12 - "스테이지 선택 UI" 자산 조사 결과 이 폴더는 그리드형 선택 화면이 아니라 이
    /// 브리핑 화면 자산이었다). HudPresenter/TurnSystem 등 기존 게임 로직은 전혀 건드리지 않는다 -
    /// 이 화면은 HudCanvas보다 sortingOrder가 높은 별도 Canvas로 화면 전체를 덮어 입력만 차단하고,
    /// "작전 실행" 클릭 시 자기 자신을 비활성화할 뿐이다(아래 진행 중이던 게임 상태는 그대로 유지).
    /// ProgressionData/StageData의 기존 필드만 읽는다 - 새 데이터를 추가하지 않는다.
    ///
    /// 하이어라키는 런타임에 Instantiate하지 않고, StageBriefingCanvas.prefab의 인스턴스를 씬 파일에
    /// 직접 배치해 둔다(CardTileView와 동일한 View 패턴이되, 프리팹 "에셋"이 아니라 씬에 미리 놓인
    /// "인스턴스"를 참조한다) - 그래야 Edit 모드의 Hierarchy/Scene 뷰에서 바로 보이고 드래그로 조절할
    /// 수 있다(런타임 Instantiate는 Play를 눌러야만 나타나는 문제가 있었다). 폰트/크기/색/앵커 같은
    /// 정적 스타일은 프리팹에 구워져 있고, 여기서는 런타임에만 정해지는 값(텍스트 내용, 미니맵 진행
    /// 상태)만 StageBriefingView.Bind/BindMap으로 채워 넣는다.</summary>
    public class StageBriefingPresenter : MonoBehaviour
    {
        [SerializeField] public PlayHudSkin skin;
        [SerializeField] StageBriefingView view;

        GameObject canvasGo;
        CanvasGroup canvasGroup;

        void Start()
        {
            EnsureEventSystem();
            BuildUI();
            // 예전엔 alpha 0에서 0.3초 동안 나타났는데, 그 0.3초 동안 브리핑이 반투명이라 아래
            // 인게임 화면이 그대로 비쳤다(스포일러). 지금은 ScreenFader가 씬 로드 순간부터 화면을
            // 검게 덮고 있으므로, 브리핑은 처음부터 불투명하게 떠 있다가 그 커튼이 걷히며 드러난다.
            canvasGroup.alpha = 1f;
        }

        void BuildUI()
        {
            canvasGo = view.gameObject;
            canvasGroup = view.CanvasGroup;

            var pc = ProgressionController.Instance;
            var installer = FindFirstObjectByType<GameInstaller>();
            var objective = pc != null ? pc.CurrentObjective() : null;
            var stageAsset = installer != null ? installer.StageAsset : null;

            int stageIndex = pc != null ? pc.Progress.CurrentStageIndex : 0;
            int stageNumber = stageAsset != null && stageAsset.stageNumber > 0 ? stageAsset.stageNumber : stageIndex + 1;
            int turnLimit = installer != null ? installer.Turns.StageMaxTurns : 0;

            // 시안의 "제목/부제" 자리에는 원래 현재 목표의 제목·본문이 들어갔는데, 그 둘은 플레이 중
            // HUD의 GOAL 카드와 그대로 겹친다. 브리핑은 "여기가 어디이고 이 구역에서 뭘 해야 하는가"만
            // 보여주면 되므로 제목=구역 이름 / 부제=구역 설명 / 메모지=이 구역의 GOAL 제목 목록으로 바꿨다
            // (배치·폰트는 시안 그대로, 들어가는 데이터만 다르게 - 사용자 지시).
            string regionName = stageAsset != null && !string.IsNullOrEmpty(stageAsset.regionName)
                ? stageAsset.regionName
                : (pc != null ? pc.CurrentStageDisplayName : "");
            string regionDescription = stageAsset != null && !string.IsNullOrEmpty(stageAsset.regionDescription)
                ? stageAsset.regionDescription
                : (pc != null ? pc.CurrentStageIntroSubtitle : "");

            view.Bind(stageNumber, regionName, regionDescription, turnLimit);
            view.BindGoals(CollectGoalTitles(stageAsset, pc, stageIndex, objective));
            BindMap(view, pc, stageIndex);

            view.LaunchButton.onClick.AddListener(Dismiss);
            view.BackButton.onClick.AddListener(GoToMainMenu);
        }

        /// <summary>이 구역의 GOAL 제목 목록. StageData.missions와 ProgressionData.objectives는 같은
        /// MissionData 배열이지만(스테이지1~4 전부 내용 일치), 실제로 플레이되는 미션 순서는
        /// GameInstaller가 읽는 StageData 쪽이므로 그것을 우선한다. 둘 다 없으면 최소한 현재 목표
        /// 하나라도 보여준다.</summary>
        static List<string> CollectGoalTitles(StageData stageAsset, ProgressionController pc, int stageIndex, MissionData current)
        {
            var titles = new List<string>();
            var missions = stageAsset != null ? stageAsset.missions : null;
            if ((missions == null || missions.Length == 0) && pc != null && pc.Data != null
                && pc.Data.stages != null && stageIndex < pc.Data.stages.Length)
                missions = pc.Data.stages[stageIndex].objectives;

            if (missions != null)
                foreach (var m in missions)
                    if (m != null && !string.IsNullOrEmpty(m.displayTitle))
                        titles.Add(m.displayTitle);

            if (titles.Count == 0 && current != null && !string.IsNullOrEmpty(current.displayTitle))
                titles.Add(current.displayTitle);
            return titles;
        }

        /// <summary>실제 지리적 좌표 데이터가 없으므로(StageData/ProgressionData에 지도 좌표 필드가 없음)
        /// 세로 스택으로 근사한다. 현재 스테이지는 currentStageIcon, 이후 스테이지는 lockedStageIcon으로,
        /// 이미 지난 스테이지는 표시하지 않는다(가이드 원본도 현재+이후만 보여준다).</summary>
        void BindMap(StageBriefingView view, ProgressionController pc, int currentIndex)
        {
            if (pc == null || pc.Data == null || pc.Data.stages == null) return;
            var stages = pc.Data.stages;
            string currentName = currentIndex < stages.Length ? stages[currentIndex].displayName : "";
            int remaining = stages.Length - currentIndex;
            view.BindMap(currentName, remaining, skin?.currentStageIcon, skin?.lockedStageIcon);
        }

        /// <summary>"작전 실행"/"메인 화면"은 눌린 뒤에도 0.25초 페이드가 도는 동안 화면이 그대로
        /// 살아 있어서 버튼이 계속 눌렸다(연타하면 코루틴이 여러 개 뜨거나 LoadScene이 중복 호출된다).
        /// 첫 클릭에서 바로 입력을 끊는다 - 플래그만으로는 이미 큐에 들어간 클릭을 못 막으므로
        /// 버튼 자체를 비활성화하고 캔버스의 레이캐스트도 함께 닫는다.</summary>
        bool inputClosed;

        bool CloseInput()
        {
            if (inputClosed) return false;
            inputClosed = true;
            if (view.LaunchButton != null) view.LaunchButton.interactable = false;
            if (view.BackButton != null) view.BackButton.interactable = false;
            if (canvasGroup != null) canvasGroup.blocksRaycasts = false;
            return true;
        }

        void Dismiss()
        {
            if (!CloseInput()) return;
            StartCoroutine(FadeOutAndDisable());
        }

        void GoToMainMenu()
        {
            if (!CloseInput()) return;
            if (ScreenFader.Instance != null) ScreenFader.Instance.LoadScene("MainMenu");
            else SceneManager.LoadScene("MainMenu");
        }

        IEnumerator FadeOutAndDisable()
        {
            float t = 0f;
            float start = canvasGroup.alpha;
            while (t < 0.25f) { t += Time.deltaTime; canvasGroup.alpha = Mathf.Lerp(start, 0f, t / 0.25f); yield return null; }
            canvasGroup.alpha = 0f;

            // gameObject 전체(= HudPresenter와 같은 호스트)가 아니라 이 화면 전용 Canvas만 끈다 -
            // 예전엔 gameObject.SetActive(false)를 썼는데, StageBriefingPresenter가 HudPresenter와
            // 같은 GameObject에 붙어 있어서 HUD 전체가 함께 꺼져버리는 버그가 있었다.
            canvasGo.SetActive(false);
        }

        void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        }
    }
}
