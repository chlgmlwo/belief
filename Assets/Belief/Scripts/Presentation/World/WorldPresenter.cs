using System;
using System.Collections.Generic;
using UnityEngine;
using Belief.Core;
using Belief.Data;
using Belief.Events;

namespace Belief.Presentation.World
{
    /// <summary>
    /// 게임 로직을 직접 수정하지 않는다 - GameInstaller의 상태를 읽어 초기 배치만 하고,
    /// 이후는 GameEventBus 구독으로만 갱신한다(폴링 없음). 상태 변경은 이미 끝난 뒤
    /// 이벤트를 받아 연출(이동 보간, 강조, 대사 표시)만 재생한다.
    /// World 오브젝트 클릭은 게임 로직을 직접 호출하지 않고 이벤트로만 바깥(TargetingController)에 알린다.
    /// </summary>
    public class WorldPresenter : MonoBehaviour
    {
        [SerializeField] GameInstaller installer;
        [SerializeField] LocationSiteView locationSitePrefab;
        [SerializeField] NpcActorView npcActorPrefab;
        [SerializeField] Transform locationRoot;
        [SerializeField] Transform npcRoot;
        [SerializeField] public PlayHudSkin skin;

        readonly Dictionary<LocationData, LocationSiteView> locationViews = new Dictionary<LocationData, LocationSiteView>();
        readonly Dictionary<NpcData, NpcActorView> npcViews = new Dictionary<NpcData, NpcActorView>();

        public IReadOnlyDictionary<LocationData, LocationSiteView> LocationViews => locationViews;
        public IReadOnlyDictionary<NpcData, NpcActorView> NpcViews => npcViews;

        /// <summary>지도 위 접선 태그가 붙은 전달 지점 카드 - 없으면 null(그 경우 예전 하단
        /// 전달 버튼이 그대로 쓰인다).</summary>
        LocationSiteView contactPointView;
        public LocationSiteView ContactPointView => contactPointView;

        public event Action<LocationData> LocationClicked;
        public event Action<LocationData> LocationHoverEnter;
        public event Action<LocationData> LocationHoverExit;
        public event Action<NpcData> NpcClicked;
        /// <summary>접선 지점 카드를 눌렀을 때 - 장소 선택이 아니라 "정보 전달 확정"이다.</summary>
        public event Action ContactPointClicked;

        // 2026-08-04: NPC를 카드 "아래"에 격자로 쌓던 방식(3-52) 대신, 장소 이미지 좌/우에 바로
        // 붙이는 방식으로 변경(사용자 지시) - 1번째는 오른쪽, 2번째는 왼쪽, 3번째부터는 각 방향에서
        // 한 칸씩 더 바깥으로 밀려난다(0:R0, 1:L0, 2:R1, 3:L1, ...).
        // PhotoHalfWidth(0.45)는 LocationSiteView의 Photo SpriteRenderer 실측값(3-49/3-52와 동일
        // 측정) - Photo 크기가 바뀌면 이 값도 같이 갱신해야 한다.
        public const float PhotoHalfWidth = 0.45f;
        const float NpcHalfWidth = 0.54f;
        // 음수 = 여백이 아니라 겹침 - 사용자 지시로 "완전히 붙게, 살짝 겹쳐도 됨"으로 변경
        // (사진↔NPC, NPC↔NPC 둘 다 이 값만큼 겹친다).
        const float NpcFlankGap = -0.15f;
        // 첫 슬롯(바로 옆)까지의 거리 - 사진 반폭 + 여백(음수면 겹침) + NPC 반폭.
        const float NpcFlankBaseOffset = PhotoHalfWidth + NpcFlankGap + NpcHalfWidth;
        // 한 칸 더 바깥으로 밀려날 때마다 추가되는 거리 - NPC 폭 + 여백.
        const float NpcFlankStep = NpcHalfWidth * 2f + NpcFlankGap;
        // 사진 세로 중심이 아니라 살짝 아래(발밑 쪽)에 맞춰야 자연스러워 보인다 - 사진 반높이(0.73)와
        // NPC 반높이(0.54) 차이만큼만 내린다(둘의 "바닥"이 대략 맞도록).
        const float NpcFlankVerticalOffset = -0.19f;
        static readonly Color ConnectionLineColor = new Color(0.6f, 0.55f, 0.4f, 0.5f);

        /// <summary>StageData.locationLayout(스테이지별 수동 배치)을 조회용 사전으로 미리 펼쳐 둔다 -
        /// 지정 안 된 장소는 LocationData.worldPosition을 그대로 쓴다(하위 호환).</summary>
        Vector2 ResolveLocationPosition(LocationData location)
        {
            var layout = installer.StageAsset != null ? installer.StageAsset.locationLayout : null;
            if (layout != null)
                foreach (var entry in layout)
                    if (entry.location == location) return entry.position;
            return location.worldPosition;
        }

        const int CityBackgroundSortingOrder = -100;

        void Start()
        {
            CreateCityBackground();

            foreach (var kvp in installer.Locations)
            {
                var view = Instantiate(locationSitePrefab, locationRoot);
                view.Bind(kvp.Key, ResolveLocationPosition(kvp.Key), skin);
                view.Clicked += d => LocationClicked?.Invoke(d);
                view.HoverEnter += d => LocationHoverEnter?.Invoke(d);
                view.HoverExit += d => LocationHoverExit?.Invoke(d);
                locationViews[kvp.Key] = view;
            }

            CreateContactPoint();
            DrawLocationConnections();

            foreach (var kvp in installer.Npcs)
            {
                var view = Instantiate(npcActorPrefab, npcRoot);
                view.Bind(kvp.Key, skin);
                view.Clicked += d => NpcClicked?.Invoke(d);
                npcViews[kvp.Key] = view;
            }

            // 초기 배치도 슬롯 계산을 거쳐야 한 장소에 여러 NPC가 겹쳐 보이지 않는다.
            foreach (var loc in installer.Locations.Keys)
                SnapNpcSlots(loc);

            // StageData.npcLayout에 수동 좌표가 지정된 NPC는 자동 슬롯 계산 대신 그 좌표에서
            // 시작한다 - "시작 배치"에만 적용되고, 이후 실제 이동(NpcRelocatedEvent)이 일어나면
            // 그때부터는 기존처럼 자동 슬롯 계산을 그대로 따른다.
            ApplyManualNpcStartLayout();

            installer.EventBus.Subscribe<NpcRelocatedEvent>(OnNpcRelocated);
            installer.EventBus.Subscribe<LocationStateChangedEvent>(OnLocationStateChanged);
            installer.EventBus.Subscribe<NpcSpokeEvent>(OnNpcSpoke);
            installer.EventBus.Subscribe<InfoSpreadEvent>(OnInfoSpread);
            installer.EventBus.Subscribe<InfoDeliveredEvent>(OnInfoDelivered);
        }

        /// <summary>정보 전달 지점(StageData.contactPoint)을 일반 장소와 같은 사진 카드로 놓되,
        /// **locationViews에는 넣지 않는다** - 넣으면 확산 대상 후보/연결선/NPC 슬롯 계산에 끼어들어
        /// 게임 규칙이 바뀐다. 이 카드는 순수 표시 + 전달 확정 입력만 담당한다. 클릭은 장소 선택이
        /// 아니라 ContactPointClicked로 나간다.
        ///
        /// 장소 정보 패널(호버)도 연결하지 않는다 - 여기는 게임 세계의 "장소"가 아니라 전달이라는
        /// 시스템 동작이 놓인 자리라서, 확산 속도/NPC 밀도 같은 장소 정보를 띄우면 오히려 혼란스럽다
        /// (사용자 지시).</summary>
        void CreateContactPoint()
        {
            var stage = installer.StageAsset;
            if (stage == null || stage.contactPoint == null) return;

            var view = Instantiate(locationSitePrefab, locationRoot);
            view.Bind(stage.contactPoint, stage.contactPointPosition, skin);
            view.BindContactTag(skin != null ? skin.blockedStamp : null);
            view.Clicked += _ => ContactPointClicked?.Invoke();
            contactPointView = view;
        }

        /// <summary>StageData.cityBackground가 있으면 Main Camera의 현재 뷰(orthographicSize·aspect)를
        /// 완전히 덮도록 스케일을 계산해 맨 뒤(가장 낮은 sortingOrder)에 깔아준다 - 스테이지/카메라
        /// 설정이 달라져도 매번 다시 계산하므로 하드코딩된 크기값이 없다.</summary>
        void CreateCityBackground()
        {
            var sprite = installer.StageAsset != null ? installer.StageAsset.cityBackground : null;
            if (sprite == null) return;

            var cam = Camera.main;
            if (cam == null) return;

            var go = new GameObject("CityBackground");
            go.transform.SetParent(transform, false);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = CityBackgroundSortingOrder;

            float cameraHeight = cam.orthographicSize * 2f;
            float cameraWidth = cameraHeight * cam.aspect;
            float spriteWidth = sprite.rect.width / sprite.pixelsPerUnit;
            float spriteHeight = sprite.rect.height / sprite.pixelsPerUnit;
            float scale = Mathf.Max(cameraWidth / spriteWidth, cameraHeight / spriteHeight);
            go.transform.localScale = new Vector3(scale, scale, 1f);
            go.transform.position = new Vector3(cam.transform.position.x, cam.transform.position.y, 0f);
        }

        void ApplyManualNpcStartLayout()
        {
            var layout = installer.StageAsset != null ? installer.StageAsset.npcLayout : null;
            if (layout == null) return;

            foreach (var entry in layout)
            {
                if (entry.npc == null || !npcViews.TryGetValue(entry.npc, out var view)) continue;

                // 수동 좌표는 에디터 도구로 "이 NPC가 원래 있던 장소" 근처에 맞춰 잡은 값이다 - 미션이
                // 시작하자마자(GameInstaller.Awake 단계, WorldPresenter가 NpcRelocatedEvent를 구독하기도
                // 전) NPC를 다른 장소로 자동 이동시키는 경우, 실제로는 이미 다른 곳에 있는데 이 좌표를
                // 그대로 쓰면 엉뚱한 곳(원래 있던 장소)에 박제돼 버린다. 수동 좌표가 가리키는 장소와
                // NPC의 실제 현재 위치가 다르면 수동 좌표를 무시하고, 바로 위 SnapNpcSlots가 이미
                // 계산해 둔 올바른 격자 위치를 그대로 둔다.
                if (installer.Npcs.TryGetValue(entry.npc, out var npcState) && npcState.CurrentLocation != null)
                {
                    var nearestToManualPos = FindNearestLocation(entry.position);
                    if (nearestToManualPos != null && nearestToManualPos != npcState.CurrentLocation)
                        continue;
                }

                view.SetWorldPosition(entry.position);
            }
        }

        LocationData FindNearestLocation(Vector2 position)
        {
            LocationData nearest = null;
            float bestDist = float.MaxValue;
            foreach (var kvp in locationViews)
            {
                float dist = Vector2.Distance(position, kvp.Value.transform.position);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    nearest = kvp.Key;
                }
            }
            return nearest;
        }

        void OnNpcRelocated(NpcRelocatedEvent e)
        {
            // 이동한 NPC뿐 아니라 출발지/도착지에 남은 다른 NPC들도 슬롯이 바뀌므로 함께 갱신한다.
            if (e.From != null) RefreshNpcSlots(e.From);
            RefreshNpcSlots(e.To);
        }

        /// <summary>장소 연결선(section 2) - LocationData.connectedLocations를 그대로 사용해 이번
        /// 스테이지에 실제로 표시되는(installer.Locations에 포함된) 장소 사이만 선으로 잇는다.
        /// 새로운 연결 데이터를 만들지 않고 기존 필드를 읽기만 하는 순수 시각 표현이다.</summary>
        void DrawLocationConnections()
        {
            var drawn = new HashSet<(LocationData, LocationData)>();
            foreach (var kvp in locationViews)
            {
                var from = kvp.Key;
                if (from.connectedLocations == null) continue;

                foreach (var to in from.connectedLocations)
                {
                    if (to == null || !locationViews.ContainsKey(to)) continue;

                    var pair = from.GetInstanceID() < to.GetInstanceID() ? (from, to) : (to, from);
                    if (!drawn.Add(pair)) continue;

                    var lineGo = new GameObject($"Connection_{from.locationId}_{to.locationId}");
                    lineGo.transform.SetParent(locationRoot, false);
                    var line = lineGo.AddComponent<LineRenderer>();
                    line.material = new Material(Shader.Find("Sprites/Default"));
                    line.sortingOrder = -1;
                    line.positionCount = 2;
                    line.useWorldSpace = true;
                    // 카드 중심이 아니라 압정 위치에 붙여야 지도 위에 그린 선처럼 보인다(카드를
                    // 뚫고 지나가지 않음).
                    line.SetPosition(0, locationViews[from].PinTransform.position);
                    line.SetPosition(1, locationViews[to].PinTransform.position);

                    // 실제 연결선 아트(장소간 연결 UI)가 있으면 타일링 텍스처로, 없으면 기존 단색으로.
                    // 문서 지도에 손으로 그은 선처럼 보이도록 얇고 흐린 톤을 쓴다(과도하게 굵거나
                    // 밝은 흰색 선은 금지 - 가이드 대비).
                    if (skin != null && skin.locationConnector != null)
                    {
                        line.material.mainTexture = skin.locationConnector.texture;
                        line.textureMode = LineTextureMode.Tile;
                        line.startColor = ConnectionLineColor;
                        line.endColor = ConnectionLineColor;
                        line.startWidth = 0.035f;
                        line.endWidth = 0.035f;
                    }
                    else
                    {
                        line.startColor = ConnectionLineColor;
                        line.endColor = ConnectionLineColor;
                        line.startWidth = 0.03f;
                        line.endWidth = 0.03f;
                    }
                }
            }
        }

        /// <summary>한 장소 안에서 NPC가 겹치지 않도록 고정 격자 슬롯을 계산한다 - 임의 좌표를 쓰지 않고
        /// 인원 수·순번만으로 결정되는 순수 함수다. NpcMaxPerRow를 넘는 인원은 아래 행으로 넘어간다.</summary>
        /// <summary>0번째는 오른쪽 바로 옆, 1번째는 왼쪽 바로 옆, 2번째부터는 같은 방향에서 한 칸씩
        /// 더 바깥으로 밀려난다(짝수 index=오른쪽, 홀수=왼쪽, index/2=바깥으로 몇 칸째인지).</summary>
        Vector2 ComputeNpcSlot(LocationData location, int index, int count)
        {
            float side = index % 2 == 0 ? 1f : -1f;
            int slot = index / 2;
            float x = side * (NpcFlankBaseOffset + slot * NpcFlankStep);

            Vector2 basePos = locationViews.TryGetValue(location, out var view)
                ? (Vector2)view.transform.position : location.worldPosition;
            return basePos + new Vector2(x, NpcFlankVerticalOffset);
        }

        void RefreshNpcSlots(LocationData location)
        {
            if (!installer.Locations.TryGetValue(location, out var locState)) return;

            var present = locState.PresentNpcs;
            for (int i = 0; i < present.Count; i++)
            {
                if (npcViews.TryGetValue(present[i].Data, out var view))
                    view.AnimateTo(ComputeNpcSlot(location, i, present.Count));
            }
        }

        void SnapNpcSlots(LocationData location)
        {
            if (!installer.Locations.TryGetValue(location, out var locState)) return;

            var present = locState.PresentNpcs;
            for (int i = 0; i < present.Count; i++)
            {
                if (npcViews.TryGetValue(present[i].Data, out var view))
                    view.SetWorldPosition(ComputeNpcSlot(location, i, present.Count));
            }
        }

        void OnLocationStateChanged(LocationStateChangedEvent e)
        {
            if (locationViews.TryGetValue(e.Location, out var view))
                view.SetSiteState(e.NewState);
        }

        /// <summary>지금 말풍선이 떠 있는 NPC 하나 - 새 대사가 오면 이 NPC의 말풍선부터 즉시 정리한다
        /// (section 3: "동시에 표시되는 주요 NPC 대사: 최대 1개").</summary>
        NpcActorView currentSpeaker;

        void OnNpcSpoke(NpcSpokeEvent e)
        {
            if (!npcViews.TryGetValue(e.Npc, out var view)) return;
            string text = e.Dialogue.IsGenerated ? e.Dialogue.GeneratedText : e.Dialogue.PredefinedLine?.text;
            if (string.IsNullOrEmpty(text)) return;

            if (currentSpeaker != null && currentSpeaker != view)
                currentSpeaker.HideDialogueImmediately();

            currentSpeaker = view;
            view.ShowDialogue(text);
        }

        void OnInfoSpread(InfoSpreadEvent e)
        {
            if (locationViews.TryGetValue(e.Location, out var view))
                view.Highlight();
        }

        void OnInfoDelivered(InfoDeliveredEvent e)
        {
            if (npcViews.TryGetValue(e.Target, out var view))
                view.Highlight();
        }

        /// <summary>TargetingController가 전달 대상으로 선택/해제한 장소를 알려줄 때 호출한다 -
        /// 게임 상태는 건드리지 않는 순수 표시 갱신.</summary>
        public void SetLocationSelected(LocationData location, bool selected)
        {
            if (locationViews.TryGetValue(location, out var view)) view.SetSelected(selected);
        }

        /// <summary>TargetingController가 전달 대상으로 선택/해제한 NPC를 알려줄 때 호출한다.</summary>
        public void SetNpcSelected(NpcData npc, bool selected)
        {
            if (npcViews.TryGetValue(npc, out var view)) view.SetSelected(selected);
        }
    }
}
