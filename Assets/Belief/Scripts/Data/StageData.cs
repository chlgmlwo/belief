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

        /// <summary>이 스테이지의 장소 카드/NPC를 화면에서 얼마나 크게 그릴지(1 = 기본).
        /// 스테이지마다 카메라 orthographicSize가 달라(Zone1 5 / Zone2·3 6 / Metropolis 14)
        /// 같은 월드 크기라도 화면상 크기가 최대 2.8배까지 차이 난다. 배치 좌표는 그대로 두고
        /// <b>보이는 크기만</b> 키우는 값이라, 너무 키우면 이웃한 장소끼리 겹친다
        /// (Metropolis 현재 배치 기준 한계 ≈1.32). NPC가 카드 옆에 붙는 간격도 같은 비율로 늘어난다.</summary>
        [Tooltip("장소 카드/NPC를 화면에서 키우는 배율(1 = 기본). 배치 좌표는 그대로 두고 크기만 바뀌므로 너무 키우면 이웃과 겹친다.")]
        public float worldViewScale = 1f;

        /// <summary>NPC 시작 위치 수동 배치 - 지정된 NPC는 소속 장소를 따라가는 자동 슬롯 계산
        /// (WorldPresenter.ComputeNpcSlot) 대신 이 좌표에서 시작한다. 이후 게임 중 NPC가 다른
        /// 장소로 이동하면(NpcRelocatedEvent) 그 시점부터는 기존처럼 자동 슬롯 계산을 그대로
        /// 따른다 - 이 좌표는 "시작 배치"에만 적용된다.</summary>
        [Header("World Layout - NPC 시작 위치 수동 배치 (지정 시 자동 슬롯 배치보다 우선)")]
        public NpcLayoutEntry[] npcLayout;

        /// <summary>정보 전달(접선) 지점 - 지도 위에 일반 장소와 같은 사진 카드로 놓이되, 카드 위에
        /// "전달" 태그가 붙어 그 자체가 전달 확정 버튼이 된다(예전 하단 패널의 "정보 전달하기" 버튼을
        /// 대체). 게임 로직상의 장소 목록(locations)에는 넣지 않는다 - 확산 대상이나 NPC 배치처로
        /// 잡히면 안 되고 순수 표시/입력 지점이기 때문이다. 비워 두면 예전 하단 버튼이 그대로 쓰인다.</summary>
        [Header("Contact Point (정보 전달 지점)")]
        public LocationData contactPoint;
        public Vector2 contactPointPosition;

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
