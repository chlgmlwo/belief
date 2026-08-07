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

        /// <summary>접선 지점을 화면 어디에 놓을지(뷰포트 비율 0~1, 좌하단 기준). 월드 좌표가 아니라
        /// 화면 기준이라 스테이지별 카메라 줌과 무관하게 항상 같은 자리에 보인다. 기본값은 Zone1에서
        /// 실측해 확정한 자리(왼쪽 12.87% / 아래 25%) - 하단 안내 띠(x 17.7%부터 시작)와도 겹치지 않는다.</summary>
        /// <summary>지도 오른쪽 아래. 손패 카드는 선택되면 120px 솟아 화면 아래 0~228px를 덮으므로
        /// (카드 4는 가로로도 1395~1869px를 차지한다) 버튼 아랫변이 그보다 위에 오도록 잡았다 -
        /// 실측 기준 버튼은 화면 x 1675~1905 / 아래에서 y 255~325에 놓인다.</summary>
        [SerializeField] Vector2 contactPointScreenAnchor = new Vector2(0.932f, 0.269f);

        [Header("진행 완료 버튼(접선 지점)")]
        /// <summary>지도 위에 접선 지점 카드를 만들지 여부. 기본은 꺼짐 - 진행 완료 버튼은 HUD로
        /// 옮겨갔다(월드에 두면 화면 테두리에 가린다). 지도 위 표식이 다시 필요해지면 켜면 된다.</summary>
        [SerializeField] bool spawnContactPointInWorld;
        [SerializeField] Sprite contactButtonSprite;

        /// <summary>버튼의 화면 가로 크기(px). 카메라 줌이 스테이지마다 달라 월드 스케일을 고정하면
        /// 크기가 제각각이 되므로 화면 픽셀로 지정하고 매번 역산한다.</summary>
        [SerializeField] float contactButtonScreenWidth = 230f;

        public event Action<LocationData> LocationClicked;
        public event Action<LocationData> LocationHoverEnter;
        public event Action<LocationData> LocationHoverExit;
        public event Action<NpcData> NpcClicked;
        /// <summary>커서가 NPC 위에 들어왔을 때 - 조사 파일을 여는 신호다(장소 정보 패널과 같은 어법).</summary>
        public event Action<NpcData> NpcHoverEnter;
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
        /// <summary>이 스테이지에서 장소 카드/NPC를 몇 배로 그릴지(StageData.worldViewScale).
        /// 카메라 orthographicSize가 스테이지마다 달라 화면상 크기가 크게 차이 나는 걸 보정하는 값이다.
        /// <b>배치 좌표는 건드리지 않고 보이는 크기만</b> 바꾼다 - 대신 NPC가 카드 옆에 붙는 간격도
        /// 같은 비율로 늘려야 카드만 커지고 NPC가 카드 안으로 파고드는 일이 없다.</summary>
        float ViewScale
        {
            get
            {
                var stage = installer != null ? installer.StageAsset : null;
                return stage != null && stage.worldViewScale > 0f ? stage.worldViewScale : 1f;
            }
        }

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
                view.transform.localScale = Vector3.one * ViewScale;
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
                view.transform.localScale *= ViewScale;
                view.Bind(kvp.Key, skin);
                view.Clicked += d => NpcClicked?.Invoke(d);
                view.HoverEnter += d => NpcHoverEnter?.Invoke(d);
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

            // 고른 카드가 바뀔 때마다 지도 위에서 "고를 수 있는 것"을 다시 정한다. 카드를 낸 뒤와
            // 턴이 넘어갈 때는 SelectedCard가 비워지므로 그 시점들도 함께 듣는다.
            installer.EventBus.Subscribe<CardSelectedEvent>(_ => RefreshTargetable());
            installer.EventBus.Subscribe<CardPlayedEvent>(_ => RefreshTargetable());
            installer.EventBus.Subscribe<TurnEndedEvent>(_ => RefreshTargetable());
            installer.EventBus.Subscribe<TurnStartedEvent>(_ => RefreshTargetable());
            RefreshTargetable();
            // InfoSpreadEvent / InfoDeliveredEvent도 구독했었지만 하는 일이 장소·NPC를 한 번
            // 번쩍이게 하는 것뿐이었고, 확산은 매 턴 여러 번 일어나 화면이 계속 깜빡였다.
            // 무슨 일이 일어났는지는 로그 패널과 NPC 대사가 이미 말해 준다(사용자 지시로 제거).
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

            // 진행 완료 버튼은 이제 HUD 캔버스(PlayHudCanvas_New/ProceedButton)에 있다.
            // 월드 오브젝트로 두면 화면 테두리(ScreenFrame)에 가린다 - 그 테두리는 Screen Space
            // Overlay 캔버스라 월드에 있는 것은 구조적으로 그 위로 올라올 수 없기 때문이다.
            // 이 자리는 게임 세계의 장소가 아니라 "턴 확정"이라는 시스템 동작이므로 HUD가 맞다.
            // StageData.contactPoint 데이터 자체는 그대로 두되 지도 위에 카드를 만들지 않는다.
            if (!spawnContactPointInWorld) return;

            var view = Instantiate(locationSitePrefab, locationRoot);
            view.Bind(stage.contactPoint, ResolveContactPointPosition(stage), skin);
            view.ShowAsActionButtonOnly(contactButtonSprite, contactButtonScreenWidth, Camera.main);
            view.Clicked += _ => ContactPointClicked?.Invoke();
            contactPointView = view;
        }

        /// <summary>접선 지점은 게임 세계의 장소가 아니라 "전달"이라는 시스템 동작이 놓인 자리라,
        /// 스테이지가 바뀌어도 <b>화면상 같은 자리</b>에 있어야 플레이어가 매번 찾지 않는다. 그런데
        /// 카메라 orthographicSize가 스테이지마다 5(Zone1)~14(Metropolis)로 달라서, 월드 좌표를 고정하면
        /// 화면 위치가 제각각이 된다(실측: 같은 (-6.6,-1.9)가 Zone1에서는 왼쪽 12.9%/아래 25.0%,
        /// Metropolis에서는 36.7%/41.4% = 거의 화면 한복판). 그래서 기본값은 월드 좌표가 아니라
        /// <b>뷰포트 비율</b>로 잡고 매번 카메라에서 역산한다 - 해상도·화면비가 달라져도 자리가 유지된다.
        /// StageData.contactPointPosition에 0이 아닌 값이 들어 있으면 그 월드 좌표를 그대로 써서
        /// 스테이지별 예외를 둘 수 있다(하위 호환).</summary>
        Vector2 ResolveContactPointPosition(StageData stage)
        {
            if (stage.contactPointPosition != Vector2.zero) return stage.contactPointPosition;

            var cam = Camera.main;
            if (cam == null || !cam.orthographic) return stage.contactPointPosition;

            float halfHeight = cam.orthographicSize;
            float halfWidth = halfHeight * cam.aspect;
            var camPos = cam.transform.position;
            return new Vector2(
                camPos.x + (contactPointScreenAnchor.x - 0.5f) * 2f * halfWidth,
                camPos.y + (contactPointScreenAnchor.y - 0.5f) * 2f * halfHeight);
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
            // 카드/NPC를 키운 스테이지에서는 옆에 붙는 간격도 같은 비율로 벌어져야 한다 -
            // 안 그러면 카드만 커지고 NPC가 카드 안으로 파고든다(ViewScale=1이면 기존과 동일).
            float scale = ViewScale;
            float x = side * (NpcFlankBaseOffset + slot * NpcFlankStep) * scale;

            Vector2 basePos = locationViews.TryGetValue(location, out var view)
                ? (Vector2)view.transform.position : location.worldPosition;
            return basePos + new Vector2(x, NpcFlankVerticalOffset * scale);
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

        /// <summary>지금 고른 카드로 <b>고를 수 있는 것</b>만 커서에 반응하게 한다 - 확산 카드는 장소,
        /// 전달 카드는 사람. 카드를 안 골랐으면 둘 다 평소대로 반응한다.
        ///
        /// 예전에는 아무 데나 눌러 본 뒤 "이 카드는 사람을 대상으로 해야 합니다" 같은 경고를 읽고
        /// 다시 누르게 했다. 커서를 올려 보는 순간 아무 반응이 없으면 그 자체가 답이므로 경고 문구를
        /// 없앴다(TargetingController에서 함께 제거).
        ///
        /// 확대 반응만 막는 것이고 <b>조사는 그대로 된다</b> - 장소 정보 패널은 계속 뜨고, NPC 조사
        /// 파일도 계속 열린다. 카드를 든 채로 상대를 살펴보는 일까지 막으면 판단할 방법이 없어진다.</summary>
        void RefreshTargetable()
        {
            var card = installer.Turns.SelectedCard;
            bool locationsTargetable = card == null || card.cardType == InfoCardType.Spread;
            bool npcsTargetable = card == null || card.cardType == InfoCardType.Deliver;

            foreach (var view in locationViews.Values) view.SetTargetable(locationsTargetable);
            foreach (var view in npcViews.Values) view.SetTargetable(npcsTargetable);
            // 접선 지점(CreateContactPoint)은 locationViews에 없다 - 대상이 아니라 전달 확정 버튼이라
            // 어떤 카드를 들고 있든 항상 반응해야 한다.
        }
    }
}
