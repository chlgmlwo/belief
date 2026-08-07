using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Belief.Presentation.HUD
{
    /// <summary>HudCanvas.prefab(에디터에서 직접 배치된 하이어라키)의 자식 참조 테이블 -
    /// CardTileView/StageBriefingView와 동일한 패턴. 폰트/크기/색/앵커/스프라이트 같은 정적 스타일은
    /// 전부 프리팹 자체에 이미 구워져 있으므로, HudPresenter는 여기 노출된 참조에 값만 대입하거나
    /// (텍스트 내용, 활성 여부) MissionConditionRowView/NpcRelationshipRowView/CardTileView를
    /// ConditionsRoot/RelationshipsRoot/OwnedRoot 아래에 계속 동적으로 Instantiate한다.</summary>
    public class HudView : MonoBehaviour
    {
        [Header("Header")]
        [SerializeField] TMP_Text stageTurnText;
        [SerializeField] TMP_Text missionTurnText;
        [SerializeField] TMP_Text stageNumberText;
        [SerializeField] TMP_Text stageNameText;
        [SerializeField] Button helpButton;

        [Header("Mission Panel")]
        [SerializeField] TMP_Text missionTitleText;
        [SerializeField] TMP_Text missionDescText;
        [SerializeField] Transform missionConditionsRoot;
        [SerializeField] TMP_Text missionTurnsText;
        [SerializeField] TMP_Text nextMissionText;

        /// <summary>GOAL2 카드(다음 미션 미리보기) - 원래는 GoalCardConditionAdapter가 매 프레임
        /// GameInstaller.StageAsset.missions를 직접 조회해 채웠으나, 디자인이 "현재 미션 카드 옆에
        /// 다음 미션을 미리 보여준다"로 안정된 뒤 HudPresenter.RefreshMission()으로 흡수했다(순수
        /// 표시 로직이 아니라 미션 진행 상태를 다루므로 미션 갱신 지점 하나에 모아두는 게 맞다).</summary>
        [SerializeField] GameObject nextMissionCardRoot;
        [SerializeField] TMP_Text nextMissionCardTitleText;
        [SerializeField] TMP_Text nextMissionCardDescText;
        [SerializeField] GameObject nextMissionConnectorGo;

        [Header("Right Panel Tabs (section 7 - 기본/프로필/로그 상태 전환)")]
        [SerializeField] Button profileTabButton;
        [SerializeField] Button logTabButton;
        [SerializeField] Image profileTabIndicator;
        [SerializeField] Image logTabIndicator;

        /// <summary>탭 위에 얹히는 빨간 점 - 닫아 둔 동안 그 문서에 새로 쌓인 내용이 있다는 표시다.
        /// 켜고 끄는 판단은 전부 HudPresenter가 한다(무엇이 "새 내용"인지는 데이터 쪽 사정이라).</summary>
        [SerializeField] GameObject profileTabBadge;
        [SerializeField] GameObject logTabBadge;

        [Header("Log Panel")]
        [SerializeField] GameObject logPanelGo;
        [SerializeField] TMP_Text logTopDialogueText;
        [SerializeField] TMP_Text logGeneralText;
        [SerializeField] TMP_Text logStatHeaderText;
        /// <summary>믿음 눈금(불신 ——→ 신뢰)의 선·양 끝 라벨·표식. 라벨 <b>문구</b>는 시안대로
        /// 고정이라 코드가 바꾸지 않지만, 아직 판단이 하나도 없을 때는 눈금 전체를 감춘다 -
        /// 빈 눈금만 떠 있으면 기록이 있는 것처럼 보인다.</summary>
        [SerializeField] RectTransform logTrustArrowLine;
        [SerializeField] RectTransform logTrustArrowHead;
        [SerializeField] TMP_Text logTrustLowLabel;
        [SerializeField] TMP_Text logTrustHighLabel;
        /// <summary>변동 표시 - 이전 위치의 흐린 점과 이전↔이후를 잇는 색 구간. 눈금 위 표식
        /// 하나만으로는 어디에서 왔는지·올랐는지 내렸는지를 알 수 없어 함께 둔다.</summary>
        [SerializeField] RectTransform logTrustPrevMarker;
        [SerializeField] RectTransform logTrustDeltaSegment;
        [SerializeField] TMP_Text logBottomDialogueText;

        [Header("Location Note")]
        [SerializeField] GameObject locationNoteGo;
        [SerializeField] TMP_Text locationNoteTitleText;
        [SerializeField] TMP_Text locationNoteBodyText;

        [Header("NPC Profile Panel")]
        [SerializeField] GameObject npcProfileGo;

        /// <summary>조사 파일 좌상단 사진 프레임에 들어가는 인물 사진 - NpcData.characterPhoto(전신)를
        /// 그대로 넣고, 부모 프레임의 RectMask2D가 잘라내 상반신만 보이게 한다(사용자 지시:
        /// "png 사진 위쪽만 보여주는 방식"). 사진이 없는 NPC는 프레임째로 숨긴다.</summary>
        [SerializeField] GameObject npcPortraitFrameGo;
        [SerializeField] Image npcPortraitImage;
        [SerializeField] TMP_Text npcNameText;
        [SerializeField] TMP_Text npcBeliefTierText;
        [SerializeField] TMP_Text npcBeliefDialogueText;
        [SerializeField] Transform npcRelationshipsRoot;
        [SerializeField] TMP_Text npcHistoryText;
        [SerializeField] GameObject npcNoneStickerGo;

        [Header("NPC Profile Panel - 성격 태그 (NpcData 특성 태그 5종)")]
        [SerializeField] TMP_Text npcJudgmentTendencyText;
        [SerializeField] TMP_Text npcPriorityText;
        [SerializeField] TMP_Text npcSensitiveInfoText;
        [SerializeField] TMP_Text npcRelationTendencyText;
        [SerializeField] TMP_Text npcTrustJudgmentText;

        [Header("Bottom Panel")]
        [SerializeField] RectTransform bottomPanelRect;
        [SerializeField] TMP_Text ownedCountLabel;
        [SerializeField] Transform ownedRoot;
        [SerializeField] GameObject cardInfoGo;
        [SerializeField] TMP_Text cardTitleText;
        [SerializeField] TMP_Text cardDescText;
        [SerializeField] TMP_Text cardKindText;
        /// <summary>하단 안내 띠의 어두운 판 + 강조선. 지속 안내와 일시 알림이 이 판 하나를 같이 쓰므로
        /// 둘 중 하나라도 표시 중이면 켜고, 둘 다 없으면 꺼서 빈 띠가 남지 않게 한다(3-85).</summary>
        [SerializeField] GameObject barBackgroundGo;
        [SerializeField] GameObject instructionGo;
        [SerializeField] TMP_Text instructionText;
        [SerializeField] GameObject deliverButtonGo;
        [SerializeField] Button deliverButton;
        /// <summary>3-85에서 하단 안내 띠로 통합되며 폐기됐다 - 프리팹에 남아 있어도 항상 꺼 둔다.</summary>
        [SerializeField] GameObject noSelectionHintGo;

        [Header("Overlay")]
        [SerializeField] GameObject overlayGo;
        [SerializeField] CanvasGroup overlayCanvasGroup;
        [SerializeField] Transform overlayBox;
        [SerializeField] TMP_Text overlayTitleText;
        [SerializeField] TMP_Text overlayDescText;
        [SerializeField] GameObject overlayButtonGo;
        [SerializeField] TMP_Text overlayButtonLabel;
        [SerializeField] Button overlayButton;

        [Header("Result Screen")]
        [SerializeField] GameObject resultScreenGo;
        [SerializeField] CanvasGroup resultCanvasGroup;
        [SerializeField] Image resultPanelImg;
        [SerializeField] Image resultPhotoFrameImg;

        /// <summary>성공/실패 아트는 구성이 서로 뒤집혀 있어 같은 텍스트를 결과에 따라 옮겨 쓴다.
        /// 실제 좌표는 ResultScreenLayout에 성공용·실패용 한 쌍씩 들어 있다.</summary>
        [SerializeField] ResultScreenLayout resultLayout;

        [SerializeField] TMP_Text resultTitleText;
        [SerializeField] TMP_Text resultDescText;
        /// <summary>리포트 상단 "NO. 001" - 이 구역 안에서 이 미션이 몇 번째인지.</summary>
        [SerializeField] TMP_Text resultMissionNoText;
        /// <summary>고리 태그 위 두 줄 - 위는 "STAGE n", 아래는 구역 이름(길면 자동 축소).</summary>
        [SerializeField] TMP_Text resultStageLabelText;
        [SerializeField] TMP_Text resultStageTagText;
        /// <summary>"사용한 턴:"과 "Turn"은 아트에 이미 인쇄돼 있으므로 숫자만 넣는다.</summary>
        [SerializeField] TMP_Text resultTurnsText;
        [SerializeField] GameObject resultPrimaryButtonGo;
        [SerializeField] GameObject resultSecondaryButtonGo;
        [SerializeField] TMP_Text resultPrimaryButtonLabel;
        [SerializeField] TMP_Text resultSecondaryButtonLabel;
        [SerializeField] Button resultPrimaryButton;
        [SerializeField] Button resultSecondaryButton;

        [Header("Feedback Banner")]
        [SerializeField] RectTransform feedbackBannerRect;
        [SerializeField] GameObject feedbackGo;
        [SerializeField] CanvasGroup feedbackCanvasGroup;
        [SerializeField] TMP_Text feedbackText;

        public TMP_Text StageTurnText => stageTurnText;
        public TMP_Text MissionTurnText => missionTurnText;
        public TMP_Text StageNumberText => stageNumberText;
        public TMP_Text StageNameText => stageNameText;
        public Button HelpButton => helpButton;

        public TMP_Text MissionTitleText => missionTitleText;
        public TMP_Text MissionDescText => missionDescText;
        public Transform MissionConditionsRoot => missionConditionsRoot;
        public TMP_Text MissionTurnsText => missionTurnsText;
        public TMP_Text NextMissionText => nextMissionText;

        public GameObject NextMissionCardRoot => nextMissionCardRoot;
        public TMP_Text NextMissionCardTitleText => nextMissionCardTitleText;
        public TMP_Text NextMissionCardDescText => nextMissionCardDescText;
        public GameObject NextMissionConnectorGo => nextMissionConnectorGo;

        public Button ProfileTabButton => profileTabButton;
        public Button LogTabButton => logTabButton;
        public Image ProfileTabIndicator => profileTabIndicator;
        public Image LogTabIndicator => logTabIndicator;
        public GameObject ProfileTabBadge => profileTabBadge;
        public GameObject LogTabBadge => logTabBadge;

        public GameObject LogPanelGo => logPanelGo;
        public TMP_Text LogTopDialogueText => logTopDialogueText;
        public TMP_Text LogGeneralText => logGeneralText;
        public TMP_Text LogStatHeaderText => logStatHeaderText;
        public RectTransform LogTrustArrowLine => logTrustArrowLine;
        public RectTransform LogTrustArrowHead => logTrustArrowHead;
        public TMP_Text LogTrustLowLabel => logTrustLowLabel;
        public TMP_Text LogTrustHighLabel => logTrustHighLabel;
        public RectTransform LogTrustPrevMarker => logTrustPrevMarker;
        public RectTransform LogTrustDeltaSegment => logTrustDeltaSegment;
        public TMP_Text LogBottomDialogueText => logBottomDialogueText;

        public GameObject LocationNoteGo => locationNoteGo;
        public TMP_Text LocationNoteTitleText => locationNoteTitleText;
        public TMP_Text LocationNoteBodyText => locationNoteBodyText;

        public GameObject NpcProfileGo => npcProfileGo;
        public GameObject NpcPortraitFrameGo => npcPortraitFrameGo;
        public Image NpcPortraitImage => npcPortraitImage;
        public TMP_Text NpcNameText => npcNameText;
        public TMP_Text NpcBeliefTierText => npcBeliefTierText;
        public TMP_Text NpcBeliefDialogueText => npcBeliefDialogueText;
        public Transform NpcRelationshipsRoot => npcRelationshipsRoot;
        public TMP_Text NpcHistoryText => npcHistoryText;
        public GameObject NpcNoneStickerGo => npcNoneStickerGo;

        public TMP_Text NpcJudgmentTendencyText => npcJudgmentTendencyText;
        public TMP_Text NpcPriorityText => npcPriorityText;
        public TMP_Text NpcSensitiveInfoText => npcSensitiveInfoText;
        public TMP_Text NpcRelationTendencyText => npcRelationTendencyText;
        public TMP_Text NpcTrustJudgmentText => npcTrustJudgmentText;

        public RectTransform BottomPanelRect => bottomPanelRect;
        public TMP_Text OwnedCountLabel => ownedCountLabel;
        public Transform OwnedRoot => ownedRoot;
        public GameObject CardInfoGo => cardInfoGo;
        public TMP_Text CardTitleText => cardTitleText;
        public TMP_Text CardDescText => cardDescText;
        public TMP_Text CardKindText => cardKindText;
        public GameObject BarBackgroundGo => barBackgroundGo;
        public GameObject InstructionGo => instructionGo;
        public TMP_Text InstructionText => instructionText;
        public GameObject DeliverButtonGo => deliverButtonGo;
        public Button DeliverButton => deliverButton;
        public GameObject NoSelectionHintGo => noSelectionHintGo;

        public GameObject OverlayGo => overlayGo;
        public CanvasGroup OverlayCanvasGroup => overlayCanvasGroup;
        public Transform OverlayBox => overlayBox;
        public TMP_Text OverlayTitleText => overlayTitleText;
        public TMP_Text OverlayDescText => overlayDescText;
        public GameObject OverlayButtonGo => overlayButtonGo;
        public TMP_Text OverlayButtonLabel => overlayButtonLabel;
        public Button OverlayButton => overlayButton;

        public GameObject ResultScreenGo => resultScreenGo;
        public CanvasGroup ResultCanvasGroup => resultCanvasGroup;
        public Image ResultPanelImg => resultPanelImg;
        public Image ResultPhotoFrameImg => resultPhotoFrameImg;
        public ResultScreenLayout ResultLayout => resultLayout;
        public TMP_Text ResultTitleText => resultTitleText;
        public TMP_Text ResultDescText => resultDescText;
        public TMP_Text ResultMissionNoText => resultMissionNoText;
        public TMP_Text ResultStageLabelText => resultStageLabelText;
        public TMP_Text ResultStageTagText => resultStageTagText;
        public TMP_Text ResultTurnsText => resultTurnsText;
        public GameObject ResultPrimaryButtonGo => resultPrimaryButtonGo;
        public GameObject ResultSecondaryButtonGo => resultSecondaryButtonGo;
        public TMP_Text ResultPrimaryButtonLabel => resultPrimaryButtonLabel;
        public TMP_Text ResultSecondaryButtonLabel => resultSecondaryButtonLabel;
        public Button ResultPrimaryButton => resultPrimaryButton;
        public Button ResultSecondaryButton => resultSecondaryButton;

        public RectTransform FeedbackBannerRect => feedbackBannerRect;
        public GameObject FeedbackGo => feedbackGo;
        public CanvasGroup FeedbackCanvasGroup => feedbackCanvasGroup;
        public TMP_Text FeedbackText => feedbackText;
    }
}
