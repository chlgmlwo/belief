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

        [Header("Right Panel Tabs (section 7 - 기본/프로필/로그 상태 전환)")]
        [SerializeField] Button profileTabButton;
        [SerializeField] Button logTabButton;
        [SerializeField] Image profileTabIndicator;
        [SerializeField] Image logTabIndicator;

        [Header("Log Panel")]
        [SerializeField] GameObject logPanelGo;
        [SerializeField] TMP_Text logTopDialogueText;
        [SerializeField] TMP_Text logGeneralText;
        [SerializeField] TMP_Text logStatHeaderText;
        [SerializeField] TMP_Text logBeliefFromText;
        [SerializeField] TMP_Text logBeliefToText;
        [SerializeField] TMP_Text logBottomDialogueText;

        [Header("Location Note")]
        [SerializeField] GameObject locationNoteGo;
        [SerializeField] TMP_Text locationNoteTitleText;
        [SerializeField] TMP_Text locationNoteBodyText;

        [Header("NPC Profile Panel")]
        [SerializeField] GameObject npcProfileGo;
        [SerializeField] TMP_Text npcNameText;
        [SerializeField] TMP_Text npcBasicInfoText;
        [SerializeField] TMP_Text npcBeliefTierText;
        [SerializeField] TMP_Text npcBeliefDialogueText;
        [SerializeField] Transform npcRelationshipsRoot;
        [SerializeField] TMP_Text npcHistoryText;
        [SerializeField] GameObject npcNoneStickerGo;

        [Header("Bottom Panel")]
        [SerializeField] RectTransform bottomPanelRect;
        [SerializeField] TMP_Text ownedCountLabel;
        [SerializeField] Transform ownedRoot;
        [SerializeField] GameObject cardInfoGo;
        [SerializeField] TMP_Text cardTitleText;
        [SerializeField] TMP_Text cardDescText;
        [SerializeField] TMP_Text cardKindText;
        [SerializeField] GameObject instructionGo;
        [SerializeField] TMP_Text instructionText;
        [SerializeField] GameObject deliverButtonGo;
        [SerializeField] Button deliverButton;
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
        [SerializeField] TMP_Text resultTitleText;
        [SerializeField] TMP_Text resultDescText;
        [SerializeField] TMP_Text resultStageTagText;
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

        public Button ProfileTabButton => profileTabButton;
        public Button LogTabButton => logTabButton;
        public Image ProfileTabIndicator => profileTabIndicator;
        public Image LogTabIndicator => logTabIndicator;

        public GameObject LogPanelGo => logPanelGo;
        public TMP_Text LogTopDialogueText => logTopDialogueText;
        public TMP_Text LogGeneralText => logGeneralText;
        public TMP_Text LogStatHeaderText => logStatHeaderText;
        public TMP_Text LogBeliefFromText => logBeliefFromText;
        public TMP_Text LogBeliefToText => logBeliefToText;
        public TMP_Text LogBottomDialogueText => logBottomDialogueText;

        public GameObject LocationNoteGo => locationNoteGo;
        public TMP_Text LocationNoteTitleText => locationNoteTitleText;
        public TMP_Text LocationNoteBodyText => locationNoteBodyText;

        public GameObject NpcProfileGo => npcProfileGo;
        public TMP_Text NpcNameText => npcNameText;
        public TMP_Text NpcBasicInfoText => npcBasicInfoText;
        public TMP_Text NpcBeliefTierText => npcBeliefTierText;
        public TMP_Text NpcBeliefDialogueText => npcBeliefDialogueText;
        public Transform NpcRelationshipsRoot => npcRelationshipsRoot;
        public TMP_Text NpcHistoryText => npcHistoryText;
        public GameObject NpcNoneStickerGo => npcNoneStickerGo;

        public RectTransform BottomPanelRect => bottomPanelRect;
        public TMP_Text OwnedCountLabel => ownedCountLabel;
        public Transform OwnedRoot => ownedRoot;
        public GameObject CardInfoGo => cardInfoGo;
        public TMP_Text CardTitleText => cardTitleText;
        public TMP_Text CardDescText => cardDescText;
        public TMP_Text CardKindText => cardKindText;
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
        public TMP_Text ResultTitleText => resultTitleText;
        public TMP_Text ResultDescText => resultDescText;
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
