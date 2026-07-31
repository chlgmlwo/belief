using System.Collections.Generic;
using UnityEngine;
using Belief.Domain;

namespace Belief.Data
{
    public readonly struct MissionEvaluationContext
    {
        public readonly IReadOnlyDictionary<LocationData, LocationState> Locations;
        public readonly IReadOnlyDictionary<NpcData, NpcState> Npcs;
        public readonly IReadOnlyList<Belief.Systems.DeliveredCardRecord> DeliveredCards;

        public MissionEvaluationContext(
            IReadOnlyDictionary<LocationData, LocationState> locations,
            IReadOnlyDictionary<NpcData, NpcState> npcs,
            IReadOnlyList<Belief.Systems.DeliveredCardRecord> deliveredCards)
        {
            Locations = locations;
            Npcs = npcs;
            DeliveredCards = deliveredCards;
        }
    }

    /// <summary>
    /// 미션 승리 조건을 코드가 아니라 데이터로 표현한다. 새 조건 종류가 필요하면
    /// 이 클래스를 상속하는 SO를 하나 추가하면 되고, MissionSystem은 손대지 않는다.
    /// </summary>
    public abstract class MissionConditionData : ScriptableObject
    {
        public int TargetCount = 1;

        /// <summary>Mission UI가 체크박스 형태의 클리어 조건 목록에 그대로 표시하는 문구.
        /// 비워두면 UI가 자산 이름 등으로 대체 표시한다.</summary>
        public string displayLabel;

        public abstract int GetCurrentProgress(MissionEvaluationContext context);
    }
}
