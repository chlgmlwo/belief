using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Belief.Core;
using Belief.Data;
using Belief.Presentation.World;

namespace Belief.EditorTools
{
    /// <summary>Edit Mode(Play 아님)에서 Scene 뷰에 도시 배경 + 장소/NPC를 미리 보여주고, 드래그하면
    /// 바로 StageData.locationLayout/npcLayout에 저장하는 순수 에디터 도구다. 런타임 코드/씬에는
    /// 아무 GameObject도 추가하지 않는다 - IMGUI Handles/GUI로만 그린다.
    ///
    /// 장소는 실제 게임과 동일한 카드 스타일(사진+프레임+압정+이름표 리본)로, NPC는 캐릭터
    /// 이미지만(장식 없음) 보여준다 - 사용자 지정.
    ///
    /// 그리기는 2단계로 나눈다: 1) 3D Handle(드래그 피킹)을 전부 처리하며 그릴 텍스처 목록만
    /// 모으고, 2) 마지막에 Handles.BeginGUI()/EndGUI()를 딱 한 번만 감싸 전부 그린다 - GUI
    /// 좌표계(BeginGUI)와 3D Handle 좌표계를 프레임 안에서 여러 번 왔다갔다 전환하면 일부
    /// 항목이 잘려 보이는 문제가 있어(3-37) 하나로 합쳤다.
    ///
    /// 장소 프레임/압정/이름표 리본의 오프셋·스케일은 하드코딩하지 않고 매 프레임
    /// `LocationSiteView.prefab`을 직접 읽는다(`ReadLocationCardLayout`) - 사용자가 프리팹에서
    /// Frame/Pin/NameTag/Photo의 Position/Scale을 조정하고 저장하면 Scene 뷰에 바로 반영된다.</summary>
    [InitializeOnLoad]
    public static class WorldLayoutSceneTool
    {
        const string MenuPath = "Belief/World Layout Preview";
        const string PrefKey = "Belief.WorldLayoutSceneTool.Enabled";

        const int NpcMaxPerRow = 3;
        const float NpcHorizontalSpacing = 2.3f;
        const float NpcVerticalSpacing = 1.1f;
        static readonly Vector2 NpcSlotOffset = new Vector2(0f, -1.8f);

        // ------------------------------------------------------------ 카드 비주얼 값
        //
        // 3-45부터 상수로 박아두지 않고 LocationSiteView.prefab을 매 OnSceneGUI마다 직접 읽는다
        // (ReadLocationCardLayout) - 사용자가 프리팹에서 Frame/Pin/NameTag/Photo의 Position/Scale을
        // 직접 조정하면서 Scene 뷰로 바로바로 확인할 수 있어야 하기 때문. 프리팹을 저장해야(또는
        // Prefab Mode의 Auto Save) 여기 반영된다 - 저장 전 미리보기 값은 디스크에 있는 값 그대로다.

        const string LocationPrefabPath = "Assets/Belief/Prefabs/World/LocationSiteView.prefab";

        struct LocationCardLayout
        {
            public bool valid;
            public Vector2 photoOffset;
            public bool hasFrame;
            public Vector2 frameOffset, frameScale;
            public Vector2 pinOffset, pinScale;
            public Vector2 nameTagOffset, nameTagScale;
        }

        const float NpcDisplayScale = 1.08f; // body localScale

        static readonly Vector2 FallbackLocationSize = new Vector2(1.4f, 1.4f);
        static readonly Vector2 FallbackNpcSize = new Vector2(1.08f, 1.08f);
        static readonly Color LocationFallbackColor = new Color(0.66f, 0.63f, 0.58f, 0.9f);
        static readonly Color NpcFallbackColor = new Color(0.68f, 0.64f, 0.56f, 0.9f);

        struct DrawRequest
        {
            public Sprite sprite;
            public Vector2 center;
            public Vector2 size;
            public Color tint; // 항상 명시적으로 채운다(틴트 없으면 Color.white) - default(Color)는 완전 투명 검정이라 그대로 두면 안 보인다
            public Color fallbackColor; // sprite==null일 때만 사용(alpha 0이면 그리지 않음)
        }

        static bool Enabled
        {
            get => EditorPrefs.GetBool(PrefKey, true);
            set => EditorPrefs.SetBool(PrefKey, value);
        }

        static WorldLayoutSceneTool()
        {
            SceneView.duringSceneGui += OnSceneGUI;
        }

        [MenuItem(MenuPath)]
        static void ToggleEnabled()
        {
            Enabled = !Enabled;
            SceneView.RepaintAll();
        }

        [MenuItem(MenuPath, true)]
        static bool ToggleEnabledValidate()
        {
            Menu.SetChecked(MenuPath, Enabled);
            return true;
        }

        static void OnSceneGUI(SceneView sceneView)
        {
            if (!Enabled || Application.isPlaying) return;

            var installer = Object.FindFirstObjectByType<GameInstaller>();
            var stage = installer != null ? installer.StageAsset : null;
            if (stage == null) return;

            var worldPresenter = Object.FindFirstObjectByType<WorldPresenter>();
            var skin = worldPresenter != null ? worldPresenter.skin : null;

            var cardLayout = ReadLocationCardLayout();

            var requests = new List<DrawRequest>();

            AddCityBackgroundRequest(stage, requests);
            HandleLocationItems(stage, skin, cardLayout, requests);
            HandleNpcItems(stage, skin, requests);

            DrawAllTextures(requests);
        }

        /// <summary>LocationSiteView.prefab에서 Photo/Frame/Pin/NameTag의 현재 Position/Scale을
        /// 그대로 읽어온다 - 루트와 Decoration 래퍼의 localScale도 곱해 반영한다(둘 다 회전 없이
        /// 축 정렬 스케일만 쓰므로 성분별 곱셈으로 정확하다).
        ///
        /// 이 프리팹을 Prefab Mode로 열어 편집 중이면(Auto Save 여부와 무관하게) 그 편집 중인
        /// 라이브 콘텐츠(`PrefabStage.prefabContentsRoot`)를 직접 읽는다 - `AssetDatabase.
        /// LoadAssetAtPath`는 실제로 디스크에 저장된 뒤에야 값이 바뀌므로, Inspector에서 값을
        /// 슬라이더로 움직이는 매 순간을 그대로 따라가려면 저장 전 메모리 상태를 봐야 한다(직접
        /// 확인함 - 저장 전에는 AssetDatabase가 옛날 값을 돌려준다). Prefab Mode가 아니면(Zone1을
        /// 그냥 보고 있을 때) 기존처럼 저장된 에셋을 읽는다.</summary>
        static LocationCardLayout ReadLocationCardLayout()
        {
            var stage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
            GameObject prefab = stage != null && stage.assetPath == LocationPrefabPath
                ? stage.prefabContentsRoot
                : AssetDatabase.LoadAssetAtPath<GameObject>(LocationPrefabPath);
            if (prefab == null) return default;

            var decoration = prefab.transform.Find("Decoration");
            if (decoration == null) return default;

            Vector2 cardScale = Vector2.Scale(prefab.transform.localScale, decoration.localScale);

            var photo = decoration.Find("Photo");
            var frame = decoration.Find("Frame");
            var pin = decoration.Find("Pin");
            var nameTag = decoration.Find("NameTag");

            return new LocationCardLayout
            {
                valid = true,
                photoOffset = photo != null ? Vector2.Scale(photo.localPosition, cardScale) : Vector2.zero,
                hasFrame = frame != null,
                frameOffset = frame != null ? Vector2.Scale(frame.localPosition, cardScale) : Vector2.zero,
                frameScale = frame != null ? Vector2.Scale(frame.localScale, cardScale) : Vector2.one,
                pinOffset = pin != null ? Vector2.Scale(pin.localPosition, cardScale) : Vector2.zero,
                pinScale = pin != null ? Vector2.Scale(pin.localScale, cardScale) : Vector2.one,
                nameTagOffset = nameTag != null ? Vector2.Scale(nameTag.localPosition, cardScale) : Vector2.zero,
                nameTagScale = nameTag != null ? Vector2.Scale(nameTag.localScale, cardScale) : Vector2.one,
            };
        }

        // ------------------------------------------------------------ 배경

        static void AddCityBackgroundRequest(StageData stage, List<DrawRequest> requests)
        {
            if (stage.cityBackground == null) return;

            var cam = Camera.main != null ? Camera.main : Object.FindFirstObjectByType<Camera>();
            if (cam == null || !cam.orthographic) return;

            float cameraHeight = cam.orthographicSize * 2f;
            float cameraWidth = cameraHeight * cam.aspect;
            var sprite = stage.cityBackground;
            Vector2 spriteSize = SpriteWorldSize(sprite, 1f);
            float scale = Mathf.Max(cameraWidth / spriteSize.x, cameraHeight / spriteSize.y);

            Vector2 center = new Vector2(cam.transform.position.x, cam.transform.position.y);
            requests.Add(new DrawRequest { sprite = sprite, center = center, size = spriteSize * scale, tint = Color.white });
        }

        // ------------------------------------------------------------ 장소

        static void HandleLocationItems(StageData stage, PlayHudSkin skin, LocationCardLayout layout, List<DrawRequest> requests)
        {
            if (stage.locations == null) return;

            foreach (var location in stage.locations)
            {
                if (location == null) continue;
                Vector2 pos = ResolveLocationPosition(stage, location);
                Vector2 photoSize = location.locationPhoto != null
                    ? SpriteWorldSize(location.locationPhoto, 1f)
                    : FallbackLocationSize;

                Vector2 newPos = HandleDrag(pos, Mathf.Max(photoSize.x, photoSize.y) * 0.6f);
                if (newPos != pos) SaveLocationPosition(stage, location, newPos);

                Vector2 photoCenter = newPos + (layout.valid ? layout.photoOffset : Vector2.zero);
                requests.Add(new DrawRequest
                {
                    sprite = location.locationPhoto, center = photoCenter, size = photoSize,
                    tint = Color.white, fallbackColor = LocationFallbackColor
                });

                if (skin != null && layout.valid)
                {
                    // 실제 게임과 동일한 레이어 순서: 사진(뒤) -> 프레임(있으면) -> 이름표 리본 -> 압정(앞).
                    // 프레임은 LocationSiteView.prefab에서 Frame 오브젝트를 지우면(사진 자체에 이미
                    // 폴라로이드 테두리가 그려져 있어 중복이라 삭제) 여기서도 자동으로 안 그린다.
                    if (layout.hasFrame)
                        AddSkinElement(requests, skin.locationImageFrame, newPos + layout.frameOffset, layout.frameScale);

                    var tag = location.displayName != null && location.displayName.Length <= 3 ? skin.locationTag3 : skin.locationTag5;
                    Vector2 tagCenter = newPos + layout.nameTagOffset;
                    AddSkinElement(requests, tag, tagCenter, layout.nameTagScale);

                    Vector2 pinCenter = newPos + layout.pinOffset;
                    AddSkinElement(requests, skin.pin, pinCenter, layout.pinScale);

                    DrawLabel(tagCenter, location.displayName);
                }
                else
                {
                    DrawLabel(photoCenter - new Vector2(0f, photoSize.y / 2f + 0.15f), location.displayName);
                }
            }
        }

        static Vector2 ResolveLocationPosition(StageData stage, LocationData location)
        {
            if (stage.locationLayout != null)
                foreach (var entry in stage.locationLayout)
                    if (entry.location == location) return entry.position;
            return location.worldPosition;
        }

        /// <summary>StageData.locationLayout 배열에서 이 장소의 항목을 찾아 갱신하거나, 없으면 새로
        /// 추가한다 - Undo 지원 + 즉시 저장(SetDirty).</summary>
        static void SaveLocationPosition(StageData stage, LocationData location, Vector2 newPosition)
        {
            Undo.RecordObject(stage, "Move Location Layout");

            var list = stage.locationLayout != null
                ? new List<LocationLayoutEntry>(stage.locationLayout)
                : new List<LocationLayoutEntry>();

            int index = list.FindIndex(e => e.location == location);
            if (index >= 0)
            {
                var entry = list[index];
                entry.position = newPosition;
                list[index] = entry;
            }
            else
            {
                list.Add(new LocationLayoutEntry { location = location, position = newPosition });
            }

            stage.locationLayout = list.ToArray();
            EditorUtility.SetDirty(stage);
        }

        // ------------------------------------------------------------ NPC

        static void HandleNpcItems(StageData stage, PlayHudSkin skin, List<DrawRequest> requests)
        {
            if (stage.npcPlacements == null) return;

            var slotCounters = new Dictionary<LocationData, int>();

            foreach (var placement in stage.npcPlacements)
            {
                var npc = placement.npc;
                if (npc == null) continue;

                Vector2 pos = ResolveNpcPosition(stage, placement, slotCounters);
                Vector2 bodySize = npc.characterPhoto != null ? SpriteWorldSize(npc.characterPhoto, NpcDisplayScale) : FallbackNpcSize;

                Vector2 newPos = HandleDrag(pos, Mathf.Max(bodySize.x, bodySize.y) * 0.6f);
                if (newPos != pos) SaveNpcPosition(stage, npc, newPos);

                // NPC는 프레임/이름표 리본/압정 없이 캐릭터 이미지만 보여준다(장소만 카드
                // 스타일링을 적용) - 사용자 지정.
                requests.Add(new DrawRequest { sprite = npc.characterPhoto, center = newPos, size = bodySize, tint = Color.white, fallbackColor = NpcFallbackColor });

                DrawLabel(newPos + new Vector2(0f, bodySize.y / 2f + 0.15f), npc.displayName);
            }
        }

        static Vector2 ResolveNpcPosition(StageData stage, NpcPlacementEntry placement, Dictionary<LocationData, int> slotCounters)
        {
            if (stage.npcLayout != null)
                foreach (var entry in stage.npcLayout)
                    if (entry.npc == placement.npc) return entry.position;

            // npcLayout에 아직 없으면(처음 여는 경우) 장소 기준 자동 슬롯값으로 초기 위치를 추정만
            // 한다 - 저장하지는 않는다(드래그해야 비로소 npcLayout에 실제로 기록됨).
            var loc = placement.EffectiveStartLocation;
            Vector2 basePos = loc != null ? ResolveLocationPosition(stage, loc) : Vector2.zero;
            if (loc == null) return basePos;

            slotCounters.TryGetValue(loc, out int index);
            slotCounters[loc] = index + 1;
            return ComputeNpcSlot(basePos, index, index + 1);
        }

        /// <summary>StageData.npcLayout 배열에서 이 NPC의 항목을 찾아 갱신하거나, 없으면 새로
        /// 추가한다 - Undo 지원 + 즉시 저장(SetDirty).</summary>
        static void SaveNpcPosition(StageData stage, NpcData npc, Vector2 newPosition)
        {
            Undo.RecordObject(stage, "Move NPC Layout");

            var list = stage.npcLayout != null
                ? new List<NpcLayoutEntry>(stage.npcLayout)
                : new List<NpcLayoutEntry>();

            int index = list.FindIndex(e => e.npc == npc);
            if (index >= 0)
            {
                var entry = list[index];
                entry.position = newPosition;
                list[index] = entry;
            }
            else
            {
                list.Add(new NpcLayoutEntry { npc = npc, position = newPosition });
            }

            stage.npcLayout = list.ToArray();
            EditorUtility.SetDirty(stage);
        }

        static Vector2 ComputeNpcSlot(Vector2 locationPos, int index, int count)
        {
            int row = index / NpcMaxPerRow;
            int col = index % NpcMaxPerRow;
            int itemsInRow = Mathf.Min(NpcMaxPerRow, count - row * NpcMaxPerRow);

            float startX = -(itemsInRow - 1) * NpcHorizontalSpacing * 0.5f;
            float x = startX + col * NpcHorizontalSpacing;
            float y = -row * NpcVerticalSpacing;

            return locationPos + NpcSlotOffset + new Vector2(x, y);
        }

        // ------------------------------------------------------------ 공용 드래그 + 그리기

        static void AddSkinElement(List<DrawRequest> requests, Sprite sprite, Vector2 center, Vector2 elementScale)
        {
            if (sprite == null) return;
            Vector2 size = SpriteWorldSize(sprite, 1f) * elementScale;
            requests.Add(new DrawRequest { sprite = sprite, center = center, size = size, tint = Color.white });
        }

        /// <summary>3D FreeMoveHandle로 드래그 피킹만 처리한다(점을 그리지 않음 - 카드 이미지 자체가
        /// 드래그 손잡이다). 반환값이 입력과 다르면 이번 프레임에 드래그로 위치가 바뀐 것이다.</summary>
        static Vector2 HandleDrag(Vector2 pos, float pickRadius)
        {
            Vector3 pos3 = new Vector3(pos.x, pos.y, 0f);
            EditorGUI.BeginChangeCheck();
            Vector3 moved = Handles.FreeMoveHandle(pos3, pickRadius, Vector3.zero, InvisiblePickCap);
            if (EditorGUI.EndChangeCheck())
                return new Vector2(moved.x, moved.y);
            return pos;
        }

        static void DrawLabel(Vector2 worldPos, string text)
        {
            var style = new GUIStyle(EditorStyles.boldLabel);
            style.normal.textColor = Color.black;
            style.alignment = TextAnchor.MiddleCenter;
            Handles.Label(new Vector3(worldPos.x, worldPos.y, 0f), text, style);
        }

        /// <summary>Repaint 단계에서는 아무것도 그리지 않는다(이미지가 대신 그려짐) - 그 외 단계
        /// (Layout/MouseDown 등)는 SphereHandleCap에 그대로 위임해 클릭/드래그 피킹만 살려둔다.</summary>
        static void InvisiblePickCap(int controlId, Vector3 position, Quaternion rotation, float size, EventType eventType)
        {
            if (eventType == EventType.Repaint) return;
            Handles.SphereHandleCap(controlId, position, rotation, size, eventType);
        }

        /// <summary>모아둔 요청을 Handles.BeginGUI()/EndGUI() 한 번으로 전부 그린다 - 월드 좌표
        /// 사각형의 네 꼭짓점을 스크린 좌표로 투영해 GUI.DrawTexture로 그리는 방식이라, Scene 뷰가
        /// 2D 모드(직교, 회전 없음)일 때 정확하다(이 프로젝트는 항상 2D 모드로 작업). requests는
        /// 추가된 순서(뒤→앞)대로 그려진다 - 사진 → 프레임 → 이름표 리본 → 압정 순으로 쌓여야
        /// 실제 게임과 같은 레이어 순서가 된다.</summary>
        static void DrawAllTextures(List<DrawRequest> requests)
        {
            Handles.BeginGUI();
            foreach (var req in requests)
            {
                Vector3 topLeftWorld = new Vector3(req.center.x - req.size.x / 2f, req.center.y + req.size.y / 2f, 0f);
                Vector3 bottomRightWorld = new Vector3(req.center.x + req.size.x / 2f, req.center.y - req.size.y / 2f, 0f);
                Vector2 topLeftScreen = HandleUtility.WorldToGUIPoint(topLeftWorld);
                Vector2 bottomRightScreen = HandleUtility.WorldToGUIPoint(bottomRightWorld);
                var screenRect = new Rect(topLeftScreen.x, topLeftScreen.y,
                    bottomRightScreen.x - topLeftScreen.x, bottomRightScreen.y - topLeftScreen.y);

                if (req.sprite != null)
                {
                    var uv = new Rect(req.sprite.rect.x / req.sprite.texture.width, req.sprite.rect.y / req.sprite.texture.height,
                        req.sprite.rect.width / req.sprite.texture.width, req.sprite.rect.height / req.sprite.texture.height);
                    var oldColor = GUI.color;
                    GUI.color = req.tint == default ? Color.white : req.tint;
                    GUI.DrawTextureWithTexCoords(screenRect, req.sprite.texture, uv, true);
                    GUI.color = oldColor;
                }
                else if (req.fallbackColor.a > 0f)
                {
                    var oldColor = GUI.color;
                    GUI.color = req.fallbackColor;
                    GUI.DrawTexture(screenRect, Texture2D.whiteTexture);
                    GUI.color = oldColor;
                }
            }
            Handles.EndGUI();
        }

        static Vector2 SpriteWorldSize(Sprite sprite, float extraScale) =>
            new Vector2(sprite.rect.width / sprite.pixelsPerUnit, sprite.rect.height / sprite.pixelsPerUnit) * extraScale;
    }
}
