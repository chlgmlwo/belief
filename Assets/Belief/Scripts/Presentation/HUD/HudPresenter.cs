using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
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
    /// </summary>
    public class HudPresenter : MonoBehaviour
    {
        [SerializeField] GameInstaller installer;
        [SerializeField] TargetingController targeting;
        [SerializeField] WorldPresenter worldPresenter;
        [SerializeField] CardTileView cardTilePrefab;
        [SerializeField] TMP_FontAsset koreanFont;

        const int MaxLogLines = 12;

        static readonly Color PanelColor = new Color(0.09f, 0.12f, 0.10f, 0.95f);
        static readonly Color AccentColor = new Color(0.30f, 0.85f, 0.55f);
        static readonly Color MutedText = new Color(0.72f, 0.78f, 0.74f);
        static readonly Color ErrorColor = new Color(0.95f, 0.45f, 0.40f);

        const float PopupFadeDuration = 0.25f;
        const float IntroHoldDuration = 1.6f;
        const float PopupScaleIn = 0.94f;
        const float PopupScaleOut = 0.97f;

        TMP_Text stageTurnText;
        TMP_Text missionTurnText;
        TMP_Text missionTitleText;
        TMP_Text missionDescText;
        Transform missionConditionsRoot;
        readonly List<TMP_Text> missionConditionRows = new List<TMP_Text>();
        TMP_Text missionTurnsText;
        TMP_Text nextMissionText;
        TMP_Text logText;
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

        // 장소 특성 메모(section 2) - LocationData의 신규 콘텐츠 필드를 enum 그대로 표시한다.
        GameObject locationNoteGo;
        TMP_Text locationNoteTitleText;
        TMP_Text locationNoteBodyText;
        LocationData selectedLocationData;

        // NPC 조사 파일 패널(section 6) - 공용 패널 하나, 클릭한 NPC로 내용만 교체한다.
        GameObject npcProfileGo;
        TMP_Text npcNameText;
        TMP_Text npcBasicInfoText;
        TMP_Text npcRoleText;
        TMP_Text npcBeliefTierText;
        TMP_Text npcBeliefDialogueText;
        Transform npcRelationshipsRoot;
        readonly List<TMP_Text> npcRelationshipRows = new List<TMP_Text>();
        TMP_Text npcHistoryText;
        NpcState selectedNpcState;

        GameObject overlayGo;
        CanvasGroup overlayCanvasGroup;
        Transform overlayBox;
        TMP_Text overlayTitleText, overlayDescText;
        GameObject overlayButtonGo;
        TMP_Text overlayButtonLabel;
        Button overlayButton;

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
            bus.Subscribe<GameOverEvent>(e => { if (e.Won) ShowFinalVictory(); else ShowMissionFailedPopup(); });
            installer.Log.OnLogAdded += AppendLog;
            targeting.PhaseChanged += RefreshBottomPanel;
            targeting.InteractionRejected += msg => ShowTransientNotice(msg, ErrorColor);

            // NPC/장소 조사용 클릭 구독 - TargetingController가 같은 이벤트를 전달 대상 지정용으로
            // 이미 소비하고 있지만, WorldPresenter.NpcClicked/LocationClicked는 멀티캐스트라 여기서
            // 순수 조회(조사 파일/특성 메모 표시)용으로 추가 구독해도 기존 전달 흐름과 충돌하지 않는다.
            if (worldPresenter != null)
            {
                worldPresenter.NpcClicked += OnNpcClickedForProfile;
                worldPresenter.LocationClicked += OnLocationClickedForNote;
            }

            var pc = ProgressionController.Instance;
            if (pc != null)
            {
                pc.ObjectivesChanged += RefreshMission;
                pc.ObjectiveCompletedPendingConfirm += OnObjectiveCompletedPending;
                pc.StageCompletedPendingConfirm += OnStageCompletedPending;

                // 구역 안내 패널(section 3)은 StageData.regionName/regionDescription을 우선 사용하고,
                // StageData가 없거나 값이 비어 있을 때만 기존 ProgressionData 문구로 하위 호환한다.
                var stageAsset = installer.StageAsset;
                string regionName = stageAsset != null && !string.IsNullOrEmpty(stageAsset.regionName)
                    ? stageAsset.regionName : pc.CurrentStageDisplayName;
                string regionDesc = stageAsset != null && !string.IsNullOrEmpty(stageAsset.regionDescription)
                    ? stageAsset.regionDescription : pc.CurrentStageIntroSubtitle;

                if (!string.IsNullOrEmpty(regionName))
                    StartCoroutine(IntroPopupRoutine(regionName, regionDesc));
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
            ShowGatedPopup("MISSION COMPLETE", AccentColor, completed.displayTitle, "다음",
                () => ProgressionController.Instance?.ConfirmMissionComplete());

        /// <summary>구역의 마지막 목표가 완료됐지만 아직 확인 대기 중일 때 호출된다 - "ZONE COMPLETE" 팝업을
        /// 띄우고 [다음 구역] 클릭 시에만 ProgressionController에 다음 씬 로드를 맡긴다.</summary>
        void OnStageCompletedPending()
        {
            var pc = ProgressionController.Instance;
            ShowGatedPopup("ZONE COMPLETE", AccentColor, pc != null ? pc.CurrentStageDisplayName : "", "다음 구역",
                () => pc?.ConfirmZoneComplete());
        }

        void ShowMissionFailedPopup()
        {
            var pc = ProgressionController.Instance;
            string missionTitle = pc?.CurrentObjective()?.displayTitle ?? "";
            ShowGatedPopup("MISSION FAILED", ErrorColor, $"{missionTitle}\n턴을 모두 소진했습니다.", "재시작",
                () => pc?.RestartCurrentMission());
        }

        void ShowFinalVictory()
        {
            overlayGo.SetActive(true);
            overlayCanvasGroup.alpha = 0f;
            overlayTitleText.text = "임무 성공";
            overlayTitleText.color = AccentColor;
            overlayDescText.text = "목표를 달성했습니다.";
            overlayButtonGo.SetActive(false);
            StartCoroutine(FadePopupIn());
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

        /// <summary>구역 시작 시 한 번, 게임 입력이 시작되기 전에 표시된다 - Fade In -> 대기 -> Fade Out
        /// 순서로 자동 진행하며(버튼 없음), Space/우클릭으로 스킵할 수 있다. 재생 중에는 Overlay의
        /// blocksInput 배경이 그대로 입력을 막는다.</summary>
        IEnumerator IntroPopupRoutine(string title, string subtitle)
        {
            overlayGo.SetActive(true);
            overlayCanvasGroup.alpha = 0f;
            overlayTitleText.text = title;
            overlayTitleText.color = AccentColor;
            overlayDescText.text = subtitle;
            overlayButtonGo.SetActive(false);

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

            float hold = 0f;
            while (hold < IntroHoldDuration && !skip)
            {
                hold += Time.deltaTime;
                yield return null;
            }

            t = 0f;
            while (t < PopupFadeDuration && !skip)
            {
                t += Time.deltaTime;
                float e = Mathf.SmoothStep(0f, 1f, t / PopupFadeDuration);
                overlayCanvasGroup.alpha = 1f - e;
                overlayBox.localScale = Vector3.one * Mathf.Lerp(1f, PopupScaleOut, e);
                yield return null;
            }
            overlayCanvasGroup.alpha = 0f;
            overlayBox.localScale = Vector3.one;

            PlaybackDirector.Instance?.Unregister(playback);
            overlayGo.SetActive(false);

            MaybeStartTutorial();
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
            missionTurnText.text = $"MISSION TURN {missionShown}/{turns.MaxTurns}";

            int stageShown = Mathf.Min(turns.StageTurn, turns.StageMaxTurns);
            stageTurnText.text = $"STAGE TURN {stageShown}/{turns.StageMaxTurns}";
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
                return;
            }

            missionTitleText.text = objective.displayTitle;
            missionDescText.text = objective.objectiveText;

            var context = new MissionEvaluationContext(installer.Locations, installer.Npcs, installer.Turns.DeliveredInformationCards);
            if (objective.successConditions != null)
            {
                foreach (var condition in objective.successConditions)
                {
                    if (condition == null) continue;
                    bool met = condition.GetCurrentProgress(context) >= condition.TargetCount;
                    string label = string.IsNullOrEmpty(condition.displayLabel) ? condition.name : condition.displayLabel;
                    // KoreanUI SDF 폰트 에셋에 ☑/☐(U+2611/U+2610) 글리프가 없어 폴백 사각형(□)으로
                    // 깨져 보이던 문제 - 폰트에 이미 있는 대괄호+ASCII 조합으로 대체한다.
                    AddMissionConditionRow((met ? "[X] " : "[ ] ") + label);
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

        /// <summary>DestroyImmediate를 쓴다 - Destroy()는 프레임 끝까지 파괴를 미루므로, 같은 프레임 안에
        /// RefreshMission()이 두 번 이상 호출되면(예: 미션 완료 직후 ConfirmMissionComplete까지 같은 호출
        /// 흐름에서 이어지는 경우) 이전 행이 실제로는 아직 살아있어 새 행과 함께 중복 표시된다.</summary>
        void ClearMissionConditionRows()
        {
            foreach (var row in missionConditionRows) DestroyImmediate(row.gameObject);
            missionConditionRows.Clear();
        }

        void AddMissionConditionRow(string text)
        {
            var row = CreateText(missionConditionsRoot, "ConditionRow", text, 14, TextAlignmentOptions.TopLeft);
            row.textWrappingMode = TextWrappingModes.Normal;
            // 조건 문구가 길어 2줄로 줄바꿈되면 고정 preferredHeight(기존 20)로는 다음 행과 겹친다 -
            // ContentSizeFitter로 실제 줄바꿈된 높이만큼 행 높이가 늘어나게 한다.
            var fitter = row.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            missionConditionRows.Add(row);
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

            if (selectedNpcState == null)
            {
                npcNameText.text = "";
                npcBasicInfoText.text = "";
                npcRoleText.text = "";
                npcBeliefTierText.text = "";
                npcBeliefDialogueText.text = "";
                npcHistoryText.text = "";
                return;
            }

            var data = selectedNpcState.Data;
            npcNameText.text = data.displayName;
            npcBasicInfoText.text = $"성별: {data.gender}\n직업: {data.job}\n소속: {data.affiliation}";
            npcRoleText.text = data.gameplayRoleSummary;

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
                    AddNpcRelationshipRow($"{rel.other.displayName}  ·  {label}\n{desc}");
                }
            }

            npcHistoryText.text = data.aiNotes != null && data.aiNotes.Length > 0
                ? string.Join("\n\n", data.aiNotes)
                : "";
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
                case BeliefState.Plausible: koreanLabel = "가능성이 있다고 판단함"; return "Possible";
                case BeliefState.NeedsVerification: koreanLabel = "확인이 필요하다고 판단함"; return "NeedVerification";
                case BeliefState.Doubtful: koreanLabel = "의심함"; return "Doubt";
                case BeliefState.Denied: koreanLabel = "부정함"; return "Reject";
                default: koreanLabel = "아직 판단할 정보가 없음"; return null;
            }
        }

        void ClearNpcRelationshipRows()
        {
            foreach (var row in npcRelationshipRows) DestroyImmediate(row.gameObject);
            npcRelationshipRows.Clear();
        }

        void AddNpcRelationshipRow(string text)
        {
            var row = CreateText(npcRelationshipsRoot, "RelationshipRow", text, 13, TextAlignmentOptions.TopLeft);
            row.textWrappingMode = TextWrappingModes.Normal;
            // 관계 설명이 길어 줄바꿈되면 고정 preferredHeight로는 다음 행과 겹친다 - ConditionRow와
            // 동일하게 ContentSizeFitter로 실제 줄바꿈 높이에 맞춘다.
            var fitter = row.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            npcRelationshipRows.Add(row);
        }

        // ------------------------------------------------------------ 장소 특성 메모 (section 2)

        /// <summary>WorldPresenter.LocationClicked 구독 - TargetingController의 전달 대상 지정과 무관하게
        /// 항상 클릭한 장소의 특성 메모를 연다.</summary>
        void OnLocationClickedForNote(LocationData location)
        {
            selectedLocationData = location;
            RefreshLocationNote();
        }

        void RefreshLocationNote()
        {
            if (selectedLocationData == null)
            {
                locationNoteTitleText.text = "";
                locationNoteBodyText.text = "";
                return;
            }

            locationNoteTitleText.text = selectedLocationData.displayName;
            locationNoteBodyText.text =
                $"확산 속도: {selectedLocationData.spreadSpeed}\n" +
                $"NPC 밀집도: {selectedLocationData.npcDensity}\n" +
                $"민감 정보 유형: {selectedLocationData.sensitiveInformationType}\n" +
                $"접근 권한: {selectedLocationData.accessType}\n" +
                $"신뢰도 보정: {selectedLocationData.credibilityModifier}";
        }

        void AppendLog(string _)
        {
            var entries = installer.Log.Entries;
            int start = Mathf.Max(0, entries.Count - MaxLogLines);
            var sb = new StringBuilder();
            for (int i = start; i < entries.Count; i++) sb.AppendLine(entries[i]);
            logText.text = sb.ToString();
        }

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

        // ------------------------------------------------------------ static layout (Placeholder UI)

        void BuildUI()
        {
            EnsureEventSystem();
            EnsurePhysics2DRaycaster();

            var canvasGo = new GameObject("HudCanvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            canvasRoot = canvasGo.transform;
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            canvasGo.AddComponent<GraphicRaycaster>();

            BuildHeader(canvasGo.transform);
            BuildLeftColumn(canvasGo.transform);
            BuildLocationNote(canvasGo.transform);
            BuildNpcProfilePanel(canvasGo.transform);
            BuildBottomPanel(canvasGo.transform);
            BuildFeedbackBanner(canvasGo.transform);
            BuildOverlay(canvasGo.transform);

            howToPlayPopup = canvasGo.AddComponent<HowToPlayPopup>();
            howToPlayPopup.Build(canvasGo.transform, koreanFont);
        }

        void BuildFeedbackBanner(Transform canvasT)
        {
            feedbackGo = CreatePanel(canvasT, "FeedbackBanner", new Color(0.05f, 0.06f, 0.05f, 0.92f));
            var frt = feedbackGo.GetComponent<RectTransform>();
            frt.anchorMin = new Vector2(0.25f, 0.295f);
            frt.anchorMax = new Vector2(0.99f, 0.345f);
            frt.offsetMin = Vector2.zero; frt.offsetMax = Vector2.zero;

            feedbackText = CreateText(feedbackGo.transform, "Text", "", 14, TextAlignmentOptions.Center);
            feedbackText.fontStyle = FontStyles.Bold;

            feedbackCanvasGroup = feedbackGo.AddComponent<CanvasGroup>();
            feedbackGo.SetActive(false);
        }

        void BuildHeader(Transform canvasT)
        {
            var header = CreatePanel(canvasT, "Header", new Color(0, 0, 0, 0));
            var hrt = header.GetComponent<RectTransform>();
            hrt.anchorMin = new Vector2(0f, 0.93f);
            hrt.anchorMax = new Vector2(1f, 1f);
            hrt.offsetMin = new Vector2(24, 0);
            hrt.offsetMax = new Vector2(-24, 0);

            var title = CreateText(header.transform, "Title", "BELIEF", 34, TextAlignmentOptions.Left);
            title.fontStyle = FontStyles.Bold;
            title.color = AccentColor;
            title.rectTransform.anchorMin = new Vector2(0f, 0f);
            title.rectTransform.anchorMax = new Vector2(0.3f, 1f);

            // 턴 티켓(section 11): STAGE TURN(구역 전체 누적)과 MISSION TURN(현재 미션 한정)을
            // 구분해 표시한다 - 둘 다 TurnSystem이 이미 들고 있는 값을 읽기만 한다(턴 로직 불변).
            stageTurnText = CreateText(header.transform, "StageTurnText", "STAGE TURN 1/8", 18, TextAlignmentOptions.Right);
            stageTurnText.color = MutedText;
            stageTurnText.rectTransform.anchorMin = new Vector2(0.46f, 0.55f);
            stageTurnText.rectTransform.anchorMax = new Vector2(0.94f, 1f);

            missionTurnText = CreateText(header.transform, "MissionTurnText", "MISSION TURN 1/4", 24, TextAlignmentOptions.Right);
            missionTurnText.fontStyle = FontStyles.Bold;
            missionTurnText.rectTransform.anchorMin = new Vector2(0.46f, 0.1f);
            missionTurnText.rectTransform.anchorMax = new Vector2(0.94f, 0.55f);

            var skipHint = CreateText(header.transform, "SkipHint", "연출 중 Space / 우클릭으로 건너뛰기", 12, TextAlignmentOptions.Right);
            skipHint.color = MutedText;
            skipHint.rectTransform.anchorMin = new Vector2(0.46f, 0f);
            skipHint.rectTransform.anchorMax = new Vector2(0.94f, 0.1f);

            var helpButtonGo = new GameObject("HelpButton", typeof(RectTransform));
            helpButtonGo.transform.SetParent(header.transform, false);
            var hbrt = (RectTransform)helpButtonGo.transform;
            hbrt.anchorMin = new Vector2(0.955f, 0.15f);
            hbrt.anchorMax = new Vector2(1f, 0.85f);
            hbrt.offsetMin = Vector2.zero; hbrt.offsetMax = Vector2.zero;
            var hbImg = helpButtonGo.AddComponent<Image>();
            hbImg.color = PanelColor;
            var hbBtn = helpButtonGo.AddComponent<Button>();
            hbBtn.targetGraphic = hbImg;
            hbBtn.onClick.AddListener(() => howToPlayPopup?.Show());
            var hbLabel = CreateText(helpButtonGo.transform, "Label", "?", 24, TextAlignmentOptions.Center);
            hbLabel.color = AccentColor;
            hbLabel.fontStyle = FontStyles.Bold;
            AnchorFill(hbLabel.rectTransform);
        }

        // 왼쪽 열(MissionPanel/LogPanel) 너비와 BottomPanel 높이를 줄여 화면 중앙(World)이 더 넓게
        // 보이도록 한다. 두 패널 사이 간격도 넓혀 답답해 보이지 않게 한다.
        void BuildLeftColumn(Transform canvasT)
        {
            var mission = CreatePanel(canvasT, "MissionPanel", PanelColor);
            var mrt = mission.GetComponent<RectTransform>();
            mrt.anchorMin = new Vector2(0.01f, 0.55f);
            mrt.anchorMax = new Vector2(0.21f, 0.91f);
            mrt.offsetMin = Vector2.zero; mrt.offsetMax = Vector2.zero;

            var missionLabel = CreateText(mission.transform, "Label", "MISSION", 14, TextAlignmentOptions.TopLeft);
            missionLabel.color = AccentColor;
            missionLabel.fontStyle = FontStyles.Bold;
            missionLabel.rectTransform.anchorMin = new Vector2(0f, 0.94f);
            missionLabel.rectTransform.anchorMax = new Vector2(1f, 1f);
            missionLabel.rectTransform.offsetMin = new Vector2(12, 0);
            missionLabel.rectTransform.offsetMax = new Vector2(-12, -4);

            missionTitleText = CreateText(mission.transform, "Title", "", 19, TextAlignmentOptions.TopLeft);
            missionTitleText.fontStyle = FontStyles.Bold;
            missionTitleText.textWrappingMode = TextWrappingModes.Normal;
            missionTitleText.rectTransform.anchorMin = new Vector2(0f, 0.82f);
            missionTitleText.rectTransform.anchorMax = new Vector2(1f, 0.94f);
            missionTitleText.rectTransform.offsetMin = new Vector2(12, 0);
            missionTitleText.rectTransform.offsetMax = new Vector2(-12, 0);

            missionDescText = CreateText(mission.transform, "Desc", "", 14, TextAlignmentOptions.TopLeft);
            missionDescText.color = MutedText;
            missionDescText.textWrappingMode = TextWrappingModes.Normal;
            missionDescText.rectTransform.anchorMin = new Vector2(0f, 0.58f);
            missionDescText.rectTransform.anchorMax = new Vector2(1f, 0.82f);
            missionDescText.rectTransform.offsetMin = new Vector2(12, 0);
            missionDescText.rectTransform.offsetMax = new Vector2(-12, 0);

            var conditionsGo = new GameObject("ConditionsList", typeof(RectTransform));
            conditionsGo.transform.SetParent(mission.transform, false);
            var crt = (RectTransform)conditionsGo.transform;
            crt.anchorMin = new Vector2(0f, 0.24f);
            crt.anchorMax = new Vector2(1f, 0.58f);
            crt.offsetMin = new Vector2(12, 0); crt.offsetMax = new Vector2(-12, 0);
            var cvlg = conditionsGo.AddComponent<VerticalLayoutGroup>();
            cvlg.childAlignment = TextAnchor.UpperLeft;
            cvlg.childForceExpandWidth = true;
            cvlg.childForceExpandHeight = false;
            cvlg.childControlHeight = true;
            cvlg.spacing = 4;
            missionConditionsRoot = conditionsGo.transform;

            missionTurnsText = CreateText(mission.transform, "Turns", "", 14, TextAlignmentOptions.TopLeft);
            missionTurnsText.color = AccentColor;
            missionTurnsText.rectTransform.anchorMin = new Vector2(0f, 0.12f);
            missionTurnsText.rectTransform.anchorMax = new Vector2(1f, 0.24f);
            missionTurnsText.rectTransform.offsetMin = new Vector2(12, 0);
            missionTurnsText.rectTransform.offsetMax = new Vector2(-12, 0);

            nextMissionText = CreateText(mission.transform, "NextMission", "", 12, TextAlignmentOptions.TopLeft);
            nextMissionText.color = MutedText;
            nextMissionText.textWrappingMode = TextWrappingModes.Normal;
            nextMissionText.rectTransform.anchorMin = new Vector2(0f, 0f);
            nextMissionText.rectTransform.anchorMax = new Vector2(1f, 0.12f);
            nextMissionText.rectTransform.offsetMin = new Vector2(12, 4);
            nextMissionText.rectTransform.offsetMax = new Vector2(-12, 0);

            var logPanel = CreatePanel(canvasT, "LogPanel", PanelColor);
            var lrt = logPanel.GetComponent<RectTransform>();
            lrt.anchorMin = new Vector2(0.01f, 0.34f);
            lrt.anchorMax = new Vector2(0.21f, 0.51f);
            lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;

            var logLabel = CreateText(logPanel.transform, "Label", "사건 기록", 14, TextAlignmentOptions.TopLeft);
            logLabel.color = AccentColor;
            logLabel.fontStyle = FontStyles.Bold;
            logLabel.rectTransform.anchorMin = new Vector2(0f, 0.92f);
            logLabel.rectTransform.anchorMax = new Vector2(1f, 1f);
            logLabel.rectTransform.offsetMin = new Vector2(12, 0);
            logLabel.rectTransform.offsetMax = new Vector2(-12, -4);

            logText = CreateText(logPanel.transform, "LogText", "", 14, TextAlignmentOptions.TopLeft);
            logText.color = MutedText;
            logText.textWrappingMode = TextWrappingModes.Normal;
            logText.rectTransform.anchorMin = new Vector2(0f, 0f);
            logText.rectTransform.anchorMax = new Vector2(1f, 0.92f);
            logText.rectTransform.offsetMin = new Vector2(12, 8);
            logText.rectTransform.offsetMax = new Vector2(-12, 0);
        }

        // 장소 특성 메모(section 2) - 좌측 하단, MissionPanel/LogPanel 아래에 배치한다.
        void BuildLocationNote(Transform canvasT)
        {
            locationNoteGo = CreatePanel(canvasT, "LocationCharacteristicNote", PanelColor);
            var rt = locationNoteGo.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.01f, 0.24f);
            rt.anchorMax = new Vector2(0.21f, 0.34f);
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;

            var label = CreateText(locationNoteGo.transform, "Label", "LOCATION NOTE", 13, TextAlignmentOptions.TopLeft);
            label.color = AccentColor;
            label.fontStyle = FontStyles.Bold;
            label.rectTransform.anchorMin = new Vector2(0f, 0.82f);
            label.rectTransform.anchorMax = new Vector2(1f, 1f);
            label.rectTransform.offsetMin = new Vector2(12, 0);
            label.rectTransform.offsetMax = new Vector2(-12, -4);

            locationNoteTitleText = CreateText(locationNoteGo.transform, "Title", "", 17, TextAlignmentOptions.TopLeft);
            locationNoteTitleText.fontStyle = FontStyles.Bold;
            locationNoteTitleText.rectTransform.anchorMin = new Vector2(0f, 0.66f);
            locationNoteTitleText.rectTransform.anchorMax = new Vector2(1f, 0.82f);
            locationNoteTitleText.rectTransform.offsetMin = new Vector2(12, 0);
            locationNoteTitleText.rectTransform.offsetMax = new Vector2(-12, 0);

            locationNoteBodyText = CreateText(locationNoteGo.transform, "Body", "", 13, TextAlignmentOptions.TopLeft);
            locationNoteBodyText.color = MutedText;
            locationNoteBodyText.textWrappingMode = TextWrappingModes.Normal;
            locationNoteBodyText.rectTransform.anchorMin = new Vector2(0f, 0f);
            locationNoteBodyText.rectTransform.anchorMax = new Vector2(1f, 0.66f);
            locationNoteBodyText.rectTransform.offsetMin = new Vector2(12, 6);
            locationNoteBodyText.rectTransform.offsetMax = new Vector2(-12, 0);
        }

        // NPC 조사 파일 패널(section 6) - 화면 오른쪽 세로 패널. NPC별로 따로 만들지 않고
        // 이 패널 하나의 내용만 클릭할 때마다 교체한다.
        void BuildNpcProfilePanel(Transform canvasT)
        {
            npcProfileGo = CreatePanel(canvasT, "NpcProfilePanel", PanelColor);
            var rt = npcProfileGo.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.79f, 0.23f);
            rt.anchorMax = new Vector2(0.99f, 0.91f);
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;

            var label = CreateText(npcProfileGo.transform, "Label", "INVESTIGATION FILE", 14, TextAlignmentOptions.TopLeft);
            label.color = AccentColor;
            label.fontStyle = FontStyles.Bold;
            label.rectTransform.anchorMin = new Vector2(0f, 0.965f);
            label.rectTransform.anchorMax = new Vector2(1f, 1f);
            label.rectTransform.offsetMin = new Vector2(14, 0);
            label.rectTransform.offsetMax = new Vector2(-14, -4);

            npcNameText = CreateText(npcProfileGo.transform, "Name", "", 25, TextAlignmentOptions.TopLeft);
            npcNameText.fontStyle = FontStyles.Bold;
            npcNameText.rectTransform.anchorMin = new Vector2(0f, 0.91f);
            npcNameText.rectTransform.anchorMax = new Vector2(1f, 0.965f);
            npcNameText.rectTransform.offsetMin = new Vector2(14, 0);
            npcNameText.rectTransform.offsetMax = new Vector2(-14, 0);

            npcBasicInfoText = CreateText(npcProfileGo.transform, "BasicInfo", "", 14, TextAlignmentOptions.TopLeft);
            npcBasicInfoText.color = MutedText;
            npcBasicInfoText.textWrappingMode = TextWrappingModes.Normal;
            npcBasicInfoText.rectTransform.anchorMin = new Vector2(0f, 0.835f);
            npcBasicInfoText.rectTransform.anchorMax = new Vector2(1f, 0.91f);
            npcBasicInfoText.rectTransform.offsetMin = new Vector2(14, 0);
            npcBasicInfoText.rectTransform.offsetMax = new Vector2(-14, 0);

            npcRoleText = CreateText(npcProfileGo.transform, "Role", "", 14, TextAlignmentOptions.TopLeft);
            npcRoleText.textWrappingMode = TextWrappingModes.Normal;
            npcRoleText.rectTransform.anchorMin = new Vector2(0f, 0.76f);
            npcRoleText.rectTransform.anchorMax = new Vector2(1f, 0.835f);
            npcRoleText.rectTransform.offsetMin = new Vector2(14, 0);
            npcRoleText.rectTransform.offsetMax = new Vector2(-14, 0);

            var beliefLabel = CreateText(npcProfileGo.transform, "BeliefLabel", "현재 믿음", 13, TextAlignmentOptions.TopLeft);
            beliefLabel.color = AccentColor;
            beliefLabel.fontStyle = FontStyles.Bold;
            beliefLabel.rectTransform.anchorMin = new Vector2(0f, 0.715f);
            beliefLabel.rectTransform.anchorMax = new Vector2(1f, 0.76f);
            beliefLabel.rectTransform.offsetMin = new Vector2(14, 0);
            beliefLabel.rectTransform.offsetMax = new Vector2(-14, 0);

            npcBeliefTierText = CreateText(npcProfileGo.transform, "BeliefTier", "", 18, TextAlignmentOptions.TopLeft);
            npcBeliefTierText.fontStyle = FontStyles.Bold;
            npcBeliefTierText.rectTransform.anchorMin = new Vector2(0f, 0.66f);
            npcBeliefTierText.rectTransform.anchorMax = new Vector2(1f, 0.715f);
            npcBeliefTierText.rectTransform.offsetMin = new Vector2(14, 0);
            npcBeliefTierText.rectTransform.offsetMax = new Vector2(-14, 0);

            npcBeliefDialogueText = CreateText(npcProfileGo.transform, "BeliefDialogue", "", 13, TextAlignmentOptions.TopLeft);
            npcBeliefDialogueText.color = MutedText;
            npcBeliefDialogueText.fontStyle = FontStyles.Italic;
            npcBeliefDialogueText.textWrappingMode = TextWrappingModes.Normal;
            npcBeliefDialogueText.rectTransform.anchorMin = new Vector2(0f, 0.59f);
            npcBeliefDialogueText.rectTransform.anchorMax = new Vector2(1f, 0.66f);
            npcBeliefDialogueText.rectTransform.offsetMin = new Vector2(14, 0);
            npcBeliefDialogueText.rectTransform.offsetMax = new Vector2(-14, 0);

            var relLabel = CreateText(npcProfileGo.transform, "RelationshipsLabel", "관계도", 13, TextAlignmentOptions.TopLeft);
            relLabel.color = AccentColor;
            relLabel.fontStyle = FontStyles.Bold;
            relLabel.rectTransform.anchorMin = new Vector2(0f, 0.545f);
            relLabel.rectTransform.anchorMax = new Vector2(1f, 0.59f);
            relLabel.rectTransform.offsetMin = new Vector2(14, 0);
            relLabel.rectTransform.offsetMax = new Vector2(-14, 0);

            var relRootGo = new GameObject("RelationshipsList", typeof(RectTransform));
            relRootGo.transform.SetParent(npcProfileGo.transform, false);
            var relRt = (RectTransform)relRootGo.transform;
            relRt.anchorMin = new Vector2(0f, 0.385f);
            relRt.anchorMax = new Vector2(1f, 0.545f);
            relRt.offsetMin = new Vector2(14, 0); relRt.offsetMax = new Vector2(-14, 0);
            var relVlg = relRootGo.AddComponent<VerticalLayoutGroup>();
            relVlg.childAlignment = TextAnchor.UpperLeft;
            relVlg.childForceExpandWidth = true;
            relVlg.childForceExpandHeight = false;
            relVlg.childControlHeight = true;
            relVlg.spacing = 6;
            npcRelationshipsRoot = relRootGo.transform;

            var historyLabel = CreateText(npcProfileGo.transform, "HistoryLabel", "History", 13, TextAlignmentOptions.TopLeft);
            historyLabel.color = AccentColor;
            historyLabel.fontStyle = FontStyles.Bold;
            historyLabel.rectTransform.anchorMin = new Vector2(0f, 0.34f);
            historyLabel.rectTransform.anchorMax = new Vector2(1f, 0.385f);
            historyLabel.rectTransform.offsetMin = new Vector2(14, 0);
            historyLabel.rectTransform.offsetMax = new Vector2(-14, 0);

            npcHistoryText = CreateText(npcProfileGo.transform, "History", "", 13, TextAlignmentOptions.TopLeft);
            npcHistoryText.color = MutedText;
            npcHistoryText.textWrappingMode = TextWrappingModes.Normal;
            npcHistoryText.rectTransform.anchorMin = new Vector2(0f, 0.09f);
            npcHistoryText.rectTransform.anchorMax = new Vector2(1f, 0.34f);
            npcHistoryText.rectTransform.offsetMin = new Vector2(14, 0);
            npcHistoryText.rectTransform.offsetMax = new Vector2(-14, 0);

            // 미해금 정보(section 6) - 실제 해금 로직 없이 시각적 잠금 슬롯만 둔다.
            var lockedGo = CreatePanel(npcProfileGo.transform, "LockedInfoSlot", new Color(0.05f, 0.06f, 0.05f, 0.7f));
            var lockedRt = lockedGo.GetComponent<RectTransform>();
            lockedRt.anchorMin = new Vector2(0f, 0f);
            lockedRt.anchorMax = new Vector2(1f, 0.085f);
            lockedRt.offsetMin = new Vector2(14, 4); lockedRt.offsetMax = new Vector2(-14, 0);
            var lockedLabel = CreateText(lockedGo.transform, "Label", "미해금 정보  ???", 14, TextAlignmentOptions.Center);
            lockedLabel.color = MutedText;
            AnchorFill(lockedLabel.rectTransform);
        }

        void BuildBottomPanel(Transform canvasT)
        {
            var panel = CreatePanel(canvasT, "BottomPanel", PanelColor);
            var prt = panel.GetComponent<RectTransform>();
            prt.anchorMin = new Vector2(0.01f, 0.01f);
            prt.anchorMax = new Vector2(0.99f, 0.23f);
            prt.offsetMin = Vector2.zero; prt.offsetMax = Vector2.zero;

            ownedCountLabel = CreateText(panel.transform, "Label", "보유 정보: 0", 17, TextAlignmentOptions.TopLeft);
            ownedCountLabel.fontStyle = FontStyles.Bold;
            ownedCountLabel.color = AccentColor;
            ownedCountLabel.rectTransform.anchorMin = new Vector2(0f, 0.86f);
            ownedCountLabel.rectTransform.anchorMax = new Vector2(1f, 1f);
            ownedCountLabel.rectTransform.offsetMin = new Vector2(16, 0);
            ownedCountLabel.rectTransform.offsetMax = new Vector2(-16, -6);

            var ownedRowGo = new GameObject("OwnedInformationRow", typeof(RectTransform));
            ownedRowGo.transform.SetParent(panel.transform, false);
            var hrrt = (RectTransform)ownedRowGo.transform;
            // HorizontalLayoutGroup의 childControlHeight가 카드 실제 높이를 이 행 자체의 높이로
            // 강제로 맞추므로(CardTileView.CollapsedHeight는 힌트일 뿐 이 행 높이를 못 넘는다),
            // 카드 폰트를 키운 만큼 행 자체를 더 크게 잡아야 한다(기존 0.58~0.86 -> 0.473~0.861,
            // 그만큼 CardInfo/Instruction 영역도 함께 줄인다).
            hrrt.anchorMin = new Vector2(0f, 0.473f);
            hrrt.anchorMax = new Vector2(1f, 0.861f);
            hrrt.offsetMin = new Vector2(16, 4); hrrt.offsetMax = new Vector2(-16, 0);
            // 손패(section 7/8): 최대 보유 4장(InformationCardSystem.MaxOwned) 기준 슬롯 정렬 -
            // 카드끼리 겹치지 않도록 양수 간격을 두고, 펼쳐진 카드는 CardTileView.SetHandState가
            // SetAsLastSibling으로 다른 카드보다 앞에 오게 한다(펼쳐질 때 커지는 세로 방향으로만
            // 다른 패널을 덮는다 - 가로로는 겹치지 않는다).
            var hlg = ownedRowGo.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 16;
            hlg.childAlignment = TextAnchor.LowerLeft;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;
            ownedRoot = ownedRowGo.transform;

            cardInfoGo = new GameObject("CardInfo", typeof(RectTransform));
            cardInfoGo.transform.SetParent(panel.transform, false);
            var cirt = (RectTransform)cardInfoGo.transform;
            cirt.anchorMin = new Vector2(0f, 0f);
            cirt.anchorMax = new Vector2(0.45f, 0.473f);
            cirt.offsetMin = new Vector2(16, 8); cirt.offsetMax = new Vector2(-8, 0);

            cardTitleText = CreateText(cardInfoGo.transform, "CardTitle", "", 24, TextAlignmentOptions.TopLeft);
            cardTitleText.fontStyle = FontStyles.Bold;
            cardTitleText.rectTransform.anchorMin = new Vector2(0f, 0.55f);
            cardTitleText.rectTransform.anchorMax = new Vector2(1f, 1f);

            cardDescText = CreateText(cardInfoGo.transform, "CardDesc", "", 15, TextAlignmentOptions.TopLeft);
            cardDescText.textWrappingMode = TextWrappingModes.Normal;
            cardDescText.rectTransform.anchorMin = new Vector2(0f, 0.20f);
            cardDescText.rectTransform.anchorMax = new Vector2(1f, 0.55f);

            cardKindText = CreateText(cardInfoGo.transform, "CardKind", "", 14, TextAlignmentOptions.TopLeft);
            cardKindText.color = AccentColor;
            cardKindText.rectTransform.anchorMin = new Vector2(0f, 0f);
            cardKindText.rectTransform.anchorMax = new Vector2(1f, 0.20f);

            instructionGo = new GameObject("Instruction", typeof(RectTransform));
            instructionGo.transform.SetParent(panel.transform, false);
            var irt = (RectTransform)instructionGo.transform;
            irt.anchorMin = new Vector2(0.47f, 0f);
            irt.anchorMax = new Vector2(1f, 0.473f);
            irt.offsetMin = new Vector2(8, 8); irt.offsetMax = new Vector2(-16, 0);

            instructionText = CreateText(instructionGo.transform, "InstructionText", "", 17, TextAlignmentOptions.TopLeft);
            instructionText.textWrappingMode = TextWrappingModes.Normal;
            instructionText.rectTransform.anchorMin = new Vector2(0f, 0.25f);
            instructionText.rectTransform.anchorMax = new Vector2(1f, 1f);

            // Popup 확인 버튼과 같은 축소된 버튼 스타일(좁은 너비/얕은 높이)을 재사용해 통일감을 준다.
            deliverButtonGo = new GameObject("DeliverButton", typeof(RectTransform));
            deliverButtonGo.transform.SetParent(instructionGo.transform, false);
            var sdrt = (RectTransform)deliverButtonGo.transform;
            sdrt.anchorMin = new Vector2(0f, 0f);
            sdrt.anchorMax = new Vector2(0.34f, 0.16f);
            sdrt.offsetMin = Vector2.zero; sdrt.offsetMax = Vector2.zero;
            var sdImg = deliverButtonGo.AddComponent<Image>();
            sdImg.color = AccentColor;
            deliverButton = deliverButtonGo.AddComponent<Button>();
            deliverButton.targetGraphic = sdImg;
            deliverButton.onClick.AddListener(OnDeliverClicked);
            var sdLabel = CreateText(deliverButtonGo.transform, "Label", "정보를 전달한다", 16, TextAlignmentOptions.Center);
            sdLabel.color = Color.black;
            AnchorFill(sdLabel.rectTransform);
            deliverButtonGo.SetActive(false);

            var hint = CreateText(panel.transform, "NoSelectionHint", "전달할 정보를 선택하세요.", 17, TextAlignmentOptions.Center);
            hint.color = MutedText;
            hint.rectTransform.anchorMin = new Vector2(0f, 0f);
            hint.rectTransform.anchorMax = new Vector2(1f, 0.473f);
            noSelectionHintGo = hint.gameObject;

            // 카드 행(ownedRowGo)을 BottomPanel의 다른 형제(CardInfo/Instruction/Hint)보다 뒤에
            // 그려지도록 맨 마지막으로 옮긴다 - 펼쳐진 카드가 그 위 정보 패널에 가려지지 않고
            // 항상 앞에 표시되어야 하기 때문이다(section 8: "펼쳐진 카드는... 앞에 표시").
            ownedRoot.SetAsLastSibling();
        }

        /// <summary>Intro/MissionComplete/MissionFailed/ZoneComplete/최종 승리 화면이 전부 공유하는
        /// 단일 팝업 구조 - 제목/본문/버튼 유무만 호출부에서 바뀐다(ShowGatedPopup/ShowFinalVictory/
        /// IntroPopupRoutine 참고). 새 UI 시스템이 아니라 기존 Overlay를 일반화한 것이다.</summary>
        void BuildOverlay(Transform canvasT)
        {
            overlayGo = CreatePanel(canvasT, "Overlay", new Color(0.02f, 0.03f, 0.02f, 0.88f), blocksInput: true);
            AnchorFill(overlayGo.GetComponent<RectTransform>());
            overlayCanvasGroup = overlayGo.AddComponent<CanvasGroup>();

            // Popup 크기를 소폭 축소하고 제목 -> 본문 -> (여백) -> 버튼 순으로 시선이 자연스럽게
            // 흐르도록 세로 배치를 다시 잡는다. 버튼은 본문보다 작고 눈에 덜 띄는 "마지막 행동 유도
            // 요소"로 축소한다(너비/높이 모두 축소, 본문과의 간격 확대).
            var box = CreatePanel(overlayGo.transform, "Box", PanelColor);
            overlayBox = box.transform;
            var brt = box.GetComponent<RectTransform>();
            brt.anchorMin = new Vector2(0.34f, 0.37f);
            brt.anchorMax = new Vector2(0.66f, 0.63f);
            brt.offsetMin = Vector2.zero; brt.offsetMax = Vector2.zero;

            overlayTitleText = CreateText(box.transform, "Title", "", 28, TextAlignmentOptions.Center);
            overlayTitleText.fontStyle = FontStyles.Bold;
            overlayTitleText.rectTransform.anchorMin = new Vector2(0f, 0.66f);
            overlayTitleText.rectTransform.anchorMax = new Vector2(1f, 0.90f);

            overlayDescText = CreateText(box.transform, "Desc", "", 14, TextAlignmentOptions.Center);
            overlayDescText.textWrappingMode = TextWrappingModes.Normal;
            overlayDescText.rectTransform.anchorMin = new Vector2(0.10f, 0.36f);
            overlayDescText.rectTransform.anchorMax = new Vector2(0.90f, 0.62f);

            overlayButtonGo = new GameObject("ConfirmButton", typeof(RectTransform));
            overlayButtonGo.transform.SetParent(box.transform, false);
            var obrt = (RectTransform)overlayButtonGo.transform;
            obrt.anchorMin = new Vector2(0.38f, 0.08f);
            obrt.anchorMax = new Vector2(0.62f, 0.19f);
            obrt.offsetMin = Vector2.zero; obrt.offsetMax = Vector2.zero;
            var obImg = overlayButtonGo.AddComponent<Image>();
            obImg.color = AccentColor;
            overlayButton = overlayButtonGo.AddComponent<Button>();
            overlayButton.targetGraphic = obImg;
            overlayButtonLabel = CreateText(overlayButtonGo.transform, "Label", "", 13, TextAlignmentOptions.Center);
            overlayButtonLabel.color = Color.black;
            overlayButtonLabel.fontStyle = FontStyles.Bold;
            AnchorFill(overlayButtonLabel.rectTransform);
            overlayButtonGo.SetActive(false);

            overlayGo.SetActive(false);
        }

        // blocksInput: 이 배경이 아래(월드/다른 UI)로의 클릭을 실제로 막아야 하는지 여부.
        // 대부분의 패널 배경은 장식용이라 false - 클릭은 그 안의 Button 같은 실제 입력 요소가 처리한다.
        // Overlay(게임오버 화면)처럼 입력을 의도적으로 차단해야 하는 경우만 true로 호출한다.
        GameObject CreatePanel(Transform parent, string name, Color color, bool blocksInput = false)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            if (color.a > 0f)
            {
                var img = go.AddComponent<Image>();
                img.color = color;
                img.raycastTarget = blocksInput;
            }
            return go;
        }

        TMP_Text CreateText(Transform parent, string name, string content, int size, TextAlignmentOptions align)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<TextMeshProUGUI>();
            if (koreanFont != null) text.font = koreanFont;
            text.text = content;
            text.fontSize = size;
            text.alignment = align;
            text.color = Color.white;
            text.raycastTarget = false; // 순수 표시용 텍스트 - 클릭 대상이 아니다.
            AnchorFill(text.rectTransform);
            return text;
        }

        void AnchorFill(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
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
