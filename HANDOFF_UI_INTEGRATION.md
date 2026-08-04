# HANDOFF — 목업 UI → Zone1 실게임 이식

작성일: 2026-08-04
상태: **Step A, E 완료. Step B/C/D — Zone1의 `HudCanvas`를 `PlayHudCanvas_New`(목업 기반)로 완전히 교체 완료 (아래 3-8 참조). 기존 `HudCanvas`는 삭제하지 않고 `HudCanvas_OLD_BACKUP`으로 비활성 보존. Step F(Zone1HudMockupCanvas 삭제, Zone2~4 적용) 미착수.**

---

## 1. 현재 목표

목업 씬에서 만든 UI를 **최종 HUD로 사용**하고, 기존 게임 기능/데이터를 그대로 유지한 채 이식한다.

### 실제로 채택한 이식 방식 (중요)

"목업 Canvas를 통째로 복제해 70개 참조를 재배선한다"가 아니라,
**기존 `HudCanvas` 하이어라키를 그대로 두고, 각 오브젝트의 비주얼(스프라이트·폰트·크기·위치·색)만 목업 값으로 덮어쓴다.**

이유:
- `HudView.cs`는 **순수 참조 테이블**이다. 70개 `[SerializeField]` 필드에 로직이 없다.
- `HudPresenter`는 그 참조에 **텍스트 값과 활성 여부만 대입**한다.
- 즉 정적 스타일은 전부 프리팹에 구워져 있으므로, **로직을 건드리지 않고 비주얼만 교체 가능**하다.
- 재배선(70개)은 실수 시 복구 비용이 크고, 덮어쓰기는 배선이 유지된다.

결과적으로 사용자가 요구한 "목업 UI가 최종 HUD가 된다"와 동일한 결과에 도달하되, 배선 리스크가 없다.

### 사용자 지정 8단계 안전 절차 (원문 그대로 — 반드시 준수)

1. 기존 Zone1 HUD 씬과 관련 프리팹을 Git 기준으로 복구 가능한 상태인지 확인한다.
2. 목업 캔버스를 실제 HUD 구조로 복제한다.
3. 기존 HudView의 70개 참조와 HudPresenter, CardTileView, 실제 데이터 연결을 새 목업 계층에 전부 재배선한다.
4. 카드 선택, 장소/NPC 클릭, Deliver/Spread, 미션 진행, Turn 갱신, Profile/Log 전환을 Play Mode에서 검증한다.
5. 새 HUD가 정상 작동하는 것을 확인한 뒤 기존 Zone1 HUD 캔버스를 삭제한다.
6. 목업 씬 3개는 참고용으로 보존한다.
7. 기존 HUD와 새 HUD가 동시에 활성화되지 않게 한다.
8. Console Error/Warning 0을 확인한다.

> 검증 전에는 기존 HUD를 삭제하지 말고 임시 비활성 상태로 유지한다.

---

## 2. 전체 계획 (A~F)

| Step | 대상 | 상태 |
|---|---|---|
| **A** | `CardTileView.prefab` + `CardTileView.cs` — 하단 손패 카드 | ✅ **완료 (B-1 포함 수정 완료)** |
| **B/C/D 통합** | `HudCanvas` 전체 — `UI_PlayHudMockup` 기반으로 방식 변경 (아래 3-6/3-7 참조) | 🟡 **손패 카드 4장(프록시+어댑터) 완료. 장소/NPC/Deliver/미션/Profile/Log/Zone2~4 미착수** |
| **E** | `StageBriefingCanvas.prefab` — 스테이지 선택 | ✅ **완료 (아래 3-5 참조)** |
| **F** | Play Mode 통합 검증 → `Zone1HudMockupCanvas` 삭제 | ⬜ 미착수 |

---

## 3. 완료된 작업 (Step A)

### 3-1. `CardTileView.prefab` 레이아웃 재구성

목업(460×230) 기준 좌표를 실제 카드 크기(380×190)에 맞춰 **S = 380/460 = 0.8261** 배로 스케일해 적용. 모든 자식을 anchor/pivot (0,1)로 통일.

| 자식 | 목업 좌표 (460×230) | 적용 좌표 (×0.8261) | 폰트 |
|---|---|---|---|
| Title | (30, −55.62) 400×30 | (24.8, −45.9) 330×24.8 | SUIT-Heavy 14.9 MidlineLeft |
| Kind | (186, −25) 170×24 | (153.6, −20.7) 140×19.8 | 8.3 Midline |
| CategoryText | (366.71, −36) 80×30 | (302.9, −29.7) 66×24.8 | 14.9 MidlineLeft |
| ExpandedDetail | — | (0,0) 380×190, **항상 활성** | — |
| └ DescriptionText | (40, −90) 380×46 | (33, −74.3) 314×38 | 11.6 Center |
| └ TagsText | (43, −150) 324×24 | (35.5, −123.9) 268×19.8 | 9.9 Center |

색상: Kind (0.16,0.13,0.11) / CategoryText (0.62,0.16,0.12) / Title (0.96,0.95,0.93) / DescriptionText (0.16,0.13,0.11) / TagsText (0.30,0.27,0.25). 루트 sizeDelta 380×190.

### 3-2. `CardTileView.cs` 연출 이식

```csharp
const float CollapsedWidth  = 380f;
const float CollapsedHeight = 190f;
const float CardSpacing     = 76f;   // 106f -> 76f (가이드 실측: 카드 폭 384 + 여백 76)
const float SelectedRaise   = 28f;
const float ExpandedScale   = 1.05f;
const float SelectAnimDuration = 0.18f;
```

- `ApplySlot` — x는 즉시 반영, y·scale은 코루틴 보간. 첫 호출/비활성 시에는 즉시 스냅(순간이동 방지).
- `AnimateSlot` — **현재 값에서** `Mathf.SmoothStep` + `Time.unscaledDeltaTime`으로 보간.
- `CancelRunningRoutine`에 `slotRoutine` 정지 추가.
- `SetHandState` — `expandedDetailRoot`를 더 이상 비활성화하지 않고 항상 활성 보장.
- `Bind()` 색상 로직 — 아트가 있으면 선택 틴트를 적용하지 않고 목업 색을 그대로 사용.

### 3-3. `Zone1.unity`

- `HUD` 루트 → `SetActive(true)`
- `Zone1HudMockupCanvas` → `SetActive(false)` (**삭제하지 않음** — 안전 절차 5·7항 준수)

### 3-4. Play Mode 검증 결과 (Step A)

- `CardTileView(Clone)` 4장 전부 `card = bound`, `ExpandedDetail.active = True`
- x 위치 −684 / −228 / 228 / 684 → 간격 456 = 380 + 76 (가이드 실측치와 일치)
- 컴파일 후 Console Error / Warning **0**

### 3-5. Step E — StageBriefingCanvas (2026-08-04, 방식 변경)

**Step E는 A와 반대 방식으로 진행 중이다** — A/B/C/D는 "기존 하이어라키를 유지하고 비주얼만 덮어쓴다"였지만,
Step E는 사용자 지시로 **"목업(`StageSelectMockup.unity`)의 Canvas/Hierarchy를 그대로 복제해 새 프리팹의
최종 구조로 삼고, 기존 프리팹에서는 런타임 기능·데이터 바인딩·버튼 이벤트만 이식한다"**로 진행했다.
레이아웃/RectTransform/스프라이트 배치/폰트 배치는 기존 프리팹에서 전혀 가져오지 않았다.

**대응표 (기존 `StageBriefingView` 필드 → 새 목업 노드):**

| 필드 | 기존 대상 | 새 대상 | 비고 |
|---|---|---|---|
| `canvasGroup` | `Background`(리프, 배경만 페이드되던 기존 결함) | 루트에 새로 추가 | 전체 화면 페이드로 정상화 |
| `launchButton` | `LaunchButton` | `ActionCard/StartLabel`에 Button 추가 | 목업엔 Button 없음(순수 비주얼) |
| `backButton` | `BackButton` | `ActionCard/TitleLinkLabel`에 Button 추가 | 동일 |
| `stageLabelText`/`titleText`/`turnLimitValueText`/`mapStageLabelText` | 동명 노드 | 동명 노드 | 직결 |
| `objectiveText` | `Objective` | `Subtitle` | 이름만 다름 |
| `blurbText` | `BlurbCard/Text`(한 덩어리) | `StoryLine1` | 목업은 4줄, 데이터는 문자열 1개 → 1번째 줄에만 연결, 2~4번째 줄은 텍스트 비우고 비활성화 |
| `markerSlots[0..3]` | `Marker0..3` + 자식 `Name` | `CurrentStageMarker`/`Marker1..3`, nameText 없음 | 목업엔 마커별 이름 라벨이 없음(`BindMap`이 null 체크로 안전 처리) |

**판단해서 결정한 것:**
- `HoverUnderlineFeedback`(버튼 호버 시 밑줄) — 목업에 대응 오브젝트가 없어 이식하지 않음. 필요하면 추후 논의.
- `Canvas.sortingOrder` — 목업 기본값(0) 대신 기존값(50)을 가져옴. HudCanvas 위에 떠야 하는 기능 요구사항이라 "레이아웃"이 아니라 "기능"으로 분류.
- `Canvas Scaler matchWidthOrHeight` — 목업 값(0.5)을 그대로 유지(기존 0 대신). 목업 좌표가 이 값 기준으로 튜닝됐다고 판단.

**작업 순서:**
1. `StageSelectMockup.unity`의 `StageSelectCanvas`를 Instantiate로 복제(씬 자체는 건드리지 않음) → `Assets/Belief/Prefabs/StageBriefing/StageBriefingCanvas_New.prefab`으로 저장.
2. Zone1 / Zone2 / Zone3 / Metropolis 4개 씬 전부에서: 기존 `StageBriefingCanvas` 인스턴스는 `SetActive(false)` + `StageBriefingCanvas_OLD_BACKUP`으로 이름 변경(삭제 아님), 같은 부모 밑에 새 프리팹 인스턴스를 배치하고 `StageBriefingPresenter.view` 필드를 새 인스턴스로 재배선.
3. 4개 씬 전부 Play Mode 진입 → 실제 스테이지 데이터(제목/목표/턴 제한/블러브) 정상 바인딩 확인, Console Error/Warning 0. Zone1에서는 추가로 "작전 실행" 버튼 클릭 → 페이드아웃 → 비활성화까지 실제 동작 확인.
4. 기존 `StageBriefingCanvas.prefab`은 전혀 수정하지 않음(11개 자식 그대로). `StageSelectMockup.unity`도 저장 없이 닫아 원본 그대로 보존.

**8번(정리) 완료 (2026-08-04):** 사용자 승인 후 4개 씬의 `StageBriefingCanvas_OLD_BACKUP` 오브젝트 삭제, 기존 `StageBriefingCanvas.prefab`은 `AssetDatabase.MoveAssetToTrash`로 OS 휴지통에 이동(완전 영구 삭제 아님 — 필요시 복구 가능). 이후 Zone1 Play Mode 재확인 결과 Console Error/Warning 0, 정상 바인딩 확인.

**리네임 완료 (2026-08-04):** `StageBriefingCanvas_New.prefab` → `StageBriefingCanvas.prefab`으로 정식 리네임(`AssetDatabase.RenameAsset`, GUID 유지 확인됨 — `a3a6b2b0...` 동일). 프리팹 내부 루트 GameObject 이름도 함께 맞춤. 4개 씬(Zone1/2/3/Metropolis) 전부 `PrefabUtility.GetPrefabInstanceStatus == Connected`로 재확인, Console Error/Warning 0.

**Step E 완전히 종료.**

### 3-6. Step B/C/D — HudCanvas 전체 (2026-08-04, 방식 변경 — 조사 + 새 프리팹 생성까지만)

**Step E와 같은 방식이지만 더 크다.** 사용자 지시로 기존 "덮어쓰기" 계획(1절 참조)을 버리고,
`UI_PlayHudMockup.unity`의 Canvas/Hierarchy + **목업 컨트롤러 동작까지** 그대로 최종 기준으로 삼는다.
StageBriefing과 다른 점: 목업엔 이미 완성된 UI *기능*(`HandCardSelectionController`,
`RightDocumentPanelController`)이 있으므로, 이번엔 비주얼뿐 아니라 그 컨트롤러 동작 자체도 보존 대상이다.

**이번 세션 범위는 조사 + 새 프리팹 생성 + 목업 기능 보존 검증까지만이다. Zone1 실데이터 재배선은 다음 세션.**

#### 대응표 요약 (전체는 대화 로그 참조 — 매우 길어 문서에 전문 포함하지 않음)

- **직결**: Log 패널 6종 텍스트, NPC History, 우측 탭 버튼, Log/Profile 콘텐츠 active 토글, 손패 카드 4장 좌표.
- **재설계 필요**: 장소 특성 메모(1덩어리 텍스트 → 라벨/값 2열 분리), NPC 이름/기본정보(2필드 → 1필드 통합),
  NPC 믿음단계(1텍스트 → 숫자+문구 분리), NPC 관계 행(헤더+설명 2줄 → 3열 테이블), 미션 조건
  (`MissionSummaryCard` 진행률+불릿 요약은 완전히 새로운 UI).
- **목업에 없는 기능** (신규 UI 필요): `stageTurnText`(스테이지 턴 — 목업엔 미션 턴 슬롯 1개뿐),
  `helpButton`, 탭 활성 색상 표시(`profileTabIndicator`/`logTabIndicator`), **`cardInfoGo`/`instructionGo`/
  `deliverButtonGo` 전체(정보 전달 버튼 자체가 목업 HandArea에 없음 — 가장 중요한 공백)**,
  `overlayGo` 전체(MISSION/ZONE COMPLETE 팝업), `resultScreenGo` 전체(별도 `UI_MissionResultMockup` 담당),
  `feedbackBannerRect`(토스트), NPC 스탯 태그 4종(`#신중형`/`#명령충실`/`#상명하복`/`#근거중시` — `NpcData`에
  대응 필드 자체가 없음, 데이터 스키마 확장 필요).
- **가장 중요한 구조적 충돌 → ✅ 해결됨 (2026-08-04, 아래 3-7 참조)**: 목업 `HandCard1~4`(`HandCardMockupView`)와
  실전 `CardTileView.prefab`의 구조 충돌을 **프록시 + 어댑터** 방식으로 해결했다.
- **중요 사실**: `LocationSiteView`/`NpcActorView`는 **UI Canvas가 아니라 `SpriteRenderer` 기반 월드
  오브젝트**(`World/LocationSites`, `World/NpcActors`)다. 이번 HUD 프리팹 교체와 **전혀 무관** — 어떤 HUD
  스킨을 쓰든 월드의 장소/NPC 핀은 그대로 렌더링된다. 목업의 `LocationCard01~03`/`LocationInfoPaper`는
  배경 목업 이미지일 뿐 그 월드 오브젝트가 아니다.

#### 새 프리팹 생성

`UI_PlayHudMockup.unity`의 루트 Canvas를 Instantiate로 복제(씬 자체는 저장 없이 닫아 원본 보존) →
`Assets/Belief/Prefabs/HUD/PlayHudCanvas_New.prefab`으로 저장. `GuideOverlay`(BasicGuide/ProfileGuide,
RawImage 참고용 트레이싱 이미지)는 지시대로 제외(삭제)했다. 기존 `HudCanvas.prefab`은 전혀 수정하지 않았다.

#### 목업 컨트롤러 보존 검증 (Play Mode, Zone1에 임시 배치 — 저장 안 함)

기존 `HudCanvas`를 임시로 비활성화하고 새 프리팹을 임시 오브젝트로 배치해 검증한 뒤, 테스트 오브젝트 삭제 +
기존 `HudCanvas` 재활성화 + Zone1을 디스크에서 다시 로드해 **씬 파일 자체는 전혀 건드리지 않았다**(재로드 후
`isDirty=False`, 실제 `HudCanvas` 활성 확인, 테스트 잔재 없음 확인).

- `HandCardSelectionController`: HandCard1 클릭 → anchoredPosition Y −10 → **110**(Δ120, `expandedYOffset`
  값과 정확히 일치), `localScale` **1.03**(`selectedScale`과 일치) 확인.
  (부수적으로 확인된 사실이 아닌 테스트 절차상 오류 4건 발생 — 기존 `HudCanvas`를 비활성화했는데
  `HudPresenter`는 부모 `HUD` 오브젝트에 붙어 있어 계속 살아있었고, 비활성화된 뷰 밑에 카드를 생성하려다 난
  에러. 새 프리팹과 무관하며 재로드로 사라짐.)
- `RightDocumentPanelController`: ProfileTab 클릭 → `Closed`→`Profile`, 패널이 (1630,−43)(닫힘)에서
  (1080,−43)(열림)으로 슬라이드, `ProfileContent` 활성화 확인. LogTab 클릭(Profile 열린 상태에서) →
  `Profile`→`Log`로 **애니메이션 없이 즉시 교체**(콘텐츠만 스왑, 패널 위치 그대로) 확인. LogTab 재클릭(같은 탭) →
  `Log`→`Closed`, 패널+`SharedTabRoot` 동시에 닫힘 위치로 슬라이드 확인.

모두 목업 원본과 동일하게 동작. Console Error/Warning 0(테스트 절차성 4건 제외, 재로드 후 완전히 정리됨).

### 3-7. 손패 카드 구조적 충돌 해결 — 프록시 + 어댑터 (2026-08-04, Step B/C/D 중 손패만 완료)

`CardTileView`(실전, 미수정)와 `HandCardMockupView`/`HandCardSelectionController`(목업, 최소 수정)를
직접 결합하지 않고 중간에 **숨김 프록시 + 어댑터**를 둬서 연결했다.

**핵심 문제**: `CardTileView.ApplySlot()`이 자기 RectTransform(anchor/pivot/sizeDelta/anchoredPosition/
localScale)을 직접 덮어쓴다 — 목업 카드에 그대로 붙이면 목업이 확정한 위치와 정면충돌한다.
또한 `HandCardSelectionController.Awake()`가 자체적으로 카드 클릭을 구독해서, 실제 선택 가능 여부와
무관하게 클릭 즉시 카드를 올려버린다(중복 결정 문제).

**해결 구조**:
```
(보이는) HandCard1~4                (안 보이는) ProxyContainer 밑 CardTileView 4개
├─ HandCardMockupView (수정 없음)     ← HudPresenter가 지금 하던 그대로 Instantiate/Bind/
├─ MockupCardTileAdapter (신규)  <──   ApplySlot/SetSiblingIndex (수정 없음, 프록시에만 적용)
```
- **`MockupCardTileAdapter.cs`(신규)**: HandCard1~4 각각에 부착. 매 프레임 어떤 프록시를 봐야 하는지
  브리지가 알려주면(`AssignProxy`), 프록시가 바뀌면 재구독하고, 카드가 바뀌면 "접어둔 기록"을 리셋한다.
  프록시의 실제 데이터(`InformationCardData`)를 읽어 목업 카드 자체 텍스트(Title/Security/SpreadPlace/
  Chian/Description/Chip1-3)에 채워 넣는다. 클릭은 프록시의 `Button.onClick.Invoke()`를 직접 호출해
  전달한다(실제 포인터 클릭이 아니라 프로그램적 호출 — `CardTileView.Clicked → HudPresenter.OnCardClicked`
  기존 경로를 그대로 태운다).
  - ⚠️ Security/SpreadPlace/Chian 텍스트 매핑은 이름만 보고 추정한 것이다(Security=categoryId,
    Chian=kind(DELIVER/SPREAD), SpreadPlace=TargetType(장소/인물)). 실제 의도와 다르면 이 세 줄만
    고치면 된다.
- **`HandCardHudBridge.cs`(신규, `HandArea`에 부착)**: 매 프레임(`LateUpdate`) `ProxyContainer`의
  자식을 인덱스로 읽어(`GetChild(i)`) 4개 어댑터에 재할당하고, `GameInstaller.Turns.SelectedCard`를
  읽어(절대 안 바꿈) 어느 카드가 펼쳐져야 하는지 계산해 `HandCardSelectionController`에 딱 한 번 반영한다.
- **`HandCardSelectionController.cs`(수정, 최소한)**: `Awake()`의 자체 클릭 구독(2줄)과 `HandleCardClicked`
  삭제 → `public SetSelectedCard(HandCardMockupView)` / `CollapseSelectedVisual()` / `ClearSelectionVisual()`
  3개 추가. `Start()`의 `Configure()`(Expanded Y Offset/Duration/Scale 배분)는 완전히 그대로.
- **`ProxyContainer`**: `HandArea` 밑에 신규 생성. `CanvasGroup(alpha=0, interactable=false,
  blocksRaycasts=false)` + 화면 밖 좌표(anchoredPosition Y −100000). `SetActive(false)`는 쓰지 않음
  (비활성화하면 `PlayAppear`/`PlayDisappear` 코루틴이 못 돈다).
- **`backendSelected` vs `visuallyExpanded` 분리**: `MockupCardTileAdapter`가 프록시의 `HandState`
  전이(Expanded→Collapsed, 여전히 backendSelected인 채)를 감지해 `userCollapsedWhileSelected` 플래그를
  세운다. 이 플래그는 이 슬롯의 카드가 바뀌거나(소비/재배치) 더 이상 backendSelected가 아니게 될 때만
  풀린다 — 그래서 관계없는 이유로 `RefreshAll`이 다시 돌아도(실제 시스템은 여전히 `isSelected`인 카드를
  매번 강제로 `Expanded`로 되돌리는 기존 동작이 있음) 접어둔 카드가 자동으로 다시 안 올라온다.

**검증 (Play Mode, Zone1에 새 프리팹 임시 배치 + 실제 `HudView.ownedRoot`를 `ProxyContainer`로 임시
재지정 — `HudPresenter`/`CardTileView`/실제 카드 데이터는 전혀 안 건드리고 그대로 재사용, 검증 후 Zone1을
디스크에서 재로드해 완전히 원복):**

| 항목 | 결과 |
|---|---|
| 실제 카드 4장 텍스트(제목/카테고리/타입/대상) 표시 | ✅ 4장 전부 실제 `InformationCardData` 데이터로 표시 |
| 카드1 클릭 → 실제 선택 + 상승 | ✅ `SelectedCard` null→Card_C_ADM_02, Y −10→110(Δ120), scale 1→1.03 |
| 카드1 재클릭 → 실제 선택 유지, 시각만 하강 | ✅ `SelectedCard` 불변, Y 110→−10, scale 1.03→1 |
| 무관한 `RefreshAll` 재발생 후에도 안 올라옴 | ✅ 프록시 HandState는 Expanded로 되돌아갔지만(기존 시스템 결함) 보이는 카드는 그대로 하강 유지 |
| 카드2 클릭 → 선택 전환, 카드2만 상승 | ✅ `SelectedCard`→Card_C_REL_01, 카드1 하강 유지·카드2 Y 110/scale 1.03 |
| 입력 잠금 중 클릭 → 선택·상승 모두 없음 | ✅ `PlaybackDirector` 잠금 중 클릭 → `SelectedCard` 불변, 카드 위치 불변 |
| 손패 순서 변경 시 재매핑 | ✅ 프록시 0/1 sibling 순서를 직접 바꿔 확인 — 어느 슬롯이든 실제 선택된 카드 데이터를 따라 텍스트+상승 상태가 이동 |
| 클릭 1회당 실제 선택 요청 1회 | ✅ 코드 경로상 `Button.onClick.Invoke()` 1회 → `CardTileView.Clicked` 1회 → `OnCardClicked` 1회 → (조건 충족 시)`SelectCard`/`CardSelectedEvent` 최대 1회 |
| Console Error/Warning 0 | ✅ |

카드 소비(`Deliver`) 자체는 이번 단계 범위 밖이라 실제로 트리거하지 않고, 대신 프록시 sibling 순서를
직접 바꿔 재매핑 메커니즘만 검증했다(메커니즘은 소비든 재배치든 동일 — 매 프레임 인덱스로 다시 읽는다).

**수정한 파일**: `MockupCardTileAdapter.cs`(신규), `HandCardHudBridge.cs`(신규),
`HandCardSelectionController.cs`(클릭 구독 제거 + public 메서드 3개 추가), `PlayHudCanvas_New.prefab`
(어댑터 4개 + ProxyContainer + 브리지 배선). **`CardTileView.cs`/`HudCanvas.prefab`/`HudPresenter.cs`는
전혀 수정하지 않았다.**

**이번 단계에서 하지 않은 것** (사용자 지시대로): 장소/NPC 선택, Deliver/Spread 실행, 미션 UI,
Profile/Log 연결, Zone2/Zone3/Metropolis 적용.

### 3-8. Zone1 HudCanvas 전체 교체 (2026-08-04) — 목업 기준 최종 확정

사용자가 "손패만 하지 말고 Zone1의 HudCanvas 자체를 목업으로 완전히 바꿔라"로 지시를 바꿨다.
`PlayHudCanvas_New.prefab`에 `HudView`를 새로 만들어 **67개 필드 전부**를 배선하고, Zone1의
`HudCanvas` 인스턴스를 이걸로 완전히 교체했다. `HudPresenter`(씬 오브젝트, 프리팹 아님 — `installer`/
`targeting`/`worldPresenter`/`cardTilePrefab`/`missionConditionRowPrefab`/`npcRelationshipRowPrefab`/
`koreanFont`/`skin`은 이미 씬에 배선돼 있었다)는 **`view` 필드 하나만** 새 인스턴스로 재배선했다 —
`HudPresenter.cs`는 단 한 줄도 수정하지 않았다.

**목업에 1:1로 있는 것**: Header(Turn/Stage 카드), Mission 요약(Header/Body), Log 패널 6종,
Location 정보(제목), NPC 프로필 다수 필드(BeliefStageValue/Note가 마침 실제 `npcBeliefTierText`/
`npcBeliefDialogueText`와 정확히 일치), 손패 카드(지난 세션 완료).

**목업에 없어서 기존 `HudCanvas.prefab`에서 통째로 빌려온 것** (Instantiate로 복제, 위치만 재배치 —
기존 프리팹은 무수정): `HelpButton`, `Overlay`(+Box/Title/Desc/ConfirmButton), `ResultScreen`
(+Panel/PhotoFrame/버튼 2개), `FeedbackBanner`, `Instruction`(+DeliverButton), `BottomPanel`에서
`OwnedInformationRow`만 뺀 나머지(`CardInfo`/`Label`/`NoSelectionHint`) — 이 부분들은 **목업 디자인이
아직 없어서 구형 비주얼 그대로**다. 목업 디자인이 나오면 나중에 다시 교체하면 된다.

**목업 구조를 살짝 보강한 것** (기존 오브젝트는 안 건드리고 옆에 새로 추가만 함):
- `HeaderArea/TurnCard/Texts/StageTurnValue`(신규) — 목업엔 턴 슬롯이 하나뿐이라 미션 턴(`TurnValue`)과
  스테이지 턴을 분리하려고 바로 아래에 작은 텍스트 하나 추가.
- `RightPeekArea/.../ProfileContent/BasicInfoExtra`(신규) — `NameAgeJob` 하나로는 이름과 나이/성별/
  직업/소속을 동시에 못 채워서 아래에 추가.
- `MissionArea/MissionSummaryCard/Texts/TurnsRemaining`, `NextMission`(신규) — 미션 요약 카드엔
  Header/Body 둘뿐이라 "남은 턴"/"다음 미션" 표시용으로 추가.
- `RightPeekArea/.../ProfileContent/RelationshipsRoot`(신규) — 처음엔 `npcRelationshipsRoot`를
  `ProfileContent` 전체로 잡았다가, 동적 관계 행이 다른 정적 필드들 사이에 마구 섞여 들어가는 걸
  발견하고(⚠️ **실측으로 잡은 버그**) 숨겨둔 정적 데모 행 자리에 전용 컨테이너를 새로 만들어 재배선했다.

**목업의 정적 데모를 숨긴 것**(삭제 아님, `SetActive(false)`): `MissionArea/GoalCard01`/`GoalCard02`/
`GoalConnector`(실제 시스템이 `MissionConditionRowView`를 그 자리에 동적으로 N개 쌓는다 — 기존
`MissionConditionRowView.prefab`을 그대로 재사용해서 **비주얼은 목업 GoalCard 아트가 아니라 구형
그대로**다), NPC 관계 정적 1행 데모(`RelationHeaderTarget/Type/Diff` 등 8개), `LocationInfoPaper/Labels`
(실제 데이터가 라벨+값을 한 덩어리 문자열로 채우므로 정적 라벨과 겹쳐 보이는 걸 막기 위함).

**Profile/Log 탭 이중 제어 방지**: `HudView.npcProfileGo`/`logPanelGo`는 **의도적으로 null**로 뒀다.
`RightDocumentPanelController`가 이미 `ProfileContent`/`LogContent`의 active 상태와 슬라이드를 전담하고
있어서, `HudPresenter.SetHudPanelState`가 같은 오브젝트를 또 SetActive하면 애니메이션 타이밍과
충돌한다(둘 다 null 체크가 있어 안전하게 생략 가능함을 코드로 확인). 텍스트 필드(`npcNameText` 등)는
패널이 닫혀 있어도 안전하게 값이 갱신된다(비활성 오브젝트의 텍스트 대입은 에러 없음).

**Play Mode 검증 (Zone1, 실제 스왑 반영, 저장까지 완료):**

| 항목 | 결과 |
|---|---|
| `HudPresenter.Start()` 크래시 없음(67개 필드 전부 요구) | ✅ Console Error/Warning 0 |
| Header(스테이지/미션 턴, 스테이지 번호) 실제 데이터 | ✅ "스테이지 진행 1/8", "1 /4", "STAGE 1" |
| Mission 요약 + 조건 스택 + 남은 턴 + 다음 미션 | ✅ 실제 제목/설명, `MissionConditionsRoot` 자식 6개(동적), "남은 턴: 4", "다음 미션: ???" |
| NPC 클릭(`OnPointerClick` 직접 호출로 시뮬레이션) → 프로필 실제 데이터 | ✅ 이름/기본정보/믿음단계/히스토리 전부 실제 텍스트, 관계 행 전용 컨테이너에 정상 격리 |
| 장소 클릭 → 정보 메모 실제 데이터 | ✅ 확산속도/밀집도/민감정보유형/접근권한/신뢰도보정 전부 실제 값 |
| Profile 탭 클릭 → 슬라이드 + 실제 데이터 동시 정상 | ✅ 패널 (1630,−43)→(1080,−43), `ProfileContent` 활성, 텍스트 그대로 유지 |
| 손패 카드 클릭(지난 세션 기능) 재확인 | ✅ 실제 선택 전환 정상 |
| Console Error/Warning 0(전 과정) | ✅ |

**검증하지 않은 것 (다음에 확인 필요)**: `Deliver`/`Spread` 실제 실행(유효한 장소/NPC 타겟 설정이
선행돼야 해서 이번엔 스킵 — `deliverButtonGo`/`OnDeliverClicked` 자체는 존재/배선 확인만 함),
`Overlay`/`ResultScreen`이 실제로 뜨는 장면(미션 완료/실패 트리거 필요), Log 탭 전환, Zone2/3/Metropolis
(전부 아직 예전 `HudCanvas` 사용 중).

**수정/생성한 파일**: `PlayHudCanvas_New.prefab`(HudView 추가 + 67필드 배선 + 보강 노드 5개 +
`HudCanvas.prefab`에서 6개 서브트리 이식), `Zone1.unity`(HudCanvas 스왑 + `HudPresenter.view` 재배선,
저장 완료). **`HudCanvas.prefab`/`HudPresenter.cs`/`CardTileView.cs`/`HandCardMockupView.cs`/
`RightDocumentPanelController.cs`는 전혀 수정하지 않았다.**

### 3-9. 사용자 피드백 반영 — 디테일 수정 (2026-08-04)

3-8 직후 사용자가 4가지를 지적했다.

1. **"미션 조건도 목업에 이미 있어"** — 맞았다. `MissionArea/GoalCard01/02`(Background+GoalLabel+Title+
   Description)는 데모 placeholder가 아니라 **실제 목업 디자인**이었다. 처음엔 이걸 숨기고 기존
   `MissionConditionRowView.prefab`(구형 비주얼)을 그대로 썼는데, 이건 틀린 판단이었다.
   → `GoalCard01`을 복제해 `MissionConditionRowView_Mockup.prefab`(신규)을 만들고
   (`background`/`goalTag`=GoalLabel/`titleText`=Title/`label`=Description으로 배선),
   `GoalCard01`의 정확한 위치(18,−18)에 전용 `MissionConditionsRoot` 컨테이너를 새로 만들어
   `HudView.missionConditionsRoot`를 재배선. Zone1의 `HudPresenter.missionConditionRowPrefab`
   (씬 레퍼런스, 프리팹 아님)도 이 신규 프리팹으로 교체. **검증 결과 실제 조건 2개가 `Goal_1_UI_수정`/
   `Goal_2_UI_수정` 아트로 정확히 렌더링됨.**
2. **"정보카드쪽 기존 거 없애도 돼"** — `BorrowedBottomPanel`(구형 `CardInfo`/`Label`/`NoSelectionHint` —
   목업 손패 카드가 이미 제목/설명을 직접 보여주므로 중복)에 `CanvasGroup(alpha=0/interactable=false/
   blocksRaycasts=false)` + 화면 밖 배치로 완전히 안 보이게 처리(`SetActive(false)`는 안 씀 — 코드가
   여전히 `SetActive`/텍스트 대입을 하므로 크래시 방지 목적).
3. **"오른쪽 상단 stage/turn 재확인"** — 실측 결과 `TurnCard`(1742,−30)/`StageCard`(1610,−8) 둘 다
   화면 우상단에 정확히 위치, 실제 데이터("스테이지 진행 1/8", "STAGE 1", "성문") 정상 표시 확인.
   `TurnLabel`이 비어 보이는 건 목업 원본부터 그랬다(라벨이 카드 아트에 각인돼 있는 방식) — 버그 아님.
4. **"로그/프로필 디테일, 탭·파일 내용"** — 점검 중 실제 버그 발견: `npcRelationshipsRoot`를
   `ProfileContent` 전체로 잡아서 동적 관계 행이 다른 정적 스탯 필드들 사이에 뒤섞여 들어가고 있었다
   → 숨겨둔 정적 데모 행 자리에 전용 `RelationshipsRoot` 컨테이너를 새로 만들어 해결(검증: 관계 행
   2개가 이제 그 컨테이너에만 깨끗하게 들어감).
   그리고 프로필의 `JudgeTendencyValue`/`PriorityValue`/`RelationTendencyValue`/`TrustJudgeValue`
   (목업엔 "#신중형"/"#명령충실"/"#상명하복"/"#근거중시"로 표시돼 있었음) 4개는 **실제 대응 데이터가
   없다** — `NpcData.trustBias`/`skepticism`, `MajorNpcData.relationships[].strength`는 코드 주석에
   "BeliefSystem 전용, 플레이어 비공개"로 명시돼 있어 플레이어에게 보여주면 안 된다. 지어내지 않고
   텍스트를 비우고 강조 밑줄(`HashHl_*`)도 숨겼다.
   ⚠️ **사용자 확인 필요**: 이 4개 태그에 실제로 보여줄 player-facing 데이터가 있다면(예: NPC JSON의
   `traits.caution`/`curiosity`/`altruism`/`suspicion`/`responsibility` 같은 정성적 필드) 알려주면
   그걸로 채운다. 없으면 이대로 빈 채로 둔다.

**Play Mode 재검증**: 4개 수정 전부 확인, Console Error/Warning 0, Zone1 저장 완료.

### 3-10. 미션 영역 재수정 + 손패 카드 CODE 칸 (2026-08-04, 3-9의 1번을 정정) — ⚠️ 미션 영역 부분은 3-11에서 다시 원복됨

3-9에서 "GoalCard01/02 = 미션 조건 1/2"로 이해하고 구현했는데, 사용자가 정정: **GoalCard01/02는
"미션 1/미션 2"(스테이지 미션 시퀀스)를 나타내는 것이고, 그 옆(공간상 MissionSummaryCard 자리,
x=327 — GoalCard 스택 x=16~346 바로 우측)이 현재 진행 중인 미션의 클리어 조건 2개를 보여주는
곳이다.**

**재배치**:
- `GoalCard01` → `missionTitleText`/`missionDescText` 배선(현재 미션 제목/설명).
- `GoalCard02` → `nextMissionText` 배선(다음 미션 제목, "다음 미션: ???" 형식 그대로).
- `MissionConditionsRoot`(실제 클리어 조건 동적 스택, `Goal_1_UI_수정`/`Goal_2_UI_수정` 아트 재사용)를
  `GoalCard01` 자리에서 `MissionSummaryCard`의 정확한 좌표(327.12,−58.68)로 이동.
- `MissionSummaryCard` 자체 Background/Header/Body/NextMission은 대체됐으므로 비활성화,
  `TurnsRemaining`(남은 턴)만 살려서 조건 스택 위쪽(y=−20)으로 옮김.

⚠️ **95px 카드 간격은 `HudPresenter.cs`의 `GoalCardStackOffsetY` 상수라 코드를 안 건드리는 한 못 바꾼다**
— 조건이 3개 이상이면 스택이 아래로 상당히 길어질 수 있다. 지금 스테이지는 조건 2개라 확인됨.

**Play Mode 검증**: GoalCard01="명령의 근거 흔들기"(제목+설명), GoalCard02="다음 미션: ???",
조건 스택 2개("경비 초소에 경비병이 남지 않음" / "집사가 명령의 근거를 의심함") 정상 렌더링.
Console Error/Warning 0.

**손패 카드 CODE 칸**: `MockupCardTileAdapter.cs`의 `Security` 필드(카드 아트에 "CODE"로 인쇄됨) =
카테고리 한글 번역 + 전달/확산 종류를 합쳐서 표시(사용자 지시: "spread, deliver는 카테고리 적는
공간에 들어간다"). `Chian` 필드는 출처(sourceName)로 재배정(기존엔 kind를 여기 넣었었음, 자리를
비워 재활용). `SpreadPlace`(장소/인물)는 변경 없음.

카테고리 한글 매핑: CRIME=범죄, ADMIN=행정, DISASTER=재난, ECONOMY=경제, MILITARY=군사,
NOBILITY=귀족, POLITICS=정치, PUBLIC=공공, RELIGION=종교, SECURITY=치안.

검증: HandCard1 CODE칸="범죄 · 확산", 출처칸="주점", 대상칸="장소" — 정상.

**⚠️ 시각적으로 직접 확인 못한 부분**: Screen Space - Overlay 캔버스라 카메라/씬뷰 캡처로 실제
렌더링 모습을 스크린샷할 방법이 없다(이 세션 내내 마찬가지). 모든 검증은 RectTransform 좌표 실측 +
텍스트 값 확인으로만 했다 — 조건 스택이 실제로 안 겹치고 예쁘게 보이는지는 사용자가 에디터에서
직접 Play 해서 눈으로 봐야 한다.

### 3-11. 사용자가 스크린샷 제공 — 3-10의 미션 영역 이해가 틀렸음이 확인됨, 3-9로 원복 (2026-08-04)

사용자가 실제 목업 스크린샷을 제공: **GoalCard01/02는 같은 미션의 조건 1/2였다**(제목 "시장의 질서를
흔들어라"를 두 카드가 공유하고, 설명만 조건별로 다름 — 정확히 3-9의 원래 이해가 맞았다). 그리고
"그 옆"(MissionSummaryCard 자리)은 "미션 1/2"가 아니라, **같은 두 조건을 불릿 텍스트로 다시 보여주는
요약 패널**("MISSION (0/1)" 헤더 + "• 조건1" / "• 조건2" 형태)이었다. 3-10에서 GoalCard1/2를
"미션1/미션2"로 재해석한 건 틀렸다 — 되돌렸다.

**최종 구조 (3-9 + 요약 패널 추가)**:
- `MissionConditionsRoot`(동적 조건 스택, `Goal_1/2_UI_수정` 아트)를 `GoalCard01` 자리(18,−18)로 복귀.
- `GoalCard01/02/GoalConnector` 정적 데모는 다시 비활성화(3-9와 동일 이유 — 동적 스택이 같은 아트로 대체).
- `MissionSummaryCard`의 Background/Header/Body를 다시 활성화 — 이번엔 "MISSION" 요약 패널로: Header는
  고정 텍스트 "MISSION", Body는 조건 설명을 불릿(`•`)으로 합친 텍스트.
- **`MissionConditionSummaryAdapter.cs`(신규)**: `HudPresenter`(수정 없음)가 `missionConditionsRoot` 밑에
  실제로 채워 넣은 조건 카드들(`Texts/Description`)을 매 프레임 읽어서 `MissionSummaryCard/Body`에
  불릿 목록으로 재조합한다. `MissionConditionRowView.cs`가 `label`/`titleText`를 public으로 노출하지
  않아서 자식 Transform 경로로 직접 읽는다(읽기 전용, 이 세션 내내 검증에 썼던 것과 같은 방식).
  - ⚠️ `missionConditionsRoot`가 일시적으로 비는 프레임(`RefreshMission`이 `ClearMissionConditionRows`로
    지운 직후, 다시 채우기 전)에 요약을 비워버리면 화면이 깜빡인다 — **`childCount==0`일 때는 마지막
    내용을 유지하고 새로 쓰지 않도록** 방어 로직을 넣었다(디버깅 중 실제로 관측된 문제).
- `missionTitleText`/`missionDescText`/`nextMissionText`는 이 디자인에서 화면에 보여줄 자리가 없어져서
  `BorrowedBottomPanel`(이미 숨김 처리된 영역)의 텍스트로 안전하게 리다이렉트(코드가 계속 값을 대입하니
  null이면 안 됨, 화면엔 안 보임).

**디버깅 중 발견한 실제 버그 (내 코드, 이번에 수정)**: `HandCardHudBridge.LateUpdate()`가
`installer.Turns`가 null일 때 방어하지 않아 `NullReferenceException`이 날 수 있었다(Play Mode를 오래
켜둔 채 스크립트를 여러 번 편집해 도메인 리로드가 반복되면서 실제로 재현됨 — `GameInstaller.Turns`가
`[SerializeField]` 없는 일반 프로퍼티라 도메인 리로드에서 값이 살아남지 못하는 것으로 보임). `installer.Turns
== null` 가드를 추가해 고쳤다. **이건 스크립트를 플레이 중에 편집하는 이번 디버깅 과정에서만 나타난
증상이고, 정상 플레이(스크립트 편집 없이)에서는 재현되지 않을 가능성이 높다** — 그래도 방어 코드는
공짜라 남겨뒀다.

**Play Mode 재검증**: 깨끗하게 재시작 후 확인 — Summary Body = "• 경비대장을 북문에서 벗어나게
해야한다.\n• 상인을 북문으로 이동시켜야한다."(스크린샷과 정확히 일치), 손패 카드 클릭도 정상,
Console Error/Warning 0. Zone1 저장 완료.

**⚠️ "MISSION (0/1)" 형식의 숫자 부분은 구현하지 않았다** — 조건 충족 여부(met)를 이 어댑터가
외부에서 읽을 방법이 없다(`MissionConditionRowView`가 그 값을 노출하지 않고, 배지 유무도 skin
자산에 좌우돼 텍스트에 항상 드러나지 않는다). 지금은 Header에 고정 "MISSION" 텍스트만 표시한다.
필요하면 다음 세션에서 `MissionConditionRowView.cs`에 `met` 상태를 공개하는 작은 getter를
추가하는 방식으로 풀 수 있다(이번엔 시간상 보류).

### 3-12. 손패 카드 3칸 헤더 정확한 의미 확정 (2026-08-04)

실측 좌표로 확인: `Title` 위에 `Security`/`SpreadPlace`/`Chian` 3개가 같은 가로줄에 나란히 배치돼
있다(좌: Security x=30, 중: SpreadPlace x=186, 우: Chian x=366). 목업 원본 placeholder 텍스트
("SECURITY", "[SPREAD / PLACE]", "치안")도 이 해석과 일치한다. 사용자가 최종 확정한 의미:

| 필드(오브젝트명) | 위치 | 의미 | 표시값 |
|---|---|---|---|
| `Security` | 좌 (CODE) | 카테고리 | `CategoryKorean(categoryId)` 단독 (예: "정치") |
| `SpreadPlace` | 중 (TYPE/TARGET) | 전달·확산 종류 + 장소·인물 대상 | `"{kind} / {targetLabel}"` (예: "확산 / 장소") |
| `Chian` | 우 | 정보 출처 | `sourceName` (예: "순찰대") |

3-10/3-11 사이에 있었던 "Security에 카테고리+종류를 합친다"는 임시 판단은 틀렸다 — 종류(kind)는
SpreadPlace 쪽으로, Security는 카테고리 단독으로 정정했다(`MockupCardTileAdapter.BindTexts()`).

**Play Mode 검증**: 4장 전부 서로 다른 실제 데이터 확인 —
예) HandCard1: CODE="정치" TYPE/TARGET="확산 / 장소" SOURCE="순찰대".
Console Error/Warning 0. Zone1 저장 완료.

**참고(디버깅 노이즈)**: 스크립트 편집 직후 Play Mode에 바로 들어가면 첫 몇 프레임 동안 "missing
script" 경고가 일시적으로 뜨거나, 어댑터가 아직 한 프레임도 안 돈 상태라 목업 원본 placeholder
텍스트가 그대로 보일 수 있다 — 둘 다 Edit Mode 재확인 결과 실제 손상은 아니었고, Play Mode를
한 번 더 들어가면 사라진다.

### 3-13. 카테고리/출처 위치 맞바꿈 + TYPE·TARGET 줄바꿈 분리 (2026-08-04)

사용자 지시로 3칸 헤더를 다시 조정:
- `Security`(좌) ↔ `Chian`(우) — 표시 내용을 서로 맞바꿈: `Security`=출처, `Chian`=카테고리(오브젝트
  자체는 옮기지 않고 어느 텍스트를 넣을지만 바꿔서, 결과적으로 위치가 바뀐 것과 동일한 효과).
- `SpreadPlace`(중) — "확산 / 장소"처럼 한 줄로 합치던 걸 `"{kind}\n{targetLabel}"`로 줄바꿈해
  타입과 타겟이 각각 위/아래 줄에 표시되게 함.

검증: HandCard1 = Security(출처)="행정기관", SpreadPlace="확산\n장소"(2줄), Chian(카테고리)="행정".
4장 전부 정상. Console Error/Warning 0. Zone1 저장 완료.

### 3-14. TYPE·TARGET을 줄바꿈이 아니라 라벨+값 구조로 분리 (2026-08-04, 3-13 보완)

3-13의 "한 텍스트에 줄바꿈"은 사용자 의도와 달랐다 — "TYPE" 라벨 아래엔 전달/확산 값만, "TARGET"
라벨 아래엔 장소/인물 값만 각각 뜨는 구조를 원한 것. `SpreadPlace`(170×24, 하위 오브젝트 없음)
하나로는 라벨 2개+값 2개를 담을 수 없어서, 4장 카드 전부에 새 오브젝트 4개를 추가했다:

- `TypeLabel`("TYPE" 고정) / `TypeValue`(전달·확산 값) — 왼쪽 반(x=186, 폭 85)
- `TargetLabel`("TARGET" 고정) / `TargetValue`(장소·인물 값) — 오른쪽 반(x=271, 폭 85)
- 기존 `SpreadPlace`는 `SetActive(false)`로 비활성화(삭제 아님).

`MockupCardTileAdapter.cs`에서 `spreadPlaceText` 필드를 `typeValueText`/`targetValueText` 2개로
교체. 폰트/색상은 원래 SpreadPlace를 복제해 그대로 물려받음(라벨 7pt, 값 11pt).

검증: 4장 전부 TYPE 아래 확산/전달, TARGET 아래 장소/인물 정상 표시. Console Error/Warning 0.
Zone1 저장 완료.

⚠️ 라벨/값 박스 크기(85×10, 85×16)는 실측 없이 임의로 잡은 값이다 — 실제로 겹치거나 잘려 보이면
알려주면 조정한다.

### 3-15. TYPE/TARGET 라벨 제거, 값만 영문으로 (2026-08-04, 3-14 보완)

사용자 지시: 라벨("TYPE"/"TARGET")은 필요 없고 값만 표시, 값은 영문으로.
- `TypeLabel`/`TargetLabel`은 `SetActive(false)`로 숨김(삭제 아님).
- `TypeValue`/`TargetValue`를 원래 `SpreadPlace` 자리(85×24, 폰트 10)로 재배치해 라벨 없이도
  자연스럽게 한 줄로 보이게 함.
- `MockupCardTileAdapter.BindTexts()`의 값을 한글(확산/전달, 장소/인물) → 영문(SPREAD/DELIVER,
  PLACE/PEOPLE)으로 변경. 카테고리(Chian)는 한글 유지(별도 지시 없었음).

검증: 4장 전부 라벨 비활성 확인, TYPE·TARGET 영문 값 정상. Console Error/Warning 0. Zone1 저장 완료.

### 3-16. 손패 카드 Description 줄바꿈 (2026-08-04)

`Description`이 `enableWordWrapping=False` + `overflowMode=Overflow`였다 — 실제로 내용이 길면
박스(380×46) 밖으로 삐져나갈 수 있는 상태였음(사용자가 관찰한 그대로, 실제 버그). 4장 카드 전부
`enableWordWrapping=true` + `overflowMode=Truncate`로 변경.

검증: 실제 카드들(약 20자 내외)은 여유 있게 안에 들어감(preferredHeight 17.5 vs 박스 46).
일부러 훨씬 긴 텍스트로 스트레스 테스트한 결과 - 가로는 줄바꿈으로 박스 폭(380) 안에 정확히 들어가고
(렌더 폭 371.6), 세로로 박스보다 커지는 경우(preferredHeight 53.5 > 46)에는 밖으로 삐져나가지 않고
Truncate로 잘림. Console Error/Warning 0. Zone1 저장 완료.

### 3-17. Stage/Turn 헤더 — 실제 구역명 + 미션 턴만 표시 (2026-08-04)

**발견한 데이터 문제**: `HudPresenter.RefreshHeader()`가 `stageNameText`에 `installer.StageAsset.stageName`을
넣는데, 이 필드가 **현재 스테이지 데이터에서 비어 있다**(코드 버그 아님, 데이터 공백). 사용자가 원한
"북문"/"상업지구" 같은 이름은 실제로 `StageAsset.regionName`에 들어있다(Zone1 실측: "북문(외곽)").

**해결**: `HudPresenter.cs`는 안 건드리고(고칠 수도 없음 - `stageNameText.text` 대입에 null 체크가
없어서 필드 자체를 없앨 수도 없다), 새 어댑터로 우회했다.
- **`StageRegionNameAdapter.cs`(신규, `StageCard`에 부착)**: `GameInstaller.StageAsset.regionName`을
  매 프레임 읽어 같은 `StageName` 텍스트 오브젝트에 직접 써넣는다.
- `HudView.stageNameText`는 이 화면에서 **비워둠**(null-safe, `RefreshHeader`가 그냥 건너뜀) —
  같은 텍스트 오브젝트를 어댑터가 대신 채우므로 이중 대입 충돌 없음.
- Turn 카드는 미션 턴("1 /4" 형식, 기존 `missionTurnText` 그대로)만 남기고, 3-8에서 추가했던
  `StageTurnValue`(스테이지 턴 진행 중복 표시)는 `SetActive(false)`로 숨김(삭제 아님).

검증: `StageName`="북문(외곽)"(실제 데이터), `MissionTurnText`="1 /4", `StageTurnValue` 비활성 확인.
Console Error/Warning 0. Zone1 저장 완료.

### 3-18. MissionArea 목업 원본과 정밀 대조 (2026-08-04)

사용자가 "좌측 상단 배치·색상이 목업과 다르다"고 지적 — `UI_PlayHudMockup.unity` 원본과
`PlayHudCanvas_New.prefab`의 `MissionArea`를 위치/색상/스프라이트까지 전부 실측 대조했다.
Background 스프라이트/색상, GoalCard01/02의 Title·Description 폰트·색상은 전부 원본과 일치했지만
**실제 버그 3개**를 발견:

1. **`MissionConditionsRoot`가 중복 생성돼 있었다**(같은 이름 오브젝트 2개, `MissionArea` 밑에).
   `HudView.missionConditionsRoot`가 참조하는 것만 남기고 나머지 삭제.
2. **Header가 "MISSION"이라는 잘못된 텍스트를 표시하고 있었다.** 원본 목업의 Header는 원래
   `"(0/1)"` 형식(충족/전체 조건 수)이었고, "MISSION"이라는 단어 자체는 배경 아트("미션 UI"
   스프라이트)에 이미 인쇄돼 있어 텍스트로 또 넣을 필요가 없었다. met 개수를 셀 방법이 아직 없어
   (3-11 참고) 일단 빈 텍스트로 정리 — 잘못된 값을 보여주는 것보다 낫다.
3. **`TurnsRemaining`이 `Body`(불릿 목록)와 겹치는 위치**(y=−20 vs Body가 차지하는 y=−28~−78)에
   있었다 — 3-10에서 옮긴 위치가 3-11 원복 때 같이 안 옮겨진 채 남아있었다. 원본 목업엔 이 자리
   자체가 없다(내가 임의로 추가한 것) — 지금은 숨겨서 겹침을 없앴다.

프리팹 전체를 대상으로 "같은 부모 밑 동일 이름 오브젝트" 스캔도 돌려봤고, 위 1건 외 다른 중복은
없음을 확인했다.

**Play Mode 검증**: `MissionArea` 자식 5개(중복 없음), Header 빈 문자열, Body="• 경비대장을...\n•
상인을...", `TurnsRemaining` 비활성, `MissionConditionsRoot` 자식 2개(실제 조건). Console
Error/Warning 0. Zone1 저장 완료.

⚠️ Header의 "(0/1)"류 카운터는 여전히 미구현 상태다(met 여부를 노출하는 작은 API가
`MissionConditionRowView.cs`에 필요 — 3-11에서 이미 남긴 노트와 동일).

### 3-19. GOAL 1/2 카드 사이 연결 이미지 누락분 추가 (2026-08-04, 3-18 보완)

사용자가 두 번째 스크린샷으로 지적: 목업 원본은 GOAL 1/GOAL 2 카드 사이에 살짝 떨어진 틈과 함께
**연결/클립 장식 이미지**가 있는데, 지금 동적으로 쌓이는 조건 카드 사이엔 이게 빠져 있었다.

**원인**: `GoalConnector`(원본 목업의 그 장식 오브젝트)는 3-9/3-11에서 `GoalCard01`/`GoalCard02`와
함께 정적 데모로 통째로 `SetActive(false)` 처리됐고, 동적 `MissionConditionsRoot` 스택에는 대응하는
오브젝트를 만들지 않았다 — 순수 누락.

**해결**:
- `GoalConnector`(숨김 상태)를 복제해 `MissionConditionsRoot` 밑에 `Connector`라는 새 자식으로 배치.
  좌표는 "원본 목업에서 `GoalConnector` 절대 위치(33.86,−114.03) − `GoalCard01` 절대 위치(18,−18)"로
  계산한 상대 오프셋 `(15.86, −96.03)`을 사용 — `MissionConditionsRoot` 자체가 정확히 `GoalCard01`의
  원래 자리에 있으므로, 이 상대 좌표를 쓰면 `Connector`가 원본 목업의 절대 위치와 정확히 일치한다.
- `MissionConditionSummaryAdapter.cs`에 `connectorGo` 필드를 추가, 매 프레임 "실제 조건 카드가
  2개 이상일 때만" 보이도록 함(원본 목업도 GoalCard01/02 두 장 사이에만 있었다 — 조건이 1개뿐이면
  숨긴다). `Connector` 자신도 `conditionsRoot`의 자식이라 `childCount`에 항상 +1 섞여 들어가므로,
  그만큼 빼고 셈(`realConditionCount = conditionsRoot.childCount - (connectorGo가 conditionsRoot의
  자식이면 1, 아니면 0)`) — 처음엔 이 -1 보정을 빼먹어 조건 1개일 때도 Connector가 계속 보이는
  자체 버그가 있었으나, 최종 커밋 전에 확인해 고쳤다.

**Play Mode 검증**: `MissionConditionsRoot.childCount=3`(실제 조건 카드 2개 + Connector 1개),
`Connector active=True`, `pos=(15.86,-96.03)`, `sprite="Goal UI 연결 이미지"`(원본 목업과 동일 아트).
Console Error/Warning 0. Zone1 저장 완료.

⚠️ **알려진 한계(스크린샷으로 재확인 필요)**: `HudPresenter.cs`의 조건 카드 스택 간격은
`GoalCardStackOffsetY = 95f`로 하드코딩돼 있는데(코드 수정 불가), 원본 목업 정적 레이아웃에서
`GoalCard01`의 실제 높이는 약 115px, `GoalCard02`는 약 100px로 서로 다르다 — 즉 동적 스택의 실제
카드 간격(95px)이 원본 정적 배치가 암시하는 간격과 정확히 같지 않을 수 있다. `Connector`의 좌표는
"원본 정적 배치 기준 절대 위치"로 맞췄기 때문에, 동적 스택이 실제로 쌓인 카드 경계와 픽셀 단위로
정확히 맞아떨어지는지는 이 세션에서 스크린샷으로 확인할 방법이 없었다(Screen Space - Overlay라
카메라/씬뷰 캡처 불가 — 세션 내내 동일한 제약). 에디터에서 직접 Play 해서 눈으로 확인 필요.

**수정한 파일**: `MissionConditionSummaryAdapter.cs`(connectorGo 필드 + 가시성 로직 추가),
`PlayHudCanvas_New.prefab`(`Connector` 신규 오브젝트 추가 + 어댑터 배선). `HudPresenter.cs`는
전혀 수정하지 않았다.

**추가 참고**: 3-19 직후 사용자가 GOAL 카드 간격(위 한계 항목)을 직접 수동으로 조정했다
("내가 고쳣어 알아서" — 코드/프리팹 재확인 결과 `GoalCardStackOffsetY`는 여전히 95f, 행 프리팹
높이도 여전히 115였음 — 즉 에디터 상에서 시각적으로 충분히 만족스럽다고 판단해 더 이상 손대지
않기로 한 것으로 보임). 이 항목은 사용자 지시로 더 이상 건드리지 않는다.

### 3-20. GOAL 카드 스택과 불릿 요약 패널 중 "겹쳐 보이는" 쪽을 정정 — 최종적으로 GOAL 스택을 숨김 (2026-08-04)

사용자가 스크린샷으로 지적: GOAL 1/GOAL 2 카드 위에 반투명한 "MISSION" 불릿 요약 박스
(`MissionSummaryCard` — 3-11에서 추가한 조건 설명 재요약 패널)가 겹쳐서 떠 있었다.

**1차 시도(틀림)**: "GOAL 카드가 진짜, 불릿 박스가 중복"이라고 판단해 `MissionSummaryCard`를
비활성화했다. → 사용자가 즉시 정정: **정반대**였다 — 사용자가 원한 건 `MissionSummaryCard`(불릿
요약 패널)를 보여주는 것이고, 겹쳐서 없애야 할 "예전 것"은 오히려 **GOAL 1/GOAL 2 카드 스택
(`MissionConditionsRoot`)** 쪽이었다.

**최종 조치**: `PlayHudCanvas_New.prefab`과 Zone1 라이브 씬 인스턴스 양쪽에서
- `MissionArea/MissionSummaryCard` → `SetActive(true)`(원복)
- `MissionArea/MissionConditionsRoot` → `SetActive(false)`(신규 — GOAL 카드 스택 숨김)

`MissionConditionSummaryAdapter`(`MissionArea`에 부착, 계속 활성 상태)는 `conditionsRoot`
(`MissionConditionsRoot`)의 자식들을 Transform 경로로 직접 읽는데, 이건 대상이 비활성이어도
안전하게 동작한다(Transform 계층 탐색은 활성 여부와 무관) — 그래서 GOAL 스택은 화면에 안 보여도
`HudPresenter`가 뒤에서 계속 조건 카드를 채워 넣고, 그 내용을 요약 패널이 그대로 읽어 불릿
텍스트로 보여준다. `connectorGo`(GOAL 카드 사이 연결 이미지, 3-19)도 `MissionConditionsRoot`의
자식이라 자동으로 함께 숨겨진다(문제 없음 — 어차피 스택 전체가 안 보이므로).

**Play Mode 검증**: `MissionSummaryCard active=True`, `MissionConditionsRoot active=False`
(자식 3개는 계속 백그라운드에서 생성됨 — 실제 조건 카드 2개 + Connector 1개), `Body` 텍스트
= "• 경비대장을 북문에서 벗어나게 해야한다.\n• 상인을 북문으로 이동시켜야한다."(실제 데이터,
정상). Console Error/Warning 0. Zone1 저장 완료.

**수정한 파일**: `PlayHudCanvas_New.prefab`, `Zone1.unity`(`MissionSummaryCard`/
`MissionConditionsRoot` 활성 상태 스왑, 저장 완료). 스크립트는 수정하지 않았다.

### 3-21. 손패 카드 Description이 흰색 박스 바깥으로 튀어나가는 실제 버그 발견 및 수정 (2026-08-04)

사용자가 스크린샷으로 지적: "행정기관의 인력 부족으로 민원 처리가 지연되고 있다는 소문이 돈다."
같은 설명 텍스트가 카드의 흰색 내용 박스 바깥(오른쪽 테두리 너머)으로 튀어나가 있었다.
(처음엔 같은 스크린샷의 하단 태그 칩 3개(ADMIN/DELAY/STAFFING)가 문제인 줄 알고 카드 배경
스프라이트의 실제 흰색 영역을 픽셀 단위로 분석했으나, 사용자가 재차 정정 — 진짜 문제는
Description 텍스트였다.)

**원인 (실측으로 확인)**: 카드 배경 스프라이트(`정보카드 UI.png`, 텍스처 402×211, `Image.type=Simple`)의
텍스처를 임시로 `isReadable=true`로 바꿔 픽셀을 직접 샘플링한 결과, 실제로 "흰색으로 칠해진"
내용 영역은 텍스처 기준 x≈30~350(카드 좌우 여백 제외 실사용 폭)이고, 460×230으로 표시될 때
디스플레이 좌표로는 x≈40~400.5(폭 ≈360.5)이다. 반면 `Description` 텍스트박스는
`anchoredPosition.x=40, width=380` → 오른쪽 끝이 x=420까지 뻗어 있어, **실제 흰색 영역(≈400.5)보다
약 20px 더 넓었다**. `enableWordWrapping=true`는 이미 3-16에서 설정됐고 정상 작동 중이었지만,
텍스트의 `preferredWidth`가 흰 영역 실제 폭(360.5)보다는 넓고 박스 설정 폭(380)보다는 좁은
경우(예: 372px) TMP 입장에서는 "박스 안에 잘 들어간다"고 판단해 줄바꿈을 하지 않는데, 그 박스
자체가 실제 흰색 그림 영역보다 넓게 잡혀 있었으므로 화면상으로는 흰 배경을 벗어나 카드의
탠(베이지)색 테두리 위로 글자가 얹히는 것처럼 보였다 — 즉 TMP 설정 버그가 아니라 **박스 크기가
실제 아트워크보다 20px 과대 설정된 것**이 원인이었다.

**해결**: 4장의 손패 카드(`HandCard1~4`) 전부의 `Texts/Description` RectTransform
`sizeDelta`를 `(380, 46)` → `(355, 46)`으로 축소(왼쪽 x=40은 유지, 폭만 축소해 오른쪽 끝이
x=395로 들어와 실제 흰 영역 안에 5px 여유를 두고 들어오게 함). `enableWordWrapping`/
`overflowMode` 등 TMP 설정 자체는 3-16에서 이미 올바르게 돼 있었으므로 변경하지 않았다.

**검증**: 실제로 문제였던 문장으로 재현 테스트 — 폭 축소 전: `preferredWidth=372.1`,
`lineCount=1`(박스 안에 들어간다고 판단해 줄바꿈 없이 렌더링 → 실제 흰 영역 밖으로 넘침).
폭 축소 후: 같은 문장이 `lineCount=2`(줄1 폭 339.0 / 줄2 폭 26.7)로 정상 줄바꿈, 두 줄 다
355 박스 안, 즉 실제 흰 영역(360.5) 안에도 안전하게 들어간다. Play Mode 재확인 결과
Console Error/Warning 0. Zone1 저장 완료(`isDirty=False`로 재확인).

**수정한 파일**: `PlayHudCanvas_New.prefab`(4장 `Description` sizeDelta 변경),
`Zone1.unity`(동일, 저장 완료). 스크립트는 수정하지 않았다.

⚠️ **참고**: 이번에 픽셀 샘플링을 위해 `정보카드 UI.png`의 Texture Import 설정
(`isReadable`)을 일시적으로 `true`로 바꿨다가 측정 직후 원래 값으로 되돌렸다(재임포트 2회,
최종 상태는 원본과 동일). 다른 부수 효과 없음.

### 3-22. 좌측상단 GOAL 1/2(정적 카드)에 실제 미션 조건 데이터 연결 — 방식 확정 (2026-08-04)

3-20에서 동적 조건 스택(`MissionConditionsRoot`)을 숨기고 옆 요약 패널(`MissionSummaryCard`)만
보이게 했었는데, 사용자가 재차 확인: **정적 원본 `GoalCard01`/`GoalCard02`(계속 비활성 상태였던,
간격 버그가 없는 고정 위치 카드 2장)에 실제 조건 데이터를 직접 연결하는 쪽으로 최종 결정**했다.
이 스테이지가 조건 2개로 고정이라 정적 카드 2장이 자연스럽고, 위치가 고정이라 3-19에서 다뤘던
95px/115px 간격 불일치 문제 자체가 발생하지 않는다.

**신규 스크립트 `GoalCardConditionAdapter.cs`** (`MissionArea`에 부착, 이 프로젝트 전반의
"숨은 프록시 읽기" 패턴과 동일): `HudPresenter`(수정 없음)는 지금처럼 계속 숨겨진
`MissionConditionsRoot` 밑에 `MissionConditionRowView_Mockup` 클론을 그대로 생성/갱신하고,
이 어댑터가 매 프레임 그 클론들의 `Texts/Title`·`Texts/Description` 값을 읽어 `GoalCard01`/
`GoalCard02`의 같은 이름 텍스트에 그대로 복사한다.
- 클론은 `conditionsRoot.GetChild(i)`를 `childCount-1`부터 `0`까지 역순으로 읽어(기존
  `MissionConditionSummaryAdapter`와 동일한 순서 규칙 재사용 — `AddMissionConditionRow`가 매번
  `SetSiblingIndex(0)`으로 새 카드를 앞에 끼워 넣으므로, 가장 오래된(슬롯 0) 카드가 가장 높은
  sibling index에 남는다) 처음 2개(조건 1·2)만 사용한다. `Texts/Description`이 없는 자식
  (`Connector`)은 자동으로 건너뛴다.
- 조건이 1개뿐이면 `GoalCard02`/`GoalConnector`를 비활성화, 2개 이상이면 앞의 2개만 카드에
  반영하고 `GoalConnector`도 함께 활성화한다(3개 이상은 이 레이아웃에 자리가 없어 표시 안 함 —
  알려진 한계, 3-19에서 이미 문서화한 것과 동일 성격).
- `conditionsRoot.childCount==0`인 프레임(미션 전환 중 clear~rebuild 사이)은 마지막 상태를
  유지한다(기존 어댑터들과 동일한 안전장치).

**요약 패널과의 중복 처리**: GOAL 1/2 카드가 이제 실제 데이터를 직접 보여주므로,
`MissionSummaryCard`(3-20에서 살렸던 불릿 요약 패널)는 다시 비활성화했다 — 사용자 확인:
"미션마다 클리어조건이 있으니 그걸(GOAL 카드에) 연결하면 된다"는 지시에 따라 중복 표시를
피하기 위함. `MissionConditionSummaryAdapter` 컴포넌트 자체는 그대로 남아 있지만(제거하지 않음,
`MissionArea`에 부착된 채 계속 동작) 대상 오브젝트가 비활성이라 실질적으로 화면에 영향 없다.

**Play Mode 검증**: `GoalCard01 active=True title='명령의 근거 흔들기' desc='집사가 명령의
근거를 의심함'`, `GoalCard02 active=True title='명령의 근거 흔들기' desc='경비 초소에 경비병이
남지 않음'`, `GoalConnector active=True`(조건 2개라 정상 표시), `MissionSummaryCard
active=False`. Console Error/Warning 0. Zone1 저장 완료(`isDirty=False` 재확인).

**수정/생성한 파일**: `GoalCardConditionAdapter.cs`(신규), `PlayHudCanvas_New.prefab`
(`MissionArea`에 어댑터 부착·배선, `MissionSummaryCard` 비활성화), `Zone1.unity`(프리팹
재인스턴스화 + `HudPresenter.view` 재배선 + `MissionSummaryCard` 비활성화, 저장 완료).
`HudPresenter.cs`/`MissionConditionRowView.cs`는 전혀 수정하지 않았다.

### 3-23. GOAL 1/2 = 미션 제목+설명, 옆 요약 패널 = 클리어조건 2개로 최종 역할 분담 (2026-08-04)

3-22에서 GOAL 1/2에 조건별(condition-level) 텍스트를 연결했었는데, 사용자가 역할을 다시 정리:
**GOAL 1/2에는 미션 제목 + 미션 설명(objectiveText, 조건별이 아니라 미션 전체 설명 1개)을 쓰고,
옆의 요약 패널(`MissionSummaryCard`)에는 클리어조건 2개를 연결**하는 것으로 최종 확정했다.

**조치**:
1. `HudView.missionTitleText`/`missionDescText`를 그동안 안 보이던 `BorrowedBottomPanel/CardTitle`
   /`CardDesc`(숨김 싱크)에서 **`GoalCard01/Texts/Title`/`Texts/Description`로 재배선** — 이제
   `HudPresenter.RefreshMission()`이 채우는 실제 미션 제목/설명이 GoalCard01에 직접, 그대로
   보인다(어댑터 없이 진짜 배선).
2. `GoalCardConditionAdapter.cs`를 완전히 재작성 — 3-22의 "숨은 조건 스택 읽기" 로직을 버리고,
   **GoalCard01의 Title/Description을 그대로 GoalCard02에 미러링**만 하는 단순한 어댑터로
   교체(목업 원본이 같은 미션 내용을 카드 2장에 겹쳐 보여주는 디자인이므로 — 조건별 분기가
   필요 없어짐).
3. `GoalCard01`/`GoalCard02`/`GoalConnector`를 조건 개수와 무관하게 **항상 활성 상태**로 고정
   (이전엔 조건 2개 이상일 때만 켰었지만, 이제 카드 내용이 조건이 아니라 미션 자체라 항상
   2장 모두 보여야 자연스럽다).
4. `MissionSummaryCard`를 다시 활성화 — 기존 `MissionConditionSummaryAdapter`(수정 없음, 3-11에서
   만든 것)가 여전히 숨겨진 `MissionConditionsRoot`의 실제 조건 카드들을 읽어 불릿 텍스트로
   보여주므로 그대로 재사용된다.

**Play Mode 검증**: `GoalCard01`/`GoalCard02` 둘 다 `title='명령의 근거 흔들기'
desc='경비 강화 명령의 정당성을 흔들어, 경비대장이 명령을 무조건 신뢰하지 않도록 만든다.'`로
동일하게 표시(실측 결과 `Mission_Stage01_01.asset`의 `displayTitle`/`objectiveText`와 정확히
일치). `MissionSummaryCard active=True`, `Body`에 실제 조건 2개가 불릿으로 정상 표시. Console
Error/Warning 0. Zone1 저장 완료(`isDirty=False` 재확인).

⚠️ **참고(이번 작업과 무관한 기존 상태)**: 검증 중 Summary 패널의 불릿 조건 텍스트("경비대장을
북문에서 벗어나게 해야한다." 등)가 `Mission_Stage01_01.asset`에서 직접 읽은 조건 텍스트("집사가
명령의 근거를 의심함" 등)와 다르게 나타났다 — 제목/설명은 정확히 일치하는데 조건 텍스트만
다른 것으로 보아, 현재 Zone1 Play 세션이 참조하는 실제 활성 미션 인스턴스가 이 세션 내내
반복 검증에 써온 어떤 특정 진행 상태(혹은 다른 조건 소스)일 가능성이 있다 — UI 배선 자체의
문제가 아니라(제목/설명은 정확히 일치하므로 배선은 검증됨), 게임 진행/세이브 상태 쪽 별개
사안으로 보이며 이번 작업 범위 밖이라 더 조사하지 않았다.

**수정한 파일**: `GoalCardConditionAdapter.cs`(전면 재작성 — 조건 읽기 → 카드1→카드2 미러링),
`PlayHudCanvas_New.prefab`(`missionTitleText`/`missionDescText` 재배선, 어댑터 필드 갱신,
GoalCard01/02/Connector/MissionSummaryCard 활성화), `Zone1.unity`(동일 반영, 저장 완료).
`HudPresenter.cs`는 전혀 수정하지 않았다.

### 3-24. 3-23의 "조건 텍스트 불일치" 원인 확인 및 수정 — `MissionConditionSummaryAdapter.conditionsRoot`가 null이었다 (2026-08-04)

3-23에서 남겼던 의문(요약 패널 조건 텍스트가 실제 `Mission_Stage01_01` 데이터와 다르게 표시됨)의
원인을 추적했다.

**진단 과정**:
1. `HudPresenter.RefreshMission()`의 데이터 출처(`ProgressionController.CurrentObjective()`)를
   Play Mode에서 직접 덤프 — `displayTitle`/`objectiveText`/`successConditions`(2개, `displayLabel`
   포함) 전부 `Mission_Stage01_01.asset` 원본과 **정확히 일치**함을 확인. 데이터 레이어는
   완전히 정상.
2. 실제 씬의 `MissionConditionsRoot` 밑에 `HudPresenter`가 그 프레임에 생성한 조건 카드 2개를
   직접 열어보니 **`Texts/Description`에 이미 올바른 실제 텍스트**("집사가 명령의 근거를
   의심함" / "경비 초소에 경비병이 남지 않음")가 들어 있었다 — 즉 동적 조건 카드 자체는 정상.
3. 그런데 `MissionSummaryCard/Texts/Body`만 계속 다른(예전) 텍스트를 보여줬다 — 데이터도
   맞고 조건 카드도 맞는데 요약 패널만 틀렸으므로, 문제는 `MissionConditionSummaryAdapter`
   자신에게 있다고 좁혀졌다.
4. `SerializedObject`로 이 어댑터의 직렬화된 필드를 직접 열어본 결과 **`conditionsRoot`가
   `NULL`**이었다(`summaryText`/`connectorGo`는 정상 배선). 코드 첫 줄이
   `if (conditionsRoot == null || summaryText == null) return;`이므로, null이 된 시점부터
   이 어댑터는 매 프레임 아무 일도 안 하고 있었고, `Body` 텍스트는 마지막으로 정상 갱신됐던
   시점의 값이 그대로 얼어붙어 있었던 것 — 여러 세션에 걸쳐 이 값을 "현재 데이터"로 착각하고
   검증해온 것이었다.

**근본 원인 추정**: 3-18에서 "`MissionConditionsRoot`가 중복 생성돼 있었다"는 걸 발견하고
`HudView.missionConditionsRoot`가 참조하는 것만 남긴 채 나머지 중복 오브젝트를
`DestroyImmediate`했는데, 이때 `MissionConditionSummaryAdapter.conditionsRoot`가 하필
**삭제된 쪽 중복 오브젝트**를 참조하고 있었던 것으로 보인다 — Unity는 참조 대상이 파괴되면
그 직렬화 참조를 자동으로 null로 되돌리므로, 그 이후 이 어댑터는 조용히 멈췄고 별도 에러도
나지 않았다(3-18 당시엔 `HudView` 쪽 참조만 확인했고 이 어댑터의 참조는 따로 점검하지 않았다).

**수정**: `PlayHudCanvas_New.prefab`과 Zone1 라이브 씬 양쪽에서
`MissionConditionSummaryAdapter.conditionsRoot`를 현재 실제 사용 중인
`MissionArea/MissionConditionsRoot`로 재배선.

**Play Mode 검증**: 수정 전 `conditionsRoot -> NULL` 확인(프리팹에서도 동일하게 null이었음
— 이전 세션의 실수가 프리팹 자체에 저장돼 있었다는 뜻). 수정 후 `Summary Body='• 집사가
명령의 근거를 의심함\n• 경비 초소에 경비병이 남지 않음\n'`으로 **`Mission_Stage01_01`의 실제
조건과 정확히 일치**. `GoalCard01`/`GoalCard02`도 계속 정상(제목/설명 동일 미러링). Console
Error/Warning 0. Zone1 저장 완료(`isDirty=False` 재확인).

**수정한 파일**: `PlayHudCanvas_New.prefab`(`MissionConditionSummaryAdapter.conditionsRoot`
재배선), `Zone1.unity`(동일 반영, 저장 완료). 스크립트는 수정하지 않았다(버그가 코드가 아니라
직렬화된 참조 데이터에 있었다).

### 3-25. GOAL 2 = 다음 미션 미리보기로 변경 + 카드 텍스트 오버플로우 방지 + 미션1 완료 시 자동 전환 검증 (2026-08-04)

사용자 요청 3가지: (1) GOAL2에 "미션·내용"을 연결(3-23까지는 GoalCard01을 그대로 미러링해
같은 내용을 보여줬는데, 이번엔 GOAL2가 **다음 미션**을 보여주길 원함), (2) 클리어조건(요약
패널)은 미션1을 클리어하면 자동으로 바뀌도록 "설계만" — 새 로직을 만들라는 게 아니라 이미 그렇게
동작하는지 확인/보장, (3) GOAL 카드 안 텍스트가 카드 바깥으로 삐져나오는 문제를 줄바꿈으로 해결
(전부 보이게 — 잘림은 안 됨).

**(1) GOAL2 = 다음 미션 미리보기**: `GoalCardConditionAdapter.cs`를 다시 전면 재작성 —
GoalCard01→GoalCard02 미러링 로직을 버리고, `GameInstaller.StageAsset.missions` 배열에서
`ProgressionController.CurrentObjective()`(현재 미션)의 배열 인덱스 바로 다음 항목을 찾아 그
`MissionData.displayTitle`/`objectiveText`를 GoalCard02에 직접 바인딩한다. `HudView`/
`HudPresenter`에는 다음 미션의 설명까지 노출하는 필드가 아예 없어서(`nextMissionText`는 제목
문자열 하나뿐, 그나마 `isHiddenUntilUnlocked`면 "???") 이 어댑터가 데이터 자산을 직접 읽어
우회했다 — 읽기 전용, 판정 로직 관여 없음. 다음 미션이 없으면(스테이지 마지막 미션)
GoalCard02/`GoalConnector`를 자동으로 숨긴다. `lastMissionId` 캐시로 `CurrentObjective()`의
missionId가 바뀔 때만 재계산한다(매 프레임 배열 탐색 낭비 방지).

**(2) 미션1 클리어 시 자동 전환 — 이미 설계돼 있음, 검증만 함**: 소스 확인 결과
`ProgressionController.ConfirmMissionComplete()`가 `Progress.CompletedMissionIds.Add(...)` +
`ObjectivesChanged?.Invoke()`를 실행하고, `HudPresenter`는 이미(수정 없이) `pc.ObjectivesChanged
+= RefreshMission;`을 구독하고 있어 GoalCard01(직접 바인딩)과 조건 요약 패널이 자동으로
다음 미션 기준으로 다시 그려진다. 새 `GoalCardConditionAdapter`도 매 프레임
`CurrentObjective().missionId` 변화를 폴링하므로 별도 이벤트 구독 없이 같은 전환에 자연히
따라간다. **Play Mode에서 `Progress.CompletedMissionIds.Add(현재미션ID)`로 미션1 완료를
인위적으로 재현**해 확인: `CurrentObjective()`가 즉시 `MISSION_STAGE01_02`로 넘어감 확인,
그 직후(다음 프레임) `GoalCard02 active=False`/`GoalConnector active=False`로 즉시 반응
(스테이지에 미션이 2개뿐이라 "다음"이 없어짐 — 정상). `GoalCard01`은 이번 테스트가
`ConfirmMissionComplete()`의 실제 팝업 확인 절차를 거치지 않고 `CompletedMissionIds`만 직접
조작한 것이라 `ObjectivesChanged`가 발화되지 않아 갱신되지 않았다 — 이는 테스트 방식의 한계일
뿐, 실제 플레이에서는 "MISSION COMPLETE" 팝업의 [다음] 버튼이 `ConfirmMissionComplete()`를
호출해 `ObjectivesChanged`를 반드시 발행하므로 GoalCard01도 함께 정상 전환된다(소스로 확인
완료, 실제 조건 충족을 통한 end-to-end 플레이 테스트는 이번 범위 밖).

**(3) GOAL 카드 텍스트 오버플로우 방지**: `Goal_1_UI_수정.png`/`Goal_2_UI_수정.png`를 3-21과
같은 방식(Import 설정 `isReadable` 일시 변경 후 픽셀 샘플링, 측정 후 원복)으로 분석해 카드
아트의 실제 불투명 영역을 확인 — GoalCard01(115 높이)은 안전영역이 대략 y 24.5~108.9,
GoalCard02(100 높이)는 y 6~91.1였다. 기존 `Description` 박스는 이미 이 경계를 살짝 넘어서고
있었다(카드1: 111.89, 카드2: 96 — 텍스트가 없어도 박스 자체가 카드보다 큼). 조치:
- `Title`/`Description`(양쪽 카드 전부) `enableAutoSizing=true`로 전환(Title
  fontSizeMin14/Max24, Description fontSizeMin9/Max14) — 고정 폰트 크기 대신 내용 길이에 맞춰
  자동으로 줄어들게 해서 "다 보이게"(잘림 없이) 요구사항을 만족.
- `Description` 박스 높이를 안전영역에 맞춰 축소: 카드1 40→36, 카드2 40→34(폭·시작좌표는
  그대로).
- `overflowMode=Truncate`를 안전장치로 유지(자동 크기 조정이 극단적인 경우에도 최후 방어선).

**Play Mode 검증**: `GoalCard01` Title 1줄(30.0/34 박스), Description 2줄(35.0/36 박스,
꽉 차지만 안 넘침). `GoalCard02` Title 1줄(30.0/34), Description 2줄 — 자동 크기 조정으로
`fontSize 14→13.6`으로 살짝 줄어 실제 내용("경비대장의 믿음 상태가 의심함 이상으로 낮아져,
경비 강화 명령이 철회된다.")이 2줄로 들어감(35.0/34 박스 — 0.1px 정도 여유 없이 딱 맞지만
카드 실측 안전영역(91.1)에는 충분히 여유 있게 들어감). Console Error/Warning 0. Zone1 저장
완료(`isDirty=False` 재확인).

**수정/생성한 파일**: `GoalCardConditionAdapter.cs`(전면 재작성 — 미러링 → 다음 미션 미리보기),
`PlayHudCanvas_New.prefab`(Title/Description auto-size 전환 + Description 박스 높이 축소 +
어댑터 필드 갱신), `Zone1.unity`(동일 반영, 저장 완료). `HudPresenter.cs`는 전혀 수정하지
않았다.

### 3-26. 3-25에서 놓친 진짜 원인 — Description 박스 자체가 카드 폭보다 넓었다 (2026-08-04)

3-25 조치 후에도 사용자가 스크린샷으로 "아직도 카드 바깥으로 나간다"고 재차 지적. 3-25는 세로
방향(박스 높이 vs 카드 안전영역)만 확인했고 **가로 방향은 확인하지 않았던 게 원인**이었다.

**실측 결과**: `Description` 박스는 `anchoredPosition.x=40.1`(카드1)/`39.47`(카드2),
`width=295` — 오른쪽 끝이 각각 `x=335.1`/`334.47`. 그런데 카드 자체의 `RectTransform` 폭은
**330**이다. 즉 워드랩이 박스 안에서는 잘 됐어도(3-25에서 확인한 그대로), 그 박스 자체가
카드보다 4~5px 더 넓게 잡혀 있어서 줄 오른쪽 끝에 걸리는 글자는 카드 아트 자체가 없는(카드
바깥) 영역에 그려지고 있었다 — 픽셀 알파값만 보고 "세로 안전영역"만 확인하고 "박스가 카드
경계 안에 있는지" 자체는 확인하지 않은 게 3-25의 누락이었다.

**수정**: 양쪽 카드의 `Description` 박스 폭을 `카드폭(330) - x좌표 - 10(여유)`로 재계산해
축소(카드1: 295→279.9, 카드2: 295→280.5) — 오른쪽 끝이 정확히 `x=320`으로 카드 안(330)에
10px 여유를 두고 들어오게 했다. x좌표/높이는 그대로 유지.

**Play Mode 검증**: 폭이 좁아졌음에도 실제 내용 기준 줄바꿈 결과 각 줄 폭이 새 박스 폭 안에
전부 들어감(카드1: 265.6/197.8 < 279.9, 카드2: 269.9/129.4 < 280.5) — 자동 크기 조정
(3-25에서 적용) 덕분에 폭이 줄어도 줄 수가 늘지 않고 그대로 2줄 유지. 박스 오른쪽 끝
`x=320`으로 카드 폭(330) 안에 안전하게 위치. Console Error/Warning 0. Zone1 저장 완료
(`isDirty=False` 재확인).

**수정한 파일**: `PlayHudCanvas_New.prefab`(`Description` 박스 폭 축소, 양쪽 카드),
`Zone1.unity`(동일 반영, 저장 완료). 스크립트는 수정하지 않았다.

---

### 3-27. GOAL2(다음 미션 미리보기) 로직을 `GoalCardConditionAdapter` 어댑터에서 `HudPresenter`로 흡수 (2026-08-04)

3-25에서 만든 `GoalCardConditionAdapter`(읽기 전용 어댑터, `MissionArea`에 부착)는 디자인이
아직 불안정하던 시점에 "HudPresenter는 절대 수정하지 않는다"는 원칙 하에 임시로 분리해 둔
것이었다. 사용자가 이제 디자인이 안정됐는데 왜 계속 어댑터로 우회하는지 질문 — 재검토 결과,
GOAL2 "다음 미션 미리보기"는 순수 비주얼 보정이 아니라 **미션 진행 상태를 다루는 로직**이고,
이미 `HudPresenter.RefreshMission()`이 같은 성격의 상태(현재 미션 제목/설명/조건)를 갱신하는
단일 지점이므로, 별도 컴포넌트가 매 프레임 독립적으로 같은 걸 폴링하는 구조를 유지하는 게
오히려 유지보수상 더 나쁘다고 판단해 흡수하기로 결정.

**흡수 방식**:
- `HudView.cs`에 `nextMissionCardRoot`/`nextMissionCardTitleText`/`nextMissionCardDescText`/
  `nextMissionConnectorGo` 필드 + public 접근자 추가(기존 70개 참조 테이블과 동일한 패턴).
- `HudPresenter.cs`의 `RefreshMission()`에 `FindNextMission(objective)` 호출을 추가 —
  `GoalCardConditionAdapter.FindNextMission`과 완전히 동일한 로직(`GameInstaller.StageAsset.missions`
  배열에서 현재 미션 바로 다음 항목을 `Array.IndexOf`로 찾음)을 그대로 옮겨왔다. `objective == null`
  분기에서도 GOAL2/커넥터를 숨기도록 함께 처리.
- `MissionArea`의 `GoalCardConditionAdapter` 컴포넌트를 제거하고, `HudView`의 새 필드를
  `GoalCard02`/`GoalCard02/Texts/Title`/`GoalCard02/Texts/Description`/`GoalConnector`에
  직접 배선(SerializedObject).
- 더 이상 참조되지 않는 `GoalCardConditionAdapter.cs` 스크립트는 삭제(죽은 코드 방치 금지).

**Play Mode 검증**:
- 초기 상태: GOAL1 = 미션1(명령의 근거 흔들기), GOAL2 = 미션2(경비대장의 믿음 전환) — 정상.
- `Progress.CompletedMissionIds`에 미션1 id를 추가하고 `MissionChangedEvent`를 발행해 미션
  완료를 시뮬레이션 → GOAL1이 미션2 내용으로 자동 전환, GOAL2/커넥터는 다음 미션이 없으므로
  (스테이지1 마지막 미션) 자동으로 비활성화됨 — 어댑터 방식과 동일한 동작을 그대로 재현.
- Console Error/Warning 0. Zone1 저장 완료(`isDirty=False` 재확인).

**수정한 파일**: `HudView.cs`(필드 4개 추가), `HudPresenter.cs`(`RefreshMission()` 확장 +
`FindNextMission` 헬퍼 추가), `PlayHudCanvas_New.prefab`(어댑터 컴포넌트 제거, `HudView` 필드
재배선), `Zone1.unity`(동일 반영, 저장 완료). **삭제한 파일**:
`Assets/Belief/Scripts/Presentation/Mockup/GoalCardConditionAdapter.cs`(더 이상 참조되지 않음).

---

### 3-28. 우측 상단 Log/Profile 탭 — 반투명 스탬프(배경 아트) 확인 + 탭 클릭 시 색깔 박스가 덮이는 배선 버그 수정 (2026-08-04)

사용자가 로그/프로필 패널에 뜨는 "반투명한 것"을 없애 달라고 요청. 조사해보니 두 가지 서로
다른 문제가 섞여 있었다.

**1) "PRIVATE & CONFIDENTIAL" 대각선 스탬프** — `log 메인UI.png`/`프로필 파일 UI.png` 배경
아트 자체에 그려져 있는 그림이라 오브젝트를 끄는 걸로는 지울 수 없음을 확인. 대체 아트가
없어 사용자에게 처리 방식(자동 지우기 시도 / 새 아트 요청 / 보류)을 문의 — 사용자가 실제로는
이걸 가리킨 게 아니라(뒤 항목 참고) 이 항목은 **보류 상태**로 남아 있다.

**2) 진짜로 사용자가 가리킨 문제 — 탭 선택/비선택 시 색깔 박스가 탭 아트를 덮는 버그**: `HudView`의
`profileTabIndicator`/`logTabIndicator`가 **클릭 감지 전용 투명 사각형(`ClickArea`, 원래
`sprite=null, color=(1,1,1,0)`으로 안 보이게 설계됨)**에 잘못 연결돼 있었다. `HudPresenter.
SetHudPanelState()`가 선택된 탭엔 흰색(불투명), 선택 안 된 탭엔 반투명 갈색을 이 오브젝트
색상에 대입하는데, 하필 그 대상이 클릭 전용 투명 사각형이다 보니 탭 라벨/아트 위에 색깔
박스가 그대로 덮이는 것처럼 보였다.

**수정**: `HudView`의 `profileTabIndicator`/`logTabIndicator` 필드를 null로 배선 해제 —
`HudPresenter`의 `if (profileTabIndicator != null) ...` 가드가 이미 있어 코드 수정 없이
프리팹 배선만 끊으면 색칠 자체가 일어나지 않는다.

**검증 중 잘못 판단해 만들었던 버그(즉시 되돌림)**: 테스트하다가 `HudView.logPanelGo`/
`npcProfileGo` 필드가 비어 있는(null) 걸 보고 "배선 누락 버그"로 오판, `LogPanelRoot`/
`ProfilePanelRoot`에 배선을 추가했다. 그 결과 사용자가 즉시 "로그/프로필 패널이 아예
사라졌다"고 재보고 — 원인을 다시 조사하니 `RightPeekArea/RightDocumentPanelController`
(`RightDocumentPanelController.cs`)라는 **이미 완성되어 있던 별도 컨트롤러**가 두 패널의
슬라이드 열림/닫힘(peek in/out 애니메이션)과 탭 버튼 클릭까지 전부 정상적으로 전담하고
있었다는 걸 확인했다. `logPanelGo`/`npcProfileGo`가 null이었던 건 버그가 아니라 **의도된
상태**였다 — `HudPresenter.SetHudPanelState`가 이 두 필드를 건드리지 않아야 컨트롤러의
`SetActive`/`anchoredPosition` 애니메이션과 충돌하지 않는다. `HudPresenter.Start()`가 항상
`SetHudPanelState(Default)`를 호출하면서 방금 배선한 `npcProfileGo`/`logPanelGo`를
`SetActive(false)`로 강제해, 컨트롤러가 `Awake()`에서 이미 세팅해 둔 peek 상태(패널은
`active=true`, 화면 밖으로 슬라이드만 되어 있음)를 덮어써 버렸던 것 — 그래서 아무것도 안
보이게 된 것이었다. `logPanelGo`/`npcProfileGo` 배선을 즉시 원복(null)했다.

**Play Mode 검증(원복 후)**: 씬 시작 시 `ProfilePanelRoot`가 `active=true`,
`anchoredPosition=(1630,-43)`(peek 상태, 화면 밖으로 슬라이드)로 정상 확인. Log 탭 클릭 →
`RightDocumentPanelController.CurrentState`가 `Log`로 전환, `LogPanelRoot` 활성화 + 애니메이션
시작(위치가 목표값으로 이동 중), `ProfilePanelRoot` 비활성화 — 정상 동작. 탭 아이콘 색깔 박스
문제(`profileTabIndicator`/`logTabIndicator` 배선 해제)는 여전히 재현 안 됨. Console
Error/Warning 0. Zone1 저장 완료(`isDirty=False` 재확인).

**수정한 파일(최종)**: `PlayHudCanvas_New.prefab`(`profileTabIndicator`/`logTabIndicator`
배선 해제만 유지, `logPanelGo`/`npcProfileGo`는 null로 원복), `Zone1.unity`(동일 반영, 저장
완료). 스크립트는 수정하지 않았다.

**교훈**: 필드가 비어 있다고 곧바로 "배선 누락"으로 단정하지 말 것 — 이 프로젝트에는
`RightDocumentPanelController`처럼 목업 단계에서 이미 완성되어 프리팹에 남아 있는 독립
컨트롤러가 더 있을 수 있다. `HudPresenter`가 특정 오브젝트를 건드리지 않는 게 오히려
의도된 설계일 수 있으므로, 배선을 추가하기 전에 해당 오브젝트를 다른 컴포넌트가 이미
제어하고 있지 않은지 먼저 확인한다.

**남은 항목**: PRIVATE & CONFIDENTIAL 스탬프(1번 항목) — 사용자가 "그냥 내버려둬"로 확정,
현재 아트 그대로 유지하기로 결정. 추가 조치 없음(종결).

---

### 3-29. NPC 조사 파일 "성격 태그" 5종 실데이터 연결 — `NPC기획` PDF 17개에서 추출 (2026-08-04)

프로필 패널의 판단 성향/우선순위/민감 정보/관계 성향/신뢰 판단 방식 텍스트가 전부
비어 있던(null) 문제를 사용자가 지적 — 데이터 출처는 `C:\Users\CHJ\Desktop\확정기획\NPC기획\`
폴더의 NPC별 기획서 PDF 17개("NPC_기획서__〈이름〉.pdf", 4스테이지 영주만
"NPC_콘텐츠_기획서_4스테이지_영주.pdf")이며, 각 문서 "1.2 특성 태그" 표에 5개 축(판단 성향/
우선순위/민감 정보/관계 성향/신뢰 판단 방식)이 `#태그` 형식으로 정리되어 있었다.

**UI 구조 확인 중 추가로 발견한 문제**: 값 텍스트 오브젝트가 `JudgeTendencyValue`/
`PriorityValue`/`RelationTendencyValue`/`TrustJudgeValue` 4개뿐이었고 "민감 정보" 칸은
아예 프리팹에 없었다 — `JudgeTendencyValue`를 복제해 `SensitiveInfoValue`(pos 572,-303)
로 새로 만들어 보완했다.

**데이터 레이어**: `NpcData.cs`(base 클래스, Major/Minor 공통)에 5개 string 필드 추가 —
`judgmentTendencyTag`/`priorityTag`/`sensitiveInfoTag`/`relationTendencyTag`/
`trustJudgmentTag`. 기존 `trustBias`/`skepticism`/`goal`/`loyalty`/`relationships`(v4
프로필 동기화분, 별도 세션에서 이미 채움)는 전혀 건드리지 않았다.

**UI 레이어**: `HudView.cs`에 5개 TMP_Text 필드 추가 후 프리팹의 실제 오브젝트에 배선.
`HudPresenter.RefreshNpcProfile()`에서 NPC 선택 시 `data.judgmentTendencyTag` 등 5개
필드를 그대로 읽어 표시(선택 해제 시 전부 빈 문자열로 초기화)하도록 확장 — 판정 로직에는
전혀 관여하지 않는 순수 표시 전용 추가.

**데이터 추출/입력**: PDF 17개를 병렬 서브에이전트 5개(배치 A~E, 각 2~3개 PDF)로 나눠
읽고 "1.2 특성 태그" 표 값을 추출 → `NpcId`/`displayName` 기준으로 기존 17개 NpcData 에셋
(`Npc_Major_*`/`Npc_Minor_*`, `Deprecated/Npc_Major_Informant` 제외)에 정확히 1:1 매칭
확인 후 `SerializedObject`로 일괄 반영, `AssetDatabase.SaveAssets()`.

**Play Mode 검증**: `World/NpcActors`(런타임 생성, 기본 비활성) 하위의 경비대장 액터를
`NpcActorView.OnPointerClick(null)`로 직접 클릭 시뮬레이션 → 프로필 패널에 5개 태그
(`#의심형`/`#명령충실`/`#권위민감`/`#상명하복`/`#근거중시`)가 정확히 표시됨을 확인. Console
Error/Warning 0. Zone1 저장 완료(`isDirty=False` 재확인).

**수정한 파일**: `NpcData.cs`(필드 5개 추가), `HudView.cs`(필드 5개 추가), `HudPresenter.cs`
(`RefreshNpcProfile()` 확장), `PlayHudCanvas_New.prefab`(`SensitiveInfoValue` 신규 생성 +
5개 필드 배선), `Zone1.unity`(재배선, 저장 완료), NPC 에셋 17개(`Npc_Major_GuardCaptain`,
`Npc_Minor_SmallMerchant`, `Npc_Major_LowRankGuard`, `Npc_Major_MerchantHead`,
`Npc_Major_Innkeeper`, `Npc_Major_Steward`, `Npc_Major_GuildMaster`,
`Npc_Major_CustomsOfficer_Stage2`, `Npc_Major_HeadMaid`, `Npc_Major_RivalNoblewoman`,
`Npc_Major_Maid`, `Npc_Minor_Smuggler_Stage2`, `Npc_Major_Bookkeeper`,
`Npc_Major_KnightCommander`, `Npc_Major_Priest`, `Npc_Major_LordsWife`, `Npc_Major_Lord`).

---

### 3-30. 월드(가운데) 건물/NPC 실사진 스프라이트 연결 — `장소&npc`/`리소스` 폴더에서 임포트 (2026-08-04)

`LocationSiteView.background`/`NpcActorView.body`는 원래 "실제 사진 자산이 없어" 단색
placeholder(`PlaceholderSquare`, 코드 주석에 이미 "실제 자산이 오면 색만 바뀌는 필드 하나로
재사용 가능"이라고 명시돼 있었음)만 채워져 있었다. 사용자가 `C:\Users\CHJ\Desktop\장소&npc\`
(및 동일 내용의 상위 폴더 `C:\Users\CHJ\Desktop\리소스\`)에 실제 건물 사진 14종 + NPC
캐릭터 스프라이트 16종을 이미 받아둔 상태였음을 확인 — `StageData` 1~4 전체를 코드로 직접
순회해 실제로 쓰이는 장소/NPC 목록을 먼저 뽑아(파일명 추측 대신) 정확히 매칭했다.

**데이터 레이어**: `LocationData.locationPhoto`(Sprite), `NpcData.characterPhoto`(Sprite)
필드 신규 추가. `LocationSiteView.Bind()`/`NpcActorView.Bind()`에 각각 2줄만 추가해 스프라이트가
있으면 적용(없으면 기존 placeholder 유지, 하위 호환).

**임포트**: 위치 사진 14개(`Loc_Barracks`~`Loc_EXCHANGE`, `StageData.locations` 기준 실사용
16개 중 14개 매칭)와 NPC 스프라이트 16개(`Npc_Major_GuardCaptain`~`Npc_Minor_Smuggler_Stage2`,
실사용 17개 중 16개)를 각 데이터 에셋과 동일한 파일명으로 `Assets/Belief/UI/World/
Locations|Npcs/`에 복사, Sprite(2D and UI) 타입으로 임포트 후 `SerializedObject`로 일괄 배선.

**빠진 것(아트 미제공, 확인 필요)**: 4스테이지 전용 장소 2곳(`Loc_Plaza`="알현실 앞 광장",
`Loc_manor_row`="저택가")은 14개 배경 사진 중 대응하는 파일이 없었다 — placeholder 유지.
`Npc_Major_Lord`(영주)도 캐릭터 폴더 자체가 없어 스프라이트 없음 — placeholder 유지. NPC
성격 태그(3-29)와 달리 이번엔 두 항목 다 "아직 아트가 안 나온 것"이지 매칭 실패가 아니다.

**발견한 버그(임포트 전 검수)**: `장부관리인_규격맞춤.png`(구분 없는 파일명)이 실제로는 걷기
애니메이션 스프라이트시트(6x6, 2048x1152)였다 — 단일 초상화가 아닌 걸 픽셀로 직접 열어
확인하고, 같은 폴더의 `장부관리인_규격맞춤(1).png`(진짜 단일 초상화)로 교체했다. 다른
파일들은 정상 확인.

**스케일 버그(1차 임포트 후 발견, 즉시 수정)**: 기본 임포트 설정(PPU=100)으로 배선한 뒤
Play Mode 스크린샷을 직접 찍어보니 NPC/건물 스프라이트가 프레임보다 수십 배 크게 렌더링됨을
발견 — 원인은 새 원본 이미지가 500~650px(NPC, 정사각형)/187x301px(장소, 세로형) 고해상도인데
기존 `PlaceholderSquare`는 4x4px@PPU4(=1유닛)였던 것과 PPU가 안 맞았기 때문. 실측(기존
placeholder의 최종 world bounds, `PlayHudSkin`의 `npcPhotoFrame`/`locationImageFrame`
사각형 크기)을 근거로 재계산해 고쳤다:
- NPC: `spritePixelsPerUnit = 이미지 자체의 픽셀 폭`(정사각형이므로) → 기존 placeholder와
  정확히 동일한 1.08×1.08 유닛 최종 크기로 복원(프리팹 `localScale`은 그대로 둠).
- 장소: 기존 `Photo` 오브젝트의 `localScale`이 (1.08, 0.58)로 비균일해서(정사각형이 아닌
  옛 landscape placeholder 기준) 세로형 사진에 그대로 적용하면 찌그러짐 → `localScale`을
  (1,1,1)로 정규화하고, `spritePixelsPerUnit = 이미지 높이 / 1.4537`(NPC가 자기 프레임을
  채우는 비율 0.966을 장소의 정사각 프레임 1.505유닛에 그대로 적용한 값)로 계산해 비율
  왜곡 없이 정사각 프레임 안에 꽉 차게 맞춤.

**Play Mode 시각 검증**: 두 번의 실제 스크린샷(`Unity_SceneView_Capture2DScene`)으로 직접
확인 — 1차(수정 전)는 캐릭터가 프레임을 완전히 뒤덮을 정도로 거대했고, 2차(수정 후)는
건물/NPC 카드 모두 압정·프레임과 자연스러운 비율로 렌더링됨을 확인. 색상 틴트(Alert/Locked/
Selected 상태별 `SpriteRenderer.color` 곱연산, 기존 로직 그대로 재사용)도 사진 위에서 자연스럽게
보임 - 별도 리디자인 불필요. Console Error/Warning 0.

**수정한 파일**: `LocationData.cs`/`NpcData.cs`(스프라이트 필드 추가), `LocationSiteView.cs`/
`NpcActorView.cs`(`Bind()` 2줄씩 추가), `LocationSiteView.prefab`(`Photo` localScale 정규화),
위치 데이터 14개 + NPC 데이터 16개(스프라이트 배선), 신규 이미지 30개(`Assets/Belief/UI/World/
Locations|Npcs/`). Zone1.unity는 런타임 스폰이라 씬 자체는 변경 없음(`isDirty=False` 확인).

---

## 4. 현재 Hierarchy (Zone1.unity, Edit Mode 확인)

```
Zone1
├─ GameInstaller            active=True
├─ Main Camera              active=True
├─ World                    active=True
│  ├─ LocationSites         active=False   ← 런타임 생성
│  └─ NpcActors             active=False   ← 런타임 생성
├─ HUD                      active=True    ★ 실제 게임 HUD (사용 중)
│  ├─ StageBriefingCanvas   active=True
│  └─ HudCanvas             active=True
├─ TargetingController      active=True
└─ Zone1HudMockupCanvas     active=False   ★ 참고용 목업 — 삭제 금지 (Step F까지)
   ├─ 01_BlackBackground
   ├─ 02_MOCKUP_MapBackground_TEMP
   ├─ 03_ScreenFrame
   ├─ 04_MissionCards
   ├─ 05_StageTurn
   ├─ 06_LocationCards
   ├─ 07_InfoDocument
   ├─ 08_ProfileDossier
   └─ 09_HandCards
```

---

## 5. 수정한 파일 (미커밋)

| 파일 | 내용 |
|---|---|
| `Assets/Belief/Prefabs/HUD/CardTileView.prefab` | 레이아웃·폰트·색상 목업 기준으로 재구성 + **CategoryText auto-size (B-1)** |
| `Assets/Belief/Scripts/Presentation/HUD/CardTileView.cs` | CardSpacing 76, 선택 상승 연출, ExpandedDetail 상시 활성 + **CODE/Kind 칸 텍스트 분리 (B-1)** |
| `Assets/Belief/Scenes/Zone1.unity` | HUD 활성 / Zone1HudMockupCanvas 비활성 |
| `Assets/Belief/Fonts/*.asset` (11개) | **의도한 변경 아님** — Play Mode 진입 시 TMP 동적 글리프 아틀라스가 자동 갱신된 부산물. 커밋 여부는 판단 필요 |

### 참고: 이전 세션에서 이미 커밋된 것들

목업 씬 3개(`UI_PlayHudMockup`, `UI_MissionResultMockup`, `StageSelectMockup`)와 `Mockup/` 스크립트 3개는 **이미 커밋 완료** 상태다.

### 백업

`C:\Users\CHJ\Desktop\belief\_PreHudMigrationBackup_20260803\`
Unity 프로젝트 **바깥**에 있는 영구 백업. 프리팹 5개, Zone 씬 4개, 스크립트 4개 + .meta = 26개 파일.
Step A 시작 직전 상태이므로, 문제 발생 시 여기서 되돌리면 된다.

---

## 6. 아직 연결되지 않은 기능

- **우측 Profile / Log 문서 패널 슬라이드 연출** — 목업의 `RightDocumentPanelController.cs` 연출이 실제 `HudCanvas`에는 아직 이식되지 않음. 실제 HUD는 `HudView.ProfileTabButton` / `LogTabButton` / `NpcProfileGo` / `LogPanelGo`로 배선되어 있고, 현재는 단순 SetActive 토글 방식.
- **상단 StageTab / TurnTab, 좌측 MissionPanel, LocationCharacteristicNote** — 목업 디자인 미반영.
- **BottomPanel 위치** — 목업 가이드에서는 선택되지 않은 카드가 화면 하단에서 잘려 보이는데, 실제 HUD는 그 클리핑이 적용되지 않음. Step C에서 처리 예정.
- **ResultScreen** — 목업은 SuccessScreen / FailScreen **2개 오브젝트**로 만들었지만, 실제 `HudPresenter`는 **단일 ResultScreen + 런타임 스프라이트 스왑** 구조다.
  ```csharp
  resultPanelImg.sprite      = won ? skin.successPanel      : skin.failurePanel;
  resultPhotoFrameImg.sprite = won ? skin.successPhotoFrame : skin.failurePhotoFrame;
  ```
  → Step D에서 **2화면을 1화면으로 접어야 한다.** 실패 화면 전용 배경(좌상단 지도 시트, `BackgroundMapSheet`)은 스프라이트 스왑만으로는 표현 불가하므로 별도 GameObject를 `won` 여부로 SetActive 하는 처리가 추가로 필요하다.
- **StageBriefingCanvas** — 스테이지 선택 목업 미반영.

---

## 7. 알려진 버그

### ✅ B-1. CardTileView — CODE 칸 텍스트가 카드 밖으로 넘침 (2026-08-04 수정 완료)

원인: `CardTileView.Bind()`가 좁은 CODE 슬롯(66.09px)에 카테고리와 종류를 함께 넣었다.

사용자 승인을 받고 문서 제안대로 적용했다.

```csharp
// 변경 전
categoryText.text = $"{categoryId} · {kind}";   // "ECONOMY · DELIVER" -> 넘침
kindText.text     = $"출처: {sourceName}";

// 변경 후
categoryText.text = categoryId;                 // "ECONOMY"
kindText.text     = $"{kind} / {sourceName}";   // "DELIVER / 술집 주인"
```

**추가로 발견·수정한 문제**: 텍스트를 분리한 뒤 10개 카테고리 전부를 실측한 결과 `DISASTER`가 여전히
1.02px 넘쳤고, `SECURITY`/`RELIGION`/`MILITARY`/`NOBILITY`도 여유가 1.3px 미만이었다.
66px 슬롯이 8글자 카테고리에 근본적으로 빠듯하다.

→ `CardTileView.prefab`의 `CategoryText`에 auto-size 적용:
`enableAutoSizing = true`, `fontSizeMax = 14.9`(목업 크기가 상한), `fontSizeMin = 11.5`,
`enableWordWrapping = false`(줄바꿈 대신 축소로 대응).

Play Mode 실측 재검증 (슬롯 66.09px):

| 카테고리 | 렌더 폰트 | 렌더 폭 | 결과 |
|---|---|---|---|
| CRIME | 14.90 | 42.18 | ok |
| ADMIN | 14.90 | 42.30 | ok |
| **DISASTER** | **14.65** | 65.98 | ok (유일하게 축소) |
| ECONOMY | 14.90 | 58.70 | ok |
| MILITARY | 14.90 | 65.13 | ok |
| NOBILITY | 14.90 | 64.80 | ok |
| POLITICS | 14.90 | 63.52 | ok |
| PUBLIC | 14.90 | 49.43 | ok |
| RELIGION | 14.90 | 65.65 | ok |
| SECURITY | 14.90 | 65.93 | ok |

10개 전부 슬롯 안. `DISASTER`만 1.7% 축소되어 육안 구분이 되지 않는다. Console Error/Warning **0**.

> 검증은 TMP `GetRenderedValues`/`preferredWidth` 실측 기반이다. Screen Space - Overlay 캔버스는
> 카메라 캡처에 잡히지 않아 스크린샷으로는 확인하지 않았다.

### 🟡 B-2. BottomPanel 카드 클리핑 미검증

가이드에서는 선택 안 된 카드가 화면 하단에서 일부 잘려 보인다. 실제 HUD의 `BottomPanelRect` 위치가 그 상태인지 확인되지 않았다. Step C 범위.

### 🟡 B-3. 폰트 SDF 에셋 자동 변경

Play Mode 진입만으로 `Assets/Belief/Fonts/*.asset` 11개가 수정됨(동적 글리프 추가). 기능 영향은 없지만 diff를 오염시킨다.

---

## 8. 다음 세션에서 가장 먼저 할 작업

1. ~~`git status` 확인~~ — `git`은 이 머신에 **설치 자체가 안 되어 있다** (Program Files / LocalAppData / scoop 모두 없음). PATH 문제가 아니므로 에이전트는 영구히 실행 불가. `.git/HEAD`, `.git/logs/HEAD` 직접 읽기로 브랜치·커밋만 확인 가능하다.
2. ~~**B-1 (CODE 칸 오버플로우) 수정**~~ → ✅ 2026-08-04 완료 (7절 참조).
3. Step A + B-1을 커밋한 뒤 **Step B (우측 Profile/Log 패널)** 착수.
   - `RightDocumentPanelController.cs`의 슬라이드 로직을 참고해 `HudPresenter`의 탭 전환에 이식.
   - `SharedTabRoot`는 **절대 `SetActive(false)` 하지 않는다** — 목업에서 패널이 안 열리던 근본 원인이 이것이었다.

---

## 9. 절대 수정하면 안 되는 항목

| 항목 | 이유 |
|---|---|
| `HudView.cs`의 필드 목록 (70개 `[SerializeField]`) | Zone1~Zone4 프리팹 배선이 전부 여기에 물려 있다. 필드명/타입 변경 = 배선 전멸 |
| `HudPresenter.cs`의 **게임 로직** | 미션 진행, Turn 갱신, Deliver/Spread 판정. 비주얼 이식과 무관 — 텍스트 대입/활성 토글 외에는 손대지 않는다 |
| 목업 씬 3개 (`UI_PlayHudMockup`, `UI_MissionResultMockup`, `StageSelectMockup`) | 사용자 지시 6항 — 참고용으로 **보존** |
| `Zone1HudMockupCanvas` | Step F 통합 검증 완료 전까지 **삭제 금지**. 비활성 유지 |
| `HUD` 와 `Zone1HudMockupCanvas` 동시 활성 | 지시 7항 위반. 항상 둘 중 하나만 활성 |
| `_PreHudMigrationBackup_20260803\` | 복구 최후 수단. 덮어쓰기·삭제 금지 |
| Active Input Handling 설정 | 이미 Input System 전용으로 정리됨. EventSystem은 `InputSystemUIInputModule` 하나만 유지 |

---

## 10. 검증 방법

### Step A 재검증

1. `Assets/Belief/Scenes/Zone1.unity` 열기
2. Play Mode 진입
3. 확인 항목:
   - 손패 카드 4장이 각각 380×190 크기로 렌더링되는가
   - 카드 x 위치가 −684 / −228 / 228 / 684 (간격 456)인가
   - 카드 클릭 시 28px 상승 + 1.05배 확대가 **부드럽게** 진행되는가 (순간이동 X)
   - 다른 카드 클릭 시 이전 카드가 부드럽게 내려가는가
   - `ExpandedDetail`(설명·태그)이 항상 보이는가
   - Console Error / Warning **0**

### 전체 통합 검증 (Step F 기준 — 지시 4항)

- 카드 선택
- 장소 클릭 / NPC 클릭
- Deliver / Spread 실행
- 미션 진행 및 클리어 판정
- Turn 갱신
- Profile / Log 전환
- Console Error / Warning 0

### 회귀 확인

`CardTileView.prefab`은 **공용 프리팹**이므로 Zone1뿐 아니라 **Zone2 / Zone3 / Zone4 전부**에서 카드 렌더링을 확인해야 한다.

---

## 11. 최근 커밋 해시

`.git/logs/HEAD`에서 직접 읽음 (git CLI 사용 불가).

| 해시 | 메시지 |
|---|---|
| `5ea43936832742f8076f8712bc185e43780be596` | **HEAD** — Fix: 작전 실패 화면 배경을 가이드에 맞게 분리 |
| `6e0a9e1b5c4f1061717308b6178388735041d1b3` | feat: 작전 결과/스테이지 선택 UI 목업을 가이드에 맞춰 구성 |
| `43b19243c012b5c8f8772c7dbd03b48515cccc02` | Feat: 우측 프로필·로그 문서 패널 전환 구조 구현 |
| `fe2e9d02d34d22ed9e98521d776047b61e2714dd` | Feat: 손패 카드 선택 상승 애니메이션 구현 |
| `ffdfa5b9850805983f306d3a0bc0186e5e672841` | Style: 손패 카드 텍스트 정렬 및 폰트 배치 보정 |
| `84e3868febd76106439d9327b50c0dd583a36d75` | Feat: 플레이 HUD 정적 목업 씬과 카드 UI 초안 구성 |

브랜치: `main`

---

## 12. 커밋 방법 (에이전트가 실행 불가)

PowerShell 도구에서 `git`을 찾을 수 없어 에이전트가 커밋할 수 없다. 아래를 직접 실행:

```powershell
cd C:\Users\CHJ\Desktop\belief\belief
git status

git add Assets/Belief/Prefabs/HUD/CardTileView.prefab
git add Assets/Belief/Scripts/Presentation/HUD/CardTileView.cs
git add Assets/Belief/Scenes/Zone1.unity
git commit
```

커밋 메시지:

```
Feat: 손패 카드 UI를 목업 디자인 기준으로 실게임에 이식

- CardTileView 프리팹 레이아웃과 폰트, 색상을 목업 카드 기준으로 재구성
- 카드 간격을 가이드 실측값 76px로 조정
- 카드 선택 시 상승과 확대를 부드러운 보간 연출로 변경
- 카드 설명과 태그 영역을 항상 표시하도록 수정
- Zone1 씬에서 실제 HUD를 활성화하고 목업 캔버스를 비활성 상태로 보존
```

> ⚠️ 이 커밋에는 **B-1 (CODE 칸 텍스트 오버플로우)** 버그가 포함된 채로 들어간다.
> 먼저 고치고 커밋할지, 지금 커밋하고 다음에 Fix로 처리할지는 판단 필요.
> `Assets/Belief/Fonts/*.asset` 11개는 자동 생성 부산물이므로 커밋에서 제외하는 것을 권장한다.
