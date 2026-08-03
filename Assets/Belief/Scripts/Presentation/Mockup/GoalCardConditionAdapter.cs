using TMPro;
using UnityEngine;
using Belief.Core;
using Belief.Data;

namespace Belief.Presentation.Mockup
{
    /// <summary>GoalCard01은 HudView.missionTitleText/missionDescText로 직접 바인딩되어(HudPresenter가
    /// 수정 없이 실제 "현재 미션"을 채운다) 항상 지금 진행 중인 미션을 보여준다. GoalCard02는 그
    /// 다음 미션을 미리 보여주는 자리다 - HudPresenter/HudView에는 다음 미션의 설명까지 노출하는
    /// 필드가 없으므로(제목 문자열 하나뿐, 그나마 "???"로 가려질 수 있음), 이 어댑터가
    /// GameInstaller.StageAsset.missions 목록에서 현재 미션 바로 다음 항목을 직접 찾아 그
    /// MissionData의 displayTitle/objectiveText를 읽기 전용으로 그대로 보여준다(판정 로직에는
    /// 전혀 관여하지 않음).
    ///
    /// 현재 미션이 완료되면(ConfirmMissionComplete -> ObjectivesChanged) HudPresenter가 GoalCard01과
    /// 조건 요약 패널을 자동으로 다음 미션 기준으로 다시 그린다 - 이 어댑터는 매 프레임
    /// ProgressionController.CurrentObjective()를 폴링해 그 전환을 그대로 따라가므로, 별도 이벤트
    /// 구독 없이도 "미션1 클리어 -> 클리어조건 자동 전환" 설계가 자연스럽게 성립한다. 다음 미션이
    /// 없으면(스테이지 마지막 미션) GoalCard02/연결이미지를 숨긴다.</summary>
    public class GoalCardConditionAdapter : MonoBehaviour
    {
        [SerializeField] GameObject goalCard2Root;
        [SerializeField] TMP_Text goalCard2Title;
        [SerializeField] TMP_Text goalCard2Description;
        [SerializeField] GameObject connectorGo;

        GameInstaller installer;
        string lastMissionId = "\0"; // 최초 프레임에도 반드시 한 번 갱신되도록 실제 missionId와 절대 겹치지 않는 값으로 시작

        void Start()
        {
            installer = FindFirstObjectByType<GameInstaller>();
        }

        void LateUpdate()
        {
            var pc = ProgressionController.Instance;
            if (installer == null || pc == null) return;

            var current = pc.CurrentObjective();
            string currentId = current != null ? current.missionId : null;
            if (currentId == lastMissionId) return; // 같은 미션이 계속 진행 중이면 다시 찾을 필요 없음
            lastMissionId = currentId;

            var next = FindNextMission(current);
            bool hasNext = next != null;

            if (hasNext)
            {
                if (goalCard2Title != null) goalCard2Title.text = next.displayTitle;
                if (goalCard2Description != null) goalCard2Description.text = next.objectiveText;
            }

            if (goalCard2Root != null) goalCard2Root.SetActive(hasNext);
            if (connectorGo != null) connectorGo.SetActive(hasNext);
        }

        MissionData FindNextMission(MissionData current)
        {
            if (current == null) return null;
            var stageAsset = installer.StageAsset;
            var missions = stageAsset != null ? stageAsset.missions : null;
            if (missions == null) return null;

            int index = System.Array.IndexOf(missions, current);
            if (index < 0 || index + 1 >= missions.Length) return null;
            return missions[index + 1];
        }
    }
}
