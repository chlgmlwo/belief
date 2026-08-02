using TMPro;
using UnityEngine;

namespace Belief.Data
{
    /// <summary>HudPresenter/MainMenuPresenter/StageBriefingPresenter가 공유하는 시각 자산 테이블.
    /// 씬마다 개별 Sprite/Font 필드를 30개씩 따로 연결하지 않고, 이 자산 하나만 Inspector에서
    /// 연결하면 된다. 값이 비어 있는 필드는 호출부가 기존 단색 placeholder로 자동 대체한다
    /// (CreatePanel(..., Sprite sprite, Color fallback)) - 자산이 일부만 채워져도 안전하게 동작한다.</summary>
    [CreateAssetMenu(fileName = "PlayHudSkin", menuName = "Belief/UI/Play Hud Skin")]
    public class PlayHudSkin : ScriptableObject
    {
        [Header("Fonts")]
        public TMP_FontAsset titleFont;      // SeoulNamsanEB - 스테이지/결과 대제목 전용(반복 등장 X, 1회성 헤드라인만)
        public TMP_FontAsset extraBoldFont;  // SUIT ExtraBold - 관계도 태그값 등 강조 칩
        public TMP_FontAsset heavyFont;      // SUIT Heavy - 카드 제목/CODE 배지
        public TMP_FontAsset boldFont;       // SUIT Bold - 라벨/버튼/미션제목/장소명/카드제목(관계도)
        public TMP_FontAsset semiBoldFont;   // SUIT SemiBold - 서브헤더(수치변동, 장소상세 제목)
        public TMP_FontAsset bodyFont;       // SUIT Regular - 본문/로그
        public TMP_FontAsset mediumFont;     // SUIT Medium - 카드 본문
        public TMP_FontAsset lightFont;      // SUIT Light - 설명/보조 텍스트
        public TMP_FontAsset extraLightFont; // SUIT ExtraLight - 태그/링크/해시태그
        public TMP_FontAsset numberFont;     // TravelingTypewriter - 턴 숫자/타자기 라벨/워터마크

        [Header("Frame / Header")]
        public Sprite screenFrame;
        public Sprite missionTab;
        public Sprite turnTab;
        public Sprite stageNameTag;
        public Sprite narrationBar;

        [Header("Goal / Mission Checklist")]
        public Sprite goalCard1;
        public Sprite goalCard2;
        public Sprite goalCard3;
        public Sprite goalConnector;
        public Sprite successBadge;
        public Sprite missionClearedLine;
        public Sprite missionNotClearedLine;

        [Header("NPC Profile / Log")]
        public Sprite npcPhotoFrame;
        public Sprite npcDialogueBubble;
        public Sprite profileFileBackground;
        public Sprite pin;
        public Sprite hashtagChip;
        public Sprite noneSticker;
        public Sprite logMainBackground;
        public Sprite logNpcLine;
        public Sprite logNpcLine2;
        public Sprite logDivider;
        public Sprite logStatChangeHeader;

        [Header("World / Card")]
        public Sprite infoCardFrame;
        public Sprite spreadTag;
        public Sprite spreadSelectedTag;
        public Sprite locationTag3;
        public Sprite locationTag5;
        public Sprite locationImageFrame;
        public Sprite locationDetailCard;
        public Sprite locationConnector; // 장소간 연결 UI - LocationSiteView 사이 LineRenderer 텍스처
        public Sprite blockedStamp; // 접선 UI - 전달 불가 상태

        [Header("Stage Briefing")]
        public Sprite briefingBackground;
        public Sprite launchButton;
        public Sprite launchButtonHoverUnderline;
        public Sprite lockedStageIcon;
        public Sprite currentStageIcon;

        [Header("Result")]
        public Sprite resultBackground;
        public Sprite successPanel;
        public Sprite failurePanel;
        public Sprite successPhotoFrame;
        public Sprite failurePhotoFrame;
    }
}
