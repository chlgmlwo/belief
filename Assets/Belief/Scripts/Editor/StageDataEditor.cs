using UnityEditor;
using UnityEngine;
using Belief.Data;

namespace Belief.EditorTools
{
    /// <summary>StageData 인스펙터에 "Validate" 버튼을 추가해 StageDataValidator를 즉시 실행하고
    /// 결과를 콘솔에 출력한다. 새 판단 로직이 아니라 기존 검증 함수를 호출하는 표시용 도구다.</summary>
    [CustomEditor(typeof(StageData))]
    public class StageDataEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            if (GUILayout.Button("Validate"))
            {
                var stage = (StageData)target;
                StageDataValidator.LogIssues(stage, StageDataValidator.Validate(stage));
            }
        }
    }
}
