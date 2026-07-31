namespace Belief.Systems.BeliefEvaluators
{
    /// <summary>WorkingMemory(MemorySelector가 이미 선별한 최대 2개)만 사용한다. LongMemory 전체는 절대 참조하지 않는다.</summary>
    public class MemoryEvaluator : IBeliefEvaluator
    {
        readonly Belief.Data.MemoryTuningData tuning;

        public MemoryEvaluator(Belief.Data.MemoryTuningData tuning)
        {
            this.tuning = tuning;
        }

        public BeliefContribution Evaluate(BeliefContext context)
        {
            if (context.WorkingMemory.IsEmpty)
                return new BeliefContribution(BeliefContributionType.Memory, 0f, isExceptional: true);

            float total = 0f;
            foreach (var entry in context.WorkingMemory.Entries)
            {
                float magnitude = entry.Importance * tuning.maxSingleMemoryModifier;
                float signed = magnitude * entry.Valence;
                total += entry.IsCore ? signed : signed * 0.5f;
            }

            return new BeliefContribution(BeliefContributionType.Memory, total, isExceptional: true);
        }
    }
}
