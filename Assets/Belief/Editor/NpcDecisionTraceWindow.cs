using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Belief.Debugging;

namespace Belief.EditorTools
{
    /// <summary>NPC 판단(정보 판단 / 턴 이동) 전 과정을 관찰하기 위한 Editor 전용 창. 순수 관찰
    /// 도구다 - 여기서 어떤 값도 계산하지 않고 NpcDecisionTraceStore가 모아 둔 기록을 그대로
    /// 보여주기만 한다. 인게임 Canvas/HUD와는 완전히 분리되어 있고, 이 창이 닫혀 있어도(또는
    /// Record가 꺼져 있어도) 게임 판단 결과는 동일하다.</summary>
    public class NpcDecisionTraceWindow : EditorWindow
    {
        [MenuItem("Window/BELIEF/NPC Decision Log")]
        static void ShowWindow()
        {
            var window = GetWindow<NpcDecisionTraceWindow>();
            window.titleContent = new GUIContent("BELIEF — NPC Decision Log");
            window.Show();
        }

        NpcDecisionTraceStore store;

        /// <summary>외부(테스트/다른 Editor 도구)가 현재 창의 저장소를 읽기 전용으로 참조할 수 있게 한다.</summary>
        public NpcDecisionTraceStore Store => store;

        bool paused;
        bool autoScroll = true;
        List<NpcDecisionTraceRecord> pausedSnapshot;

        Vector2 listScroll;
        Vector2 detailScroll;
        string selectedDecisionId;

        // 필터/검색
        TraceFilter filter;
        string npcFilterLabel = "(전체)";
        string missionFilterLabel = "(전체)";
        string cardFilterLabel = "(전체)";
        int stageTurnFilter; // 0 = 전체
        int decisionTypeIndex; // 0 전체 / 1 InformationJudgment / 2 TurnMove
        int resultSourceIndex; // TraceResultSourceFilter와 동일 순서

        // Foldout 상태 - 12개 고정 섹션
        bool showMetadata = true, showReceivedInfo = true, showStateBefore, showEvaluators = true,
             showBeliefTransition = true, showIntentAction = true, showDialogue, showGoal,
             showMoveCandidates = true, showLlmFallback = true, showFinalResolution = true, showErrors = true;
        bool showRawPromptResponse; // LLM/Fallback 안의 원문 - 기본 접힘

        void OnEnable()
        {
            store = new NpcDecisionTraceStore();
            store.Subscribe();
            store.Changed += OnStoreChanged;
        }

        void OnDisable()
        {
            if (store != null)
            {
                store.Changed -= OnStoreChanged;
                store.Unsubscribe();
            }
        }

        void OnStoreChanged()
        {
            if (autoScroll && !paused)
            {
                var all = store.Records;
                if (all.Count > 0) selectedDecisionId = all[all.Count - 1].DecisionId;
                listScroll.y = float.MaxValue;
            }
            Repaint();
        }

        void OnGUI()
        {
            DrawToolbar();
            DrawFilterBar();

            var visible = paused && pausedSnapshot != null ? pausedSnapshot : store.Query(BuildFilter());

            EditorGUILayout.BeginHorizontal();
            DrawList(visible);
            DrawDetail(visible);
            EditorGUILayout.EndHorizontal();
        }

        TraceFilter BuildFilter()
        {
            filter.StageTurn = stageTurnFilter > 0 ? stageTurnFilter : (int?)null;
            filter.DecisionType = decisionTypeIndex == 1 ? "InformationJudgment" : decisionTypeIndex == 2 ? "TurnMove" : null;
            filter.ResultSourceFilter = (TraceResultSourceFilter)resultSourceIndex;
            return filter;
        }

        // ---------------------------------------------------------------- Toolbar

        void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            bool recordOn = NpcDecisionTraceHub.Enabled;
            bool newRecordOn = GUILayout.Toggle(recordOn, "Record", EditorStyles.toolbarButton, GUILayout.Width(60));
            if (newRecordOn != recordOn) NpcDecisionTraceHub.Enabled = newRecordOn;

            bool newPaused = GUILayout.Toggle(paused, "Pause View", EditorStyles.toolbarButton, GUILayout.Width(80));
            if (newPaused != paused)
            {
                paused = newPaused;
                pausedSnapshot = paused ? store.Query(BuildFilter()) : null;
            }

            if (GUILayout.Button("Clear", EditorStyles.toolbarButton, GUILayout.Width(50)))
            {
                store.Clear();
                selectedDecisionId = null;
                pausedSnapshot = paused ? new List<NpcDecisionTraceRecord>() : null;
            }

            autoScroll = GUILayout.Toggle(autoScroll, "Auto Scroll", EditorStyles.toolbarButton, GUILayout.Width(80));

            if (GUILayout.Button("Expand All", EditorStyles.toolbarButton, GUILayout.Width(80))) SetAllFoldouts(true);
            if (GUILayout.Button("Collapse All", EditorStyles.toolbarButton, GUILayout.Width(90))) SetAllFoldouts(false);

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Copy Selected as JSON", EditorStyles.toolbarButton, GUILayout.Width(150)))
                CopySelectedAsJson();

            if (GUILayout.Button("Export JSONL", EditorStyles.toolbarButton, GUILayout.Width(100)))
                ExportJsonl();

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.HelpBox(
                $"기록 {store.Records.Count} / 최대 {NpcDecisionTraceStore.MaxRecords} (초과 시 오래된 기록부터 자동 제거) — " +
                "관찰 전용 창입니다. 이 창의 열림/닫힘, Record On/Off는 NPC 판단 결과에 전혀 영향을 주지 않습니다.",
                MessageType.None);
        }

        void SetAllFoldouts(bool value)
        {
            showMetadata = showReceivedInfo = showStateBefore = showEvaluators = showBeliefTransition =
                showIntentAction = showDialogue = showGoal = showMoveCandidates = showLlmFallback =
                showFinalResolution = showErrors = value;
        }

        void CopySelectedAsJson()
        {
            var record = FindSelected(store.Records);
            if (record == null) { ShowNotification(new GUIContent("선택된 기록이 없습니다.")); return; }
            EditorGUIUtility.systemCopyBuffer = JsonUtility.ToJson(record, true);
            ShowNotification(new GUIContent("클립보드에 복사했습니다."));
        }

        void ExportJsonl()
        {
            var toExport = paused && pausedSnapshot != null ? pausedSnapshot : store.Query(BuildFilter());
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var path = NpcDecisionTraceStore.ExportJsonl(toExport, timestamp);
            ShowNotification(new GUIContent("저장됨:\n" + path));
            Debug.Log($"[NPC Decision Trace] Exported {toExport.Count} record(s) to {path}");
        }

        // ---------------------------------------------------------------- Filter bar

        void DrawFilterBar()
        {
            var all = store.Records;

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            DrawPopup("NPC", ref npcFilterLabel, ref filter.NpcId, () =>
                DistinctPairs(all, r => r.NpcId, r => $"{r.NpcId} — {r.NpcDisplayName}"));

            GUILayout.Label("Turn", GUILayout.Width(30));
            stageTurnFilter = EditorGUILayout.IntField(stageTurnFilter, GUILayout.Width(30));

            DrawPopup("Mission", ref missionFilterLabel, ref filter.MissionId, () =>
                DistinctPairs(all, r => r.MissionId, r => r.MissionId));

            decisionTypeIndex = EditorGUILayout.Popup(decisionTypeIndex,
                new[] { "Type: 전체", "정보 판단", "턴 이동" }, EditorStyles.toolbarPopup, GUILayout.Width(90));

            resultSourceIndex = EditorGUILayout.Popup(resultSourceIndex,
                new[] { "결과: 전체", "RuleOnly", "LLM", "Fallback" }, EditorStyles.toolbarPopup, GUILayout.Width(100));

            DrawPopup("Card", ref cardFilterLabel, ref filter.CardId, () =>
                DistinctPairs(all.Where(r => r.HasReceivedInformation), r => r.CardId, r => $"{r.CardId} — {r.CardTitle}"));

            filter.ErrorOnly = GUILayout.Toggle(filter.ErrorOnly, "Error Only", EditorStyles.toolbarButton, GUILayout.Width(80));

            GUILayout.Label("Search", GUILayout.Width(45));
            filter.SearchText = EditorGUILayout.TextField(filter.SearchText ?? "", EditorStyles.toolbarSearchField, GUILayout.MinWidth(120));

            EditorGUILayout.EndHorizontal();
        }

        static List<(string id, string label)> DistinctPairs(
            IEnumerable<NpcDecisionTraceRecord> source, Func<NpcDecisionTraceRecord, string> idSelector, Func<NpcDecisionTraceRecord, string> labelSelector)
        {
            return source.Where(r => !string.IsNullOrEmpty(idSelector(r)))
                .GroupBy(idSelector)
                .Select(g => (g.Key, labelSelector(g.First())))
                .OrderBy(p => p.Item2)
                .ToList();
        }

        void DrawPopup(string prefix, ref string currentLabel, ref string boundValue, Func<List<(string id, string label)>> optionsProvider)
        {
            GUILayout.Label(prefix, GUILayout.Width(prefix.Length * 7 + 4));
            var options = optionsProvider();
            var labels = new List<string> { "(전체)" };
            labels.AddRange(options.Select(o => o.label));

            string boundValueSnapshot = boundValue;
            int currentIndex = string.IsNullOrEmpty(boundValueSnapshot) ? 0
                : Math.Max(0, options.FindIndex(o => o.id == boundValueSnapshot) + 1);

            int newIndex = EditorGUILayout.Popup(currentIndex, labels.ToArray(), EditorStyles.toolbarPopup, GUILayout.Width(140));
            if (newIndex != currentIndex)
            {
                if (newIndex == 0) { boundValue = null; currentLabel = "(전체)"; }
                else { boundValue = options[newIndex - 1].id; currentLabel = options[newIndex - 1].label; }
            }
        }

        // ---------------------------------------------------------------- List (left)

        void DrawList(List<NpcDecisionTraceRecord> visible)
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(320));
            listScroll = EditorGUILayout.BeginScrollView(listScroll);

            for (int i = visible.Count - 1; i >= 0; i--) // 최신이 위로
            {
                var r = visible[i];
                bool isSelected = r.DecisionId == selectedDecisionId;

                var style = isSelected ? EditorStyles.helpBox : GUI.skin.box;
                EditorGUILayout.BeginVertical(style);

                string typeTag = r.DecisionType == "TurnMove" ? "이동" : "정보판단";
                string sourceTag = !r.UsedLlm ? "" : r.FallbackOccurred ? " [Fallback]" : " [LLM]";
                string errorTag = r.HasError ? " ⚠" : "";

                if (GUILayout.Button(
                    $"T{r.StageTurn} · {r.NpcDisplayName} ({r.NpcId})\n{typeTag} · {r.ThinkerMode}{sourceTag}{errorTag}",
                    EditorStyles.label))
                {
                    selectedDecisionId = r.DecisionId;
                }

                EditorGUILayout.EndVertical();
            }

            if (visible.Count == 0)
                EditorGUILayout.HelpBox("표시할 기록이 없습니다. Play Mode에서 정보 카드를 전달하거나 턴을 진행해 보세요.", MessageType.Info);

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        NpcDecisionTraceRecord FindSelected(IEnumerable<NpcDecisionTraceRecord> source)
            => string.IsNullOrEmpty(selectedDecisionId) ? null : source.FirstOrDefault(r => r.DecisionId == selectedDecisionId);

        // ---------------------------------------------------------------- Detail (right)

        void DrawDetail(List<NpcDecisionTraceRecord> visible)
        {
            EditorGUILayout.BeginVertical();
            var record = FindSelected(visible) ?? FindSelected(store.Records);

            if (record == null)
            {
                EditorGUILayout.HelpBox("왼쪽 목록에서 기록을 선택하세요.", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            detailScroll = EditorGUILayout.BeginScrollView(detailScroll);

            DrawMetadata(record);
            DrawReceivedInformation(record);
            DrawStateBefore(record);
            DrawEvaluators(record);
            DrawBeliefTransition(record);
            DrawIntentAndAction(record);
            DrawDialogue(record);
            DrawGoal(record);
            DrawMoveCandidates(record);
            DrawLlmFallback(record);
            DrawFinalResolution(record);
            DrawErrors(record);

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        static string IdName(string id, string name) => string.IsNullOrEmpty(id) ? "(없음)" : $"{id} — {name}";
        static string F3(float v) => v.ToString("F3");
        static string Bool(bool b) => b ? "예" : "아니오";

        void DrawMetadata(NpcDecisionTraceRecord r)
        {
            showMetadata = EditorGUILayout.Foldout(showMetadata, "Metadata", true);
            if (!showMetadata) return;
            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField("DecisionId", r.DecisionId);
            EditorGUILayout.LabelField("Stage", $"{r.StageId} (StageTurn {r.StageTurn})");
            EditorGUILayout.LabelField("Mission", $"{r.MissionId} (MissionTurn {r.MissionTurn})");
            EditorGUILayout.LabelField("NPC", IdName(r.NpcId, r.NpcDisplayName));
            EditorGUILayout.LabelField("Location", IdName(r.CurrentLocationId, r.CurrentLocationDisplayName));
            EditorGUILayout.LabelField("ThinkerMode", r.ThinkerMode);
            EditorGUILayout.LabelField("DecisionType", r.DecisionType == "TurnMove" ? "Turn Move (정보 수신 없이 매 턴 이동만 판단)" : "Information Judgment (정보 수신 판단)");
            EditorGUILayout.LabelField("Timestamp (UTC)", r.StartTimestampUtc);
            EditorGUILayout.LabelField("Processing Time", $"{r.ProcessingTimeMs:F3} ms");
            EditorGUI.indentLevel--;
        }

        void DrawReceivedInformation(NpcDecisionTraceRecord r)
        {
            showReceivedInfo = EditorGUILayout.Foldout(showReceivedInfo, "Received Information", true);
            if (!showReceivedInfo) return;
            EditorGUI.indentLevel++;
            if (!r.HasReceivedInformation)
            {
                EditorGUILayout.LabelField("Received Information", "없음 (Decision Type: Turn Move)");
            }
            else
            {
                EditorGUILayout.LabelField("Card", IdName(r.CardId, r.CardTitle));
                EditorGUILayout.LabelField("Category", r.CardCategoryId);
                EditorGUILayout.LabelField("Description", r.CardDescription, EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField("Source", IdName(r.CardSourceId, r.CardSourceDisplayName));
                EditorGUILayout.LabelField("Delivery / Target Type", $"{r.CardDeliveryType} / {r.CardTargetType}");
                EditorGUILayout.LabelField("Tags", r.CardTags != null && r.CardTags.Length > 0 ? string.Join(", ", r.CardTags) : "(없음)");

                EditorGUILayout.Space(2);
                EditorGUILayout.LabelField("Location Mechanics - 확산(§3, §4)", EditorStyles.boldLabel);
                if (!r.ReachedViaRespread)
                {
                    EditorGUILayout.LabelField("재확산 경유 여부", "아니오 (플레이어 직접 전달 - spreadSpeed/npcDensity 미적용)");
                }
                else
                {
                    EditorGUILayout.LabelField("Base / Location Spread Speed", $"{F3(r.LocBaseSpreadPower)} / {r.LocSourceSpreadSpeed}");
                    EditorGUILayout.LabelField("Spread Multiplier / Effective", $"{F3(r.LocSpreadMultiplier)} / {F3(r.LocEffectiveSpreadPower)}");
                    EditorGUILayout.LabelField("Location NPC Density", r.LocTargetNpcDensity);
                    EditorGUILayout.LabelField("Candidate / Limit / Selected / Excluded",
                        $"{r.LocCandidateNpcCount} / {r.LocDensityTargetLimit} / {r.LocSelectedSecondaryRecipientCount} / {r.LocExcludedRecipientCount}");
                }
            }
            EditorGUI.indentLevel--;
        }

        void DrawStateBefore(NpcDecisionTraceRecord r)
        {
            showStateBefore = EditorGUILayout.Foldout(showStateBefore, "State Before", true);
            if (!showStateBefore) return;
            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField("Goal (판단 전)", r.GoalBefore);
            EditorGUILayout.LabelField("Belief (판단 전)", $"{r.BeliefStageBeforeLabel} ({r.BeliefStageBeforeRaw})");
            EditorGUILayout.LabelField("Conviction / Doubt Ratio", $"{F3(r.ConvictionRatio)} / {F3(r.DoubtRatio)}");
            if (!string.IsNullOrEmpty(r.IntentBefore))
                EditorGUILayout.LabelField("Intent (판단 전 belief 기준)", r.IntentBefore);

            if (r.BeliefsBefore.Count > 0)
            {
                EditorGUILayout.LabelField("보유 Belief 전체", EditorStyles.boldLabel);
                foreach (var b in r.BeliefsBefore)
                    EditorGUILayout.LabelField("  " + b.CardId, b.BeliefStateRaw);
            }
            if (r.Relationships.Count > 0)
            {
                EditorGUILayout.LabelField("관계", EditorStyles.boldLabel);
                foreach (var rel in r.Relationships)
                    EditorGUILayout.LabelField("  " + IdName(rel.TargetNpcId, rel.TargetDisplayName), rel.RelationshipTypeLabel);
            }
            EditorGUI.indentLevel--;
        }

        void DrawEvaluators(NpcDecisionTraceRecord r)
        {
            showEvaluators = EditorGUILayout.Foldout(showEvaluators, "Belief Evaluators", true);
            if (!showEvaluators) return;
            EditorGUI.indentLevel++;
            if (r.Evaluators.Count == 0)
            {
                EditorGUILayout.LabelField("(이번 판단에서는 Belief 평가가 실행되지 않았습니다 - Turn Move)");
            }
            else
            {
                foreach (var e in r.Evaluators)
                    EditorGUILayout.LabelField($"{e.EvaluatorName} / {e.InputSummary}", $"Score {F3(e.ScoreDelta)}{(e.IsExceptional ? "  [예외]" : "")}");
                EditorGUILayout.Space(2);
                EditorGUILayout.LabelField("Base Judgment Score", F3(r.BaseJudgmentScore));
                EditorGUILayout.LabelField("Exceptional Modifier (raw / capped)", $"{F3(r.RawExceptionalModifier)} / {F3(r.CappedExceptionalModifier)}");
                EditorGUILayout.LabelField("Final Score", F3(r.FinalScore));
                EditorGUILayout.LabelField("Reversed By Exception", Bool(r.WasReversedByException));

                EditorGUILayout.Space(2);
                EditorGUILayout.LabelField("Location Mechanics - 신뢰도(§5, §6)", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Card / Location Info Type", $"{r.LocCardInformationType} / {r.LocSensitiveInfoType}");
                EditorGUILayout.LabelField("Type Matched / Bonus", $"{Bool(r.LocSensitiveTypeMatched)} / {F3(r.LocSensitiveTypeBonus)}");
                EditorGUILayout.LabelField("Base Credibility / Location Delta", $"{F3(r.LocBaseCredibility)} / {F3(r.LocCredibilityDelta)}");
                EditorGUILayout.LabelField("Effective Credibility", F3(r.LocEffectiveCredibility));
                EditorGUILayout.LabelField("CredibilityEvaluator Final Contribution", F3(r.LocCredibilityEvaluatorContribution));
            }
            EditorGUI.indentLevel--;
        }

        void DrawBeliefTransition(NpcDecisionTraceRecord r)
        {
            showBeliefTransition = EditorGUILayout.Foldout(showBeliefTransition, "Belief Transition", true);
            if (!showBeliefTransition) return;
            EditorGUI.indentLevel++;
            if (string.IsNullOrEmpty(r.BeliefAfterLabel))
            {
                EditorGUILayout.LabelField("(이번 판단에서는 Belief 변화가 없습니다 - Turn Move)");
            }
            else
            {
                EditorGUILayout.LabelField("Before → After", $"{r.BeliefBeforeLabel} → {r.BeliefAfterLabel} ({r.BeliefAfterRaw})");
                EditorGUILayout.LabelField("Changed", Bool(r.BeliefChanged));
            }
            EditorGUI.indentLevel--;
        }

        void DrawIntentAndAction(NpcDecisionTraceRecord r)
        {
            showIntentAction = EditorGUILayout.Foldout(showIntentAction, "Intent and Action", true);
            if (!showIntentAction) return;
            EditorGUI.indentLevel++;
            if (r.ActionCandidates.Count == 0 && string.IsNullOrEmpty(r.SelectedActionId))
            {
                EditorGUILayout.LabelField("(이번 판단에서는 행동 선택이 없습니다 - Turn Move)");
            }
            else
            {
                EditorGUILayout.LabelField("Belief → Intent 매핑", $"{r.ActionInputBeliefStage} → {r.ActionIntentMapping}");
                foreach (var c in r.ActionCandidates)
                {
                    string marker = c.IsSelected ? "  ◀ 선택됨" : c.IntentMatches ? "  (Intent 일치)" : "";
                    EditorGUILayout.LabelField($"  {IdName(c.ActionId, c.DisplayLabel)} [{c.Intent}]", marker);
                }
                EditorGUILayout.LabelField("Selected Action", IdName(r.SelectedActionId, r.SelectedActionLabel));
            }
            EditorGUI.indentLevel--;
        }

        void DrawDialogue(NpcDecisionTraceRecord r)
        {
            showDialogue = EditorGUILayout.Foldout(showDialogue, "Dialogue", true);
            if (!showDialogue) return;
            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField("Context Tag", r.DialogueContextTag ?? "(없음)");
            foreach (var d in r.DialogueCandidates)
                EditorGUILayout.LabelField($"  [{d.ContextTag}] {d.Text}", d.IsSelected ? "◀ 선택됨" : "");
            EditorGUILayout.LabelField("Selected Text", string.IsNullOrEmpty(r.SelectedDialogueText) ? "(없음)" : r.SelectedDialogueText, EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("Generated (LLM)", Bool(r.DialogueIsGenerated));
            EditorGUI.indentLevel--;
        }

        void DrawGoal(NpcDecisionTraceRecord r)
        {
            showGoal = EditorGUILayout.Foldout(showGoal, "Goal", true);
            if (!showGoal) return;
            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField("Goal Text", string.IsNullOrEmpty(r.GoalTextAfter) ? "(없음)" : r.GoalTextAfter, EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("Goal Content Parsed", Bool(r.GoalContentParsed) + "  (텍스트 내용은 실제 판단 코드가 파싱하지 않습니다)");
            EditorGUILayout.LabelField("Goal Presence Used", Bool(r.GoalPresenceUsed || (r.DecisionType == "InformationJudgment" && !string.IsNullOrEmpty(r.GoalTextAfter))) + "  (Goal '존재 여부'만 이동 점수에 반영됨)");
            EditorGUILayout.LabelField("Changed", Bool(r.GoalChanged));
            EditorGUI.indentLevel--;
        }

        void DrawMoveCandidates(NpcDecisionTraceRecord r)
        {
            showMoveCandidates = EditorGUILayout.Foldout(showMoveCandidates, "Move Candidates", true);
            if (!showMoveCandidates) return;
            EditorGUI.indentLevel++;

            if (r.DecisionType != "TurnMove")
            {
                EditorGUILayout.LabelField("(이번 판단은 이동 판단이 아닙니다)");
                EditorGUI.indentLevel--;
                return;
            }

            EditorGUILayout.LabelField("Movement Candidates", r.MovementCandidateIds.Count > 0 ? string.Join(", ", r.MovementCandidateIds) : "(없음)");
            EditorGUILayout.LabelField("Preferred Locations", r.PreferredLocationIds.Count > 0 ? string.Join(", ", r.PreferredLocationIds) : "(없음)");
            EditorGUILayout.LabelField("Avoided Locations", r.AvoidedLocationIds.Count > 0 ? string.Join(", ", r.AvoidedLocationIds) : "(없음)");
            EditorGUILayout.LabelField("확신 / 미확인 / 의심 비율",
                $"{F3(r.MoveConvictionRatio)} / {F3(r.MoveUnverifiedRatio)} / {F3(r.MoveDoubtRatio)}");

            if (r.MoveCandidateScores.Count == 0)
            {
                EditorGUILayout.LabelField("Candidate Scores", r.UsedResultSource == "LLM"
                    ? "(RuleBased 미실행 - LLM 결과를 그대로 사용)"
                    : "(없음)");
            }
            else
            {
                EditorGUILayout.LabelField("Goal Match / Preference / Belief(확신+미확인+의심) / Doubt Override / Final", EditorStyles.boldLabel);
                foreach (var c in r.MoveCandidateScores)
                {
                    string sel = c.IsSelected ? " ◀ 선택됨" : "";
                    EditorGUILayout.LabelField($"  {IdName(c.LocationId, c.LocationDisplayName)}{sel}",
                        $"{F3(c.GoalTerm)} / {F3(c.PreferenceTerm)} / {F3(c.BeliefTerm)} / {F3(c.DoubtOverrideTerm)} / {F3(c.FinalScore)}");
                }
            }

            foreach (var ex in r.ExcludedMoveCandidates)
                EditorGUILayout.LabelField("  제외: " + ex.LocationId, ex.Reason);

            EditorGUILayout.LabelField("Best Score / Stay Score", $"{F3(r.MoveBestScore)} / {F3(r.MoveStayScore)}");
            EditorGUILayout.LabelField("Tie 여부", Bool(r.MoveHadTie));
            EditorGUILayout.LabelField("Stay 여부", $"{Bool(r.MoveIsStay)}  (최고 점수가 Stay Score 이하이면 Stay)");
            EditorGUILayout.LabelField("Selected Destination", string.IsNullOrEmpty(r.SelectedDestinationId) ? "(없음 - Stay)" : r.SelectedDestinationId);
            EditorGUILayout.LabelField("movement.rules", r.MovementRulesStatus);
            EditorGUI.indentLevel--;
        }

        void DrawLlmFallback(NpcDecisionTraceRecord r)
        {
            showLlmFallback = EditorGUILayout.Foldout(showLlmFallback, "LLM / Fallback", true);
            if (!showLlmFallback) return;
            EditorGUI.indentLevel++;

            if (!r.UsedLlm)
            {
                EditorGUILayout.LabelField("(ThinkerMode가 RuleOnly라 LLM을 시도하지 않았습니다)");
            }
            else
            {
                EditorGUILayout.LabelField("Timeout 설정", $"{r.TimeoutMs} ms");
                EditorGUILayout.LabelField("Request Start (UTC)", r.LlmRequestStartUtc);
                EditorGUILayout.LabelField("Response Time", $"{r.LlmResponseTimeMs:F3} ms");
                EditorGUILayout.LabelField("Response Succeeded", Bool(r.LlmResponseSucceeded));
                EditorGUILayout.LabelField("Parse Succeeded", Bool(r.LlmParseSucceeded));
                EditorGUILayout.LabelField("Destination/Action Valid", Bool(r.LlmDestinationValid));
                EditorGUILayout.LabelField("Fallback Occurred", Bool(r.FallbackOccurred));
                if (r.FallbackOccurred)
                    EditorGUILayout.LabelField("Fallback Reason", r.FallbackReason);
                EditorGUILayout.LabelField("Used Result", r.UsedResultSource);
                EditorGUILayout.LabelField("Resolved By", r.ResolvedByLlm ? "LLM" : r.ResolvedByFallback ? "RuleBased (Fallback)" : "(미확정)");
                EditorGUILayout.LabelField("늦은 응답 폐기됨", Bool(r.LateResponseDiscarded));

                showRawPromptResponse = EditorGUILayout.Foldout(showRawPromptResponse, "Raw Prompt / Response (원문)", true);
                if (showRawPromptResponse)
                {
                    EditorGUILayout.LabelField("Prompt", string.IsNullOrEmpty(r.PromptText) ? "(없음)" : r.PromptText, EditorStyles.wordWrappedLabel);
                    EditorGUILayout.LabelField("Raw Response", string.IsNullOrEmpty(r.RawResponseText) ? "(없음)" : r.RawResponseText, EditorStyles.wordWrappedLabel);
                }
            }
            EditorGUI.indentLevel--;
        }

        void DrawFinalResolution(NpcDecisionTraceRecord r)
        {
            showFinalResolution = EditorGUILayout.Foldout(showFinalResolution, "Final Resolution", true);
            if (!showFinalResolution) return;
            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField("Final Intent", r.FinalIntent ?? "(없음)");
            EditorGUILayout.LabelField("Final Action", string.IsNullOrEmpty(r.FinalActionId) ? "(없음)" : r.FinalActionId);
            EditorGUILayout.LabelField("Final Dialogue", string.IsNullOrEmpty(r.FinalDialogueText) ? "(없음)" : r.FinalDialogueText, EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("Final Goal", r.FinalGoal);
            EditorGUILayout.LabelField("Final Move Destination", string.IsNullOrEmpty(r.FinalMoveDestinationId) ? "(없음)" : r.FinalMoveDestinationId);
            EditorGUILayout.LabelField("ActionResolution 호출 횟수", r.ActionResolutionCount.ToString());
            EditorGUILayout.LabelField("Position Before → After", $"{r.PositionBeforeMove} → {r.PositionAfterMove}");
            if (!string.IsNullOrEmpty(r.MissionImpactNote))
                EditorGUILayout.LabelField("Mission Impact", r.MissionImpactNote);
            EditorGUI.indentLevel--;
        }

        void DrawErrors(NpcDecisionTraceRecord r)
        {
            showErrors = EditorGUILayout.Foldout(showErrors, "Errors", true);
            if (!showErrors) return;
            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField("Has Error", Bool(r.HasError));
            if (r.HasError)
                EditorGUILayout.LabelField("Message", r.ErrorMessage, EditorStyles.wordWrappedLabel);
            EditorGUI.indentLevel--;
        }
    }
}
