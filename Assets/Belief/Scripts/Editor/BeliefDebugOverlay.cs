using System.Linq;
using UnityEditor;
using UnityEngine;
using Belief.AI.LLM;
using Belief.Core;
using Belief.Data;
using Belief.Domain;
using Belief.Systems;

namespace Belief.EditorTools
{
    /// <summary>
    /// 개발/Editor 전용 읽기 전용 뷰어. NPC를 선택하면 Belief/Memory/Action 상태를 전부 보여준다.
    /// 어떤 게임 상태도 수정하지 않는다 - 표시만 한다.
    /// Contribution 표시는 BeliefContributionType 기준이며 문자열 Reason에 의존하지 않는다.
    /// (참고: "개발 빌드"용 인게임 오버레이는 별도 런타임 UI가 필요해 이번 Phase 범위에서는
    /// Editor Window만 구현했다 - Play 모드에서 에디터를 통해서만 확인 가능하다.)
    /// </summary>
    public class BeliefDebugOverlay : EditorWindow
    {
        Vector2 npcListScroll;
        Vector2 detailScroll;
        Vector2 promptScroll;
        NpcData selected;
        bool promptFoldout;

        [MenuItem("Belief/Debug Overlay")]
        public static void Open()
        {
            var win = GetWindow<BeliefDebugOverlay>("Belief Debug");
            win.minSize = new Vector2(520, 360);
        }

        void OnEnable() => EditorApplication.update += Repaint;
        void OnDisable() => EditorApplication.update -= Repaint;

        void OnGUI()
        {
            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Play 모드에서만 확인할 수 있습니다.", MessageType.Info);
                return;
            }

            var installer = FindFirstObjectByType<GameInstaller>();
            if (installer == null || installer.Npcs == null || installer.Npcs.Count == 0)
            {
                EditorGUILayout.HelpBox("GameInstaller를 찾을 수 없습니다 (아직 초기화되지 않음).", MessageType.Warning);
                return;
            }

            DrawGlobalStatus(installer);

            EditorGUILayout.BeginHorizontal();
            DrawNpcList(installer);
            DrawDetail(installer);
            EditorGUILayout.EndHorizontal();
        }

        void DrawGlobalStatus(GameInstaller installer)
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            EditorGUILayout.LabelField($"Turn {installer.Turns.CurrentTurn}/{installer.Turns.MaxTurns}", GUILayout.Width(90));
            EditorGUILayout.LabelField($"Thinker: {installer.CurrentThinkerMode}", GUILayout.Width(150));
            EditorGUILayout.LabelField($"Transport: {installer.CurrentTransportDescription}");
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(4);
        }

        void DrawNpcList(GameInstaller installer)
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(160));
            EditorGUILayout.LabelField("NPC 목록", EditorStyles.boldLabel);
            npcListScroll = EditorGUILayout.BeginScrollView(npcListScroll);

            // 예전에는 Rank(Major/Minor) 순으로 먼저 묶었지만 등급 구분이 사라져 이름순으로만 정렬한다.
            foreach (var npcData in installer.Npcs.Keys.OrderBy(n => n.displayName))
            {
                bool isSelected = npcData == selected;
                var style = isSelected ? EditorStyles.miniButtonMid : EditorStyles.miniButton;
                string label = npcData.displayName;
                if (GUILayout.Button(label, style)) selected = npcData;
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        void DrawDetail(GameInstaller installer)
        {
            EditorGUILayout.BeginVertical();
            detailScroll = EditorGUILayout.BeginScrollView(detailScroll);

            if (selected == null)
            {
                EditorGUILayout.HelpBox("왼쪽에서 NPC를 선택하세요.", MessageType.Info);
            }
            else
            {
                var state = installer.Npcs[selected];
                DrawNpcHeader(selected, state);
                DrawBeliefs(installer, selected, state);
                DrawActionState(selected, state);
                DrawLlmSection(installer, selected, state);
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        void DrawNpcHeader(NpcData data, NpcState state)
        {
            EditorGUILayout.LabelField($"{data.displayName}  [{data.job}]", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"현재 위치: {(state.CurrentLocation != null ? state.CurrentLocation.displayName : "-")}");
            EditorGUILayout.LabelField($"LongMemory 개수: {state.LongMemory.Count}");
            EditorGUILayout.Space(6);
        }

        void DrawBeliefs(GameInstaller installer, NpcData data, NpcState state)
        {
            EditorGUILayout.LabelField("정보 카드별 BeliefState", EditorStyles.boldLabel);

            if (state.Beliefs.Count == 0)
            {
                EditorGUILayout.LabelField("  (아직 접한 정보 없음)");
                return;
            }

            foreach (var kvp in state.Beliefs)
            {
                var card = kvp.Key;
                var belief = kvp.Value;
                string title = card.information != null ? card.information.title : "?";
                string sourceName = card.source != null ? card.source.displayName : "?";
                EditorGUILayout.LabelField($"  [{title}] 출처:{sourceName} -> {belief}", EditorStyles.miniBoldLabel);

                if (installer.BeliefDebug.TryGetLastEvaluation(state, card, out var result))
                    DrawEvaluationBreakdown(result);
                else
                    EditorGUILayout.LabelField("    (평가 기록 없음)");

                EditorGUILayout.Space(4);
            }
        }

        void DrawEvaluationBreakdown(BeliefEvaluationResult result)
        {
            EditorGUI.indentLevel++;

            string informationId = result.Card?.information != null ? result.Card.information.informationId : "-";
            string informationTitle = result.Card?.information != null ? result.Card.information.title : "-";
            string sourceId = result.DeclaredSource != null ? result.DeclaredSource.sourceId : "-";
            string sourceName = result.DeclaredSource != null ? result.DeclaredSource.displayName : "-";
            string targetNpcId = result.TargetNpc != null ? result.TargetNpc.npcId : "-";

            EditorGUILayout.LabelField($"Information: {informationTitle}  (Id: {informationId})");
            EditorGUILayout.LabelField($"Declared Source: {sourceName}  (Id: {sourceId})  -> Source 기여로 반영됨");
            EditorGUILayout.LabelField($"Target NPC Id: {targetNpcId}");
            EditorGUILayout.Space(2);

            foreach (var c in result.Breakdown)
                EditorGUILayout.LabelField($"{c.Type,-16} {(c.ScoreDelta >= 0 ? "+" : "")}{c.ScoreDelta:F2}{(c.IsExceptional ? "  (예외축)" : "")}");

            bool relationshipApplied = result.Breakdown.Any(c => c.Type == BeliefContributionType.Relationship && Mathf.Abs(c.ScoreDelta) > 0.0001f);

            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField($"기본판단축 합계: {result.BaseJudgmentScore:F2}");
            EditorGUILayout.LabelField($"예외보정 원본: {result.RawExceptionalModifier:F2}  ->  상한적용후: {result.CappedExceptionalModifier:F2}");
            EditorGUILayout.LabelField($"최종 점수: {result.FinalScore:F2}  ->  {result.FinalBelief}");
            EditorGUILayout.LabelField($"관계 보정 적용됨: {relationshipApplied}");
            EditorGUILayout.LabelField($"판단 역전 여부: {result.WasReversedByException}");

            EditorGUILayout.LabelField("WorkingMemory (최대 2개):");
            if (result.UsedWorkingMemory == null || result.UsedWorkingMemory.IsEmpty)
            {
                EditorGUILayout.LabelField("  (없음)");
            }
            else
            {
                foreach (var entry in result.UsedWorkingMemory.Entries)
                    EditorGUILayout.LabelField($"  - {entry.Description} (중요도 {entry.Importance:F2}, valence {(entry.IsCore ? "core" : "일반")})");
            }

            EditorGUI.indentLevel--;
        }

        void DrawActionState(NpcData data, NpcState state)
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("행동 상태", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"현재 행동: {(state.CurrentAction != null ? state.CurrentAction.displayLabel : "-")}");
            EditorGUILayout.LabelField($"BehaviorModifier: {state.CurrentBehaviorModifier ?? "-"}");

            if (data is MajorNpcData major)
            {
                EditorGUILayout.LabelField("사용 가능 행동 목록:");
                if (major.availableActions == null || major.availableActions.Length == 0)
                {
                    EditorGUILayout.LabelField("  (없음)");
                }
                else
                {
                    foreach (var action in major.availableActions)
                        EditorGUILayout.LabelField($"  - {action.displayLabel} [{action.intent}]");
                }
            }
        }

        void DrawLlmSection(GameInstaller installer, NpcData data, NpcState state)
        {
            if (!(data is MajorNpcData)) return;

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("LLM 판단 기록 (Major NPC 전용)", EditorStyles.boldLabel);

            if (installer.PromptRepo == null || !installer.PromptRepo.TryGetLast(state, out var entry))
            {
                EditorGUILayout.LabelField("  (아직 LLM 판단 기록 없음 - RuleOnly 모드이거나 아직 판단 전)");
                return;
            }

            EditorGUILayout.LabelField($"Fallback 사용 여부: {entry.UsedFallback}");
            EditorGUILayout.LabelField($"Validation 성공: {entry.Validation.IsValid}");
            if (!entry.Validation.IsValid)
                EditorGUILayout.LabelField($"실패 사유: {entry.Validation.FailureReason}");
            else
                EditorGUILayout.LabelField($"선택된 Action: {(entry.Validation.ChosenAction != null ? entry.Validation.ChosenAction.displayLabel : "-")}");

            EditorGUILayout.LabelField("Raw Response:");
            EditorGUILayout.LabelField($"  {entry.RawResponse ?? "(없음)"}");

            promptFoldout = EditorGUILayout.Foldout(promptFoldout, $"최근 Prompt (문자 수: {entry.Prompt?.Length ?? 0})", true);
            if (promptFoldout)
            {
                promptScroll = EditorGUILayout.BeginScrollView(promptScroll, GUILayout.Height(180));
                EditorGUILayout.TextArea(entry.Prompt ?? "", GUILayout.ExpandHeight(true));
                EditorGUILayout.EndScrollView();
            }
        }
    }
}
