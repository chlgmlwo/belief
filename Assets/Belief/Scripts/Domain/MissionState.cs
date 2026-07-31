using Belief.Data;

namespace Belief.Domain
{
    /// <summary>미션 진행 상태. 쓰기는 MissionSystem 전용.</summary>
    public class MissionState
    {
        public MissionData Data { get; }
        public int CurrentProgress { get; private set; }
        public bool IsComplete => CurrentProgress >= Data.SuccessTarget;

        public MissionState(MissionData data)
        {
            Data = data;
        }

        public void UpdateProgress(int progress) => CurrentProgress = progress;
    }
}
