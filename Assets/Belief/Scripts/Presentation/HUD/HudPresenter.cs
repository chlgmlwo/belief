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

        // 예전엔 12줄이었는데 실제 GeneralLogText 박스가 그 정도 줄 수를 담을 만큼 크지 않아(1줄
        // 높이로 디자인돼 있었음) 아래 Divider/ValueChangeBar/Trust 화살표 위로 텍스트가 흘러넘쳐
        // 겹쳐 보이던 원인이었다(2026-08-04) - 박스를 넉넉히 키우고(그만큼 아래 요소들도 함께 내림),
        // 그 박스가 실제로 담을 수 있는 줄 수에 맞춰 5로 줄였다. RectMask2D도 안전장치로 추가해
        // 혹시 유난히 긴 한 줄이 껴도 박스 밖으로는 절대 안 나가게 막는다.
        const int MaxLogLines = 5;

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
        TMP_Text missionConditionsText;
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

        // 탭 위 빨간 점 - "닫아 둔 동안 이 문서에 새 내용이 생겼다"만 뜻한다. 문서를 열어 보면
        // 그 순간 꺼진다. 열려 있는 동안 들어온 내용은 이미 눈앞에 보이므로 점을 켜지 않는다.
        GameObject profileTabBadgeGo;
        GameObject logTabBadgeGo;
        /// <summary>문서를 실제로 밀어 넣고 빼는 쪽 - 탭 버튼과 별개로 코드에서도 열어야 해서 잡아 둔다
        /// (NPC 호버로 조사 파일 자동 열기). 이 프레젠터의 panelState와 짝을 맞춰 함께 움직인다.</summary>
        Mockup.RightDocumentPanelController rightDocumentPanel;
        bool logUnseen;
        bool profileUnseen;
        // 프로필은 로그처럼 "줄이 늘었다"로 판단할 수 없다 - 같은 NPC라도 믿음 단계나 관계가 바뀌면
        // 다시 볼 가치가 있고, 그냥 다시 그려졌을 뿐이면 아니다. 그래서 화면에 실제로 찍히는 값들을
        // 한 줄로 이어 마지막으로 본 것과 비교한다.
        string seenProfileSignature = "";

        // 월드 하단 힌트 바(BottomPanel/FeedbackBanner) - Profile/Log 패널이 열리면 우측 패널
        // 왼쪽 경계(NpcProfilePanel/LogPanel의 anchorMin.x) 전까지만 폭을 좁힌다(section 신규 지시).
        RectTransform bottomPanelRect;
        RectTransform feedbackBannerRect;
        float bottomPanelFullMaxX;
        float feedbackBannerFullMaxX;
        float feedbackBannerFullMinX;
        const float RightPanelLeftEdge = 0.68f;
        const float HintBarRightMargin = 0.02f;

        GameObject logPanelGo;
        TMP_Text logTopDialogueText;
        TMP_Text logGeneralText;
        TMP_Text logStatHeaderText;
        // 눈금 양 끝 라벨("불신"/"신뢰")은 시안대로 고정이라 코드가 건드리지 않는다 - 대신
        // 표식(화살표 머리)을 눈금선 위에서 옮겨 현재 믿음 위치를 나타낸다(ShowBeliefChange).
        RectTransform logTrustArrowHead;
        RectTransform logTrustArrowLine;
        TMP_Text logTrustLowLabel;
        TMP_Text logTrustHighLabel;
        RectTransform logTrustPrevMarker;
        RectTransform logTrustDeltaSegment;
        TMP_Text logBottomDialogueText;
        NpcState lastLoggedNpcState;

        TMP_Text ownedCountLabel;
        Transform ownedRoot;
        readonly Dictionary<InformationCardData, CardTileView> ownedTiles = new Dictionary<InformationCardData, CardTileView>();

        GameObject cardInfoGo;
        TMP_Text cardTitleText, cardDescText, cardKindText;

        // 하단 안내 띠(3-85) - 예전에는 안내가 세 군데로 흩어져 있었다: 이 띠, 지도 한가운데 떠 있던
        // FeedbackBanner, 그리고 손패 위 NoSelectionHint. 셋을 이 띠 하나로 합쳤다. 판(barBackgroundGo)은
        // 지속 안내와 일시 알림이 공유하고, 알림이 뜬 동안에는 지속 안내를 잠시 감춰 글자가 겹치지 않게 한다.
        GameObject barBackgroundGo;
        GameObject instructionGo;
        TMP_Text instructionText;
        GameObject deliverButtonGo;
        Button deliverButton;
        GameObject noSelectionHintGo;
        string barInstruction = "";
        bool noticeShowing;

        // 장소 정보 패널(LocationInfoPaper) - LocationData의 콘텐츠 필드를 enum 그대로 표시한다.
        GameObject locationNoteGo;
        RectTransform locationNoteRect;
        TMP_Text locationNoteTitleText;
        TMP_Text locationNoteBodyText;
        LocationData selectedLocationData;

        // NPC 조사 파일 패널(section 6) - 공용 패널 하나, 클릭한 NPC로 내용만 교체한다.
        GameObject npcProfileGo;
        GameObject npcPortraitFrameGo;
        Image npcPortraitImage;
        TMP_Text npcNameText;
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
        ResultScreenLayout resultLayout;
        /// <summary>마지막으로 HUD에 떠 있던 미션 - 구역이 전부 끝나면 CurrentObjective()가 null이 되므로
        /// 결과 리포트가 참조할 미션을 여기서 유지한다.</summary>
        MissionData lastKnownObjective;
        TMP_Text resultTitleText, resultDescText, resultMissionNoText;
        TMP_Text resultStageLabelText, resultStageTagText, resultTurnsText;
        GameObject resultPrimaryButtonGo, resultSecondaryButtonGo;
        /// <summary>진행 버튼(NEXT/RETRY)의 문구는 아트에 인쇄돼 있어 라벨이 없다 - 보조 버튼만 라벨을 쓴다.</summary>
        TMP_Text resultSecondaryButtonLabel;
        Button resultPrimaryButton, resultSecondaryButton;
        ResultTabHoverFeedback resultPrimaryTabHover;
        /// <summary>결과 리포트가 떠 있는 동안에는 일시정지로 빠져나갈 수 없어야 한다 - 여기서
        /// 할 수 있는 행동은 NEXT/RETRY와 메인 화면뿐이다.</summary>
        PauseMenuController pauseMenu;

        GameObject feedbackGo;
        CanvasGroup feedbackCanvasGroup;
        TMP_Text feedbackText;
        Coroutine feedbackRoutine;

        HowToPlayPopup howToPlayPopup;
        Transform canvasRoot;

        /// <summary>TutorialController가 카드 타일을 반복 Highlight하기 위해 읽는다.</summary>
        public IEnumerable<CardTileView> OwnedCardTiles => ownedTiles.Values;

        /// <summary>TutorialController가 "정보원에게 전달" 버튼을 강조하기 위해 읽는다 - 접선 지점이
        /// 있는 스테이지에서는 이 버튼이 항상 꺼져 있으므로 아래 ContactPointView 쪽이 쓰인다.</summary>
        public GameObject DeliverButtonGo => deliverButtonGo;

        /// <summary>TutorialController가 지도 위 "전달" 태그를 강조하기 위해 읽는다.</summary>
        public Presentation.World.LocationSiteView ContactPointView =>
            worldPresenter != null ? worldPresenter.ContactPointView : null;

        void Start()
        {
            EnsurePlaybackDirector();
            BuildUI();

            var bus = installer.EventBus;
            // 예전엔 여기서 턴 숫자를 강조색(민트)으로 한 번 번쩍이게 했다 - 턴마다 미션 글자가
            // 초록으로 깜빡여 거슬린다는 지적으로 제거. 숫자는 어차피 값이 바뀌어 눈에 띈다.
            bus.Subscribe<TurnStartedEvent>(_ => RefreshAll());
            bus.Subscribe<CardSelectedEvent>(_ => RefreshAll());
            // 예전엔 여기서 "결과를 확인하세요." 알림을 띄웠지만 3-85에서 삭제 - 무엇을 확인하라는
            // 건지도 모호했고, 카드를 낸 직후에는 NPC 대사/로그가 이미 결과를 말해 준다.
            bus.Subscribe<CardPlayedEvent>(_ => RefreshAll());
            bus.Subscribe<InformationAcquiredEvent>(OnInformationAcquired);
            // 미션 자체가 교체됐다는 직접 신호 - ObjectivesChanged/TurnStartedEvent 경로와 별개로
            // MissionSystem.LoadMission이 발행하는 즉시 미션 패널을 완전히 재구성한다.
            bus.Subscribe<MissionChangedEvent>(_ => RefreshMission());
            bus.Subscribe<GameOverEvent>(e => StartCoroutine(WaitForPlaybackThen(() => { if (e.Won) ShowFinalVictory(); else ShowMissionFailedPopup(); })));
            installer.Log.OnLogAdded += AppendLog;
            bus.Subscribe<NpcSpokeEvent>(OnLogNpcSpoke);
            bus.Subscribe<CardJudgedEvent>(OnLogCardJudged);
            targeting.PhaseChanged += RefreshBottomPanel;
            // 손패도 함께 다시 그린다 - "사용 중" 잠금이 targeting.IsDelivering에서 계산되므로,
            // 전달이 시작·종료될 때 이 신호를 받지 못하면 잠긴 카드가 그대로 남는다.
            targeting.PhaseChanged += RefreshOwnedInformation;
            // "정보 전파중" 표시는 여기서 걸지 않는다 - IsDelivering은 판단이 끝난 뒤의 연출까지
            // 포함하는 구간이라, 그 값에 맞추면 NPC가 말하고 움직이는 동안에도 표시가 남는다.
            // 실제로 응답을 기다리는 동안만 켜지도록 Update에서 LlmRequestMonitor를 본다.
            targeting.InteractionRejected += msg => ShowTransientNotice(msg, ErrorColor);

            // NPC/장소 조사용 클릭 구독 - TargetingController가 같은 이벤트를 전달 대상 지정용으로
            // 이미 소비하고 있지만, WorldPresenter.NpcClicked/LocationClicked는 멀티캐스트라 여기서
            // 순수 조회(조사 파일/특성 메모 표시)용으로 추가 구독해도 기존 전달 흐름과 충돌하지 않는다.
            if (worldPresenter != null)
            {
                worldPresenter.NpcClicked += OnNpcClickedForProfile;
                worldPresenter.LocationHoverEnter += OnLocationHoverEnter;
                worldPresenter.LocationHoverExit += OnLocationHoverExit;
                // 지도 위 접선 지점의 "전달" 태그가 예전 하단 전달 버튼을 대신한다.
                worldPresenter.ContactPointClicked += OnDeliverClicked;
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
        /// 작전 성공 리포트를 띄우고 아트의 NEXT를 눌렀을 때만 ProgressionController에 실제 전환을 맡긴다.
        /// (예전엔 단색 "MISSION COMPLETE" 오버레이를 썼으나, 전용 성공 아트가 들어오면서 교체했다.)</summary>
        void OnObjectiveCompletedPending(MissionData completed) =>
            StartCoroutine(WaitForPlaybackThen(() =>
                ShowResultScreen(true, completed, () => ProgressionController.Instance?.ConfirmMissionComplete(),
                    null, null)));

        /// <summary>구역의 마지막 목표가 완료됐지만 아직 확인 대기 중일 때 호출된다 - 같은 성공 리포트를
        /// 띄우되 NEXT가 다음 구역 로드로 이어진다. 이 시점엔 남은 목표가 없어 CurrentObjective()가
        /// null이므로, 방금 완료된 미션을 이벤트 인자로 받아 쓴다.</summary>
        void OnStageCompletedPending(MissionData completed)
        {
            var pc = ProgressionController.Instance;
            StartCoroutine(WaitForPlaybackThen(() =>
                ShowResultScreen(true, completed ?? lastKnownObjective, () => pc?.ConfirmZoneComplete(), null, null)));
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

        /// <summary>작전 결과 화면(section 13) - 미션 성공/구역 완료/최종 승리는 성공 리포트로,
        /// 턴 소진은 실패 리포트로 뜬다. 진행(NEXT/RETRY)은 아트에 이미 인쇄돼 있어 라벨 텍스트를
        /// 따로 넣지 않고 그 자리에 투명 버튼만 겹쳐 둔다.</summary>
        void ShowMissionFailedPopup()
        {
            var pc = ProgressionController.Instance;
            ShowResultScreen(false, CurrentOrLastObjective(pc), () => pc?.RestartCurrentMission(),
                "메인 화면", GoToMainMenu);
        }

        /// <summary>마지막 구역(대도시)까지 끝냈을 때의 엔딩 - 성공 리포트를 그대로 쓰되 남는 행동이
        /// [메인 화면] 하나뿐이다. 이 시점엔 갈 곳이 없어 NEXT가 있으면 안 되므로, 아트에 인쇄된 탭은
        /// 글자 없는 탭으로 덮는다(ShowResultScreen이 onPrimary가 없는 것을 보고 처리한다).
        ///
        /// 제목·설명은 미션 데이터 대신 마무리 문구로 덮어쓴다 - 마지막 미션의 목표문을 그대로 두면
        /// "아직 할 일이 남았다"로 읽힌다.</summary>
        void ShowFinalVictory()
        {
            var pc = ProgressionController.Instance;
            ShowResultScreen(true, CurrentOrLastObjective(pc), null, "메인 화면", GoToMainMenu);

            if (resultTitleText != null) resultTitleText.text = EndingTitle;
            if (resultDescText != null) resultDescText.text = EndingMessage;
        }

        const string EndingTitle = "작전 종료";
        const string EndingMessage = "데모 버전을 플레이해 주셔서 감사합니다.";

        /// <summary>구역의 모든 목표가 끝난 뒤(최종 승리 등)에는 CurrentObjective()가 null이 되므로,
        /// 마지막으로 화면에 떠 있던 미션을 대신 쓴다.</summary>
        MissionData CurrentOrLastObjective(ProgressionController pc) =>
            pc?.CurrentObjective() ?? lastKnownObjective;

        void GoToMainMenu()
        {
            if (ScreenFader.Instance != null) ScreenFader.Instance.LoadScene("MainMenu");
            else UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
        }

        /// <summary>리포트에 들어가는 값은 전부 지금 진행 중인 미션/구역 데이터에서 그대로 읽는다 -
        /// 화면 전용 문구를 따로 만들지 않는다. 실패 설명만은 미션 목표문을 그대로 쓰면 "달성했다"로
        /// 읽히므로 실패 사유를 쓴다.</summary>
        void ShowResultScreen(bool won, MissionData mission, Action onPrimary,
            string secondaryLabel, Action onSecondary)
        {
            resultScreenGo.SetActive(true);
            resultCanvasGroup.alpha = 0f;

            // 리포트가 뜨면 일시정지 입구를 닫는다 - 열려 있었다면 함께 닫히며 시간도 되돌아온다.
            if (pauseMenu != null) pauseMenu.SetAvailable(false);

            // 리포트가 뜨는 순간 플레이용 입력 자리를 감춘다 - 여기서 할 수 있는 행동은 RETRY와
            // 메인 화면뿐이다. RefreshBottomPanel은 이 시점에 다시 불리지 않으므로 직접 부른다.
            SetDeliverAffordance(false, false);
            SetBarInstruction("");

            if (resultLayout != null) resultLayout.Apply(won);

            if (resultPanelImg != null && skin != null)
                resultPanelImg.sprite = won ? skin.successPanel : skin.failurePanel;
            if (resultPhotoFrameImg != null && skin != null)
                resultPhotoFrameImg.sprite = won ? skin.successPhotoFrame : skin.failurePhotoFrame;

            var stage = installer != null ? installer.StageAsset : null;
            var pc = ProgressionController.Instance;

            if (resultTitleText != null)
                resultTitleText.text = mission != null ? mission.displayTitle : "";
            if (resultDescText != null)
                resultDescText.text = won
                    ? (mission != null ? mission.objectiveText : "")
                    : "제한 기간 안에 목표를 달성하지 못했다.";
            if (resultMissionNoText != null)
                resultMissionNoText.text = $"NO. {MissionNumber(stage, mission):000}";

            int stageNumber = stage != null && stage.stageNumber > 0
                ? stage.stageNumber
                : (pc != null ? pc.Progress.CurrentStageIndex + 1 : 1);
            if (resultStageLabelText != null) resultStageLabelText.text = $"STAGE {stageNumber}";
            if (resultStageTagText != null)
                resultStageTagText.text = stage != null && !string.IsNullOrEmpty(stage.regionName)
                    ? stage.regionName
                    : (pc != null ? pc.CurrentStageDisplayName : "");

            if (resultTurnsText != null)
                resultTurnsText.text = Mathf.Min(installer.Turns.CurrentTurn, installer.Turns.MaxTurns).ToString();

            // 결과 리포트가 뜨는 동안에는 뒤에 남은 기간이 남아 있으면 안 된다 - 마지막 날 행동 후
            // 실패하는 구조라 표시가 "남은 기간 1일"에서 얼어붙고, 리포트의 "제한 기간 초과"와
            // 나란히 보이면 서로 모순처럼 읽힌다. RefreshMission은 이 시점에 다시 호출되지 않으므로
            // 여기서 직접 지운다(재시작·다음 미션은 RefreshMission을 다시 타 정상 값으로 돌아온다).
            if (missionTurnsText != null) missionTurnsText.text = "";

            // onPrimary가 없으면 "다음"이 없는 화면이다(엔딩) - 아트에 인쇄된 NEXT를 글자 없는 탭으로
            // 덮고 호버까지 막아, 남는 행동이 아래의 [메인 화면] 하나가 되게 한다.
            bool hasPrimary = onPrimary != null;
            if (resultPrimaryTabHover != null)
            {
                resultPrimaryTabHover.enabled = true;   // 껐다 켜야 Rest 상태부터 다시 시작한다
                if (hasPrimary) resultPrimaryTabHover.SetResult(won);
                else { resultPrimaryTabHover.ShowBlankTab(); resultPrimaryTabHover.enabled = false; }
            }

            resultPrimaryButton.interactable = hasPrimary;
            resultPrimaryButton.onClick.RemoveAllListeners();
            if (hasPrimary)
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

        /// <summary>이 구역 안에서 이 미션이 몇 번째인지(1부터). 찾지 못하면 1로 둔다.</summary>
        static int MissionNumber(StageData stage, MissionData mission)
        {
            if (stage?.missions == null || mission == null) return 1;
            for (int i = 0; i < stage.missions.Length; i++)
                if (stage.missions[i] == mission) return i + 1;
            return 1;
        }

        IEnumerator ConfirmResultRoutine(Action onConfirm)
        {
            resultPrimaryButton.interactable = false;
            if (resultSecondaryButton != null) resultSecondaryButton.interactable = false;
            yield return FadeCanvasGroupRoutine(resultCanvasGroup, 1f, 0f, PopupFadeDuration);
            resultScreenGo.SetActive(false);
            // 리포트가 닫혔으니 플레이용 입력 자리를 원래대로 되돌린다 - 감춘 주체가 여기라
            // 되살리는 것도 여기서 해야 한다(재시작/다음 미션 어느 쪽이든 하단 패널이 다시 산다).
            RefreshBottomPanel();
            // 일시정지 입구도 같은 이유로 되살린다. ShowResultScreen에서 닫아 놓고 여기서 열지
            // 않아, 미션을 깨고 다음 미션으로 넘어가면 버튼이 사라진 채로 남았다(ESC도 함께 막혔다 -
            // SetAvailable(false)는 버튼을 숨기는 동시에 열기 자체를 잠근다).
            if (pauseMenu != null) pauseMenu.SetAvailable(true);
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

        // ---------------------------------------------------------------- 정보 전파 표시

        /// <summary>정보를 전달한 뒤 <b>NPC의 판단을 기다리는 동안에만</b> 하단 안내 띠에 띄워 두는
        /// 상태 표시. 이 구간은 길면 몇 초씩 걸리는데 그동안 화면이 아무 말도 하지 않아 "눌리긴 한
        /// 건가" 싶었다.
        ///
        /// <b>켜지는 조건이 "전달 중"이 아니라 "응답 대기 중"이다.</b> 예전에는
        /// TargetingController.IsDelivering에 맞춰 켰는데, 그 구간에는 판단이 끝난 뒤의 NPC 대사·이동
        /// 연출까지 들어 있어서 이미 세계가 반응하고 있는데도 "기다리는 중"이 함께 떠 있었다. 게다가
        /// 확산은 NPC가 한 명씩 차례로 판단하므로(InfoDeliverySystem.ExposeCardAtLocationAsync의
        /// foreach) 대기와 연출이 번갈아 나와, 어떤 때는 겹치고 어떤 때는 안 겹쳐 더 헷갈렸다.
        /// 지금은 <see cref="Belief.AI.LLM.LlmRequestMonitor"/>가 세는 실제 대기 요청 수를 따른다.
        ///
        /// 규칙 판단(LLM 없이 도는 구역)에서는 기다릴 것이 없어 아예 뜨지 않는다 - 그 경우 턴은
        /// 한 프레임에 끝나므로 보여 줄 대기 자체가 없다.
        ///
        /// 일시 알림(ShowTransientNotice)과 같은 슬롯을 쓰되 스스로 사라지지 않는다.</summary>
        bool processingShowing;
        Coroutine processingRoutine;

        const string TurnProcessingMessage = "정보 전파중";

        void Update()
        {
            // 이벤트가 아니라 매 프레임 확인한다 - 요청의 시작/끝은 Transport 코루틴 안에서 일어나고,
            // 거기서 UI로 신호를 쏘게 하면 표시 계층이 통신 계층에 끼어들게 된다.
            bool waiting = targeting != null && targeting.IsDelivering && Belief.AI.LLM.LlmRequestMonitor.IsWaiting;

            // 대기 중이어도 <b>연출이 돌고 있으면 비켜 준다.</b> 판단은 NPC 한 명씩 차례로 이뤄지는데
            // (InfoDeliverySystem의 foreach), 앞선 NPC의 대사 말풍선은 다음 NPC의 요청이 나가는 동안에도
            // 계속 떠 있다. 그래서 대기 여부만 보면 "정보 전파중"과 혼잣말이 나란히 뜬다.
            // PlaybackDirector에는 대사와 이동이 모두 등록되므로 이 하나로 둘 다 걸러진다.
            var director = PlaybackDirector.Instance;
            bool playing = director != null && director.IsPlaying;

            SetTurnProcessing(waiting && !playing);
        }

        void SetTurnProcessing(bool on)
        {
            if (processingShowing == on) return;
            processingShowing = on;

            if (processingRoutine != null) StopCoroutine(processingRoutine);
            if (on)
            {
                // 진행 표시가 우선이다 - 돌고 있던 일시 알림은 자리를 비켜 준다.
                if (feedbackRoutine != null) { StopCoroutine(feedbackRoutine); feedbackRoutine = null; }
                processingRoutine = StartCoroutine(TurnProcessingRoutine());
            }
            else
            {
                processingRoutine = StartCoroutine(FadeOutProcessingRoutine());
            }
        }

        IEnumerator TurnProcessingRoutine()
        {
            feedbackText.color = AccentColor;
            feedbackGo.SetActive(true);
            noticeShowing = true;
            RefreshBarVisibility();

            const float fade = 0.15f;
            float t = 0f;
            while (t < fade)
            {
                t += Time.unscaledDeltaTime;
                feedbackCanvasGroup.alpha = Mathf.SmoothStep(0f, 1f, t / fade);
                feedbackText.text = TurnProcessingMessage;
                yield return null;
            }
            feedbackCanvasGroup.alpha = 1f;

            // 점이 늘었다 줄며 "아직 돌고 있다"를 보여 준다 - 멈춰 있는 글자만으로는 굳은 것처럼 보인다.
            int dots = 0;
            float tick = 0f;
            while (true)
            {
                tick += Time.unscaledDeltaTime;
                if (tick >= 0.35f)
                {
                    tick = 0f;
                    dots = (dots + 1) % 4;
                    feedbackText.text = TurnProcessingMessage + new string('.', dots);
                }
                yield return null;
            }
        }

        IEnumerator FadeOutProcessingRoutine()
        {
            const float fade = 0.25f;
            float start = feedbackCanvasGroup.alpha;
            float t = 0f;
            while (t < fade)
            {
                t += Time.unscaledDeltaTime;
                feedbackCanvasGroup.alpha = Mathf.Lerp(start, 0f, t / fade);
                yield return null;
            }
            feedbackCanvasGroup.alpha = 0f;

            feedbackGo.SetActive(false);
            noticeShowing = false;
            RefreshBarVisibility();
            processingRoutine = null;
        }

        /// <summary>하단 안내 띠의 "일시 알림" 슬롯에 짧게 띄운다 - 클릭이 거부된 이유, 손패가 저절로
        /// 늘어난 이유처럼 <b>규칙이 뒤에서 움직인 사실</b>을 보고하는 용도다. 3-85에서 "~하세요" 류의
        /// 지시 문구는 전부 걷어냈으니 여기에 다시 넣지 말 것.</summary>
        void ShowTransientNotice(string message, Color color)
        {
            // 응답을 기다리는 동안에는 "정보 전파중"이 이 슬롯을 쓰고 있다 - 덮어쓰면 그 표시가
            // 사라진 채로 남아, 끝났는지 아닌지 알 수 없게 된다.
            if (processingShowing) return;
            if (feedbackRoutine != null) StopCoroutine(feedbackRoutine);
            feedbackRoutine = StartCoroutine(TransientNoticeRoutine(message, color));
        }

        IEnumerator TransientNoticeRoutine(string message, Color color)
        {
            feedbackText.text = message;
            feedbackText.color = color;
            feedbackGo.SetActive(true);
            noticeShowing = true;
            RefreshBarVisibility();
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
            noticeShowing = false;
            RefreshBarVisibility();
            feedbackRoutine = null;
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

            // 문서를 연 순간이 곧 "확인함"이다 - 여는 경로가 이 메서드 하나뿐이라 여기만 지우면 된다.
            if (state == HudPanelState.Log) logUnseen = false;
            if (state == HudPanelState.Profile)
            {
                profileUnseen = false;
                seenProfileSignature = CurrentProfileSignature();
            }
            RefreshTabBadges();

            // Profile/Log 상태에서는 월드 하단 힌트 바가 우측 패널을 가리지 않도록 폭을 좁힌다 -
            // Default 상태에서는 원래 전체 폭으로 복원한다.
            bool rightPanelOpen = state != HudPanelState.Default;
            float targetMaxX = rightPanelOpen ? RightPanelLeftEdge - HintBarRightMargin : bottomPanelFullMaxX;
            if (bottomPanelRect != null)
                bottomPanelRect.anchorMax = new Vector2(Mathf.Min(targetMaxX, bottomPanelFullMaxX), bottomPanelRect.anchorMax.y);

            // 안내 띠만은 좌우를 <b>대칭으로</b> 줄인다. 오른쪽만 당기면 띠의 중심이 화면 중심보다
            // 왼쪽으로 밀려(0.177~0.66이면 중심 0.419) 글이 가운데에서 벗어나 보인다 - 아래 보유 정보
            // 패널은 내용이 왼쪽 정렬이라 오른쪽만 줄이는 게 맞지만, 이 띠는 가운데 정렬 문장이다.
            float bannerMaxX = rightPanelOpen
                ? Mathf.Min(RightPanelLeftEdge - HintBarRightMargin, feedbackBannerFullMaxX)
                : feedbackBannerFullMaxX;
            // 0.5를 축으로 접은 값. 원래 폭보다 넓어지지 않도록 왼쪽 한계로 한 번 더 막는다.
            float bannerMinX = rightPanelOpen
                ? Mathf.Max(1f - bannerMaxX, feedbackBannerFullMinX)
                : feedbackBannerFullMinX;
            if (feedbackBannerRect != null)
            {
                feedbackBannerRect.anchorMin = new Vector2(bannerMinX, feedbackBannerRect.anchorMin.y);
                feedbackBannerRect.anchorMax = new Vector2(bannerMaxX, feedbackBannerRect.anchorMax.y);
            }
        }

        void OnProfileTabClicked() => SetHudPanelState(panelState == HudPanelState.Profile ? HudPanelState.Default : HudPanelState.Profile);
        void OnLogTabClicked() => SetHudPanelState(panelState == HudPanelState.Log ? HudPanelState.Default : HudPanelState.Log);

        void RefreshTabBadges()
        {
            if (logTabBadgeGo != null) logTabBadgeGo.SetActive(logUnseen);
            if (profileTabBadgeGo != null) profileTabBadgeGo.SetActive(profileUnseen);
        }

        /// <summary>프로필 패널에 지금 찍혀 있는 것 중 <b>바뀔 수 있는</b> 값만 이어 붙인다 - 태그·관계·
        /// 배경 서사는 NPC마다 고정이라 이름 하나로 대표되고, 실제로 변하는 건 믿음 단계뿐이다.
        /// 아직 아무도 고르지 않았으면 빈 문자열이라 시작 시점에는 점이 켜지지 않는다.</summary>
        string CurrentProfileSignature()
        {
            if (selectedNpcState == null) return "";
            CurrentBeliefTag(selectedNpcState, out string koreanLabel, out var belief);
            return selectedNpcState.Data.displayName + "|" + BeliefTierNumber(belief) + "|" + koreanLabel;
        }

        /// <summary>프로필을 다시 그릴 때마다 "지난번에 본 것과 달라졌는지"를 판정한다. 열려 있으면
        /// 보고 있는 중이니 그대로 본 것으로 치고, 닫혀 있으면 점을 켠다.
        ///
        /// 단, 다른 인물을 고른 것뿐이면 점을 켜지 않는다(playerInitiated) - 자기가 방금 바꿔 놓고
        /// "새 내용이 있다"는 알림을 받는 꼴이라 알림의 뜻이 흐려진다. 점은 믿음 단계가 저절로
        /// 바뀌었을 때처럼 <b>가만히 있었는데 달라진 경우</b>만 뜬다.</summary>
        void TrackProfileBadge(bool playerInitiated)
        {
            string signature = CurrentProfileSignature();
            if (playerInitiated || panelState == HudPanelState.Profile)
            {
                seenProfileSignature = signature;
                profileUnseen = false;
            }
            else if (signature != seenProfileSignature)
            {
                profileUnseen = true;
            }

            RefreshTabBadges();
        }

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
            // 표기만 일수로 바꾼다 - CurrentTurn/StageTurn 계산과 의미는 그대로다.
            // "DAY" 라벨은 턴 카드 아트(턴UI_수정본.png)에 인쇄돼 있으므로 여기서는 숫자만 넣는다 -
            // 둘 다 쓰면 카드에 "DAY"와 "DAY 1"이 겹쳐 보인다.
            // 제한 기간을 "1 / 4"로 함께 적는다. 남은 기간 UI가 따로 있긴 하지만 그쪽은 "남은 기간 N일"
            // 이라 지금이 몇 번째 날인지와는 다른 정보이고, 카드만 보고도 압박을 읽을 수 있어야 한다
            // (TurnValue 자리는 원래 이 형식을 전제로 만들어져 폭이 충분하다).
            var turns = installer.Turns;
            int missionShown = Mathf.Min(turns.CurrentTurn, turns.MaxTurns);
            missionTurnText.text = $"{missionShown} / {turns.MaxTurns}";

            // 미션 일차(DAY N)와 축이 다르다 - 미션이 바뀌어도 StageTurn은 리셋되지 않으므로
            // "경과 N일"로 누적임을 문구에 드러내 DAY 1과 나란히 놓여도 모순으로 읽히지 않게 한다.
            int stageShown = Mathf.Min(turns.StageTurn, turns.StageMaxTurns);
            stageTurnText.text = $"구역 경과 {stageShown}일 / {turns.StageMaxTurns}일";

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
            if (objective != null) lastKnownObjective = objective;

            ClearMissionConditionRows();

            if (objective == null)
            {
                missionTitleText.text = "";
                missionDescText.text = "";
                missionTurnsText.text = "";
                if (missionConditionsText != null) missionConditionsText.text = "";
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
            var conditionsBody = new StringBuilder();
            if (objective.successConditions != null)
            {
                foreach (var condition in objective.successConditions)
                {
                    if (condition == null) continue;
                    bool met = condition.GetCurrentProgress(context) >= condition.TargetCount;
                    // displayLabel이 비면 애셋 이름(= Condition_Stage02_M01_BookkeeperExposed 같은 원문 ID)이
                    // 그대로 플레이어에게 노출된다. 폴백 자체는 "아무것도 안 보이는 것"보다 나으므로 남기되,
                    // 조용히 지나가면 또 이번처럼 플레이 화면에서야 발견되므로 에디터에서 경고를 띄운다.
                    string label = condition.displayLabel;
                    if (string.IsNullOrWhiteSpace(label))
                    {
                        label = condition.name;
#if UNITY_EDITOR
                        Debug.LogWarning($"[Mission] '{objective.displayTitle}'의 클리어 조건 '{condition.name}'에 " +
                            "displayLabel이 비어 있어 애셋 이름이 그대로 표시된다. 플레이어가 읽을 한글 문구를 채워야 한다.", condition);
#endif
                    }
                    // 조건 둘 중 <b>하나만</b> 채우면 되는 미션(clearMode=Any)이 대부분인데, 그냥
                    // 나란히 적으면 둘 다 해야 하는 것으로 읽힌다 - 둘째 줄부터 "또는"을 붙여 선택지임을
                    // 드러낸다. 이 한 단어가 없어서 미션이 어렵게 읽힌다는 지적이 있었다.
                    // 조건 둘 중 <b>하나만</b> 채우면 되는 미션(clearMode=Any)이 대부분인데, 그냥
                    // 나란히 적으면 둘 다 해야 하는 것으로 읽힌다. "또는"을 조건 사이 독립된 줄로
                    // 빼면 선택 관계가 한눈에 들어오고, 조건 문구에 붙였을 때처럼 줄을 넘겨
                    // 밀어내지도 않는다(붙이면 세 줄이 칸 폭 270을 넘겼다 - 실측).
                    if (objective.clearMode == MissionClearMode.Any && conditionsBody.Length > 0)
                        conditionsBody.AppendLine("<color=#8A857E>  또는</color>");

                    conditionsBody.Append("•  ");
                    // 달성한 조건은 취소선 + 흐린 색. 체크 표시(✓)를 쓰지 않는 이유는 이 프로젝트
                    // 폰트에 그 글리프가 없어서다(화살표·삼각형과 같은 사정).
                    if (met) conditionsBody.Append("<color=#8A857E><s>").Append(label).AppendLine("</s></color>");
                    else conditionsBody.AppendLine(label);

                    AddMissionConditionRow(objective.displayTitle, label, met, missionConditionRows.Count);
                }
            }

            // MISSION 종이 본문 - 예전에는 여기에 Zone1 시안 문구("경비대장을 북문에서 벗어나게
            // 해야한다" 등)가 박힌 채 어떤 코드도 갱신하지 않아, 어느 구역 어느 미션에서든 같은 두
            // 줄이 떠 있었다. 이제 실제 미션의 달성 조건을 그대로 적는다.
            if (missionConditionsText != null) missionConditionsText.text = conditionsBody.ToString().TrimEnd();

            // 표기만 일수로 바꾼다 - inclusive 계산(마지막 날에 1일)은 그대로다.
            // 제한 기간 초과로 결과 리포트가 뜰 때 이 값을 지우는 것은 ShowResultScreen이 담당한다
            // (그 시점엔 이 메서드가 다시 호출되지 않아 여기서 처리할 수 없다).
            int remaining = Mathf.Max(0, installer.Turns.MaxTurns - installer.Turns.CurrentTurn + 1);
            missionTurnsText.text = $"남은 기간 {remaining}일";

            nextMissionText.text = string.IsNullOrEmpty(objective.nextMissionTitle)
                ? ""
                : objective.isHiddenUntilUnlocked
                    ? "다음 미션: ???"
                    : $"다음 미션: {objective.nextMissionTitle}";

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
            // 이 조건 카드 더미는 GoalCard01/02 두 장으로 대체됐다 - 프리팹 참조가 비어 있고
            // MissionConditionsRoot도 네 씬 모두 꺼져 있다(실측). 그런데도 여기까지 들어와
            // Instantiate(null)로 예외를 던지고 있었고, 그 바람에 RefreshMission이 이 줄에서 끊겨
            // 뒤쪽(남은 기간·다음 미션 문구)이 아예 실행되지 않았다. 조건 문구는 이제 MISSION
            // 종이에 직접 적으므로 참조가 없으면 조용히 넘어간다.
            if (missionConditionRowPrefab == null || missionConditionsRoot == null) return;

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
        /// <summary>NPC를 누르면 그 인물의 조사 파일이 열린다 - 예전에는 내용만 조용히 갈아 끼우고
        /// 패널은 Profile 탭을 따로 눌러야 열렸다. 클릭했는데 화면에 아무 일도 일어나지 않아 눌린
        /// 줄도 모르던 자리다.
        ///
        /// <b>닫는 건 Profile 탭 재클릭이다.</b> 이 문서는 화면 왼쪽에 붙어 있어서, 다른 곳을 누를 때
        /// 자동으로 닫아 버리면 읽는 도중에 사라진다.</summary>
        void OnNpcClickedForProfile(NpcData npcData)
        {
            if (!installer.Npcs.TryGetValue(npcData, out var state)) return;
            if (selectedNpcState != state)
            {
                selectedNpcState = state;
                // 플레이어가 스스로 고른 인물이라 "안 본 새 내용"이 아니다 - 여기서 본 것으로 친다.
                RefreshNpcProfile(playerInitiated: true);
            }

            OpenProfilePanel();
        }

        /// <summary>Profile 문서를 연다. 다만 Log를 펼쳐 둔 상태에서는 건드리지 않는다 - 로그를 읽는
        /// 중에 지도에서 사람을 눌렀다는 이유로 문서가 바뀌면 읽던 것을 뺏기는 셈이다.</summary>
        void OpenProfilePanel()
        {
            if (panelState != HudPanelState.Default) return;
            if (rightDocumentPanel != null) rightDocumentPanel.OpenProfile();
            SetHudPanelState(HudPanelState.Profile);
        }

        /// <param name="playerInitiated">플레이어가 직접 다른 인물을 고른 결과인지 - 그렇다면 탭에
        /// 빨간 점을 띄우지 않는다. 점은 "가만히 있었는데 내용이 바뀌었다"를 뜻해야 한다.</param>
        void RefreshNpcProfile(bool playerInitiated = false)
        {
            RefreshNpcProfileContent();
            TrackProfileBadge(playerInitiated);
        }

        void RefreshNpcProfileContent()
        {
            ClearNpcRelationshipRows();
            if (npcNoneStickerGo != null) npcNoneStickerGo.SetActive(selectedNpcState == null);

            if (selectedNpcState == null)
            {
                npcNameText.text = "";
                npcBeliefTierText.text = "";
                npcBeliefDialogueText.text = "";
                npcHistoryText.text = "";
                if (npcJudgmentTendencyText != null) npcJudgmentTendencyText.text = "";
                if (npcPriorityText != null) npcPriorityText.text = "";
                if (npcSensitiveInfoText != null) npcSensitiveInfoText.text = "";
                if (npcRelationTendencyText != null) npcRelationTendencyText.text = "";
                if (npcTrustJudgmentText != null) npcTrustJudgmentText.text = "";
                if (npcPortraitFrameGo != null) npcPortraitFrameGo.SetActive(false);
                return;
            }

            var data = selectedNpcState.Data;
            npcNameText.text = data.displayName;
            if (npcJudgmentTendencyText != null) npcJudgmentTendencyText.text = data.judgmentTendencyTag;
            if (npcPriorityText != null) npcPriorityText.text = data.priorityTag;
            if (npcSensitiveInfoText != null) npcSensitiveInfoText.text = data.sensitiveInfoTag;
            if (npcRelationTendencyText != null) npcRelationTendencyText.text = data.relationTendencyTag;
            if (npcTrustJudgmentText != null) npcTrustJudgmentText.text = data.trustJudgmentTag;

            // 좌상단 사진 프레임 - 전신 사진을 그대로 넣고 프레임의 RectMask2D가 아래를 잘라
            // 상반신만 보이게 한다(프레임/사진 크기는 프리팹에 고정, 여기선 교체만).
            if (npcPortraitImage != null) npcPortraitImage.sprite = data.characterPhoto;
            if (npcPortraitFrameGo != null) npcPortraitFrameGo.SetActive(data.characterPhoto != null);
            // 이름 아래에 있던 "나이/성별/직업/소속" 줄(BasicInfoExtra)은 제거했다(2026-08-05 사용자
            // 지시) - 넣을 자리가 성격 태그 표 바로 위 좁은 여백뿐이라 어떻게 배치해도 표와 겹쳐
            // 보였고, 나이는 NpcData에 필드 자체가 없어 항상 "—"로 나오던 자리였다. 상단은 이제
            // NPC 이름만 표시한다.

            // 배경 아트의 슬롯 이름 그대로(BeliefStageValue / BeliefStageNote) - 큰 자리는 믿음 단계를
            // 1~5 숫자로, 그 아래 작은 자리는 단계 이름("가능성 있음")으로 넣는다. 예전에는 큰 자리에
            // 단계 이름을, 작은 자리에 그 단계의 NPC 대사 미리보기를 넣고 있었다(사용자 지시로 교체 -
            // 실제로 한 말은 Log 탭에 NpcSpokeEvent로 쌓이므로 여기서 미리보기를 겹쳐 보여줄 필요가 없다).
            CurrentBeliefTag(selectedNpcState, out string koreanLabel, out var beliefState);
            npcBeliefTierText.text = BeliefTierNumber(beliefState);
            npcBeliefDialogueText.text = koreanLabel;

            if (data is MajorNpcData majorForRel && majorForRel.relationships != null)
            {
                int slot = 0;
                foreach (var rel in majorForRel.relationships)
                {
                    if (rel.other == null) continue;
                    if (slot >= RelationshipSlotOffsetsY.Length) break; // 배경 아트에 회색 칸이 3개뿐
                    string label = string.IsNullOrEmpty(rel.relationshipTypeLabel) ? "" : rel.relationshipTypeLabel;
                    string desc = string.IsNullOrEmpty(rel.relationshipDescription) ? "" : rel.relationshipDescription;
                    AddNpcRelationshipRow(slot, rel.other.displayName, label, desc);
                    slot++;
                }
            }

            // History 칸은 인물 배경 서사(backstory)만 보여준다 - 예전엔 gameplayRoleSummary("1스테이지
            // 클리어 목표 대상" 같은 개발용 메모)와 aiNotes를 빈 줄로 이어 붙였는데, 플레이어에게 보일
            // 내용이 아닌 데다 빈 줄만으로 칸 높이의 절반 이상을 잡아먹어 넘쳤다(2026-08-05).
            npcHistoryText.text = data.backstory;
        }

        /// <summary>NpcState가 받은 정보 중 가장 최근 것에 대한 믿음 단계를 "현재 믿음"으로 삼는다 -
        /// 아직 아무 정보도 받지 않았으면 Unknown(판단 대상 없음)으로 취급한다. contextTag는
        /// RuleBasedMajorThinker.ChooseDialogue와 동일한 5단계 태그를 그대로 재사용한다.</summary>
        /// <summary>믿음 단계를 프로필 패널에 크게 띄우는 1~5 숫자. 순서는 새로 정하지 않고
        /// Log 탭 눈금(BeliefScalePosition, 불신 0 ~ 신뢰 1)에서 그대로 환산한다 - 두 표시가
        /// 절대 어긋나지 않게 하려는 것이므로, 단계가 바뀌면 그쪽 하나만 고치면 된다.
        /// 판단 전(Unknown)은 매길 숫자가 없으므로 "-".</summary>
        static string BeliefTierNumber(BeliefState state)
        {
            float t = BeliefScalePosition(state);
            return t < 0f ? "-" : Mathf.RoundToInt(t * 4f + 1f).ToString();
        }

        string CurrentBeliefTag(NpcState state, out string koreanLabel, out BeliefState belief)
        {
            InformationCardData lastCard = null;
            var received = state.ReceivedInformation;
            if (received.Count > 0) lastCard = received[received.Count - 1].Card;

            belief = lastCard != null ? state.GetBelief(lastCard) : BeliefState.Unknown;
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

        /// <summary>배경 아트에 그려진 관계도 회색 칸 3개의 세로 위치 - RelationshipsRoot(=첫 칸)
        /// 기준 상대 오프셋이다. LayoutGroup의 균일 간격 대신 실측값을 직접 쓴다.
        ///
        /// 값은 실제로 화면에 깔리는 스프라이트(ProfileVisual = `프로필 파일 UI_Left.png`)를 픽셀
        /// 스캔해 다시 잰 것이다 - 칸 윗변이 444 / 549 / 653px, 높이는 셋 다 73px. 예전 값
        /// (114 / 95)은 지금 쓰지 않는 `프로필 파일 UI.png` 쪽 좌표라 둘째 칸이 14px 내려가 있었다.
        /// 아트가 교체되면 이 값도 다시 재야 한다.</summary>
        static readonly float[] RelationshipSlotOffsetsY = { 0f, -105f, -209f };

        /// <summary>NpcRelationshipRowView 프리팹을 Instantiate하고 3열(관계 대상/관계 유형/반응 차이)
        /// 값을 채운 뒤, 배경 아트의 해당 회색 칸 위에 정확히 얹는다. 폰트/색/열 위치는 전부 프리팹에
        /// 구워져 있어 여기서는 위치와 데이터만 다룬다.</summary>
        void AddNpcRelationshipRow(int slot, string target, string type, string diff)
        {
            var row = Instantiate(npcRelationshipRowPrefab, npcRelationshipsRoot);
            row.Bind(target, type, diff);

            var rowRect = row.transform as RectTransform;
            if (rowRect != null) rowRect.anchoredPosition = new Vector2(0f, RelationshipSlotOffsetsY[slot]);

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

            // 라벨 칸(Labels, 프리팹에 고정 텍스트로 이미 있음)과 값 칸(Values)을 나란히 세운
            // 2단 표 형태로 정렬한다 - 값이 전부 한글로 짧게 번역된 뒤(2026-08-04)라 어느 값도
            // 칸 폭 안에서 줄바꿈되지 않음을 실측으로 확인했다(가장 긴 값 "사실 정보"/"매우 높음"도
            // 폭 70 안에서 줄바꿈 없이 자연 폭 52 - 예전에 영문 enum 이름(FactualInformation 등)을
            // 그대로 쓸 때는 이 표 레이아웃이 줄바꿈 시 라벨과 어긋나며 패널 밖으로 흘러넘쳤었다).
            // 두 칸 다 같은 빈 줄 간격(\n\n)을 써서 줄 수·줄 높이가 항상 똑같이 맞물린다.
            // accessType(접근 권한)은 게임에서 실제로 아무것도 막지 않게 된 지 오래라
            // (LocationMechanicsSettings.CanTargetLocationDirectly 참고) 패널에서도 아예 뺐다(사용자 지시).
            locationNoteTitleText.text = selectedLocationData.displayName;
            locationNoteBodyText.text =
                $"{SpreadSpeedKoreanLabel(selectedLocationData.spreadSpeed)}\n\n" +
                $"{NpcDensityKoreanLabel(selectedLocationData.npcDensity)}\n\n" +
                $"{SensitiveInfoTypeKoreanLabel(selectedLocationData.sensitiveInformationType)}\n\n" +
                $"{CredibilityModifierKoreanLabel(selectedLocationData.credibilityModifier)}";
        }

        static string SpreadSpeedKoreanLabel(LocationSpreadSpeed value) => value switch
        {
            LocationSpreadSpeed.Low => "하",
            LocationSpreadSpeed.Medium => "중",
            LocationSpreadSpeed.High => "상",
            _ => "미지정"
        };

        static string NpcDensityKoreanLabel(LocationNpcDensity value) => value switch
        {
            LocationNpcDensity.Low => "하",
            LocationNpcDensity.Medium => "중",
            LocationNpcDensity.High => "상",
            _ => "미지정"
        };

        static string SensitiveInfoTypeKoreanLabel(LocationSensitiveInfoType value) => value switch
        {
            LocationSensitiveInfoType.Rumor => "소문",
            LocationSensitiveInfoType.Intelligence => "첩보",
            LocationSensitiveInfoType.FactualInformation => "사실 정보",
            LocationSensitiveInfoType.OrderDocument => "명령 문서",
            LocationSensitiveInfoType.CriminalDeal => "범죄 거래",
            LocationSensitiveInfoType.ForgedDocument => "위조 문서",
            _ => "미지정"
        };

        static string CredibilityModifierKoreanLabel(LocationCredibilityModifier value) => value switch
        {
            LocationCredibilityModifier.Low => "낮음",
            LocationCredibilityModifier.Neutral => "중립",
            LocationCredibilityModifier.High => "높음",
            LocationCredibilityModifier.VeryHigh => "매우 높음",
            _ => "미지정"
        };

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

        /// <summary>일반 사건 로그(가이드: "캐릭터의 이동, 믿음 수치 변화는 일반 텍스트로") - 대사 줄만
        /// 제외하고 나머지는 installer.Log.Entries를 그대로 재사용한다. 대사 포맷("이름: \"...\"")은
        /// EventLogSystem.OnNpcSpoke가 만드는 고정 포맷이라 이 필터만으로 안전하게 구분된다.
        ///
        /// 몇 줄을 담을지는 개수가 아니라 <b>칸 높이로</b> 정한다. 줄바꿈이 켜져 있어 기록 하나가
        /// 두세 줄을 차지할 수 있어서 "최근 N개"로는 높이를 보장할 수 없었다 - 실제로 35자짜리
        /// 기록 5개면 10줄(210px)이 되어 칸(140px)을 한참 넘겼다. 최신 것부터 거꾸로 담다가
        /// 넘치기 직전에서 멈추므로, 오래된 기록이 먼저 밀려나고 방금 일어난 일은 항상 남는다.</summary>
        void AppendLog(string _)
        {
            if (logGeneralText == null) return;

            var entries = installer.Log.Entries;
            float maxHeight = logGeneralText.rectTransform.rect.height;
            float width = logGeneralText.rectTransform.rect.width;

            logLinesBuffer.Clear();
            var sb = new StringBuilder();
            for (int i = entries.Count - 1; i >= 0 && logLinesBuffer.Count < MaxLogLines; i--)
            {
                var line = entries[i];
                if (line.Contains(": \"")) continue; // 대사 줄은 상/하단 카드에서 따로 보여준다

                logLinesBuffer.Insert(0, line);
                sb.Length = 0;
                for (int k = 0; k < logLinesBuffer.Count; k++) sb.AppendLine(logLinesBuffer[k]);
                if (logGeneralText.GetPreferredValues(sb.ToString(), width, 0f).y > maxHeight)
                {
                    logLinesBuffer.RemoveAt(0); // 방금 넣은 줄이 넘치면 되돌리고 종료
                    break;
                }
            }

            sb.Length = 0;
            for (int k = 0; k < logLinesBuffer.Count; k++) sb.AppendLine(logLinesBuffer[k]);
            logGeneralText.text = sb.ToString();

            // 대사 줄은 위에서 걸러 냈지만 그것도 로그 문서에 남는 새 기록이다(대사 카드 쪽에 찍힌다) -
            // 그래서 걸러진 줄까지 포함해 "새 기록이 왔다"는 신호 자체로 점을 켠다.
            if (panelState != HudPanelState.Log)
            {
                logUnseen = true;
                RefreshTabBadges();
            }
        }

        readonly List<string> logLinesBuffer = new List<string>(MaxLogLines);

        /// <summary>로그 패널을 빈 상태로 되돌린다.
        ///
        /// 이 패널의 글자들은 <b>이벤트가 올 때만</b> 채워진다(OnNpcSpoke / OnLogCardJudged / 일반 로그).
        /// 그래서 스테이지를 막 시작한 시점처럼 아직 아무 일도 없었을 때는, 프리팹에 시안용으로
        /// 적혀 있던 예시 문구("주요 NPC의 대사는 이곳에", "[NPC 명] 수치 변동 사항" 등)가 그대로
        /// 남아 실제 기록처럼 보였다. 시작할 때 한 번 비워 준다.
        ///
        /// 눈금은 양 끝 라벨("불신"/"신뢰")과 선까지 <b>통째로</b> 감춘다 - 표식만 숨기면 빈 눈금이
        /// 덩그러니 남아 이미 무언가 기록된 것처럼 보였다(사용자 지시). 첫 판단이 오면
        /// ShowBeliefChange가 다시 켠다.</summary>
        void ClearLogPanel()
        {
            recentDialogueLines.Clear();
            if (logTopDialogueText != null) logTopDialogueText.text = "";
            if (logBottomDialogueText != null) logBottomDialogueText.text = "";
            if (logGeneralText != null) logGeneralText.text = "";
            if (logStatHeaderText != null) logStatHeaderText.text = "";
            ShowTrustScale(false);
            if (logTrustPrevMarker != null) logTrustPrevMarker.gameObject.SetActive(false);
            if (logTrustDeltaSegment != null) logTrustDeltaSegment.gameObject.SetActive(false);
            lastLoggedNpcState = null;

            // 기록을 비웠으니 "안 본 새 기록"도 없다 - 안 지우면 미션을 새로 시작해도 점이 남는다.
            logUnseen = false;
            profileUnseen = false;
            seenProfileSignature = CurrentProfileSignature();
            RefreshTabBadges();
        }

        /// <summary>눈금 전체(선 + 양 끝 라벨 + 표식)를 한 번에 여닫는다.</summary>
        void ShowTrustScale(bool visible)
        {
            if (logTrustArrowLine != null) logTrustArrowLine.gameObject.SetActive(visible);
            if (logTrustArrowHead != null) logTrustArrowHead.gameObject.SetActive(visible);
            if (logTrustLowLabel != null) logTrustLowLabel.gameObject.SetActive(visible);
            if (logTrustHighLabel != null) logTrustHighLabel.gameObject.SetActive(visible);
        }

        /// <summary>NpcSpokeEvent를 EventLogSystem과 별도로 한 번 더 구독 - 최근 대사 2줄을 상단(최신)/
        /// 하단(이전) 카드에 채운다. 새 저장소가 아니라 같은 이벤트를 두 번째로 구독하는 것뿐이다.
        ///
        /// 대사가 1개뿐일 때 하단 카드는 글자만 비고 카드 배경은 남는다 - 이는 의도된 동작이다
        /// (카드 두 장이 항상 자리를 지키는 시안). 카드를 숨기도록 바꿔 봤다가 되돌렸다.</summary>
        void OnLogNpcSpoke(NpcSpokeEvent e)
        {
            string text = e.Dialogue.IsGenerated ? e.Dialogue.GeneratedText : e.Dialogue.PredefinedLine?.text;
            if (string.IsNullOrEmpty(text)) return;

            recentDialogueLines.Insert(0, $"{e.Npc.displayName}: \"{text}\"");
            if (recentDialogueLines.Count > 2) recentDialogueLines.RemoveAt(recentDialogueLines.Count - 1);

            if (logTopDialogueText != null) logTopDialogueText.text = recentDialogueLines.Count > 0 ? recentDialogueLines[0] : "";
            if (logBottomDialogueText != null) logBottomDialogueText.text = recentDialogueLines.Count > 1 ? recentDialogueLines[1] : "";
        }

        /// <summary>CardJudgedEvent를 EventLogSystem과 별도로 한 번 더 구독 - 믿음 판단이 바뀔 때마다
        /// [이름] 수치 변동 사항 헤더를 갱신하고, 눈금 위 표식을 새 믿음 위치로 옮긴다.
        ///
        /// ⚠️ 예전엔 눈금 양 끝 라벨("불신"/"신뢰")을 이전 믿음/이후 믿음 텍스트로 **덮어썼다**.
        /// 그래서 화면에 "불신 ——→ 확인이 필요하다고 판단함"처럼 좌우 길이가 제각각인 문장이 나왔고,
        /// 게다가 첫 판단이라 이전 값이 Unknown일 때도 "불신"으로 표시돼 실제로는 판단 전인데
        /// 불신했다가 바뀐 것처럼 읽혔다. 시안(`UI/Guides/[배치가이드] ... 로그.jpg`)은
        /// "불신 ——→ 신뢰"를 **고정 눈금**으로 두는 구조이므로, 양 끝 라벨은 프리팹 원문 그대로 두고
        /// 표식만 눈금 위에서 움직인다(2026-08-05). 이전/이후와 방향은 눈금 위 표식 두 개와
        /// 그 사이 색 구간, 그리고 아래 글자 줄로 나눠 보여 준다(ShowBeliefChange).</summary>
        void OnLogCardJudged(CardJudgedEvent e)
        {
            if (logStatHeaderText != null) logStatHeaderText.text = $"[{e.Npc.displayName}] 수치 변동 사항";
            ShowBeliefChange(e.PreviousBelief, e.ResultBelief);
        }

        // 방향 색 - 종이 배경(밝은 베이지) 위에서 읽히도록 전부 어두운 채도로 잡았다.
        static readonly Color RiseColor = new Color32(0x1F, 0x6B, 0x4A, 0xFF); // 신뢰 쪽으로 이동
        static readonly Color FallColor = new Color32(0xA8, 0x32, 0x2B, 0xFF); // 불신 쪽으로 이동
        static readonly Color FlatColor = new Color32(0x6B, 0x63, 0x5C, 0xFF); // 그대로 / 첫 판단
        static readonly Color PrevMarkColor = new Color32(0x9A, 0x92, 0x8A, 0xFF); // 이전 자리 표식

        /// <summary>믿음 눈금 위에 <b>이전 → 이후</b>를 한 번에 보여 준다.
        ///
        /// 예전에는 표식 하나를 현재 위치로 옮기기만 했다. 그러면 "지금 어디"는 알아도 "어디에서
        /// 왔는지"와 "올랐는지 내렸는지"를 알 수 없어, 수치 변동 칸인데 변동이 안 보였다. 눈금 <b>한
        /// 줄</b> 위에 세 가지를 겹쳐 표시한다 - 이전 위치의 흐린 점, 이전↔이후를 잇는 색 구간,
        /// 진행 방향으로 뒤집히는 촉. 색은 방향(상승/하락/유지)에 따라 셋 중 하나로 통일한다.
        /// 단계 이름을 글자로 덧붙이는 줄도 만들어 봤지만 사용자 지시로 뺐다 - 눈금은 한 줄이다.
        ///
        /// ⚠️ 화살표·삼각형 기호(→ ▲ ▼ ◆ …)는 <b>쓸 수 없다</b>. 프로젝트의 폰트 13종(SUIT 전 굵기 /
        /// KoreanUI / TravelingTypewriter / SeoulNamsan / LiberationSans 폴백)에 하나도 없어서 전부
        /// 네모(□)로 나온다 - 실측 확인. 그래서 구간과 점은 Image로 그리고, 촉은 ASCII
        /// '&gt;' / '&lt;' / '|'만 쓴다.</summary>
        void ShowBeliefChange(BeliefState before, BeliefState after)
        {
            float toT = BeliefScalePosition(after);
            if (toT < 0f) return; // 판단 전(Unknown)으로는 되돌아가지 않는다 - 표시를 그대로 둔다

            float fromT = BeliefScalePosition(before);
            bool hasBefore = fromT >= 0f;

            Color color = !hasBefore || Mathf.Approximately(fromT, toT) ? FlatColor
                        : toT > fromT ? RiseColor : FallColor;

            // ① 현재 위치 표식 - 기존 화살표 머리를 그대로 쓰되 방향 색을 입히고, 촉이 진행 방향을
            //    향하게 뒤집는다. 왼쪽으로 내려갔는데 '>'가 오른쪽을 가리키면 온 길을 되짚는 것처럼
            //    읽힌다. 움직임이 없으면 방향이 없으므로 눈금 위 눈금표시('|')로 둔다.
            ShowTrustScale(true); // 시작 시 감춰 둔 눈금을 첫 판단에서 되살린다(ClearLogPanel 참고)
            PlaceOnScale(logTrustArrowHead, toT);
            if (logTrustArrowHead != null)
            {
                var headText = logTrustArrowHead.GetComponent<TMP_Text>();
                if (headText != null)
                {
                    headText.color = color;
                    headText.text = !hasBefore || Mathf.Approximately(fromT, toT) ? "|"
                                  : toT > fromT ? ">" : "<";
                }
            }

            // ② 이전 위치 표식 - 첫 판단이면 가리킬 자리가 없으므로 감춘다.
            if (logTrustPrevMarker != null)
            {
                logTrustPrevMarker.gameObject.SetActive(hasBefore);
                if (hasBefore)
                {
                    PlaceOnScale(logTrustPrevMarker, fromT);
                    var img = logTrustPrevMarker.GetComponent<Image>();
                    if (img != null) img.color = PrevMarkColor;
                }
            }

            // ③ 이전↔이후 구간 - 움직인 거리를 눈금 위에 색으로 칠한다.
            if (logTrustDeltaSegment != null && logTrustArrowLine != null)
            {
                bool moved = hasBefore && !Mathf.Approximately(fromT, toT);
                logTrustDeltaSegment.gameObject.SetActive(moved);
                if (moved)
                {
                    float lineLeft = logTrustArrowLine.anchoredPosition.x;
                    float lineWidth = logTrustArrowLine.sizeDelta.x;
                    float lo = Mathf.Min(fromT, toT), hi = Mathf.Max(fromT, toT);
                    logTrustDeltaSegment.anchoredPosition = new Vector2(
                        lineLeft + lineWidth * lo, logTrustDeltaSegment.anchoredPosition.y);
                    logTrustDeltaSegment.sizeDelta = new Vector2(
                        lineWidth * (hi - lo), logTrustDeltaSegment.sizeDelta.y);
                    var img = logTrustDeltaSegment.GetComponent<Image>();
                    if (img != null) img.color = color;
                }
            }

        }

        /// <summary>눈금선(logTrustArrowLine)의 실제 폭에서 계산하므로 아트가 바뀌어도 따라간다.
        /// 표식은 자기 폭의 절반만큼 왼쪽으로 당겨 눈금 위 지점에 가운데가 오게 한다.</summary>
        void PlaceOnScale(RectTransform marker, float t)
        {
            if (marker == null || logTrustArrowLine == null) return;
            float lineLeft = logTrustArrowLine.anchoredPosition.x;
            float lineWidth = logTrustArrowLine.sizeDelta.x;
            float centerX = lineLeft + lineWidth * t;
            marker.anchoredPosition = new Vector2(
                centerX - marker.sizeDelta.x * 0.5f, marker.anchoredPosition.y);
        }

        /// <summary>불신(0) ~ 신뢰(1) 눈금 상의 위치. Unknown(판단 전)은 -1로 "표시 안 함".</summary>
        static float BeliefScalePosition(BeliefState state) => state switch
        {
            BeliefState.Denied => 0f,
            BeliefState.Doubtful => 0.25f,
            BeliefState.NeedsVerification => 0.5f,
            BeliefState.Plausible => 0.75f,
            BeliefState.Trusted => 1f,
            _ => -1f
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

                    // 소멸 연출(0.2초) 동안 이 타일은 컨테이너에 그대로 남는다. 그 사이 살아 있는
                    // 카드들이 앞쪽 sibling으로 밀려 이 타일이 마지막 자리로 오는데, 화면의 손패를
                    // 인덱스로 물려 주는 HandCardHudBridge가 그걸 집으면 아직 SelectedCard인 카드가
                    // 맨 오른쪽 슬롯에 붙어 혼자 솟았다 내려앉는다. 상태를 남겨 걸러지게 한다.
                    tile.SetHandState(CardHandState.Removed);
                    tile.PlayDisappear(() => Destroy(tile.gameObject));
                }
            }

            bool deliveringNow = targeting != null && targeting.IsDelivering;

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
                // 여기서는 selected == true일 때만 강제로 펼친다.
                //
                // 손패 잠금(Using)은 이 새로고침이 targeting.IsDelivering을 보고 매번 다시 계산한다 -
                // 예전에는 확정 버튼이 누르는 순간 Using을 직접 박아 넣었는데, 그 뒤 전달이 실제로는
                // 시작되지 않으면(Phase 불일치·튜토리얼 필터로 조기 return) 아무도 되돌려주지 않아
                // 그 카드가 영구히 클릭 불가로 죽었다. 상태를 여기서만 만들면 그 구멍이 생기지 않는다.
                if (isSelected && deliveringNow)
                {
                    if (tile.HandState != CardHandState.Using)
                        tile.SetHandState(CardHandState.Using);
                }
                else if (isSelected)
                {
                    // SetAsLastSibling()을 쓰면 HorizontalLayoutGroup 배치 순서까지 맨 끝으로
                    // 밀려나 카드가 실제로 자리를 옮기는 버그가 있었다(1·2번 카드를 선택하면 마지막
                    // 슬롯으로 이동) - 카드가 더 이상 겹치지 않게(음수 spacing 제거) 바뀌어서 그리기
                    // 순서를 맨 앞으로 올릴 필요 자체가 없어졌으므로 완전히 제거한다.
                    if (tile.HandState != CardHandState.Expanded)
                        tile.SetHandState(CardHandState.Expanded);
                }
                else if (tile.HandState != CardHandState.Collapsed)
                {
                    // Expanded뿐 아니라 Using/Removed 잔재까지 되돌린다 - 어느 상태로 남아 있든
                    // 선택되지 않은 카드는 항상 다시 누를 수 있어야 한다.
                    tile.SetHandState(CardHandState.Collapsed);
                }
            }
        }

        bool IsInputLocked => PlaybackDirector.Instance != null && PlaybackDirector.Instance.IsPlaying;

        /// <summary>카드 선택은 <b>연출 중에도 받는다</b>. 예전에는 IsInputLocked(PlaybackDirector가
        /// 무언가 재생 중)면 조용히 버렸는데, 카드 등장 연출(0.2초)·하이라이트·HUD 페이드·NPC 대사까지
        /// 전부 여기에 걸려서 플레이어가 누른 클릭이 아무 반응 없이 사라졌다("씹힘"). 카드 선택은
        /// 손패의 표시 상태만 바꾸는 동작이라 연출과 충돌하지 않고, 게임을 진행시키는 확정 동작
        /// (OnDeliverClicked)은 그대로 잠가 둔다. 팝업이 떠 있는 동안은 오버레이가 레이캐스트를
        /// 직접 막으므로 여기서 또 막을 필요도 없다.
        ///
        /// 다만 전달이 이미 진행 중일 때는 선택을 바꾸지 않는다 - 그 사이에 다른 카드를 고르면
        /// 방금 보낸 카드와 화면에 펼쳐진 카드가 어긋난다.</summary>
        void OnCardClicked(InformationCardData card)
        {
            if (targeting != null && targeting.IsDelivering) return;
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
            // NoSelectionHint("전달할 정보를 선택하세요.")는 3-85에서 폐기 - 손패가 이미 화면에 깔려
            // 있어 같은 말을 반복할 뿐이었다. 프리팹에 남아 있어도 항상 꺼 둔다.
            if (noSelectionHintGo != null) noSelectionHintGo.SetActive(false);

            if (!hasSelection)
            {
                SetBarInstruction("");
                SetDeliverAffordance(false, false);
                return;
            }

            cardTitleText.text = card.information != null ? card.information.title : "?";
            cardDescText.text = card.information != null ? card.information.description : "";
            string kind = card.cardType == InfoCardType.Spread ? "확산형" : "전달형";
            string sourceName = card.source != null ? card.source.displayName : "알 수 없음";
            cardKindText.text = $"{kind}   출처: {sourceName}";

            // 전달 확정 버튼 활성 조건(section 10): 카드 선택 + 유효한 대상 + 이번 턴 미사용.
            // Phase==AwaitingConfirm은 이미 "카드 선택 + 카드 타입에 맞는 유효 대상 선택 완료"를
            // 내포하므로(TargetingController), 여기서는 턴 소진 여부만 추가로 확인한다.
            // 전달이 진행 중인 동안에도 Phase는 AwaitingConfirm 그대로라, IsDelivering을 함께 봐야
            // 버튼이 눌린 채로 남지 않는다(재진입 자체는 TargetingController가 막지만 표시도 맞춘다).
            bool canDeliver = targeting.Phase == TargetingPhase.AwaitingConfirm
                              && !targeting.IsDelivering
                              && !installer.Turns.TurnsExhausted;

            switch (targeting.Phase)
            {
                case TargetingPhase.AwaitingTarget:
                    // "장소/대상을 선택하세요"는 3-85에서 삭제 - 카드를 고르면 지도에서 유효한 대상이
                    // 이미 강조되므로 화면이 하는 말을 글로 한 번 더 하는 것뿐이었다.
                    SetBarInstruction("");
                    SetDeliverAffordance(false, false);
                    break;

                case TargetingPhase.AwaitingConfirm:
                    // 확정 버튼 위치를 모르면 진행 자체가 막히므로 이 안내만 남긴다.
                    SetBarInstruction(card.cardType == InfoCardType.Spread
                        ? "오른쪽 아래 [진행 완료]를 눌러 전달한다. (다른 장소를 클릭하면 대상을 바꿀 수 있습니다.)"
                        : "오른쪽 아래 [진행 완료]를 눌러 전달한다. (다른 사람을 클릭하면 대상을 바꿀 수 있습니다.)");
                    SetDeliverAffordance(true, canDeliver);
                    break;

                default:
                    SetBarInstruction("");
                    SetDeliverAffordance(false, false);
                    break;
            }
        }

        /// <summary>하단 안내 띠의 "지속 안내" 슬롯. 빈 문자열이면 슬롯이 꺼지고, 일시 알림도 없으면
        /// 판까지 꺼져서 지도 위에 빈 띠가 남지 않는다.</summary>
        void SetBarInstruction(string text)
        {
            barInstruction = text ?? "";
            if (instructionText != null) instructionText.text = barInstruction;
            RefreshBarVisibility();
        }

        /// <summary>지속 안내와 일시 알림이 판 하나를 공유한다 - 알림이 뜬 동안에는 지속 안내를 감춰
        /// 같은 자리에 두 문장이 겹쳐 그려지지 않게 한다.</summary>
        void RefreshBarVisibility()
        {
            bool hasInstruction = !string.IsNullOrEmpty(barInstruction);
            if (instructionGo != null) instructionGo.SetActive(hasInstruction && !noticeShowing);
            if (barBackgroundGo != null) barBackgroundGo.SetActive(hasInstruction || noticeShowing);
        }

        /// <summary>전달 확정 입력 자리(우하단 "진행 완료" 버튼)의 상태를 갱신한다.
        ///
        /// 버튼은 <b>껐다 켜지 않고 항상 두되 누를 수 있는지만 바꾼다</b> - 사라졌다 나타나면
        /// "진행 지점이 없어졌다"처럼 읽히고, 매 턴 위치를 다시 찾게 된다(지도 위 접선 태그 시절에
        /// 같은 이유로 정한 규칙을 그대로 가져왔다).
        ///
        /// 지도 위 접선 지점을 쓰는 스테이지가 있으면(WorldPresenter.spawnContactPointInWorld) 그쪽을
        /// 우선하고 이 버튼은 숨긴다 - 현재 4개 스테이지는 모두 HUD 버튼을 쓴다.
        ///
        /// <b>단, 작전 결과 화면이 떠 있는 동안에는 아예 감춘다.</b> "항상 두되 누를 수만 없게" 규칙은
        /// 플레이 중 진행 지점을 잃지 않게 하려는 것인데, 결과 리포트에서는 할 수 있는 행동이
        /// RETRY/메인 화면뿐이라 진행 완료 버튼이 남아 있으면 아직 뭔가 더 할 수 있는 것처럼 읽힌다.</summary>
        void SetDeliverAffordance(bool visible, bool canDeliver)
        {
            var contact = worldPresenter != null ? worldPresenter.ContactPointView : null;

            if (IsResultScreenOpen)
            {
                if (contact != null) contact.SetContactReady(false);
                if (deliverButtonGo != null) deliverButtonGo.SetActive(false);
                return;
            }

            if (contact != null)
            {
                contact.SetContactReady(visible && canDeliver);
                if (deliverButtonGo != null) deliverButtonGo.SetActive(false);
                return;
            }

            if (deliverButtonGo != null) deliverButtonGo.SetActive(true);
            if (deliverButton != null) deliverButton.interactable = visible && canDeliver;
        }

        /// <summary>작전 결과 리포트가 화면을 차지하고 있는지 - 이때는 플레이용 입력 자리를 감춘다.</summary>
        bool IsResultScreenOpen => resultScreenGo != null && resultScreenGo.activeSelf;

        void OnDeliverClicked()
        {
            if (IsInputLocked) return;

            // 손패를 "사용 중"으로 잠그는 일은 여기서 하지 않는다 - 전달이 실제로 시작됐는지는
            // TargetingController만 알고(Phase 불일치나 튜토리얼 필터로 조기 return할 수 있다),
            // 그 결과는 IsDelivering으로 드러난다. RefreshOwnedCards가 그 값을 보고 매번 다시
            // 계산하므로, 시작되지 않은 전달 때문에 카드가 잠긴 채 남는 일이 없다.
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
            missionConditionsText = view.MissionConditionsText;
            nextMissionText = view.NextMissionText;
            nextMissionCardRoot = view.NextMissionCardRoot;
            nextMissionCardTitleText = view.NextMissionCardTitleText;
            nextMissionCardDescText = view.NextMissionCardDescText;
            nextMissionConnectorGo = view.NextMissionConnectorGo;

            profileTabButton = view.ProfileTabButton;
            logTabButton = view.LogTabButton;
            profileTabIndicator = view.ProfileTabIndicator;
            logTabIndicator = view.LogTabIndicator;
            profileTabBadgeGo = view.ProfileTabBadge;
            logTabBadgeGo = view.LogTabBadge;
            // 탭 버튼과 같은 캔버스 안에 있다 - HudView에 필드를 하나 더 만들어 씬마다 배선하는 것보다
            // 여기서 찾는 편이 안전하다(배선을 빠뜨리면 조용히 안 열린다).
            rightDocumentPanel = view.GetComponentInChildren<Mockup.RightDocumentPanelController>(true);

            logPanelGo = view.LogPanelGo;
            logTopDialogueText = view.LogTopDialogueText;
            logGeneralText = view.LogGeneralText;
            logStatHeaderText = view.LogStatHeaderText;
            logTrustArrowHead = view.LogTrustArrowHead;
            logTrustArrowLine = view.LogTrustArrowLine;
            logTrustLowLabel = view.LogTrustLowLabel;
            logTrustHighLabel = view.LogTrustHighLabel;
            logTrustPrevMarker = view.LogTrustPrevMarker;
            logTrustDeltaSegment = view.LogTrustDeltaSegment;
            logBottomDialogueText = view.LogBottomDialogueText;

            locationNoteGo = view.LocationNoteGo;
            locationNoteRect = locationNoteGo != null ? locationNoteGo.GetComponent<RectTransform>() : null;
            locationNoteTitleText = view.LocationNoteTitleText;
            locationNoteBodyText = view.LocationNoteBodyText;

            npcProfileGo = view.NpcProfileGo;
            npcPortraitFrameGo = view.NpcPortraitFrameGo;
            npcPortraitImage = view.NpcPortraitImage;
            npcNameText = view.NpcNameText;
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
            barBackgroundGo = view.BarBackgroundGo;
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
            resultLayout = view.ResultLayout;
            resultTitleText = view.ResultTitleText;
            resultDescText = view.ResultDescText;
            resultMissionNoText = view.ResultMissionNoText;
            resultStageLabelText = view.ResultStageLabelText;
            resultStageTagText = view.ResultStageTagText;
            resultTurnsText = view.ResultTurnsText;
            resultPrimaryButtonGo = view.ResultPrimaryButtonGo;
            resultSecondaryButtonGo = view.ResultSecondaryButtonGo;
            resultSecondaryButtonLabel = view.ResultSecondaryButtonLabel;
            resultPrimaryButton = view.ResultPrimaryButton;
            resultSecondaryButton = view.ResultSecondaryButton;
            resultPrimaryTabHover = resultPrimaryButton != null
                ? resultPrimaryButton.GetComponent<ResultTabHoverFeedback>() : null;
            pauseMenu = view != null ? view.GetComponentInParent<PauseMenuController>() : null;

            feedbackBannerRect = view.FeedbackBannerRect;
            if (feedbackBannerRect != null)
            {
                feedbackBannerFullMaxX = feedbackBannerRect.anchorMax.x;
                feedbackBannerFullMinX = feedbackBannerRect.anchorMin.x;
            }

            feedbackGo = view.FeedbackGo;
            feedbackCanvasGroup = view.FeedbackCanvasGroup;
            feedbackText = view.FeedbackText;

            // 정적 리스너(팝업마다 바뀌는 overlayButton/resultPrimaryButton 등과 달리 한 번만 붙이면 됨).
            view.HelpButton.onClick.AddListener(() => howToPlayPopup?.Show());
            deliverButton.onClick.AddListener(OnDeliverClicked);
            if (profileTabButton != null) profileTabButton.onClick.AddListener(OnProfileTabClicked);
            if (logTabButton != null) logTabButton.onClick.AddListener(OnLogTabClicked);
            ClearLogPanel();
            // 이전 씬에서 응답을 기다리던 요청이 남아 있으면 표시가 켜진 채로 굳는다 - 새 구역을
            // 시작하는 이 지점에서 기다리는 것은 없으므로 0으로 되돌린다.
            Belief.AI.LLM.LlmRequestMonitor.Reset();
            SetHudPanelState(HudPanelState.Default);

            howToPlayPopup = view.gameObject.AddComponent<HowToPlayPopup>();
            howToPlayPopup.Build(canvasRoot, skin);
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
