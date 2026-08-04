using System;
using UnityEngine;

namespace Belief.Data
{
    /// <summary>
    /// 한 스테이지(구역)를 구성하는 정적 데이터를 연결하는 허브 에셋. 장소 목록/NPC 배치/카드 풀/
    /// 미션 목록/최대 턴/시작 미션을 한 곳에서 참조만 한다 - 직접 로직을 갖지 않는다.
    /// GameInstaller/ProgressionController가 이 에셋을 실제로 읽어 런타임 상태를 만드는 배선은
    /// 별도 단계(1스테이지 데이터 입력 및 통합)에서 처리한다.
    /// </summary>
    [CreateAssetMenu(fileName = "Stage_", menuName = "Belief/Stage Data", order = 0)]
    public class StageData : ScriptableObject
    {
        [Header("Identity")]
        public string stageId;
        public int stageNumber;
        public string stageName;
        public string regionName;
        [TextArea(1, 3)] public string regionDescription;
        [TextArea(1, 3)] public string objective;

        [Header("World")]
        public LocationData[] locations;
        public NpcPlacementEntry[] npcPlacements;

        [Tooltip("City 씬 배경 - WorldPresenter가 카메라 뷰를 덮도록 자동 스케일한다. 비어 있으면 기존 카메라 단색 배경 그대로.")]
        public Sprite cityBackground;

        /// <summary>이 스테이지(씬) 전용 수동 화면 배치 - 지정된 장소는 LocationData.worldPosition
        /// 대신 이 좌표를 사용한다(HUD 좌/우/상/하 패널을 피한 중앙 가시 영역에 들어가도록 스테이지마다
        /// 직접 잡은 값). 같은 LocationData 에셋이 여러 스테이지에 공유되어(worldPosition 하나로는
        /// 스테이지별 배치가 불가능한 경우, 예: Metropolis) 반드시 필요하다. 지정하지 않은 장소는
        /// 기존처럼 LocationData.worldPosition을 그대로 쓴다(하위 호환).</summary>
        [Header("World Layout (수동 배치 - 지정 시 LocationData.worldPosition보다 우선)")]
        public LocationLayoutEntry[] locationLayout;

        /// <summary>NPC 시작 위치 수동 배치 - 지정된 NPC는 소속 장소를 따라가는 자동 슬롯 계산
        /// (WorldPresenter.ComputeNpcSlot) 대신 이 좌표에서 시작한다. 이후 게임 중 NPC가 다른
        /// 장소로 이동하면(NpcRelocatedEvent) 그 시점부터는 기존처럼 자동 슬롯 계산을 그대로
        /// 따른다 - 이 좌표는 "시작 배치"에만 적용된다.</summary>
        [Header("World Layout - NPC 시작 위치 수동 배치 (지정 시 자동 슬롯 배치보다 우선)")]
        public NpcLayoutEntry[] npcLayout;

        [Header("Information")]
        public InformationCardPoolData cardPool;

        [Header("Missions")]
        public MissionData[] missions;
        public MissionData startMission;
        public int maxTurns = 6;
    }

    /// <summary>
    /// 스테이지마다 달라질 수 있는 NPC 배치값. NpcData 자체(고정 데이터/AI Profile)는 건드리지 않고,
    /// 이 스테이지 한정으로 적용할 시작 위치/초기 믿음만 별도로 얹는다.
    /// </summary>
    [Serializable]
    public struct NpcPlacementEntry
    {
        public NpcData npc;

        /// <summary>비워두면(null) NpcData.homeLocation을 그대로 사용한다.</summary>
        public LocationData startLocation;

        public InitialBeliefEntry[] initialBeliefs;

        public LocationData EffectiveStartLocation => startLocation != null ? startLocation : npc != null ? npc.homeLocation : null;
    }

    [Serializable]
    public struct InitialBeliefEntry
    {
        public InformationCardData card;
        public BeliefState belief;
    }

    [Serializable]
    public struct LocationLayoutEntry
    {
        public LocationData location;
        public Vector2 position;
    }

    [Serializable]
    public struct NpcLayoutEntry
    {
        public NpcData npc;
        public Vector2 position;
    }
}
