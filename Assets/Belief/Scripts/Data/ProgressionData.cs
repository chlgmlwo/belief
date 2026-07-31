using System;
using UnityEngine;

namespace Belief.Data
{
    /// <summary>
    /// 구역(씬) 진행 순서와 각 단계의 목표를 담는 단일 설정 에셋. 씬 이름을 코드에
    /// 하드코딩하지 않기 위해 ProgressionController는 이 데이터만 읽는다. 마지막 원소가 대도시 단계다.
    /// </summary>
    [CreateAssetMenu(fileName = "ProgressionData", menuName = "Belief/Progression Data", order = 20)]
    public class ProgressionData : ScriptableObject
    {
        public StageInfo[] stages;
    }

    [Serializable]
    public class StageInfo
    {
        public string stageId;
        public string displayName;
        public string sceneName;
        public MissionData[] objectives;

        /// <summary>구역 시작 인트로 팝업에 제목(displayName) 아래 함께 보여줄 부제 - 예: "왕도의 북동 방위 지구".</summary>
        [TextArea(1, 2)] public string introSubtitle;
    }
}
