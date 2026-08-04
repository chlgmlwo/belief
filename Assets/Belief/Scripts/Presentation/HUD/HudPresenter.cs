using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using TMPro;
using Belief.Core;
using Belief.Data;
using Belief.Domain;
using Belief.Events;
using Belief.Presentation;
using Belief.Presentation.World;

namespace Belief.Presentation.HUD
{
    /// <summary>
    /// 게임 로직을 직접 수정하지 않는다 - GameInstaller.Turns/TargetingController의 공개 메서드만
    /// 호출하고, 표시 갱신은 이벤트 구독으로만 한다. 대상 선택은 World 클릭(TargetingController)이
    /// 담당하고, 여기서는 진행 안내 문구와 "정보원에게 전달" 버튼만 제공한다.
    ///
    /// 하이어라키는 런타임에 Instantiate하지 않고, HudCanvas.prefab의 인스턴스를 씬 파일에 직접
    /// 배치해 둔다(CardTileView/StageBriefingView와 동일한 View 패턴이되, 프리팹 "에셋"이 아니라 씬에
    /// 미리 놓인 "인스턴스"를 참조한다) - Edit 모드의 Hierarchy/Scene 뷰에서 바로 보이고 드래그로
    /// 조절할 수 있어야 하기 때문이다(런타임 Instantiate는 Play를 눌러야만 나타나는 문제가 있었다).
    /// 단, 개수가 게임 상태에 따라 바뀌는 미션 조건 행/NPC 관계도 행/카드 타일은 여전히 프리팹
    /// "에셋"을 런타임에 Instantiate한다(몇 개가 나올지 미리 알 수 없어 씬에 고정 배치할 수 없다).
    /// 이 프리젠터의 Refresh*/Show* 로직은 전혀 바뀌지 않는다 - 각 필드가 가리키는 대상이 "코드가 막
    /// 만든 오브젝트"에서 "씬에 미리 놓인 인스턴스의 필드"로 바뀔 뿐이다.
    /// </summary>
    /// <summary>section 3/7 - 기본/프로필/로그 3상태. 우측 패널은 항상 이 중 하나만 활성화된다
    /// (Profile/Log 동시 표시 금지).</summary>
    public enum HudPanelState
    {
        Default,
        Profile,
        Log
    }

    public class HudPresenter : MonoBehaviour
    {
        [SerializeField] GameInstaller installer;
        [SerializeField] TargetingController targeting;
        [SerializeField] WorldPresenter worldPresenter;
        [SerializeField] CardTileView cardTilePrefab;
        [SerializeField] HudView view;
        [SerializeField] MissionConditionRowView missionConditionRowPrefab;
        [SerializeField] NpcRelationshipRowView npcRelationshipRowPrefab;
        [SerializeField] TMP_FontAsset koreanFont;

        /// <summary>UI 아트 자산 테이블(리소스 폴더 기반) - 비어 있으면(null) 기존 단색 placeholder로
        /// 그대로 동작한다(하위 호환). public - 에디터 스크립트가 SerializedObject 없이 씬 배선 시
        /// 직접 대입할 수 있게 한다.</summary>
        [SerializeField] public PlayHudSkin skin;

        const int MaxLogLines = 12;

        static readonly Color PanelColor = new Color(0.09f, 0.12f, 0.10f, 0.95f);
        static readonly Color AccentColor = new Color(0.30f, 0.85f, 0.55f);
        static readonly Color MutedText = new Color(0.72f, 0.78f, 0.74f);
        static readonly Color ErrorColor = new Color(0.95f, 0.45f, 0.40f);

        const float PopupFadeDuration = 0.25f;
        const float PopupScaleIn = 0.94f;
        const float PopupScaleOut = 0.97f;

        TMP_Text stageTurnText;
        TMP_Text missionTurnText;
        TMP_Text stageNumberText;
        TMP_Text stageNameText;
        TMP_Text missionTitleText;
        TMP_Text missionDescText;
        Transform missionConditionsRoot;
        readonly List<GameObject> missionConditionRows = new List<GameObject>();
        TMP_Text missionTurnsText;
        TMP_Text nextMissionText;
        GameObject nextMissionCardRoot;
        TMP_Text nextMissionCardTitleText;
        TMP_Text nextMissionCardDescText;
        GameObject nextMissionConnectorGo;
        // 우측 패널 상태(section 3/7) - Default/Profile/Log 중 하나만.
        HudPanelState panelState = HudPanelState.Default;
        Button profileTabButton;
        Button logTabButton;
        Image profileTabIndicator;
        Image logTabIndicator;
        static readonly Color TabActiveColor = Color.white;
        static readonly Color TabInactiveColor = new Color(0.55f, 0.52f, 0.48f, 0.75f);

        // 월드 하단 힌트 바(BottomPanel/FeedbackBanner) - Profile/Log 패널이 열리면 우측 패널
        // 왼쪽 경계(NpcProfilePanel/LogPanel의 anchorMin.x) 전까지만 폭을 좁힌다(section 신규 지시).
        RectTransform bottomPanelRect;
        RectTransform feedbackBannerRect;
        float bottomPanelFullMaxX;
        float feedbackBannerFullMaxX;
        const float RightPanelLeftEdge = 0.68f;
        const float HintBarRightMargin = 0.02f;

        GameObject logPanelGo;
        TMP_Text logTopDialogueText;
        TMP_Text logGeneralText;
        TMP_Text logStatHeaderText;
        TMP_Text logBeliefFromText;
        TMP_Text logBeliefToText;
        TMP_Text logBottomDialogueText;
        NpcState lastLoggedNpcState;

        TMP_Text ownedCountLabel;
        Transform ownedRoot;
        readonly Dictionary<InformationCardData, CardTileView> ownedTiles = new Dictionary<InformationCardData, CardTileView>();

        GameObject cardInfoGo;
        TMP_Text cardTitleText, cardDescText, cardKindText;

        GameObject instructionGo;
        TMP_Text instructionText;
        GameObject deliverButtonGo;
        Button deliverButton;
        GameObject noSelectionHintGo;

        // 장소 정보 패널(LocationInfoPaper) - LocationData의 콘텐츠 필드를 enum 그대로 표시한다.
        GameObject locationNoteGo;
        RectTransform locationNoteRect;
        TMP_Text locationNoteTitleText;
        TMP_Text locationNoteBodyText;
        LocationData selectedLocationData;

        // NPC 조사 파일 패널(section 6) - 공용 패널 하나, 클릭한 NPC로 내용만 교체한다.
        GameObject npcProfileGo;
        TMP_Text npcNameText;
        TMP_Text npcBasicInfoText;
        TMP_Text npcBeliefTierText;
        TMP_Text npcBeliefDialogueText;
        Transform npcRelationshipsRoot;
        readonly List<GameObject> npcRelationshipRows = new List<GameObject>();
        TMP_Text npcHistoryText;
        NpcState selectedNpcState;
        GameObject npcNoneStickerGo;
        TMP_Text npcJudgmentTendencyText, npcPriorityText, npcSensitiveInfoText, npcRelationTendencyText, npcTrustJudgmentText;

        GameObject overlayGo;
        CanvasGroup overlayCanvasGroup;
        Transform overlayBox;
        TMP_Text overlayTitleText, overlayDescText;
        GameObject overlayButtonGo;
        TMP_Text overlayButtonLabel;
        Button overlayButton;

        // 작전 결과 화면(section 13) - 실패/승리 전용, Overlay와 별개 CanvasGroup.
        GameObject resultScreenGo;
        CanvasGroup resultCanvasGroup;
        Image resultPanelImg;
        Image resultPhotoFrameImg;
        TMP_Text resultTitleText, resultDescText, resultStageTagText, resultTurnsText;
        GameObject resultPrimaryButtonGo, resultSecondaryButtonGo;
        TMP_Text resultPrimaryButtonLabel, resultSecondaryButtonLabel;
        Button resultPrimaryButton, resultSecondaryButton;

        GameObject feedbackGo;
        CanvasGroup feedbackCanvasGroup;
        TMP_Text feedbackText;
        Coroutine feedbackRoutine;

        HowToPlayPopup howToPlayPopup;
        Transform canvasRoot;

        /// <summary>TutorialController가 카드 타일을 반복 Highlight하기 위해 읽는다.</summary>
        public IEnumerable<CardTileView> OwnedCardTiles => ownedTiles.Values;

        /// <summary>TutorialController가 "정보원에게 전달" 버튼을 강조하기 위해 읽는다.</summary>
        public GameObject DeliverButtonGo => deliverButtonGo;

        void Start()
        {
            EnsurePlaybackDirector();
            BuildUI();

            var bus = installer.EventBus;
            bus.Subscribe<TurnStartedEvent>(_ => { RefreshAll(); PulseOnce(missionTurnText); PulseOnce(stageTurnText); });
            bus.Subscribe<CardSelectedEvent>(_ => RefreshAll());
            bus.Subscribe<CardPlayedEvent>(_ => { RefreshAll(); ShowTransientNotice("결과를 확인하세요.", AccentColor); });
            bus.Subscribe<InformationAcquiredEvent>(OnInformationAcquired);
            // 미션 자체가 교체됐다는 직접 신호 - ObjectivesChanged/TurnStartedEvent 경로와 별개로
            // MissionSystem.LoadMission이 발행하는 즉시 미션 패널을 완전히 재구성한다.
            bus.Subscribe<MissionChangedEvent>(_ => RefreshMission());
            bus.Subscribe<GameOverEvent>(e => StartCoroutine(WaitForPlaybackThen(() => { if (e.Won) ShowFinalVictory(); else ShowMissionFailedPopup(); })));
            installer.Log.OnLogAdded += AppendLog;
            bus.Subscribe<NpcSpokeEvent>(OnLogNpcSpoke);
            bus.Subscribe<CardJudgedEvent>(OnLogCardJudged);
            targeting.PhaseChanged += RefreshBottomPanel;
            targeting.InteractionRejected += msg => ShowTransientNotice(msg, ErrorColor);

            // NPC/장소 조사용 클릭 구독 - TargetingController가 같은 이벤트를 전달 대상 지정용으로
            // 이미 소비하고 있지만, WorldPresenter.NpcClicked/LocationClicked는 멀티캐스트라 여기서
            // 순수 조회(조사 파일/특성 메모 표시)용으로 추가 구독해도 기존 전달 흐름과 충돌하지 않는다.
            if (worldPresenter != null)
            {
                worldPresenter.NpcClicked += OnNpcClickedForProfile;
                worldPresenter.LocationHoverEnter += OnLocationHoverEnter;
                worldPresenter.LocationHoverExit += OnLocationHoverExit;
            }

            var pc = ProgressionController.Instance;
            if (pc != null)
            {
                pc.ObjectivesChanged += RefreshMission;
                pc.ObjectiveCompletedPendingConfirm += OnObjectiveCompletedPending;
                pc.StageCompletedPendingConfirm += OnStageCompletedPending;

                // 구역 안내 패널은 더 이상 표시하지 않는다(사용자 지시) - 다만 이 팝업이 끝나는 시점에
                // 걸려 있던 튜토리얼 시작(MaybeStartTutorial)은 그대로 유지해야 하므로 직접 호출한다.
                MaybeStartTutorial();
            }

            RefreshAll();
            RefreshNpcProfile();
            RefreshLocationNote();
        }

        /// <summary>ProgressionController(DontDestroyOnLoad로 씬 전환 간 유지되는 영속 오브젝트)의 이벤트는
        /// 이 HudPresenter가 파괴돼도 자동으로 끊어지지 않는다 - 구독 해제를 하지 않으면 다음 씬의 새
        /// HudPresenter와 함께 파괴된 이전 씬의 HudPresenter가 이중으로 호출되어(멀티캐스트 델리게이트),
        /// 파괴된 GameObject에 접근하다 예외가 나고 그 뒤에 구독된 새 HudPresenter의 처리까지 막아버린다.</summary>
        void OnDestroy()
        {
            var pc = ProgressionController.Instance;
            if (pc == null) return;
            pc.ObjectivesChanged -= RefreshMission;
            pc.ObjectiveCompletedPendingConfirm -= OnObjectiveCompletedPending;
            pc.StageCompletedPendingConfirm -= OnStageCompletedPending;
        }

        /// <summary>목표 하나가 완료됐지만 아직 확인 대기 중일 때(구역의 마지막 목표는 아님) 호출된다 -
        /// "MISSION COMPLETE" 팝업을 띄우고 [다음] 클릭 시에만 ProgressionController에 실제 전환을 맡긴다.</summary>
        void OnObjectiveCompletedPending(MissionData completed) =>
            StartCoroutine(WaitForPlaybackThen(() =>
                ShowGatedPopup("MISSION COMPLETE", AccentColor, completed.displayTitle, "다음",
                    () => ProgressionController.Instance?.ConfirmMissionComplete())));

        /// <summary>구역의 마지막 목표가 완료됐지만 아직 확인 대기 중일 때 호출된다 - "ZONE COMPLETE" 팝업을
        /// 띄우고 [다음 구역] 클릭 시에만 ProgressionController에 다음 씬 로드를 맡긴다.</summary>
        void OnStageCompletedPending()
        {
            var pc = ProgressionController.Instance;
            StartCoroutine(WaitForPlaybackThen(() =>
                ShowGatedPopup("ZONE COMPLETE", AccentColor, pc != null ? pc.CurrentStageDisplayName : "", "다음 구역",
                    () => pc?.ConfirmZoneComplete())));
        }

        /// <summary>NPC 이동/대사 같은 월드 연출이 재생 중일 때 결과·완료 팝업이 그 위로 먼저 떠버리는
        /// 문제(사용자 리포트: "이동이 끝나기 전에 결과가 나옴")를 막는다 - PlaybackDirector에 등록된
        /// 연출이 전부 끝날 때까지 한 프레임씩 기다린 뒤에야 실제 팝업을 띄운다.</summary>
        IEnumerator WaitForPlaybackThen(Action show)
        {
            while (PlaybackDirector.Instance != null && PlaybackDirector.Instance.IsPlaying)
                yield return null;
            show();
        }

        /// <summary>작전 결과 화면(section 13) - 실패는 미션마다(턴 소진 시) 반복해서 뜬다(재시작
        /// 가능), 승리는 GameOverEvent(true)가 게임 전체에서 정확히 한 번만 발행될 때만 뜬다.
        /// MISSION COMPLETE/ZONE COMPLETE 같은 중간 전환 팝업은 이 화면을 쓰지 않는다 - 전용 아트
        /// 자산이 없어 기존 Overlay(ShowGatedPopup)를 그대로 유지한다.</summary>
        void ShowMissionFailedPopup()
        {
            var pc = ProgressionController.Instance;
            string missionTitle = pc?.CurrentObjective()?.displayTitle ?? "";
            string stageTag = pc != null ? pc.CurrentStageDisplayName : "";
            int turnsUsed = Mathf.Min(installer.Turns.CurrentTurn, installer.Turns.MaxTurns);
            ShowResultScreen(false, missionTitle, "턴을 모두 소진했습니다.", stageTag, turnsUsed,
                "재시작", () => pc?.RestartCurrentMission(),
                "메인 화면", GoToMainMenu);
        }

        void ShowFinalVictory()
        {
            var pc = ProgressionController.Instance;
            string stageTag = pc != null ? pc.CurrentStageDisplayName : "";
            int turnsUsed = Mathf.Min(installer.Turns.CurrentTurn, installer.Turns.MaxTurns);
            ShowResultScreen(true, "임무 성공", "목표를 달성했습니다.", stageTag, turnsUsed,
                "확인", GoToMainMenu, null, null);
        }

        void GoToMainMenu() => UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");

        void ShowResultScreen(bool won, string title, string desc, string stageTag, int turnsUsed,
            string primaryLabel, Action onPrimary, string secondaryLabel, Action onSecondary)
        {
            resultScreenGo.SetActive(true);
            resultCanvasGroup.alpha = 0f;

            if (resultPanelImg != null && skin != null)
                resultPanelImg.sprite = won ? skin.successPanel : skin.failurePanel;
            if (resultPhotoFrameImg != null && skin != null)
                resultPhotoFrameImg.sprite = won ? skin.successPhotoFrame : skin.failurePhotoFrame;

            resultTitleText.text = title;
            resultTitleText.color = won ? AccentColor : ErrorColor;
            resultDescText.text = desc;
            resultStageTagText.text = stageTag;
            resultTurnsText.text = $"Turn {turnsUsed}";

            resultPrimaryButtonLabel.text = primaryLabel;
            resultPrimaryButton.interactable = true;
            resultPrimaryButton.onClick.RemoveAllListeners();
            resultPrimaryButton.onClick.AddListener(() => StartCoroutine(ConfirmResultRoutine(onPrimary)));

            bool hasSecondary = !string.IsNullOrEmpty(secondaryLabel) && onSecondary != null;
            resultSecondaryButtonGo.SetActive(hasSecondary);
            if (hasSecondary)
            {
                resultSecondaryButtonLabel.text = secondaryLabel;
                resultSecondaryButton.interactable = true;
                resultSecondaryButton.onClick.RemoveAllListeners();
                resultSecondaryButton.onClick.AddListener(() => StartCoroutine(ConfirmResultRoutine(onSecondary)));
            }

            StartCoroutine(FadeCanvasGroupRoutine(resultCanvasGroup, 0f, 1f, PopupFadeDuration));
        }

        IEnumerator ConfirmResultRoutine(Action onConfirm)
        {
            resultPrimaryButton.interactable = false;
            if (resultSecondaryButton != null) resultSecondaryButton.interactable = false;
            yield return FadeCanvasGroupRoutine(resultCanvasGroup, 1f, 0f, PopupFadeDuration);
            resultScreenGo.SetActive(false);
            onConfirm?.Invoke();
        }

        IEnumerator FadeCanvasGroupRoutine(CanvasGroup cg, float from, float to, float duration)
        {
            bool skip = false;
            var playback = new DelegatePlayback(() => skip = true);
            PlaybackDirector.Instance?.Register(playback);

            float t = 0f;
            cg.alpha = from;
            while (t < duration && !skip)
            {
                t += Time.deltaTime;
                cg.alpha = Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / duration));
                yield return null;
            }
            cg.alpha = to;

            PlaybackDirector.Instance?.Unregister(playback);
        }

        /// <summary>Intro/MissionComplete/MissionFailed/ZoneComplete가 전부 공유하는 확인 팝업 - 제목/본문/
        /// 버튼 문구/확인 콜백만 다르다. 버튼을 누르기 전까지는 화면을 덮어 입력을 차단한다(Overlay의
        /// blocksInput=true 배경 재사용).</summary>
        void ShowGatedPopup(string title, Color titleColor, string body, string buttonLabel, Action onConfirm)
        {
            overlayGo.SetActive(true);
            overlayCanvasGroup.alpha = 0f;
            overlayTitleText.text = title;
            overlayTitleText.color = titleColor;
            overlayDescText.text = body;
            overlayButtonGo.SetActive(true);
            overlayButtonLabel.text = buttonLabel;
            overlayButton.interactable = true;
            overlayButton.onClick.RemoveAllListeners();
            overlayButton.onClick.AddListener(() => StartCoroutine(ConfirmPopupRoutine(onConfirm)));
            StartCoroutine(FadePopupIn());
        }

        IEnumerator FadePopupIn()
        {
            bool skip = false;
            var playback = new DelegatePlayback(() => skip = true);
            PlaybackDirector.Instance?.Register(playback);

            float t = 0f;
            while (t < PopupFadeDuration && !skip)
            {
                t += Time.deltaTime;
                float e = Mathf.SmoothStep(0f, 1f, t / PopupFadeDuration);
                overlayCanvasGroup.alpha = e;
                overlayBox.localScale = Vector3.one * Mathf.Lerp(PopupScaleIn, 1f, e);
                yield return null;
            }
            overlayCanvasGroup.alpha = 1f;
            overlayBox.localScale = Vector3.one;

            PlaybackDirector.Instance?.Unregister(playback);
        }

        /// <summary>버튼 클릭 직후 호출된다 - 중복 클릭 방지를 위해 즉시 비활성화한 뒤 짧게 페이드 아웃하고,
        /// 팝업을 완전히 숨긴 다음에야 실제 진행(onConfirm)을 실행한다.</summary>
        IEnumerator ConfirmPopupRoutine(Action onConfirm)
        {
            overlayButton.interactable = false;

            float t = 0f;
            while (t < PopupFadeDuration)
            {
                t += Time.deltaTime;
                float e = Mathf.SmoothStep(0f, 1f, t / PopupFadeDuration);
                overlayCanvasGroup.alpha = 1f - e;
                overlayBox.localScale = Vector3.one * Mathf.Lerp(1f, PopupScaleOut, e);
                yield return null;
            }
            overlayCanvasGroup.alpha = 0f;
            overlayGo.SetActive(false);
            overlayBox.localScale = Vector3.one;

            onConfirm?.Invoke();
        }

        /// <summary>Main Menu -> 게임 시작 -> Zone01(City) -> Zone Intro Popup -> Mission 시작 순서의
        /// 마지막 단계 - Intro Popup이 끝나 입력이 가능해지는 바로 그 시점에 시작한다. Zone01(첫 구역)이고
        /// 아직 완료 기록이 없는 최초 플레이에서만 실행되며, 두 번째 플레이부터는 아무 것도 하지 않는다.</summary>
        void MaybeStartTutorial()
        {
            if (TutorialController.IsCompleted) return;
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "City") return;

            var tutorial = gameObject.AddComponent<TutorialController>();
            tutorial.Begin(installer, targeting, this, canvasRoot, koreanFont);
        }

        void EnsurePlaybackDirector()
        {
            if (PlaybackDirector.Instance == null) gameObject.AddComponent<PlaybackDirector>();
        }

        void PulseOnce(TMP_Text text) => StartCoroutine(PulseGraphic(text, AccentColor, Color.white, 0.3f));

        /// <summary>카드 선택 여부와 무관하게 항상 보이는 배너로 짧게 메시지를 띄운다 -
        /// 잘못된 클릭 사유, 실행 직후 "결과를 확인하세요" 안내에 쓰인다.</summary>
        void ShowTransientNotice(string message, Color color)
        {
            if (feedbackRoutine != null) StopCoroutine(feedbackRoutine);
            feedbackRoutine = StartCoroutine(TransientNoticeRoutine(message, color));
        }

        IEnumerator TransientNoticeRoutine(string message, Color color)
        {
            feedbackText.text = message;
            feedbackText.color = color;
            feedbackGo.SetActive(true);
            feedbackCanvasGroup.alpha = 0f;

            const float fade = 0.2f;
            float t = 0f;
            while (t < fade) { t += Time.deltaTime; feedbackCanvasGroup.alpha = Mathf.SmoothStep(0f, 1f, t / fade); yield return null; }
            feedbackCanvasGroup.alpha = 1f;

            yield return new WaitForSeconds(1.4f);

            t = 0f;
            while (t < fade) { t += Time.deltaTime; feedbackCanvasGroup.alpha = 1f - Mathf.SmoothStep(0f, 1f, t / fade); yield return null; }
            feedbackCanvasGroup.alpha = 0f;

            feedbackGo.SetActive(false);
            feedbackRoutine = null;
        }

        IEnumerator PulseGraphic(Graphic graphic, Color flashColor, Color normalColor, float duration)
        {
            bool skip = false;
            var playback = new DelegatePlayback(() => skip = true);
            PlaybackDirector.Instance?.Register(playback);

            graphic.color = flashColor;
            float t = 0f;
            while (t < duration && !skip)
            {
                t += Time.deltaTime;
                graphic.color = Color.Lerp(flashColor, normalColor, t / duration);
                yield return null;
            }
            graphic.color = normalColor;

            PlaybackDirector.Instance?.Unregister(playback);
        }

        // ------------------------------------------------------------ 우측 패널 상태 (section 3/7)

        /// <summary>우측 패널 상태를 바꾸는 유일한 지점 - 다른 스크립트가 npcProfileGo/logPanelGo를
        /// 직접 SetActive하지 않는다(section 7 요구사항). 항상 이 메서드 하나만 거친다.</summary>
        void SetHudPanelState(HudPanelState state)
        {
            panelState = state;
            if (npcProfileGo != null) npcProfileGo.SetActive(state == HudPanelState.Profile);
            if (logPanelGo != null) logPanelGo.SetActive(state == HudPanelState.Log);

            if (profileTabIndicator != null) profileTabIndicator.color = state == HudPanelState.Profile ? TabActiveColor : TabInactiveColor;
            if (logTabIndicator != null) logTabIndicator.color = state == HudPanelState.Log ? TabActiveColor : TabInactiveColor;

            // Profile/Log 상태에서는 월드 하단 힌트 바가 우측 패널을 가리지 않도록 폭을 좁힌다 -
            // Default 상태에서는 원래 전체 폭으로 복원한다.
            bool rightPanelOpen = state != HudPanelState.Default;
            float targetMaxX = rightPanelOpen ? RightPanelLeftEdge - HintBarRightMargin : bottomPanelFullMaxX;
            if (bottomPanelRect != null)
                bottomPanelRect.anchorMax = new Vector2(Mathf.Min(targetMaxX, bottomPanelFullMaxX), bottomPanelRect.anchorMax.y);

            float feedbackTargetMaxX = rightPanelOpen ? RightPanelLeftEdge - HintBarRightMargin : feedbackBannerFullMaxX;
            if (feedbackBannerRect != null)
                feedbackBannerRect.anchorMax = new Vector2(Mathf.Min(feedbackTargetMaxX, feedbackBannerFullMaxX), feedbackBannerRect.anchorMax.y);
        }

        void OnProfileTabClicked() => SetHudPanelState(panelState == HudPanelState.Profile ? HudPanelState.Default : HudPanelState.Profile);
        void OnLogTabClicked() => SetHudPanelState(panelState == HudPanelState.Log ? HudPanelState.Default : HudPanelState.Log);

        void RefreshAll()
        {
            RefreshHeader();
            RefreshMission();
            RefreshOwnedInformation();
            RefreshBottomPanel();
            RefreshNpcProfile();
        }

        void RefreshHeader()
        {
            var turns = installer.Turns;
            int missionShown = Mathf.Min(turns.CurrentTurn, turns.MaxTurns);
            missionTurnText.text = $"{missionShown} /{turns.MaxTurns}";

            int stageShown = Mathf.Min(turns.StageTurn, turns.StageMaxTurns);
            stageTurnText.text = $"스테이지 진행 {stageShown}/{turns.StageMaxTurns}";

            if (stageNumberText != null) stageNumberText.text = installer.StageAsset != null ? $"STAGE {installer.StageAsset.stageNumber}" : "";
            if (stageNameText != null) stageNameText.text = installer.StageAsset != null ? installer.StageAsset.stageName : "";
        }

        void OnInformationAcquired(InformationAcquiredEvent e)
        {
            if (e.Cards.Count == 0) return;
            string message = e.IsInitialSupply
                ? $"정보 {e.Cards.Count}개를 획득했습니다."
                : $"보유 정보가 부족해 새로운 정보 {e.Cards.Count}개를 획득했습니다.";
            ShowTransientNotice(message, AccentColor);
        }

        /// <summary>플레이어에게는 지금 진행 중인 미션 하나만 보여준다. MissionData를 화면 표시와 판정의
        /// 단일 데이터 원본으로 삼는다 - 제목/설명/조건별 체크박스/남은 턴/다음 미션 예고를 전부 여기서
        /// MissionData 필드를 그대로 읽어 그린다. GameInstaller.mission(싱글 필드)은 씬 전환 오작동을 막기
        /// 위해 항상 완료되지 않는 더미로 배선돼 있으므로, 실제 표시 대상은
        /// ProgressionController.CurrentObjective()다.</summary>
        void RefreshMission()
        {
            var pc = ProgressionController.Instance;
            var objective = pc != null ? pc.CurrentObjective() : null;

            ClearMissionConditionRows();

            if (objective == null)
            {
                missionTitleText.text = "";
                missionDescText.text = "";
                missionTurnsText.text = "";
                nextMissionText.text = "";
                if (nextMissionCardRoot != null) nextMissionCardRoot.SetActive(false);
                if (nextMissionConnectorGo != null) nextMissionConnectorGo.SetActive(false);
                return;
            }

            missionTitleText.text = objective.displayTitle;
            missionDescText.text = objective.objectiveText;

            var upcoming = FindNextMission(objective);
            bool hasUpcoming = upcoming != null;
            if (hasUpcoming)
            {
                if (nextMissionCardTitleText != null) nextMissionCardTitleText.text = upcoming.displayTitle;
                if (nextMissionCardDescText != null) nextMissionCardDescText.text = upcoming.objectiveText;
            }
            if (nextMissionCardRoot != null) nextMissionCardRoot.SetActive(hasUpcoming);
            if (nextMissionConnectorGo != null) nextMissionConnectorGo.SetActive(hasUpcoming);

            var context = new MissionEvaluationContext(installer.Locations, installer.Npcs, installer.Turns.DeliveredInformationCards);
            if (objective.successConditions != null)
            {
                foreach (var condition in objective.successConditions)
                {
                    if (condition == null) continue;
                    bool met = condition.GetCurrentProgress(context) >= condition.TargetCount;
                    string label = string.IsNullOrEmpty(condition.displayLabel) ? condition.name : condition.displayLabel;
                    AddMissionConditionRow(objective.displayTitle, label, met, missionConditionRows.Count);
                }
            }

            int remaining = Mathf.Max(0, installer.Turns.MaxTurns - installer.Turns.CurrentTurn + 1);
            missionTurnsText.text = $"남은 턴: {remaining}";

            nextMissionText.text = string.IsNullOrEmpty(objective.nextMissionTitle)
                ? ""
                : objective.isHiddenUntilUnlocked
                    ? "다음 미션: ???"
                    : $"다음 미션: {objective.nextMissionTitle}";

            PulseOnce(missionTitleText);
        }

        /// <summary>GOAL2 카드(다음 미션 미리보기) 전용 조회 - StageAsset.missions 배열에서 현재 미션
        /// 바로 다음 항목을 찾는다. 판정 로직에는 관여하지 않는 순수 표시용 조회다. 스테이지의 마지막
        /// 미션이면(다음이 없으면) null을 돌려주고, 호출부에서 GOAL2 카드/연결 이미지를 숨긴다.</summary>
        MissionData FindNextMission(MissionData current)
        {
            var stageAsset = installer.StageAsset;
            var missions = stageAsset != null ? stageAsset.missions : null;
            if (missions == null) return null;

            int index = Array.IndexOf(missions, current);
            if (index < 0 || index + 1 >= missions.Length) return null;
            return missions[index + 1];
        }

        /// <summary>DestroyImmediate를 쓴다 - Destroy()는 프레임 끝까지 파괴를 미루므로, 같은 프레임 안에
        /// RefreshMission()이 두 번 이상 호출되면(예: 미션 완료 직후 ConfirmMissionComplete까지 같은 호출
        /// 흐름에서 이어지는 경우) 이전 행이 실제로는 아직 살아있어 새 행과 함께 중복 표시된다.</summary>
        void ClearMissionConditionRows()
        {
            foreach (var row in missionConditionRows) DestroyImmediate(row);
            missionConditionRows.Clear();
        }

        /// <summary>Goal 카드 스택(가이드 배치가이드 _ 기본) 세로 겹침 간격 - GOAL2 카드 상단이
        /// GOAL1 카드 상단보다 이만큼 아래에 오도록 배치해 클립으로 고정된 카드 더미처럼 보이게 한다.</summary>
        const float GoalCardStackOffsetY = 95f;

        /// <summary>미션 조건 한 줄(section 9) - MissionConditionRowView 프리팹을 Instantiate해 Goal
        /// 카드 스프라이트를 슬롯 순서대로 순환 배정하고, 조건 충족 시 성공 배지를 켠다. 배지 아트가
        /// 없으면(스킨 미배선) 기존 "[X]/[ ]" ASCII 접두사로 폴백한다. ConditionsList는 더 이상
        /// VerticalLayoutGroup을 쓰지 않고(가이드는 자동 정렬이 아니라 겹쳐진 카드 스택) 슬롯 순서로
        /// anchoredPosition을 직접 내려 쌓는다.</summary>
        void AddMissionConditionRow(string missionTitle, string label, bool met, int slotIndex)
        {
            var row = Instantiate(missionConditionRowPrefab, missionConditionsRoot);

            Sprite cardSprite = skin != null
                ? (slotIndex % 3 == 0 ? skin.goalCard1 : slotIndex % 3 == 1 ? skin.goalCard2 : skin.goalCard3)
                : null;
            if (row.Background != null && cardSprite != null) row.Background.sprite = cardSprite;

            bool hasBadge = skin != null && skin.successBadge != null;
            if (hasBadge && row.BadgeImage != null) row.BadgeImage.sprite = skin.successBadge;
            string displayLabel = hasBadge ? label : (met ? "[X] " : "[ ] ") + label;
            row.Bind(slotIndex + 1, missionTitle, displayLabel, hasBadge, met);
            // Goal 카드 아트(goalCard1/2/3, 예: "Goal 1 UI 수정.png") 자체에 "GOAL N" 라벨이 이미
            // 스탬프로 그려져 있다 - 별도 TMP GoalTag를 얹으면 같은 텍스트가 두 번 겹쳐 보인다
            // (아트 위 스탬프 + 우리 텍스트). 아트에 라벨이 없는 폴백(카드 아트 없음) 상황만 대비해
            // 텍스트값은 계속 Bind에서 채워 두되, 아트가 배정된 경우엔 중복 렌더를 막기 위해 숨긴다.
            row.SetGoalTagVisible(cardSprite == null);

            var rowRt = row.GetComponent<RectTransform>();
            rowRt.anchorMin = new Vector2(0f, 1f);
            rowRt.anchorMax = new Vector2(0f, 1f);
            rowRt.pivot = new Vector2(0f, 1f);
            rowRt.anchoredPosition = new Vector2(0f, -slotIndex * GoalCardStackOffsetY);
            row.transform.SetSiblingIndex(0); // 나중 카드가 앞서 쌓인 카드를 가리지 않도록 뒤로(먼저 그려지게)

            missionConditionRows.Add(row.gameObject);
        }

        // ------------------------------------------------------------ NPC 조사 파일 패널 (section 6)

        /// <summary>WorldPresenter.NpcClicked 구독 - TargetingController의 전달 대상 지정과 무관하게
        /// 항상 클릭한 NPC의 조사 파일을 연다(카드 선택 여부와 상관없이 동작).</summary>
        void OnNpcClickedForProfile(NpcData npcData)
        {
            if (!installer.Npcs.TryGetValue(npcData, out var state)) return;
            selectedNpcState = state;
            RefreshNpcProfile();
        }

        void RefreshNpcProfile()
        {
            ClearNpcRelationshipRows();
            if (npcNoneStickerGo != null) npcNoneStickerGo.SetActive(selectedNpcState == null);

            if (selectedNpcState == null)
            {
                npcNameText.text = "";
                npcBasicInfoText.text = "";
                npcBeliefTierText.text = "";
                npcBeliefDialogueText.text = "";
                npcHistoryText.text = "";
                if (npcJudgmentTendencyText != null) npcJudgmentTendencyText.text = "";
                if (npcPriorityText != null) npcPriorityText.text = "";
                if (npcSensitiveInfoText != null) npcSensitiveInfoText.text = "";
                if (npcRelationTendencyText != null) npcRelationTendencyText.text = "";
                if (npcTrustJudgmentText != null) npcTrustJudgmentText.text = "";
                return;
            }

            var data = selectedNpcState.Data;
            npcNameText.text = data.displayName;
            if (npcJudgmentTendencyText != null) npcJudgmentTendencyText.text = data.judgmentTendencyTag;
            if (npcPriorityText != null) npcPriorityText.text = data.priorityTag;
            if (npcSensitiveInfoText != null) npcSensitiveInfoText.text = data.sensitiveInfoTag;
            if (npcRelationTendencyText != null) npcRelationTendencyText.text = data.relationTendencyTag;
            if (npcTrustJudgmentText != null) npcTrustJudgmentText.text = data.trustJudgmentTag;
            // section 3 - NpcData에 나이 필드가 없다(Frozen 스키마 변경 금지) - 임의 생성 대신 "—".
            // 믿음 단계 바(아트에 고정) 바로 위 여백이 좁아 4줄이 들어가지 않는다 - 폰트를 줄이는 대신
            // 2줄로 합쳐 같은 크기 그대로 공간에 맞춘다.
            npcBasicInfoText.text = $"나이: — · 성별: {data.gender}\n직업: {data.job} · 소속: {data.affiliation}";

            string tag = CurrentBeliefTag(selectedNpcState, out string koreanLabel);
            npcBeliefTierText.text = koreanLabel;

            string dialogueLine = "";
            if (data is MajorNpcData majorForDialogue && majorForDialogue.beliefDialogues != null && tag != null)
            {
                var line = Array.Find(majorForDialogue.beliefDialogues, d => d != null && d.contextTag == tag);
                if (line != null) dialogueLine = line.text;
            }
            npcBeliefDialogueText.text = dialogueLine;

            if (data is MajorNpcData majorForRel && majorForRel.relationships != null)
            {
                foreach (var rel in majorForRel.relationships)
                {
                    if (rel.other == null) continue;
                    string label = string.IsNullOrEmpty(rel.relationshipTypeLabel) ? "" : rel.relationshipTypeLabel;
                    string desc = string.IsNullOrEmpty(rel.relationshipDescription) ? "" : rel.relationshipDescription;
                    AddNpcRelationshipRow($"{rel.other.displayName}  ·  {label}", desc);
                }
            }

            // 아트에 별도 Role 박스가 없어 History(자유 서술 영역)에 게임플레이 역할 요약을 첫 문단으로
            // 합쳐 넣는다 - 새 박스를 추가하지 않고 기존 영역을 재사용한다.
            var historyParts = new List<string>();
            if (!string.IsNullOrEmpty(data.gameplayRoleSummary)) historyParts.Add(data.gameplayRoleSummary);
            if (data.aiNotes != null) historyParts.AddRange(data.aiNotes);
            npcHistoryText.text = string.Join("\n\n", historyParts);
        }

        /// <summary>NpcState가 받은 정보 중 가장 최근 것에 대한 믿음 단계를 "현재 믿음"으로 삼는다 -
        /// 아직 아무 정보도 받지 않았으면 Unknown(판단 대상 없음)으로 취급한다. contextTag는
        /// RuleBasedMajorThinker.ChooseDialogue와 동일한 5단계 태그를 그대로 재사용한다.</summary>
        string CurrentBeliefTag(NpcState state, out string koreanLabel)
        {
            InformationCardData lastCard = null;
            var received = state.ReceivedInformation;
            if (received.Count > 0) lastCard = received[received.Count - 1].Card;

            var belief = lastCard != null ? state.GetBelief(lastCard) : BeliefState.Unknown;
            switch (belief)
            {
                case BeliefState.Trusted: koreanLabel = "신뢰함"; return "Trust";
                case BeliefState.Plausible: koreanLabel = "가능성 있음"; return "Possible";
                case BeliefState.NeedsVerification: koreanLabel = "확인 필요함"; return "NeedVerification";
                case BeliefState.Doubtful: koreanLabel = "의심함"; return "Doubt";
                case BeliefState.Denied: koreanLabel = "부정함"; return "Reject";
                default: koreanLabel = "판단 전"; return null;
            }
        }

        void ClearNpcRelationshipRows()
        {
            foreach (var row in npcRelationshipRows) DestroyImmediate(row);
            npcRelationshipRows.Clear();
        }

        /// <summary>NpcRelationshipRowView 프리팹을 Instantiate하고 데이터만 채운다(가이드: 관계 대상명 =
        /// SUIT BOLD 15, 설명 = SUIT REGULAR 10 - 두 자식 텍스트 구조는 프리팹에 이미 구워져 있다).</summary>
        void AddNpcRelationshipRow(string header, string desc)
        {
            var row = Instantiate(npcRelationshipRowPrefab, npcRelationshipsRoot);
            row.Bind(header, desc);
            npcRelationshipRows.Add(row.gameObject);
        }

        // ------------------------------------------------------------ 장소 정보 패널 (LocationInfoPaper)

        const float LocationNoteHorizontalMargin = 0.12f;
        const float LocationNoteVerticalLift = 0.3f;

        /// <summary>WorldPresenter.LocationHoverEnter 구독 - 커서가 장소 카드 위에 들어오면 패널을
        /// 그 장소 사진 오른쪽에 띄운다(사용자 지시로 클릭 대신 호버 트리거).</summary>
        void OnLocationHoverEnter(LocationData location)
        {
            selectedLocationData = location;
            RefreshLocationNote();
            PositionLocationNote(location);
        }

        /// <summary>커서가 나갈 때 발생 - 그 사이에 다른 장소의 HoverEnter가 먼저 도착해 있었다면
        /// (연속된 카드 사이를 빠르게 지나갈 때) 방금 연 패널을 잘못 닫지 않도록 방어한다.</summary>
        void OnLocationHoverExit(LocationData location)
        {
            if (selectedLocationData != location) return;
            selectedLocationData = null;
            RefreshLocationNote();
        }

        void RefreshLocationNote()
        {
            // 장소 정보 패널(LocationInfoPaper) - 커서가 올라간 장소가 있을 때만 보인다.
            if (locationNoteGo != null) locationNoteGo.SetActive(selectedLocationData != null);

            if (selectedLocationData == null)
            {
                locationNoteTitleText.text = "";
                locationNoteBodyText.text = "";
                return;
            }

            // Values 칸은 프리팹의 정적 Labels 칸(확산 속도/밀집도/민감 정보 유형/신뢰도 보정, 4줄)과
            // 같은 순서·줄 간격(빈 줄로 구분)으로 값만 채운다 - 라벨을 텍스트에 다시 넣지 않는다
            // (라벨은 이미 Labels 칸에 있으므로 중복 표시 방지). accessType(접근 권한)은 게임에서
            // 실제로 아무것도 막지 않게 된 지 오래라(LocationMechanicsSettings.CanTargetLocationDirectly
            // 참고) 패널에서도 아예 뺐다(사용자 지시).
            locationNoteTitleText.text = selectedLocationData.displayName;
            locationNoteBodyText.text =
                $"{selectedLocationData.spreadSpeed}\n\n" +
                $"{selectedLocationData.npcDensity}\n\n" +
                $"{selectedLocationData.sensitiveInformationType}\n\n" +
                $"{selectedLocationData.credibilityModifier}";
        }

        /// <summary>LocationInfoPaper(pivot 좌상단)의 화면 좌표를 그 장소 사진의 오른쪽 바로 옆으로
        /// 옮긴다 - HudCanvas가 Screen Space Overlay라 RectTransform.position에 화면 픽셀 좌표를
        /// 그대로 대입하면 앵커/피벗/CanvasScaler와 무관하게 정확히 그 지점에 놓인다(Overlay 캔버스의
        /// 표준 기법). 장소 사진의 실제 반폭(WorldPresenter.PhotoHalfWidth, NPC를 사진 좌우에 붙일 때와
        /// 동일한 값)만큼 월드 좌표에서 오른쪽으로 민 뒤 화면 좌표로 투영해, 카메라 줌/팬과도 항상
        /// 맞아떨어지게 한다.</summary>
        void PositionLocationNote(LocationData location)
        {
            if (locationNoteRect == null || worldPresenter == null) return;
            if (!worldPresenter.LocationViews.TryGetValue(location, out var siteView)) return;
            var cam = Camera.main;
            if (cam == null) return;

            Vector3 anchorWorld = siteView.transform.position +
                new Vector3(WorldPresenter.PhotoHalfWidth + LocationNoteHorizontalMargin, LocationNoteVerticalLift, 0f);
            Vector3 screenPos = cam.WorldToScreenPoint(anchorWorld);
            locationNoteRect.position = new Vector3(screenPos.x, screenPos.y, 0f);
        }

        /// <summary>로그 패널(section 1) - 새 저장소를 만들지 않고 installer.Log(EventLogSystem)의
        /// 기존 이벤트 구독만 재사용한다. EventLogSystem이 이미 NpcSpokeEvent/CardJudgedEvent 등을
        /// 구독해 entries에 누적하므로, 여기서는 같은 GameEventBus 이벤트를 "한 번 더" 구독해
        /// 대사/믿음변화만 별도 표시 슬롯(상단·하단 대사 카드, 수치 변동 바)에 분리해 보여준다 -
        /// entries 자체를 파싱하지 않는다(취약한 문자열 매칭 회피).</summary>
        readonly List<string> recentDialogueLines = new List<string>(2);

        void AppendLog(string _)
        {
            // 일반 사건 로그(가이드: "캐릭터의 이동, 믿음 수치 변화는 일반 텍스트로") - 대사 줄만 제외하고
            // 나머지는 installer.Log.Entries를 그대로 재사용한다. 대사 포맷("이름: \"...\"")은
            // EventLogSystem.OnNpcSpoke가 만드는 고정 포맷이라 이 필터만으로 안전하게 구분된다.
            var entries = installer.Log.Entries;
            int start = Mathf.Max(0, entries.Count - MaxLogLines);
            var sb = new StringBuilder();
            for (int i = start; i < entries.Count; i++)
            {
                var line = entries[i];
                if (line.Contains(": \"")) continue; // 대사 줄은 상/하단 카드에서 따로 보여준다
                sb.AppendLine(line);
            }
            if (logGeneralText != null) logGeneralText.text = sb.ToString();
        }

        /// <summary>NpcSpokeEvent를 EventLogSystem과 별도로 한 번 더 구독 - 최근 대사 2줄을 상단(최신)/
        /// 하단(이전) 카드에 채운다. 새 저장소가 아니라 같은 이벤트를 두 번째로 구독하는 것뿐이다.</summary>
        void OnLogNpcSpoke(NpcSpokeEvent e)
        {
            string text = e.Dialogue.IsGenerated ? e.Dialogue.GeneratedText : e.Dialogue.PredefinedLine?.text;
            if (string.IsNullOrEmpty(text)) return;

            recentDialogueLines.Insert(0, $"{e.Npc.displayName}: \"{text}\"");
            if (recentDialogueLines.Count > 2) recentDialogueLines.RemoveAt(recentDialogueLines.Count - 1);

            if (logTopDialogueText != null) logTopDialogueText.text = recentDialogueLines.Count > 0 ? recentDialogueLines[0] : "";
            if (logBottomDialogueText != null) logBottomDialogueText.text = recentDialogueLines.Count > 1 ? recentDialogueLines[1] : "";
        }

        readonly Dictionary<NpcData, BeliefState> lastKnownBelief = new Dictionary<NpcData, BeliefState>();

        /// <summary>CardJudgedEvent를 EventLogSystem과 별도로 한 번 더 구독 - 믿음 판단이 바뀔 때마다
        /// [이름] 수치 변동 사항 헤더 + 이전→이후 라벨을 갱신한다(가이드: "불신 -----> 신뢰").</summary>
        void OnLogCardJudged(CardJudgedEvent e)
        {
            lastKnownBelief.TryGetValue(e.Npc, out var previous);
            lastKnownBelief[e.Npc] = e.ResultBelief;

            if (logStatHeaderText != null) logStatHeaderText.text = $"[{e.Npc.displayName}] 수치 변동 사항";
            if (logBeliefFromText != null) logBeliefFromText.text = BeliefKoreanLabel(previous);
            if (logBeliefToText != null) logBeliefToText.text = BeliefKoreanLabel(e.ResultBelief);
        }

        static string BeliefKoreanLabel(BeliefState state) => state switch
        {
            BeliefState.Trusted => "신뢰함",
            BeliefState.Plausible => "가능성이 있다고 판단함",
            BeliefState.NeedsVerification => "확인이 필요하다고 판단함",
            BeliefState.Doubtful => "의심함",
            BeliefState.Denied => "부정함",
            _ => "불신"
        };

        /// <summary>매번 전부 파괴 후 재생성하지 않고 보유 카드 목록과 비교해 차이만 반영한다 -
        /// 새로 보유하게 된 카드만 등장 연출을, 더 이상 보유하지 않는 카드만 소멸 연출을 재생하고
        /// 그대로 남아있는 카드는 건드리지 않는다(매 갱신마다 전체가 깜빡이는 것을 방지).</summary>
        void RefreshOwnedInformation()
        {
            var owned = installer.Turns.OwnedInformationCards;
            ownedCountLabel.text = $"보유 정보: {owned.Count}   전달할 정보를 선택하세요.";

            var ownedSet = new HashSet<InformationCardData>(owned);

            List<InformationCardData> toRemove = null;
            foreach (var kvp in ownedTiles)
            {
                if (ownedSet.Contains(kvp.Key)) continue;
                (toRemove ??= new List<InformationCardData>()).Add(kvp.Key);
            }
            if (toRemove != null)
            {
                foreach (var card in toRemove)
                {
                    var tile = ownedTiles[card];
                    ownedTiles.Remove(card);

                    // 레이아웃 계산에서 제외해야 사라지는 카드가 자리를 지키는 동안 나머지 카드가
                    // 먼저 자연스럽게 당겨질 수 있다.
                    var layoutElement = tile.GetComponent<LayoutElement>();
                    if (layoutElement == null) layoutElement = tile.gameObject.AddComponent<LayoutElement>();
                    layoutElement.ignoreLayout = true;

                    tile.PlayDisappear(() => Destroy(tile.gameObject));
                }
            }

            int siblingIndex = 0;
            foreach (var card in owned)
            {
                bool isNew = !ownedTiles.TryGetValue(card, out var tile);
                if (isNew)
                {
                    tile = Instantiate(cardTilePrefab, ownedRoot);
                    tile.Clicked += OnCardClicked;
                    tile.SetHandState(CardHandState.Collapsed);
                    tile.PlayAppear();
                    ownedTiles[card] = tile;
                }
                bool isSelected = card == installer.Turns.SelectedCard;
                tile.Bind(card, isSelected);
                tile.ApplySlot(siblingIndex, owned.Count, isSelected);
                tile.transform.SetSiblingIndex(siblingIndex++);

                // 접힘/펼침(section 8): 선택된 카드 한 장만 펼쳐지고 나머지는 항상 접혀 있는다.
                // "같은 카드를 다시 눌러 접기"는 OnCardClicked에서 SelectCard 호출 전에 처리하므로
                // 여기서는 selected == true일 때만 강제로 펼치고, 접힘 유지가 필요한 카드는 건드리지 않는다.
                if (isSelected)
                {
                    // SetAsLastSibling()을 쓰면 HorizontalLayoutGroup 배치 순서까지 맨 끝으로
                    // 밀려나 카드가 실제로 자리를 옮기는 버그가 있었다(1·2번 카드를 선택하면 마지막
                    // 슬롯으로 이동) - 카드가 더 이상 겹치지 않게(음수 spacing 제거) 바뀌어서 그리기
                    // 순서를 맨 앞으로 올릴 필요 자체가 없어졌으므로 완전히 제거한다.
                    if (tile.HandState != CardHandState.Expanded)
                        tile.SetHandState(CardHandState.Expanded);
                }
                else if (tile.HandState == CardHandState.Expanded)
                {
                    tile.SetHandState(CardHandState.Collapsed);
                }
            }
        }

        bool IsInputLocked => PlaybackDirector.Instance != null && PlaybackDirector.Instance.IsPlaying;

        void OnCardClicked(InformationCardData card)
        {
            if (IsInputLocked) return;
            if (targeting.CardSelectionAllowed != null && !targeting.CardSelectionAllowed(card))
            {
                ShowTransientNotice("지금 단계에서는 다른 행동을 할 수 없습니다.", ErrorColor);
                return;
            }

            // 같은(이미 펼쳐진) 카드를 다시 누르면 접기만 한다 - TurnSystem.SelectCard는 null을
            // 받지 않아 "선택 해제" 자체를 지원하지 않으므로(기존 턴 시스템 API를 확장하지 않기 위해),
            // 시각적 접힘만 되돌리고 실제 선택 카드/전달 대상 상태는 건드리지 않는다.
            if (card == installer.Turns.SelectedCard && ownedTiles.TryGetValue(card, out var tile) && tile.HandState == CardHandState.Expanded)
            {
                tile.SetHandState(CardHandState.Collapsed);
                return;
            }

            installer.Turns.SelectCard(card);
        }

        void RefreshBottomPanel()
        {
            var card = installer.Turns.SelectedCard;
            bool hasSelection = card != null;

            cardInfoGo.SetActive(hasSelection);
            instructionGo.SetActive(hasSelection);
            noSelectionHintGo.SetActive(!hasSelection);

            if (!hasSelection) return;

            cardTitleText.text = card.information != null ? card.information.title : "?";
            cardDescText.text = card.information != null ? card.information.description : "";
            string kind = card.cardType == InfoCardType.Spread ? "확산형" : "전달형";
            string sourceName = card.source != null ? card.source.displayName : "알 수 없음";
            cardKindText.text = $"{kind}   출처: {sourceName}";

            // 전달 확정 버튼 활성 조건(section 10): 카드 선택 + 유효한 대상 + 이번 턴 미사용.
            // Phase==AwaitingConfirm은 이미 "카드 선택 + 카드 타입에 맞는 유효 대상 선택 완료"를
            // 내포하므로(TargetingController), 여기서는 턴 소진 여부만 추가로 확인한다.
            bool canDeliver = targeting.Phase == TargetingPhase.AwaitingConfirm && !installer.Turns.TurnsExhausted;

            switch (targeting.Phase)
            {
                case TargetingPhase.AwaitingTarget:
                    instructionText.text = card.cardType == InfoCardType.Spread
                        ? "정보를 전달할 장소를 선택하세요."
                        : "정보를 전달할 대상을 선택하세요.";
                    deliverButtonGo.SetActive(false);
                    break;

                case TargetingPhase.AwaitingConfirm:
                    instructionText.text = card.cardType == InfoCardType.Spread
                        ? "정보를 전달한다. (다른 장소를 클릭하면 대상을 바꿀 수 있습니다.)"
                        : "정보를 전달한다. (다른 사람을 클릭하면 대상을 바꿀 수 있습니다.)";
                    deliverButtonGo.SetActive(true);
                    if (deliverButton != null) deliverButton.interactable = canDeliver;
                    break;

                default:
                    instructionText.text = "";
                    deliverButtonGo.SetActive(false);
                    break;
            }
        }

        void OnDeliverClicked()
        {
            if (IsInputLocked) return;

            // 사용 중 카드 타일을 Using 상태로 표시해 중복 클릭을 시각적으로도 막는다.
            var card = installer.Turns.SelectedCard;
            if (card != null && ownedTiles.TryGetValue(card, out var tile))
                tile.SetHandState(CardHandState.Using);

            targeting.DeliverByInformant();
        }

        // ------------------------------------------------------------ UI 인스턴스화 (HudCanvas.prefab)

        void BuildUI()
        {
            EnsureEventSystem();
            EnsurePhysics2DRaycaster();

            canvasRoot = view.transform;

            stageTurnText = view.StageTurnText;
            missionTurnText = view.MissionTurnText;
            stageNumberText = view.StageNumberText;
            stageNameText = view.StageNameText;

            missionTitleText = view.MissionTitleText;
            missionDescText = view.MissionDescText;
            missionConditionsRoot = view.MissionConditionsRoot;
            missionTurnsText = view.MissionTurnsText;
            nextMissionText = view.NextMissionText;
            nextMissionCardRoot = view.NextMissionCardRoot;
            nextMissionCardTitleText = view.NextMissionCardTitleText;
            nextMissionCardDescText = view.NextMissionCardDescText;
            nextMissionConnectorGo = view.NextMissionConnectorGo;

            profileTabButton = view.ProfileTabButton;
            logTabButton = view.LogTabButton;
            profileTabIndicator = view.ProfileTabIndicator;
            logTabIndicator = view.LogTabIndicator;

            logPanelGo = view.LogPanelGo;
            logTopDialogueText = view.LogTopDialogueText;
            logGeneralText = view.LogGeneralText;
            logStatHeaderText = view.LogStatHeaderText;
            logBeliefFromText = view.LogBeliefFromText;
            logBeliefToText = view.LogBeliefToText;
            logBottomDialogueText = view.LogBottomDialogueText;

            locationNoteGo = view.LocationNoteGo;
            locationNoteRect = locationNoteGo != null ? locationNoteGo.GetComponent<RectTransform>() : null;
            locationNoteTitleText = view.LocationNoteTitleText;
            locationNoteBodyText = view.LocationNoteBodyText;

            npcProfileGo = view.NpcProfileGo;
            npcNameText = view.NpcNameText;
            npcBasicInfoText = view.NpcBasicInfoText;
            npcBeliefTierText = view.NpcBeliefTierText;
            npcBeliefDialogueText = view.NpcBeliefDialogueText;
            npcRelationshipsRoot = view.NpcRelationshipsRoot;
            npcHistoryText = view.NpcHistoryText;
            npcNoneStickerGo = view.NpcNoneStickerGo;
            npcJudgmentTendencyText = view.NpcJudgmentTendencyText;
            npcPriorityText = view.NpcPriorityText;
            npcSensitiveInfoText = view.NpcSensitiveInfoText;
            npcRelationTendencyText = view.NpcRelationTendencyText;
            npcTrustJudgmentText = view.NpcTrustJudgmentText;

            bottomPanelRect = view.BottomPanelRect;
            if (bottomPanelRect != null) bottomPanelFullMaxX = bottomPanelRect.anchorMax.x;

            ownedCountLabel = view.OwnedCountLabel;
            ownedRoot = view.OwnedRoot;
            cardInfoGo = view.CardInfoGo;
            cardTitleText = view.CardTitleText;
            cardDescText = view.CardDescText;
            cardKindText = view.CardKindText;
            instructionGo = view.InstructionGo;
            instructionText = view.InstructionText;
            deliverButtonGo = view.DeliverButtonGo;
            deliverButton = view.DeliverButton;
            noSelectionHintGo = view.NoSelectionHintGo;

            overlayGo = view.OverlayGo;
            overlayCanvasGroup = view.OverlayCanvasGroup;
            overlayBox = view.OverlayBox;
            overlayTitleText = view.OverlayTitleText;
            overlayDescText = view.OverlayDescText;
            overlayButtonGo = view.OverlayButtonGo;
            overlayButtonLabel = view.OverlayButtonLabel;
            overlayButton = view.OverlayButton;

            resultScreenGo = view.ResultScreenGo;
            resultCanvasGroup = view.ResultCanvasGroup;
            resultPanelImg = view.ResultPanelImg;
            resultPhotoFrameImg = view.ResultPhotoFrameImg;
            resultTitleText = view.ResultTitleText;
            resultDescText = view.ResultDescText;
            resultStageTagText = view.ResultStageTagText;
            resultTurnsText = view.ResultTurnsText;
            resultPrimaryButtonGo = view.ResultPrimaryButtonGo;
            resultSecondaryButtonGo = view.ResultSecondaryButtonGo;
            resultPrimaryButtonLabel = view.ResultPrimaryButtonLabel;
            resultSecondaryButtonLabel = view.ResultSecondaryButtonLabel;
            resultPrimaryButton = view.ResultPrimaryButton;
            resultSecondaryButton = view.ResultSecondaryButton;

            feedbackBannerRect = view.FeedbackBannerRect;
            if (feedbackBannerRect != null) feedbackBannerFullMaxX = feedbackBannerRect.anchorMax.x;

            feedbackGo = view.FeedbackGo;
            feedbackCanvasGroup = view.FeedbackCanvasGroup;
            feedbackText = view.FeedbackText;

            // 정적 리스너(팝업마다 바뀌는 overlayButton/resultPrimaryButton 등과 달리 한 번만 붙이면 됨).
            view.HelpButton.onClick.AddListener(() => howToPlayPopup?.Show());
            deliverButton.onClick.AddListener(OnDeliverClicked);
            if (profileTabButton != null) profileTabButton.onClick.AddListener(OnProfileTabClicked);
            if (logTabButton != null) logTabButton.onClick.AddListener(OnLogTabClicked);
            SetHudPanelState(HudPanelState.Default);

            howToPlayPopup = view.gameObject.AddComponent<HowToPlayPopup>();
            howToPlayPopup.Build(canvasRoot, koreanFont);
        }

        void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>();
        }

        /// <summary>월드 클릭(LocationSiteView/NpcActorView의 IPointerClickHandler)이 동작하려면
        /// EventSystem이 Collider2D도 레이캐스트 대상으로 인식해야 한다 - GraphicRaycaster는 UI Graphic만
        /// 담당하므로 Main Camera에 Physics2DRaycaster가 별도로 있어야 한다. Scene에 이미 있으면 그대로 둔다.</summary>
        void EnsurePhysics2DRaycaster()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                Debug.LogWarning("HudPresenter: Main Camera를 찾을 수 없어 Physics2DRaycaster를 추가하지 못했습니다. 장소/NPC 클릭이 동작하지 않을 수 있습니다.");
                return;
            }
            if (cam.GetComponent<Physics2DRaycaster>() != null) return;
            cam.gameObject.AddComponent<Physics2DRaycaster>();
        }
    }
}
