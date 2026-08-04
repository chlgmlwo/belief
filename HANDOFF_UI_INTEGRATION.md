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

### 3-31. "장소랑 NPC가 안 보여서 플레이를 못 한다" — 진짜 원인 2개(둘 다 오늘 세션과 무관한 기존 버그) 발견 및 수정 (2026-08-04)

사용자가 실제로 Zone1을 Play해보고 스크린샷으로 "성소"/"장소"/"뒷골목"이라는, Zone1(북문)에는
존재하지도 않는 장소명이 뜨고 실제 장소/NPC를 못 보고 못 누른다고 보고. 조사해보니 서로 다른
원인 2개가 겹쳐 있었다 — **둘 다 오늘 만든 버그가 아니라 훨씬 이전부터 있던 기존 결함**이었고,
지금까지 드러나지 않은 이유까지 확인했다.

**원인 1 — `PlayHudCanvas_New` 프리팹 안에 정적 목업 장식이 그대로 남아 화면을 뒤덮고 있었음**:
`WorldArea`라는 하위 오브젝트에 `LocationCard01/02/03`(하드코딩된 가짜 "성소"/"장소"/"뒷골목"
카드), `ConnectionLines`, `ContactStamp`("접선" + PRIVATE 스탬프)가 전부 `active=true`로
살아있었다 — 목업 씬(UI_PlayHudMockup)을 프로덕션 캔버스로 전환할 때 이 정적 장식들을 정리하지
않고 그대로 가져온 것. HUD 캔버스는 항상 실제 월드보다 앞에 그려지므로, 이 가짜 카드들이 실제
`World/LocationSites`·`NpcActors`를 완전히 가리고 있었다. **단, 같은 `WorldArea` 밑의
`LocationInfoPaper`는 `HudView.locationNoteGo`/`locationNoteTitleText`/`locationNoteBodyText`로
실제 배선되어 쓰이고 있는 진짜 UI라서(장소 클릭 시 특성 메모 패널) `WorldArea` 전체를 끄지 않고
가짜 카드 5개만 개별적으로 비활성화했다.**

**원인 2 — 원인 1을 걷어내고 보니 그 밑에 있어야 할 진짜 월드도 안 보임**: `WorldPresenter.
locationRoot`/`npcRoot`가 가리키는 실제 씬 오브젝트 `World/LocationSites`·`NpcActors`가
Zone1.unity에 **`active=false`로 저장되어 있었다**. `WorldPresenter.Start()`는 이 컨테이너
밑에 `LocationSiteView`/`NpcActorView`를 `Instantiate`만 할 뿐 컨테이너 자체의
`SetActive(true)`는 코드 어디에도 없다 — 즉 씬에 저장된 활성 상태가 그대로 최종 상태이고,
누군가 에디터에서 끈 채로 저장한 뒤 다시 켜지 않은 것으로 보인다. 인트로 팝업 완료 등 어떤
트리거로도 자동으로 켜지지 않는다는 것도 코드로 확인(참조하는 코드가 아예 없음). 이 버그가
지금까지 드러나지 않았던 이유가 바로 원인 1 — 가짜 `WorldArea` 카드가 항상 화면을 채우고
있어서, 그 뒤에서 진짜 월드가 안 보이고 있다는 사실 자체를 아무도 눈치채지 못했던 것.

**수정**: `PlayHudCanvas_New.prefab`에서 `WorldArea/{ConnectionLines, LocationCard01,
LocationCard02, LocationCard03, ContactStamp}` 5개 `SetActive(false)`(`LocationInfoPaper`는
그대로 유지). `Zone1.unity`에서 `World/LocationSites`·`NpcActors` `SetActive(true)`로 변경,
저장.

**Play Mode 검증**: 수정 전에는 (강제 활성화 스크립트 없이는) `LocationSiteView`/
`NpcActorView`가 `activeInHierarchy=False`로 남아 있었음을 재현 확인. 수정 후에는 아무 개입
없이 Play만 눌러도 4개 장소·5개 NPC 전부 `activeInHierarchy=True`로 자연스럽게 나타남을
스크린샷으로 재확인(실제로 사용자가 마우스로 클릭할 수 있는 정상 상태). Console Error/Warning
0. Zone1 저장 완료(`isDirty=False` 재확인).

**수정한 파일**: `PlayHudCanvas_New.prefab`(가짜 `WorldArea` 카드 5개 비활성화),
`Zone1.unity`(`LocationSites`/`NpcActors` 활성화, 저장 완료). 스크립트는 수정하지 않았다.

**남은 확인 사항**: 이 두 버그(특히 원인 2 - `LocationSites`/`NpcActors` 비활성 상태)는
Zone1뿐 아니라 Zone2/Zone3/Metropolis 씬에도 똑같이 저장되어 있을 가능성이 높다 — 아직
그쪽 HudCanvas 교체 작업 자체가 안 됐으므로(원인 1은 Zone1에만 해당) 원인 2만 별도로 확인이
필요하다.

---

### 3-32. 3-31로도 안 고쳐짐 — 진짜 원인은 `OuterBackground`가 전체화면을 덮는 불투명 흰색이었다 (2026-08-04)

3-31 조치 후 사용자가 직접 Play해서 스크린샷을 보냈는데도 `WorldArea`가 완전히 텅 빈 흰색
화면이었다. 재조사 결과 3-31의 두 원인 다 정확했지만 **세 번째 원인**이 남아있었다 —
`PlayHudCanvas_New` 밑 `Background/OuterBackground`(Image, `sprite=null`, `color=(1,1,1,1)`
완전 불투명 흰색, `sizeDelta=(1920,1080)` 전체화면)가 **항상** 존재했다.

**근본 원인**: `HudCanvas`의 `Canvas.renderMode`는 `ScreenSpaceOverlay`다 — 이 모드는 Unity가
Main Camera가 그린 월드 렌더 결과 위에 캔버스를 완전히 별도 패스로 무조건 마지막에 그린다.
`OuterBackground`가 스프라이트 없이 불투명 흰색으로 전체 화면(1920×1080)을 덮고 있었으므로,
`World/LocationSites`·`NpcActors`가 아무리 정상적으로 활성화되고 정상 위치에 있어도 **애초에
카메라가 그린 결과 자체가 화면에 도달하지 못하고** 캔버스의 흰 배경에 완전히 가려지고 있었다
— 이건 이번 세션이 아니라 이 프로젝트에서 `PlayHudCanvas_New`가 처음 만들어진 시점부터 계속
있었던 문제로 보이며, 3-31에서 지적한 두 원인(가짜 목업 카드, 비활성 컨테이너)을 전부 고쳐도
이 배경 하나 때문에 세상이 안 보였던 것이다.

**왜 지금까지 이걸로 검증했다고 착각했나(내 실수)**: 3-31에서 "확인 완료"라고 보고할 때 쓴
`Unity_SceneView_Capture2DScene` 도구는 **월드 오브젝트만 직접 정사영으로 캡처**하고
`ScreenSpaceOverlay` 캔버스 합성을 아예 거치지 않는다 - 그래서 실제로는 안 보이는 화면인데도
월드 오브젝트 자체는 잘 그려져 있으니 "정상"으로 잘못 확인했다. 이후 `Unity_Camera_Capture`도
Scene View 자체(카메라 인스턴스 ID 미지정 시)는 게임 카메라와 무관한 걸 보여준다는 걸 뒤늦게
파악. 이번엔 `Unity_Camera_Capture(cameraInstanceID=Main Camera)`로 실제 Main Camera가 그리는
결과를 직접 확인했고, `OuterBackground`의 런타임 `color.a` 값도 코드로 직접 읽어 0인지 확인한
뒤에야 "된다"고 판단했다.

**수정**: `OuterBackground`의 `Image.color.a`를 `1` → `0`으로 변경(스프라이트/`raycastTarget`은
원래도 없었으므로 클릭 차단 등 다른 기능에 영향 없음 확인 후 진행). 흰색 자체는 Main Camera의
`clearFlags=SolidColor`/`backgroundColor=흰색`이 그대로 유지하므로 배경 색상 톤 자체는 기존과
동일하게 보이고, 그 위에 이제 실제 월드가 그려진다.

**검증**: `Unity_Camera_Capture(cameraInstanceID=Main Camera)`로 캡처해 장소 4개·NPC 5개가
실제로 화면에 나타나는 것을 직접 이미지로 확인. Console Error/Warning 0. Zone1 저장 완료.

**수정한 파일**: `PlayHudCanvas_New.prefab`(`OuterBackground` alpha 0으로 변경). Zone1.unity는
프리팹 재적용만 있었고 별도 씬 변경 없음.

**교훈**: 월드-카메라 렌더링과 `ScreenSpaceOverlay` 캔버스를 함께 쓰는 화면은, 캔버스 쪽에
불투명 요소가 하나라도 전체 화면을 덮고 있으면 카메라가 뭘 그리든 안 보인다 - 이후 "월드가
보이는지" 검증할 때는 반드시 `Unity_Camera_Capture`에 실제 Main Camera의 instance ID를
넘겨서 확인하고, `Unity_SceneView_Capture2DScene`(캔버스 합성 안 함)만으로는 검증을 끝내지
않는다.

---

### 3-33. 장소 접근 제한(Location Mechanics V1 §7) 제거 — 모든 정보 카드는 모든 장소·NPC에 사용 가능 (2026-08-04)

3-32 수정 후 사용자가 실제 플레이 중 SPREAD 카드를 접근 제한된 장소(경비 초소=
`GuardRestricted`, 영주 저택 앞=`StewardRestricted`)에 쓰려다 "이 장소는 출입이 제한되어
있어..." 안내와 함께 거부당함. 처음엔 이게 기존에 이미 있던 의도된 게임 규칙(Location
Mechanics V1 §7 - `LocationData.accessType`이 `Public`/`Unspecified`가 아닌 장소는 SPREAD
카드로 "장소 전체" 직접 지정 불가, 개별 NPC만 가능)이라고 설명했으나, 사용자가 "아니야아니야
이거 모든 정보 카드는 모든 장소랑 npc에 다 쓸 수 있어야대 규칙이 만들면서 바뀐거 같아"로
명확히 정정 — 접근 제한 자체를 없애 달라는 요청.

범위 확인(AskUserQuestion): SPREAD→장소 전용, DELIVER→NPC 전용이라는 카드 타입 구분 자체는
유지하고, "접근 제한(accessType)으로 인한 장소 전체 대상 차단"만 제거하기로 확정.

**수정**: `LocationMechanicsSettings.CanTargetLocationDirectly(LocationData)`가 항상 `true`를
반환하도록 변경 — 기존에는 `accessType`이 `Unspecified`/`Public`일 때만 허용했음.
`TargetingController.OnLocationClicked()`/`TurnSystem.PlayCardOnLocationAsync()`는 이 메서드
하나만 거치므로 호출부 수정 없이 이 한 곳만 고치면 전체 차단이 풀린다. `accessType` 필드 자체와
조사 파일의 "접근 권한" 표시, `spreadSpeed`/`npcDensity`/`credibilityModifier` 등 다른 수치
보정 로직은 전혀 건드리지 않았다(요청 범위 밖).

**Play Mode 검증**: `GuardRestricted`인 "경비 초소"에 SPREAD 카드를 직접 타겟팅 →
`CanTargetLocationDirectly=True`, `Phase=AwaitingConfirm`(거부 안 됨) → `DeliverByInformant()`
로 실제 전달까지 정상 완료 확인. Console Error/Warning 0. 씬 변경 없음(스크립트 전용 수정).

**수정한 파일**: `LocationMechanicsSettings.cs`(`CanTargetLocationDirectly` 항상 true로 변경).

---

### 3-34. 도시 배경 이미지 연결 (2026-08-04)

월드 카메라 뒤에 아무 배경도 없어 흰 화면이었던 부분(3-32에서 `OuterBackground`를 투명화한
뒤 드러난 카메라의 순수 배경색)에 실제 도시 지도 배경을 깔아 달라는 요청. 소스는
`장소&npc\배경\도시배경\<N>스테이지_...\<N>스테이지.png`(1~3), `리소스\배경\도시배경\
4스테이지_영주\4스테이지.png`(4 - `장소&npc` 쪽은 `.jpg`라 화질이 더 나은 `리소스`의 `.png`
사용).

**데이터/로직**: `StageData.cityBackground`(Sprite) 필드 신규 추가.
`WorldPresenter.Start()`에 `CreateCityBackground()` 추가 - `installer.StageAsset.
cityBackground`가 있으면 `Main Camera`의 **현재** `orthographicSize`/`aspect`를 읽어 그
뷰를 완전히 덮도록 스케일을 그때그때 계산해(하드코딩 크기값 없음) `sortingOrder=-100`으로
맨 뒤에 까는 `SpriteRenderer`를 만든다 - 카메라 설정이 스테이지/씬마다 달라도 자동으로
맞는다.

**임포트**: 4개 스테이지 배경을 `Assets/Belief/UI/World/CityBackgrounds/Stage_0N.png`로 복사,
Sprite 임포트 후 `Stage_01~04.asset`의 `cityBackground`에 배선.

**Play Mode 검증**: `Unity_Camera_Capture(cameraInstanceID=Main Camera)`로 확인 - 지도
배경이 카메라 뷰 전체를 정확히 덮고, 그 위에 장소 사진 카드·NPC 스프라이트가 올바른 위치에
겹쳐 보임(1스테이지 성벽 지도 위에 4개 장소 + 5개 NPC가 정확히 자리 잡음). Console
Error/Warning 0. 씬은 런타임 생성이라 별도 변경 없음.

**수정한 파일**: `StageData.cs`(`cityBackground` 필드 추가), `WorldPresenter.cs`
(`CreateCityBackground()` 추가), `Stage_01~04.asset`(배선), 신규 이미지 4개(`Assets/Belief/
UI/World/CityBackgrounds/`).

---

### 3-35. Edit Mode 전용 "월드 레이아웃 미리보기" 에디터 도구 신규 제작 (2026-08-04)

배경이 Play Mode(런타임)에만 생성되기 때문에(`WorldPresenter.Start()`), Edit Mode에서는
배경도 장소/NPC도 전혀 안 보여 사용자가 직접 위치를 눈으로 보며 조정할 방법이 없었다.
`Assets/Belief/Scripts/Editor/WorldLayoutSceneTool.cs` 신규 제작 - Play를 누르지 않아도
Scene 뷰에서 바로 확인·조정 가능한 순수 에디터 도구(런타임 코드/씬 GameObject는 전혀
추가하지 않음, `[InitializeOnLoad]` + `SceneView.duringSceneGui`만 사용).

**기능**:
- 활성 씬의 `GameInstaller.StageAsset`을 찾아 `cityBackground`를 Main Camera 뷰에 맞춰
  Scene 뷰에 그대로 그린다(`CreateCityBackground()`와 동일한 스케일 계산 로직 - 3-34에서
  검증된 것과 동일).
- `StageData.locations` 각각을 노란 구 핸들 + 이름표로 표시하고(`locationLayout` 오버라이드
  우선, 없으면 `LocationData.worldPosition`), **드래그하면 즉시 `StageData.locationLayout`에
  저장**(Undo 지원, `SetDirty` 처리) - 기존 항목은 갱신, 없으면 새로 추가.
- `StageData.npcPlacements`를 `EffectiveStartLocation` 기준으로 그룹핑해 `WorldPresenter.
  ComputeNpcSlot`과 동일한 슬롯 계산으로 초록 점 + 이름표를 그린다(읽기 전용 미리보기 -
  NPC는 위치가 독립 저장되지 않고 소속 장소를 따라 자동 배치되므로, 장소 위치를 옮기면
  NPC 미리보기도 함께 따라간다).
- `Belief > World Layout Preview` 메뉴로 켜고 끌 수 있다(기본 켜짐, `EditorPrefs` 저장).

**검증**: 컴파일 통과, `SceneView.RepaintAll()`로 강제 리페인트해도 Console Error/Warning 0.
`GameInstaller.StageAsset`가 가리키는 실제 데이터(장소 4개+`locationLayout` 4개+NPC 배치
5개)를 직접 조회해 이 도구가 읽는 모든 값이 null 없이 정상 채워져 있음을 확인 - 실제 드래그
동작 자체는 에디터 GUI라 스크린샷 검증이 불가능해 사용자가 직접 확인해야 한다.

**수정한 파일**: `Assets/Belief/Scripts/Editor/WorldLayoutSceneTool.cs`(신규). 씬/런타임
코드는 전혀 건드리지 않았다.

---

### 3-36. NPC를 장소에서 분리 — 개별적으로 자유롭게 드래그 배치 가능하게 변경 (2026-08-04)

3-35 도구가 NPC를 소속 장소에 자동으로 묶어(슬롯 격자 계산) 보여줬는데, 사용자가 "장소 NPC
묶지 말아줘 세부 위치 조정좀 하게"로 요청 - NPC도 장소처럼 독립적으로 드래그해서 세밀하게
배치하고 싶다는 것.

**데이터 레이어**: `StageData.npcLayout`(`NpcLayoutEntry[]`, `LocationLayoutEntry`와 동일한
패턴) 신규 추가 - NPC별 시작 위치를 수동으로 저장한다.

**런타임 로직**: `WorldPresenter.Start()`에 `ApplyManualNpcStartLayout()` 추가 - 초기 배치
(자동 슬롯 계산, `SnapNpcSlots`) 이후에 `npcLayout`에 좌표가 있는 NPC만 그 좌표로 다시
옮긴다. **"시작 배치"에만 적용**되고, 게임 중 실제로 다른 장소로 이동하면(`NpcRelocatedEvent`)
그 시점부터는 기존 자동 슬롯 계산(`RefreshNpcSlots`)을 그대로 따른다 - 즉 이 좌표는 "처음
화면이 어떻게 보일지"만 결정하고 판정/이동 로직에는 전혀 관여하지 않는다.

**에디터 도구 수정**: `WorldLayoutSceneTool.cs`의 NPC 표시를 "장소별로 묶어 슬롯 계산"에서
"NPC마다 독립적인 드래그 핸들"로 전면 변경 - `npcLayout`에 이미 저장된 좌표가 있으면 그걸,
없으면(처음 여는 경우) 자동 슬롯값을 초기 추정치로만 보여주고, 드래그하면 그 순간
`npcLayout`에 실제로 기록된다(Undo 지원).

**검증**: 컴파일 통과. Play Mode 진입 - `npcLayout`이 아직 비어 있는 상태이므로
`ApplyManualNpcStartLayout()`이 사실상 아무 것도 안 바꾸는 no-op임을 확인(기존 자동 배치
그대로 유지), Console Error/Warning 0. 씬 변경 없음.

**수정한 파일**: `StageData.cs`(`npcLayout`/`NpcLayoutEntry` 추가), `WorldPresenter.cs`
(`ApplyManualNpcStartLayout()` 추가), `WorldLayoutSceneTool.cs`(NPC 미리보기를 장소 묶음
→ 개별 드래그 핸들로 변경).

---

### 3-37. 에디터 미리보기를 점 마커 대신 실제 이미지로 변경 (2026-08-04)

3-36의 점(구 핸들) 표시에 대해 사용자가 "점으로 표시해주지 말고 이미지 그대로 보이게 해줘
그래야 세부 위치 조정을 하지"로 요청 - 실제 사진/스프라이트를 봐야 배경과 맞춰 정밀하게
배치할 수 있다는 것.

**수정**: `WorldLayoutSceneTool.cs`를 점 대신 실제 이미지를 그리도록 전면 수정.
- `Handles.FreeMoveHandle`의 캡 함수를 `EventType.Repaint`에서는 아무것도 안 그리게(빈 처리)
  바꾸고, Layout/MouseDown 등 나머지 이벤트는 그대로 `SphereHandleCap`에 위임 - 드래그 피킹은
  살리고 시각적으로는 점을 안 그린다.
- 그 대신 `location.locationPhoto`/`npc.characterPhoto` 스프라이트를 실제 게임 표시 크기
  그대로(장소는 3-34와 동일한 PPU 계산값 그대로, NPC는 `NpcActorView.prefab`의 `body`
  localScale인 1.08배를 추가로 곱함) `GUI.DrawTextureWithTexCoords`로 그린다 - 3-34의 배경
  그리기와 같은 "월드 사각형 → 스크린 좌표 투영" 방식 재사용.
- 사진이 아직 없는 항목(영주 캐릭터, 4스테이지 전용 장소 2곳 등)은 반투명 색 박스로 폴백
  표시(완전히 안 보이는 것보다 낫다).

**검증**: 컴파일 에러 1건(`using System`이 `UnityEngine.Object`와 `System.Object`를
모호하게 만듦) 발견 즉시 수정, 이후 컴파일 통과 + `SceneView.RepaintAll()` 강제 리페인트에도
Console Error/Warning 0.

**수정한 파일**: `WorldLayoutSceneTool.cs`(점 마커 → 실제 이미지 드래그 방식으로 전면 수정).

---

### 3-38. 3-37의 장소 이미지가 Scene 뷰에서 잘려 보이던 문제 수정 (2026-08-04)

사용자가 "장소 이미지가 잘리면 안대 다 보여야대"로 재보고. `sprite.rect`가 텍스처 전체를
정확히 덮고 있는지(트림/패딩 문제 아닌지) 직접 확인해 배제했고, AskUserQuestion으로 Scene
뷰(에디터 도구) 쪽 문제임을 먼저 확정한 뒤 원인을 좁혔다.

**원인으로 판단한 것**: 3-37 구조가 매 항목(배경 1 + 장소 4 + NPC 5)마다 각각
`Handles.BeginGUI()`...`GUI.DrawTextureWithTexCoords()`...`Handles.EndGUI()`를 반복
호출했다 - 그 사이사이에 `Handles.FreeMoveHandle`(3D Handle 좌표계) 호출도 섞여 있어, 한
프레임 안에서 GUI 좌표계와 Handle 좌표계를 여러 번 오가는 구조였다. Unity 공식 권장 패턴은
`BeginGUI`/`EndGUI`를 프레임당 한 번만 감싸는 것 - 여러 번 왔다갔다 하면 일부 항목의 클립
영역이 이전 `EndGUI` 상태를 제대로 못 이어받아 잘려 보일 수 있다.

**수정**: 그리기를 2단계로 분리 - 1) 3D Handle 드래그 처리 + 이름표 표시는 항목마다 즉시
하되, 그릴 텍스처 정보(스프라이트/중심/크기)는 리스트에만 쌓아두고, 2) 모든 항목 처리가
끝난 뒤 `Handles.BeginGUI()`/`EndGUI()`를 **딱 한 번**만 감싸 리스트에 쌓인 걸 전부 그린다.
스프라이트가 없는 폴백 박스도 같은 단일 GUI 패스 안에서 `GUI.DrawTexture(rect,
Texture2D.whiteTexture)` + 색상 틴트로 통일해 그린다(기존에는 `Handles.
DrawSolidRectangleWithOutline`으로 3D Handle 방식이었음 - 검은 테두리는 없어졌지만 잘림
문제 재발 방지를 위해 텍스처 그리기 경로를 하나로 통일했다).

**검증**: 컴파일 통과, `SceneView.RepaintAll()` 강제 리페인트에도 Console Error/Warning 0.
`sprite.rect`(텍스처 트림 여부)는 3개 샘플 장소 이미지 전부 정확히 텍스처 전체(0,0 ~
width,height)를 덮고 있음을 재확인해 UV 계산 자체는 처음부터 문제없었음도 함께 확인했다.

**수정한 파일**: `WorldLayoutSceneTool.cs`(GUI 그리기를 프레임당 1회 배치 처리로 재구성).

---

### 3-39. 에디터 미리보기에 실제 게임과 동일한 프레임·압정·이름표 리본 추가 (2026-08-04)

사용자가 실제 게임 스크린샷("CITADEL GUARD POST" 폴라로이드 프레임 + 빨간 압정 + 하단 NPC
2명 + 이름표 리본)을 보여주며 "이런식으로 표현하고 싶어" - 사진만 덜렁 보여주는 게 아니라
실제 인게임과 동일한 장식까지 다 보고 싶다는 것.

**데이터 확보**: `LocationSiteView.prefab`/`NpcActorView.prefab`에서 `Photo(Body)`/`Frame`/
`Pin`/`NameTag`/`Label` 각각의 `localPosition`/`localScale` 실측값을 전부 뽑고,
`PlayHudSkin_Default`에서 `locationImageFrame`/`npcPhotoFrame`/`pin`/`locationTag3`/
`locationTag5` 스프라이트도 함께 확인 - 전부 그대로 상수로 복제했다(장소 프레임 스케일이
(0.50, 0.31)로 비균일하다는 것도 이번에 실측으로 확인).

**수정**: `WorldLayoutSceneTool.cs`에 `WorldPresenter.skin`(`PlayHudSkin`) 참조를 추가하고,
장소/NPC 각각에 대해 사진(또는 폴백 박스) 뒤에 프레임 → 이름표 리본 → 압정 순서로 겹쳐
그리도록 확장(실제 게임의 시각적 레이어 순서와 동일 - Z값 기준 사진이 제일 뒤, 압정이
제일 앞). 이름표는 이름 길이 3자 이하면 `locationTag3`, 그 초과면 `locationTag5` 스프라이트를
쓴다(실제 `LocationSiteView.Bind()`/`NpcActorView.Bind()`와 동일 분기). 드래그 손잡이는
사진 크기 기준 유지, 이름 텍스트는 이름표 리본 위치에 겹쳐 표시(기존에는 사진 위쪽에
별도로 떠 있었음).

**검증**: 컴파일 에러(초안에 죽은 코드 한 줄) 1건 즉시 제거, 이후 컴파일 통과.
`WorldPresenter.skin`이 Zone1 씬에 실제로 배선되어 있음을 확인(`PlayHudSkin_Default`).
`SceneView.RepaintAll()` 강제 리페인트에도 Console Error/Warning 0.

**수정한 파일**: `WorldLayoutSceneTool.cs`(프레임/압정/이름표 리본 레이어 추가).

---

### 3-40. NPC는 카드 스타일링 제거 — 캐릭터 이미지만 표시 (2026-08-04)

3-39에서 NPC에도 장소와 동일하게 프레임/압정/이름표 리본을 붙였는데, 사용자가 "장소만
프레임이랑 핀 같은 이름표 스타일링 해주면 되고 캐릭터는 그냥 캐릭터 이미지만 있으면
돼"로 범위를 정정 - 카드 스타일링은 장소 전용, NPC는 순수 캐릭터 이미지만.

**수정**: `WorldLayoutSceneTool.cs`의 NPC 처리에서 `npcPhotoFrame`/이름표 리본/`pin` 그리기
호출을 전부 제거하고 캐릭터 이미지(또는 스프라이트 없을 때 폴백 박스)만 남겼다. 이름
라벨은 이미지 위쪽(3-37 방식)으로 되돌렸다. 더 이상 쓰이지 않는 NPC 프레임/압정/이름표
오프셋·스케일 상수 6개도 함께 제거(죽은 코드 방치 금지).

**검증**: 컴파일 통과, `SceneView.RepaintAll()` 강제 리페인트에도 Console Error/Warning 0.

**수정한 파일**: `WorldLayoutSceneTool.cs`(NPC 카드 장식 제거, 미사용 상수 정리).

---

### 3-41. 장소도 프레임 제거 — 사진 아래 압정+이름표만 남기고 재배치 (2026-08-04)

사용자가 다시 정정: "장소도 프레임 압정 이름표 다 필요없네 그냥 장소 이미지 아래에
압정으로 이름표 꽂고 거기에 장소 이름이 나오게 하면 되는거였어" - 프레임 자체가 불필요(사진
원본에 이미 폴라로이드 테두리가 그려져 있어 중복), 압정+이름표는 필요하되 위치를 사진
"아래"로 바꿔야 함(3-39는 프레임 안쪽에 압정을 올리는 실제 게임 프리팹 배치를 그대로
복제해서 사진 위쪽에 있었음).

**수정**: `WorldLayoutSceneTool.cs`에서 `locationImageFrame` 그리기 호출 제거. 이름표 리본을
사진 아랫변에서 살짝 띄운 위치(`LocationNameTagGap`)에 배치하고, 압정은 이름표 리본 윗변에
살짝 겹쳐 꽂힌 것처럼(`LocationPinOverlap`) 그 위에 배치 - 둘 다 사진 중심 X좌표에 맞춰
정렬. 장소 이름 라벨은 이름표 리본 위치에 그대로 표시. 프레임 관련 상수(`LocationFrameOffset`/
`LocationFrameScale`)와 기존 고정 오프셋 상수(`LocationPinOffset`/`LocationNameTagOffset`/
`LocationLabelOffsetY`)는 전부 제거하고, 사진 크기 기준으로 매번 계산하는 방식으로 바꿨다.

**검증**: 컴파일 통과, `SceneView.RepaintAll()` 강제 리페인트에도 Console Error/Warning 0.

**수정한 파일**: `WorldLayoutSceneTool.cs`(장소 프레임 제거, 압정+이름표를 사진 아래로 재배치).

---

### 3-42. 압정 위치 미세조정 — 이름표에 박히지 않고 사진·이름표를 이어주는 위치로 (2026-08-04)

3-41에서 압정을 이름표 리본 윗변에 살짝 겹치게(박힌 것처럼) 배치했는데, 사용자가 시안
스크린샷을 다시 보여주며 "압정이 이름표 중앙에 박는게 아니고 장소 이미지랑 이름표를
이어주는 느낌으로"로 정정 - 압정이 이름표에 박힌 게 아니라, 사진 아랫변과 이름표 윗변 사이
빈 공간에 떠서 둘을 시각적으로 이어주는 위치여야 한다는 것.

**수정**: `WorldLayoutSceneTool.cs`에서 이름표를 사진에서 더 떨어뜨려(압정이 들어갈 공간
`connectorGap = LocationNameTagGap*2 + pinSize.y`만큼 확보) 배치하고, 압정은 "사진 아랫변과
이름표 윗변의 정중앙"(`pinCenterY = (photoBottomY + tagTopY) / 2`)에 위치하도록 변경 - 더 이상
이름표에 겹치지 않고 둘 사이에 떠서 연결하는 모양이 됐다. 더 이상 안 쓰는
`LocationPinOverlap` 상수 제거.

**검증**: 컴파일 통과, `SceneView.RepaintAll()` 강제 리페인트에도 Console Error/Warning 0.

**수정한 파일**: `WorldLayoutSceneTool.cs`(압정 위치를 사진-이름표 사이 중앙으로 조정).

---

### 3-43. 실제 Play Mode와 픽셀 단위로 정확히 일치하도록 재보정 (2026-08-04)

사용자가 실제 Play Mode 스크린샷과 Scene 뷰 미리보기 스크린샷을 나란히 보여주며 "둘이 맞지가
않아 색감도 다르고 그리고 장소 이미지 밑에 바로 딱 붙어야대 이름표가" - 지금까지(3-39~3-42)
는 "시안"(목표 디자인 러프 스케치)을 기준으로 추측 조정해왔는데, 이번엔 실제 게임 화면과
직접 비교당해 추측이 아니라 실측이 필요해졌다.

**Play Mode에서 직접 측정**: `LocationSiteView`/`NpcActorView` 인스턴스의 `SpriteRenderer.
bounds`/`color`를 코드로 직접 읽어 진짜 원인을 찾았다.

1. **놓치고 있던 카드 전체 배율**: `LocationSiteView` 프리팹 **루트 자체의 localScale(1.40,
   0.85)** 와 그 밑 `Decoration` 래퍼의 localScale(0.97, 1.60)이 곱해져 최종적으로 균일
   1.36배(`lossyScale`)가 적용되고 있었다 - 3-39~3-42는 전부 각 요소(Photo/Frame/Pin/
   NameTag)의 "직계 부모 기준" localScale/localPosition만 읽었을 뿐, 그 위에 있는 루트/래퍼
   자체의 스케일은 한 번도 확인하지 않아서 실제보다 훨씬 작게 그려지고 있었다. 이 1.36배를
   전체 오프셋·크기 계산 마지막에 곱하도록 `LocationCardScale` 상수를 추가했다.
2. **색상 틴트 누락**: `LocationSiteView.ApplyBaseColor()`가 사진에 `NormalColor(0.66, 0.63,
   0.58)`를, `NpcActorView.Bind()`가 캐릭터 이미지에 Major(0.68, 0.64, 0.56)/Minor(0.60,
   0.60, 0.62) 톤을 곱하고 있는데(프레임/압정/이름표는 흰색 그대로) 에디터 도구는 전부 원본
   색 그대로 그리고 있었다 - 실측으로 정확한 값을 확인해 `DrawRequest`에 `tint` 필드를 추가,
   사진/캐릭터에만 적용했다.
3. **이름표 위치**: "장소 이미지 밑에 바로 딱 붙어야" 요청은 실측 결과 프레임(사진과 이름표
   사이 간격을 시각적으로 채워주는 큰 장식)까지 포함해서 봐야 자연스럽게 이어져 보인다는 걸
   확인 - 3-41에서 뺐던 프레임을 다시 포함시키고, 압정/이름표 오프셋도 3-39의 원래 프리팹
   실측값(Pin +0.62, NameTag -1.05, 사진 중심 기준)에 `LocationCardScale`만 곱해 그대로
   사용하는 것으로 되돌렸다(3-41/3-42의 "사진과 이름표 사이에 압정을 띄운다" 방식은 실제
   게임과 안 맞는 추측이었음이 이번에 확인됨).

**검증**: 계산된 크기/오프셋을 실측값과 코드로 직접 대조 - Photo/Frame/Pin/NameTag 전부
소수점 둘째 자리까지 일치(예: Photo size 계산 1.23×1.98 vs 실측 1.23×1.97). 컴파일 통과,
`SceneView.RepaintAll()` 강제 리페인트에도 Console Error/Warning 0.

**수정한 파일**: `WorldLayoutSceneTool.cs`(`LocationCardScale` 1.36 상수 추가, 프레임 복원,
압정/이름표 오프셋을 실측 기반으로 재조정, 사진/NPC 색 틴트 추가).

**교훈**: 프리팹 요소의 위치/크기를 코드로 복제할 때는 그 요소의 직계 부모뿐 아니라 **루트부터
전체 부모 체인의 스케일을 전부 곱해야** 한다(`Transform.lossyScale`을 실제로 확인하는 게
가장 안전). 그리고 "이렇게 보일 것 같다"는 추측이 아니라, 가능하면 Play Mode에서 실제
`SpriteRenderer.bounds`/`color`를 직접 읽어 대조하는 쪽이 훨씬 빠르고 정확하다.

---

### 3-44. 3-43과 반대 방향 — 에디터 도구가 아니라 실제 게임 쪽을 고쳤다 (2026-08-04)

3-43 직후 사용자가 "아니 반대로 햇어야대 scene뷰에서 보이던 색감이랑 구조가 맞는거였어" -
Scene 뷰 미리보기를 Play Mode에 맞추는 게 아니라, **Play Mode(실제 게임) 쪽을 Scene 뷰가
보여주던 원래 모습(틴트 없고 1.36배 확대 없는 상태)에 맞춰 고쳐야 했다**는 것. AskUserQuestion
으로 "에디터 도구만 되돌리기" vs "실제 게임까지 고치기" 중 확인 - 실제 게임까지 고치는 쪽으로
확정.

**판단 근거**: 3-43에서 찾은 `NormalColor`/`baseColor` 틴트와 프리팹 루트의 1.36배 스케일은
전부 **실제 사진/캐릭터 아트가 들어오기 전, placeholder 단색 시절에나 의미가 있던 값**이었다
(코드 주석에도 "실제 인물 사진 자산이 없어..." 라고 명시돼 있었음). 진짜 아트가 다 들어온
지금은 이 틴트/확대가 오히려 사진을 흐리고 의도보다 크게 보이게 만드는 leftover 버그였던 것
- Zone1 월드 오버레이(3-31), OuterBackground(3-32) 등 이번 세션에서 반복적으로 발견한
"실제 아트 도입 전 임시 처리가 그대로 남아 문제를 일으킨" 패턴과 동일 계열.

**수정**:
- `LocationSiteView.cs`: `NormalColor`를 `new Color(0.66, 0.63, 0.58)` → `Color.white`로
  변경(Alert/Locked/Highlight/Selection 등 상태 강조용 틴트는 의도된 기능이라 그대로 유지).
- `NpcActorView.cs`: `baseColor`를 Major/Minor별로 다르게 주던 것을 전부 `Color.white`로
  통일(더 이상 Major/Minor를 색으로 구분하지 않음).
- `LocationSiteView.prefab`: 프리팹 루트의 `localScale`(1.40, 0.85)과 `Decoration` 래퍼의
  `localScale`(0.97, 1.60)을 둘 다 `(1,1,1)`로 초기화 - 곱해서 균일 1.36배였던 숨은 확대를
  제거.
- `WorldLayoutSceneTool.cs`: 3-43에서 추가했던 `LocationCardScale`(1.36) 배율과
  `LocationPhotoTint`/`NpcMajorTint`/`NpcMinorTint` 틴트를 전부 제거 - 이제 실제 게임 쪽이
  고쳐졌으므로 에디터 도구는 원본 그대로(배율/틴트 없이) 그리기만 하면 자동으로 일치한다.

**검증**: Play Mode에서 재측정 - Location Photo `bounds.size=(0.90, 1.45)`(3-34에서 원래
의도했던 정확한 목표 크기와 일치), `color=(1,1,1,1)`(흰색, 틴트 없음). NPC body
`bounds.size=(1.08,1.08)`, `color=(1,1,1,1)`. `Unity_Camera_Capture`로 실제 Main Camera
화면을 다시 캡처해 색이 선명해지고 사진 크기가 원래(작은) 비율로 돌아온 것을 육안으로도
확인. Console Error/Warning 0. 씬 변경 없음(`isDirty=False`).

**수정한 파일**: `LocationSiteView.cs`(`NormalColor`), `NpcActorView.cs`(`baseColor`),
`LocationSiteView.prefab`(루트/`Decoration` 스케일 초기화), `WorldLayoutSceneTool.cs`
(1.36배·틴트 제거, 원본 그대로 그리기).

---

### 3-45. 장소 카드 프레임/압정/이름표 위치를 하드코딩 대신 프리팹에서 실시간으로 읽도록 변경 (2026-08-04)

사용자가 앞으로 `LocationSiteView.prefab`의 Frame/Pin/NameTag Position/Scale을 직접
Inspector에서 조정하겠다고 결정 - "Scene 미리보기 도구에서 내가 조정하는게 보이게 해줘
코드로 박아놓으면 내가 확인하면서 조정을 할 수가 없자나". 3-39~3-44에서 상수로 박아둔
`LocationFrameScale`/`LocationPinOffset`/`LocationPinScale`/`LocationNameTagOffset`/
`LocationNameTagScale`/`LocationPhotoOffsetY`는 프리팹이 바뀌어도 절대 따라가지 않는
구조라 이 워크플로우 자체가 불가능했다.

**수정**: `WorldLayoutSceneTool.cs`에 `ReadLocationCardLayout()` 추가 - 매 `OnSceneGUI` 호출마다
`LocationSiteView.prefab`을 `AssetDatabase.LoadAssetAtPath`로 다시 읽어 `Decoration/Photo`,
`Decoration/Frame`, `Decoration/Pin`, `Decoration/NameTag`의 현재 `localPosition`/
`localScale`을 그대로 가져온다(프리팹 루트와 `Decoration` 래퍼 자신의 `localScale`도 성분별로
곱해 반영 - 3-43에서 확인했듯 둘 다 회전 없는 축 정렬 스케일이라 곱셈만으로 정확하다). 기존
하드코딩 상수는 전부 제거하고 `HandleLocationItems`가 이 실시간 값을 받아 쓰도록 변경.

**사용 방법**: `LocationSiteView.prefab`을 열어 Frame/Pin/NameTag/Photo 위치·크기를 조정하고
저장(또는 Prefab Mode의 Auto Save 켜기)하면, Zone1 Scene 뷰의 미리보기가 다음 리페인트에
바로 최신값을 반영한다 - Play 안 눌러도 즉시 확인 가능.

**검증**: 프리팹에서 직접 값을 재조회해 기존에 파악해 둔 실측값과 정확히 일치함을 확인
(Photo localPos=(0,0.06), Frame localScale=(0.50,0.31), Pin localPos=(0,0.62) 등, 루트/
Decoration 모두 3-44에서 이미 (1,1,1)로 초기화됨). 컴파일 통과, `SceneView.RepaintAll()`
강제 리페인트에도 Console Error/Warning 0. 씬 변경 없음.

**수정한 파일**: `WorldLayoutSceneTool.cs`(`ReadLocationCardLayout()` 추가, 하드코딩 상수
전부 제거).

---

### 3-46. Prefab Mode 편집 중에는 저장 전 상태도 실시간으로 반영되도록 수정 (2026-08-04)

3-45 직후 사용자가 실제로 `LocationSiteView.prefab`을 Prefab Mode(Auto Save 켜진 상태)로
열어 `Pin`을 선택해 보여주며 "내가 인스펙터에서 조절하면 바로 이게 보여야대" - 저장된 뒤가
아니라 슬라이더를 움직이는 바로 그 순간 반영돼야 한다는 것.

**직접 검증해서 찾은 문제**: `AssetDatabase.LoadAssetAtPath`는 프리팹이 실제로 디스크에
저장된 뒤에야 새 값을 돌려준다 - Prefab Mode에서 값을 바꾼 직후(저장 전) 같은 경로를 다시
읽어보면 옛날 값 그대로임을 코드로 직접 확인했다. Auto Save가 켜져 있어도 저장은 약간의
지연을 두고 일어나므로, 3-45 방식으로는 "바로" 반영되지 않는다.

**수정**: `ReadLocationCardLayout()`이 `PrefabStageUtility.GetCurrentPrefabStage()`로 지금
Prefab Mode로 열려 있는 스테이지가 있는지, 그리고 그게 `LocationSiteView.prefab`인지 확인한다
- 맞으면 저장된 에셋이 아니라 **편집 중인 라이브 콘텐츠**(`stage.prefabContentsRoot`)를 직접
읽는다(저장 여부와 무관하게 항상 최신). Prefab Mode가 아니면(Zone1을 그냥 보고 있을 때) 기존
그대로 저장된 에셋을 읽는다.

**검증**: Prefab Mode에서 Pin 위치를 코드로 직접 바꾼 뒤 저장 없이 `AssetDatabase.
LoadAssetAtPath`로 다시 읽으면 옛날 값이 나온다는 것을 재현 확인(문제 원인 확정) →
`PrefabStageUtility` 경로로 전환 후 컴파일 통과, `SceneView.RepaintAll()` 강제 리페인트에도
Console Error/Warning 0. (참고: 스크립트 재컴파일 때문에 Prefab Mode 스테이지 자체가 닫혀
후속 실사용 재현 테스트는 사용자가 직접 다시 열어 확인해야 한다 - 코드 경로 자체는 검증
완료.)

**수정한 파일**: `WorldLayoutSceneTool.cs`(`ReadLocationCardLayout()`이 열린 Prefab Stage를
우선 사용하도록 변경).

---

### 3-47. Frame 완전 삭제 — 미리보기도 자동으로 따라가게 처리, 재컴파일이 삭제를 되돌린 사고 수습 (2026-08-04)

사용자가 Prefab Mode에서 `Decoration/Frame`을 직접 삭제해보고 "프레임이 뭐야? 없애니까
갑자기 이렇게 바껴 프레임만 지우고 싶어" - 스크린샷 두 장 비교: 삭제 전엔 사진 위에 폴라로이드
카드가 하나 더 겹쳐 있는 것처럼 보였는데(사진 자체 아트에 이미 종이집게+카드 테두리가
그려져 있는데, `Frame`(`PlayHudSkin.locationImageFrame`)이 똑같은 폴라로이드+집게 장식을
한 번 더 겹쳐 그리고 있었던 것), 삭제 후엔 깔끔하게 카드 하나만 남음 - 3-41에서 "사진 자체에
이미 폴라로이드 테두리가 그려져 있어 프레임이 중복"이라고 판단했던 것과 정확히 같은 원인.

**실제 게임 쪽 안전성 확인**: `LocationSiteView.cs`의 `Bind()`는 `if (frame != null && skin
!= null) frame.sprite = ...`로 이미 null 가드가 되어 있어 Frame을 통째로 지워도(참조가
자동으로 null이 됨) 에러 없이 안전하게 동작함을 코드로 확인.

**사고와 수습**: 확인차 재조회했더니 사용자가 지운 Frame이 프리팹에 다시 남아있었다 - 방금
전(3-46) 내가 코드를 고치며 발생시킨 재컴파일이, Auto Save가 디스크에 채 쓰기 전에 Prefab
Mode 세션을 끊어버려 삭제가 저장되지 않고 되돌아간 것으로 보인다. 사용자가 다시 지우게
하는 대신 이번엔 내가 직접(`PrefabUtility.LoadPrefabContents` → `DestroyImmediate` →
`SaveAsPrefabAsset`) Frame을 삭제해 반영했다.

**에디터 도구 수정**: `LocationCardLayout`에 `hasFrame` 플래그 추가 - `ReadLocationCardLayout()`
이 `Decoration/Frame`이 없으면 `hasFrame=false`를 돌려주고, `HandleLocationItems`는
`hasFrame`일 때만 프레임을 그린다. 이제 프리팹에서 Frame을 지우거나 다시 추가하거나 하면
미리보기도 자동으로 따라간다(하드코딩된 "항상 그린다"가 아님).

**검증**: 삭제 후 저장된 프리팹을 재조회해 `Decoration` 자식이 `Photo`/`NameTag`/`Pin` 3개만
남았음을 확인. Play Mode에서 Console Error/Warning 0(참조 null 가드 정상 동작). 컴파일 통과.
씬 변경 없음.

**수정한 파일**: `LocationSiteView.prefab`(`Decoration/Frame` 삭제), `WorldLayoutSceneTool.cs`
(`hasFrame` 플래그로 프레임 그리기를 프리팹 상태에 따라 자동 반영).

---

### 3-48. 실제 게임에서도 NPC 프레임/압정/이름표 삭제 - 캐릭터 이미지만 남기기 (2026-08-04)

3-40에서 에디터 미리보기 도구에서만 NPC의 프레임/이름표 리본/압정을 뺐었는데, **실제
게임(`NpcActorView.prefab`)에는 손대지 않았었다** - 사용자가 실제 Play 화면 스크린샷을
보여주며 "캐릭터에 지금 프레임이랑 핀이 있어 캐릭터는 캐릭터 이미지만 있으면 돼"로 실제
게임 쪽도 똑같이 정리해 달라고 요청.

**수정**: `NpcActorView.prefab`에서 `Frame`/`Pin`/`NameTag` 3개 오브젝트를 전부 삭제(`Label`
(이름 텍스트)과 `DialogueBubble`(대사창)은 무관한 기능이라 그대로 유지). `NpcActorView.cs`의
`Bind()`가 이미 `if (frame != null && ...)` 같은 null 가드로 되어 있어 코드 수정 없이 안전.

**검증**: Play Mode에서 라이브 인스턴스의 자식 목록을 직접 조회해 `Label`/`DialogueBubble`/
`Background`만 남고 `Pin`/`Frame`/`NameTag`가 없음을 확인. `Unity_Camera_Capture`로 실제
화면도 확인 - 스크린샷에서 NPC 근처에 여전히 보이는 빨간 압정은 NPC 것이 아니라 근처
장소 카드의 압정(NPC가 소속 장소 바로 아래에 배치되어 가까워 보일 뿐)임을 라이브 계층
조회로 구분해 확인. Console Error/Warning 0. 씬 변경 없음.

**수정한 파일**: `NpcActorView.prefab`(`Frame`/`Pin`/`NameTag` 삭제).

### 3-49. 장소 이름표에 텍스트가 안 보이던 진짜 원인 — sortingOrder가 아니라 `Label` 위치 자체가 어긋나 있었다 (2026-08-04)

사용자 요청: "장소밑에 태그에 이미지에 맞는 장소 이름이 붙게 데이터 이어줘". 먼저 데이터 배선을
확인했는데 **`LocationSiteView.Bind()`는 이미 정확했다** — `label.text = data.displayName`, `nameTag`
스프라이트도 이름 길이로 올바르게 선택됨(4개 장소 전부 Play Mode에서 실측 확인: "경비 초소"/"시장"/
"여관"/"영주 저택 앞"). 즉 **데이터 연결 문제가 아니었다**.

**1차 오진(틀렸음)**: `Label`(`MeshRenderer`)의 `sortingOrder=0`이 `NameTag`(`SpriteRenderer`,
`sortingOrder=2`)보다 낮아서 리본에 가려진 걸로 판단, `sortingOrder`를 4로 올림. 하지만 스크린샷이
전혀 바뀌지 않아 재검증한 결과 — **이건 진짜 원인이 아니었다**(다만 텍스트가 다른 요소 위에 그려지게
하는 자체는 필요하므로 되돌리지 않고 유지).

**진짜 원인**: `Label`의 `localPosition.y`(-1.68)가 `NameTag`의 `localPosition.y`(-0.54)와 전혀
안 맞았다 — 월드 좌표로 환산하면 `Label`이 `NameTag`보다 **1.14 유닛 아래**, 리본 밖 완전히 빈
공간에 렌더링되고 있었다(`NameTag` bounds Y: -3.08~-2.80, `Label` 위치 Y: -4.08). **3-44에서
`LocationSiteView.prefab`의 루트 `localScale`을 (1.40, 0.85, 1)→(1,1,1)로 되돌렸을 때, `Decoration`
밑이 아니라 **루트 바로 밑 자식**인 `Label`은 그 스케일 보정의 영향을 받지 않았고, 원래 옛 스케일
기준으로 잡혀 있던 `Label`의 `localPosition.y` 값만 그대로 남아 어긋난 것** — 3-3 문서에 이미 기록된
"placeholder 시절 잔재" 패턴과 같은 종류의 버그다([[project_placeholder_era_leftovers_pattern]] 참고,
다만 이번엔 스케일이 아니라 위치값이 잔재로 남은 케이스).

**수정**: `LocationSiteView.prefab`에서 `Label.localPosition.y`를 `-1.68` → `-0.54`(= `NameTag`의
`localPosition.y`와 동일)로 변경.

**검증**: Play Mode 재진입 후 4개 장소 전부 `Label.position.y == NameTag.position.y`로 정확히 일치
확인(예: 여관 -2.944355로 둘 다 동일). `Unity_Camera_Capture`(카메라 인스턴스ID 조회)가 이번 세션
내내 "No GameObject found with Instance ID" 에러로 실패해 대신 `Unity_SceneView_Capture2DScene`으로
전환 — 이 캡처 도구는 `(worldX, worldY)`가 중심이 아니라 **캡처 사각형의 좌하단 모서리**라는 것을
실측으로 확인(문서화되지 않은 동작, 처음엔 중심으로 착각해 여러 번 빈 배경만 캡처함). 좌표를
`Photo`/`NameTag` bounds 중심 기준으로 역산해 정확히 프레이밍한 스크린샷에서 "영주 저택 앞" 텍스트가
리본 위에 정확하게 렌더링되는 것을 육안으로 확인. Console Error/Warning 0(실제 게임 로그 기준 —
캡처 도구 자체의 파라미터 실수로 난 에러 3건은 게임과 무관). 씬은 `isDirty=False`(변경 사항 전부
프리팹에만 존재, 씬 저장 불필요).

**수정한 파일**: `LocationSiteView.prefab`(`Label.sortingOrder` 0→4, `Label.localPosition.y` -1.68→-0.54).

### 3-50. 장소 이름표 텍스트 자동 크기 조절 — 이름 길이와 무관하게 리본 폭에 맞춰 자동 스케일 (2026-08-04, 3-50a에서 방식 정정됨)

사용자 요청: 이름 길이(2~8자, 4개 스테이지 전체 실측 완료)에 관계없이 이름표 밖으로 안 나가게,
폰트/크기는 알아서 판단해서 잘 보이게 자동 조절. 기존엔 `Label`(`TextMesh`)이 고정 스케일이라
짧은 이름("여관", 2자)은 리본 대비 작게(폭 채움 32%), 긴 이름은 상대적으로 크게 보이는 등 이름마다
일관성이 없었다.

**1차 구현(→ 3-50a에서 정정됨)**: 이 장소 자신의 실제 이름 폭을 매번 측정해 리본 폭의 70%를 채우도록
개별 스케일을 계산하는 방식으로 처음 구현했다. 결과적으로 이름마다 서로 다른 폰트 크기가 나왔는데
(예: "여관" 2자는 배율 ~2.1배, "영주 저택 앞" 7자는 배율 ~1.18배), 사용자가 "폰트 크기는 통일되어야지
글자수마다 다르면 안 된다"고 정정 — 장소마다 폰트 크기가 다른 건 원한 게 아니었다.

**최종 방식(3-50a)**: `LocationSiteView.cs`에 **정적(static) 캐시** `cachedUniformFitScaleMultiplier`를
추가 — 이 장소 자신의 실제 이름이 아니라 **게임 전체에서 가장 긴 장소 이름 하나**(`WorstCaseReferenceName`
= "알현실 앞 광장", 2026-08-04 기준 4개 스테이지 전체 실측 결과 8자로 최장, Stage_04)를 기준으로
"이 정도 폰트 크기면 가장 긴 이름도 가장 넓은 리본(`locationTag5`)의 85%를 넘지 않는다"는 스케일을
**딱 한 번만** 계산해서 모든 `LocationSiteView` 인스턴스가 동일하게 재사용한다. 계산 방법
(`ComputeUniformFitScaleMultiplier`): `label.text`를 최장 이름 문자열로 잠깐 바꿔치기해
`MeshRenderer.bounds.size.x`로 실제 렌더 폭을 측정한 뒤 원래 텍스트로 즉시 복원(한 프레임 내에서
끝나 화면 깜빡임 없음), `skin.locationTag5.bounds.size.x * nameTag.transform.lossyScale.x`로 리본의
실제 월드 폭을 계산해 목표 폭(85%)에 도달하는 배율을 역산한다. 배율은 `LabelMinScaleMultiplier`(0.5)~
`LabelMaxScaleMultiplier`(2.5)로 클램프. TextMeshPro로 교체하지 않고 기존 legacy `TextMesh` 그대로
계산만으로 해결.

⚠️ **기술 부채**: 최장 이름 문자열이 코드에 하드코딩돼 있다 — 나중에 이보다 더 긴 장소 이름이
추가되면 `WorstCaseReferenceName` 상수도 함께 갱신해야 안전(안 하면 그 이름만 살짝 넘칠 수 있음,
다른 장소는 영향 없음).

**검증**: Play Mode 재진입 후 4개 장소 전부 `label.localScale`이 완전히 동일한 값
`(0.03, 0.06, 0.59)`으로 통일된 것을 확인(짧은 이름/긴 이름 관계없이). 폭 채움 비율은 자연히
이름마다 다르지만(여관 40%, 시장 39%, 경비초소 54%, 영주저택앞 72%) 전부 100% 미만으로 넘치지
않음. 짧은 이름("여관")과 Zone1 기준 최장 이름("영주 저택 앞", 7자) 둘 다 스크린샷으로 동일한
폰트 크기, 리본 안에 자연스러운 여백을 두고 렌더링되는 것을 육안 확인. Console Error/Warning 0.
씬 변경 없음(스크립트만 수정, 프리팹/씬은 무수정).

**수정한 파일**: `LocationSiteView.cs`(`FitLabelToNameTag()`/`ComputeUniformFitScaleMultiplier()`
신규, `Bind()`에서 호출 추가, `labelBaseLocalScale`/`labelBaseScaleCaptured`/
`cachedUniformFitScaleMultiplier`(static) 필드 추가, 튜닝 상수 4개 추가).

### 3-51. NPC가 실제 위치가 아니라 엉뚱한 곳에 서 있는 버그 — `ApplyManualNpcStartLayout`이 이미 이동한 NPC를 옛 수동 좌표로 되돌리고 있었다 (2026-08-04)

사용자가 스크린샷으로 지적: 미션 로그엔 "경비대장이 여관으로 이동함"이라고 나오는데, 실제 화면에선
경비대장(및 다른 NPC 몇몇)이 어느 장소 카드와도 상관없는 빈 공간에 서 있었다.

**원인**: 3-36에서 추가한 `StageData.npcLayout`(에디터 도구로 드래그해 잡은 "이 NPC의 시작 위치")이
`WorldPresenter.Start()`에서 `SnapNpcSlots()`(현재 게임 상태 기준 올바른 격자 위치 계산) 바로 뒤에
무조건 실행돼 그 결과를 덮어썼다. `npcLayout`에 저장된 좌표는 "이 NPC가 원래 있던(홈) 장소" 근처로
잡힌 값인데, **미션 평가가 `GameInstaller.Awake()` 안에서 `WorldPresenter.Start()`(이벤트 구독 시점)보다
먼저 끝나기 때문에**, 턴 1 시작과 동시에 NPC를 자동으로 이동시키는 미션 연출이 있으면 그 NPC는 씬이
보이기도 전에 이미 다른 장소로 옮겨간 상태다. 이 경우 `ApplyManualNpcStartLayout`이 "옛 홈 장소 근처"
좌표를 그대로 적용해버려, 실제 상태(여관에 있음)와 화면(경비 초소 근처 빈 공간에 고정)이 어긋났다.
실측 확인: `Stage_01.npcLayout`의 경비대장 좌표 `(1.45, 2.91)`은 "경비 초소" 위치 `(2.06, 3.17)`
바로 옆이고, "여관" 위치는 `(1.00, -1.73)`으로 전혀 다른 곳이었다.

**해결**: `WorldPresenter.ApplyManualNpcStartLayout()`에 `FindNearestLocation(Vector2)` 헬퍼를 추가해,
각 `npcLayout` 항목의 좌표가 "가리키는" 장소(가장 가까운 장소)와 그 NPC의 실제 현재 위치
(`NpcState.CurrentLocation`)를 비교한다. 둘이 다르면 — 즉 이 NPC가 시작 시점에 이미 옛 수동 좌표가
가리키던 장소를 벗어난 상태라면 — 수동 좌표 적용을 건너뛰고, 바로 위에서 `SnapNpcSlots`가 계산해 둔
(실제 현재 장소 기준) 격자 위치를 그대로 둔다. 둘이 같으면(대부분의 경우, 아직 홈 장소에 그대로 있는
NPC) 기존처럼 수동 좌표를 적용해 보기 좋게 배치한다.

**검증**: 리플렉션이 차단돼 있어(`UNAUTHORIZED_NAMESPACE`) `WorldPresenter`에 임시 테스트 훅
(`__TestApplyManualLayout`/`__TestSnap`, public 래퍼)을 잠깐 추가해 직접 호출로 검증한 뒤 즉시 제거했다.
Play Mode에서 경비대장을 코드로 강제 이동(경비 초소→여관, `NpcMovementService.MoveTo`와 동일하게
`PresentNpcs` 갱신 + `CurrentLocation` 변경)시킨 뒤 두 메서드를 재실행한 결과: 수정 전이었다면
옛 좌표 `(1.45, 2.91)`(경비 초소 근처)로 돌아갔을 것이 수정 후엔 `(2.15, -3.53)` — 정확히 여관 위치
`(1.00, -1.73)` 기준 `SnapNpcSlots`가 계산한 격자 슬롯(오프셋 `(0,-1.8)` + 2인 슬롯 중 2번째 열)으로
확인됨. Console Error/Warning 0. 씬 변경 없음(스크립트만 수정, 테스트 훅은 검증 후 제거해 최종 파일엔
남지 않음).

**수정한 파일**: `WorldPresenter.cs`(`ApplyManualNpcStartLayout()`에 실제 위치 비교 후 스킵 로직 추가,
`FindNearestLocation()` 신규).

### 3-52. NPC가 이동은 정상인데 도착 위치가 장소 이미지와 너무 멀어 보임 — 슬롯 오프셋이 예전 카드 크기·NPC 프레임 기준으로 남아있던 leftover (2026-08-04)

3-51 직후 사용자가 재확인: "지금 npc들이 이동해서 도착하는 위치가 장소이미지랑 어긋나있는거지" — 특정
NPC의 도착 장소 자체는 맞는데(3-51에서 검증한 그대로), 도착한 자리가 장소 이미지에서 시각적으로 너무
멀리 떨어져 보인다는 지적. 실제 프로덕션 이동 코드(`NpcMovementService.MoveTo`+`NpcRelocatedEvent`
발행, 직접 상태 조작이 아닌 진짜 경로)로 재현한 결과도 동일하게 확인 — 경비대장이 여관으로 이동하면
위치 데이터(`CurrentLocation`)는 정확히 "여관"인데, 화면상 좌표 `(2.15, -3.53)`는 여관 카드 위치
`(1.00, -1.73)`에서 세로로 1.8, 대각선으로 약 2.1유닛이나 떨어진 곳이었다(카드 자체 세로 길이가
1.46인데 그보다 더 먼 거리).

**원인**: `WorldPresenter.cs`의 `NpcSlotOffset=(0,-1.8)`/`NpcHorizontalSpacing=2.3` 두 상수 모두 이번
세션 초반의 다른 수정으로 전제가 깨진 leftover였다([[project_placeholder_era_leftovers_pattern]]과
동일 패턴, 이번엔 색·스케일이 아니라 "간격" 상수). 기존 코드 주석 근거:
- "장소 카드가 커진 만큼(세로 2.5~3유닛) 카드 아래로 더 떨어뜨린다" → 그런데 3-44에서 `LocationSiteView`
  프리팹의 숨은 1.36배 스케일을 제거해 카드가 원래 크기로 되돌아갔다(NameTag 하단이 루트 기준 0.686
  아래일 뿐, 2.5~3유닛이 아니다). "커진" 전제가 이미 없어졌는데 오프셋(-1.8)은 그대로 남아있었다.
- "NPC 프레임 장식끼리 맞닿아 보인다" → 그런데 3-40에서 NPC의 Frame/Pin/NameTag를 전부 삭제해
  캐릭터 이미지만 남았다(장식 자체가 없으므로 "맞닿는" 문제도 이미 사라짐). 간격(2.3)은 그대로 남아있었다.

**해결**: 실측(NPC 스프라이트 반높이 0.54, NameTag 하단이 장소 루트보다 0.686 아래)을 기준으로
"NameTag 바로 아래에 작은 여백만 두고 붙는" 값으로 재계산 — `NpcSlotOffset` `(0,-1.8)`→`(0,-1.3)`,
`NpcHorizontalSpacing` `2.3`→`1.4`(NPC 폭 1.08 기준 적당한 간격). `NpcVerticalSpacing`(1.1, 행간)은
현재 최대 인원(장소당 최대 2명 확인됨)에서 문제가 없어 그대로 둠. 스테일해진 주석도 실측값 기준으로
재작성.

**검증**: 3-51과 동일한 방식으로 프로덕션 이동 코드를 재실행 — 경비대장 `(1.70, -3.03)`, 여관 주인
`(0.30, -3.03)`, 여관 카드 `(1.00, -1.73)` 기준으로 훨씬 가까워짐(세로 거리 1.3, 대각선 약 1.4~1.5).
`Unity_SceneView_Capture2DScene`으로 실제 스크린샷 확인 — 두 NPC가 여관 이름표 바로 아래에 자연스럽게
붙어 있는 것을 육안 확인(수정 전 스크린샷과 뚜렷이 대조됨). Console Error/Warning 0. 씬 변경 없음.

**수정한 파일**: `WorldPresenter.cs`(`NpcSlotOffset`/`NpcHorizontalSpacing` 상수값 재보정, 스테일 주석
갱신).

### 3-53. NPC 배치를 "카드 아래 격자"에서 "카드 좌우 플랭킹"으로 변경 (2026-08-04, 사용자 지시)

사용자 지시: 3-52로 카드와의 거리는 가까워졌지만, 여전히 "아래" 배치였다. 대신 장소 이미지 바로
좌/우에 붙게 하고, 3명 이상 모이면 좌우로 한 명씩 더 바깥에 붙는 방식으로 바꿔 달라는 요청.

**구현**: `WorldPresenter.ComputeNpcSlot()`의 격자(행/열) 계산을 걷어내고, `PresentNpcs`의 인덱스를
"좌/우 + 몇 번째로 바깥쪽인지"로 직접 매핑하는 방식으로 교체했다 - 인덱스 0=오른쪽 첫 칸, 1=왼쪽
첫 칸, 2=오른쪽 둘째 칸(더 바깥), 3=왼쪽 둘째 칸, ... (`side = index%2==0 ? +1 : -1`,
`slot = index/2`). 가로 오프셋은 실측값으로 계산: `PhotoHalfWidth`(0.45, `LocationSiteView`의
`Photo` 스프라이트 실측 반폭) + `NpcFlankGap`(0.12) + `NpcHalfWidth`(0.54, NPC 스프라이트 실측
반폭) = 첫 칸 거리 1.11, 이후 칸마다 `NpcHalfWidth*2+NpcFlankGap`(1.20)씩 더 바깥으로. 세로는
사진 중심이 아니라 살짝 아래(발밑 쪽 정렬, `NpcFlankVerticalOffset=-0.19` = 사진 반높이 0.73 −
NPC 반높이 0.54)로 소폭 내렸다. 기존 `NpcMaxPerRow`/`NpcVerticalSpacing`/`NpcSlotOffset` 등 격자
관련 상수·로직은 전부 삭제(더 이상 여러 행으로 안 쌓이므로 불필요).

**검증**: 실제 프로덕션 이동 코드(`NpcMovementService.MoveTo`+`NpcRelocatedEvent` 발행)로 5명
전원을 "여관" 한 곳에 모이게 한 뒤 좌표 확인 — 여관 위치 `(1.00,-1.73)` 기준 상대 오프셋이 정확히
`±1.11`(1번째 칸 좌우 한 쌍), `±2.31`(2번째 칸 좌우 한 쌍, 1.11+1.20), `+3.51`(3번째 칸, 5번째
NPC라 오른쪽에만) — 설계한 공식과 정확히 일치. `Unity_SceneView_Capture2DScene` 스크린샷으로 5명이
장소 이미지 좌우에 나란히 붙어 겹치지 않고 자연스럽게 늘어선 것을 육안 확인. Console Error/Warning 0.
씬 변경 없음(스크립트만 수정).

**수정한 파일**: `WorldPresenter.cs`(`ComputeNpcSlot()` 좌우 플랭킹 방식으로 재작성, 격자 관련 상수
삭제, `PhotoHalfWidth`/`NpcHalfWidth`/`NpcFlankGap`/`NpcFlankBaseOffset`/`NpcFlankStep`/
`NpcFlankVerticalOffset` 신규).

### 3-54. NPC를 장소 이미지에 완전히 붙게(살짝 겹침 허용) 재조정 (2026-08-04, 3-53 보완)

3-53으로 좌우 배치는 됐지만 사용자가 참고 스크린샷을 보여주며 더 가깝게, 완전히 붙어서 살짝 겹쳐도
된다고 요청(사진↔NPC, NPC↔NPC 둘 다).

**해결**: `NpcFlankGap`을 여백(+0.12) 대신 겹침(−0.15)으로 부호를 바꿨다 — `NpcFlankBaseOffset`/
`NpcFlankStep` 계산식은 3-53과 동일하게 그대로 두고 이 상수 하나만 바꿔서, 사진 가장자리와 NPC
가장자리가 0.15유닛만큼 겹치고, NPC끼리도 서로 0.15유닛씩 겹치도록 만들었다(계산식을 건드리지
않고 파라미터 하나로 "여백↔겹침"을 뒤집을 수 있게 3-53에서 이미 그렇게 설계해 둔 덕분에 수정
범위가 최소화됨).

**검증**: 3-53과 동일하게 5명 전원을 프로덕션 이동 코드로 "여관"에 모은 뒤
`Unity_SceneView_Capture2DScene` 스크린샷 확인 — 사용자가 보여준 참고 이미지처럼 NPC들이 장소
이미지·서로와 자연스럽게 겹쳐 붙어 있는 것을 육안 확인. Console Error/Warning 0. 씬 변경 없음.

**수정한 파일**: `WorldPresenter.cs`(`NpcFlankGap` 0.12→−0.15).

### 3-55. NPC 캐릭터 idle/걷기(move) 애니메이션 추가 (2026-08-04)

사용자 요청: `C:\Users\CHJ\Desktop\장소&npc\캐릭터` 폴더의 걷기 사이클 스프라이트를 이용해
idle/move 애니메이션 구현. 폴더 구조: NPC별로 `{이름}_최종.png`(정지 초상화, 이미 `characterPhoto`로
연결돼 있음 = idle) + `{이름}_스프라이트_최종.png`(걷기 사이클 시트, 흰 배경처럼 보이지만 실제로는
**투명 배경**, 대략 6x6 격자로 생성된 여러 프레임).

**폴더 → NpcData 매핑**: 폴더명(한글 역할명)과 `NpcData.displayName`으로 17개 폴더 중 16개를
실사용 `NpcData`에 매칭(예: `경비대장`→`Npc_Major_GuardCaptain`, `기사단장`→
`Npc_Major_KnightCommander`, `경비병`→`Npc_Major_LowRankGuard`(파일명이 "경비병"이라 표기됐지만
실제 그림은 창+방패를 든 하급 경비병 아트와 일치 확인)). 제외: `공용시민`/`귀족` 폴더(대응하는
`NpcData` 자산 없음, 배경용 범용 NPC로 추정 - 이번 범위 밖), `영주`(art 자체가 없음 - 기존에
이미 알려진 gap, [[project_placeholder_era_leftovers_pattern]] 무관).

⚠️ **`정보원` 폴더는 처음에 매칭했다가 되돌림**: `Npc_Major_Informant.asset`은
`Assets/Belief/Data/Npcs/Deprecated/` 밑에 있는 **Deprecated 자산**이다(2026-07-31자
`Deprecated/README.md`에 "어떤 StageData/씬/미션 조건에서도 참조 0건", "정보원은 실제 NPC가
아니라 `TargetingController.DeliverByInformant`를 가리키는 시스템 명칭일 뿐", "필드를 수정하지
말고 원본 그대로 보존" 명시). 처음엔 이 사실을 놓치고 `characterPhoto`+`walkFrames`를 채워
넣었다가, 나중에 자산 경로에 `Deprecated`가 포함된 걸 발견하고 **즉시 원복**(`characterPhoto`→
null, `walkFrames`→빈 배열)했다. 정보원 폴더의 idle/walk 이미지 자체는 `Assets/Belief/UI/World/
Npcs/Npc_Major_Informant.png` + `NpcWalkSheets/Npc_Major_Informant_Walk.png`로 임포트는 돼
있지만(무해하게 방치, 삭제 안 함 - 나중에 정보원을 실제 NPC로 구현할 때 바로 쓸 수 있음), 어떤
`NpcData`에도 연결돼 있지 않다.

**슬라이싱 - 균일 격자 가정이 틀렸음을 발견**: 처음엔 모든 시트가 6x6=36칸일 거라 가정했으나,
`Npc_Major_HeadMaid_Walk.png`(1124×316, 다른 시트들과 가로세로 비율이 확연히 다름)를 직접 열어
확인한 결과 6열×3행에 13개 프레임만 채워진 불규칙한 시트였다 - 균일 그리드 가정을 버리고 **연결
성분(connected-component) 블롭 탐지**로 방식을 바꿨다. 처음엔 "흰 배경"이라는 프롬프트 문구를
믿고 RGB>245를 배경으로 판정했으나 전체가 블롭 1개로 뭉쳐 나와 실패 - 실제로는 **투명 배경**(알파
0)이었음을 픽셀 샘플링으로 확인, `alpha<20`을 배경 판정 기준으로 바꾸니 정상적으로 개별 캐릭터
프레임이 분리됐다.

**구현**:
- `WorldLayoutSceneTool`과 무관한 신규 1회성 에디터 스크립트(RunCommand로만 실행, 프로젝트에
  파일로 남기지 않음) - 각 시트를 읽기 가능한 임시 `Texture2D`로 로드 → 알파 기반 flood-fill로
  블롭 탐지(최소 면적 1200px² 필터로 노이즈 제거) → Y좌표 기준 행 묶음 후 행 내 X좌표 정렬로
  읽기 순서 재구성 → `TextureImporter.spritesheet`에 `SpriteMetaData[]`로 기록(`spriteMode=Multiple`,
  `pixelsPerUnit=500`, `pivot=(0.5,0.5)`, 이름은 `{파일명}_00`~`_35`). 17개 시트 전부 처리(정보원
  포함, 임포트 자체는 해 둠) - 16개는 36프레임, `HeadMaid`만 13프레임으로 정상 슬라이스됨.
- `NpcData.cs`에 `Sprite[] walkFrames` 필드 추가(비어 있으면 하위 호환으로 이동 중에도 idle 사진
  유지). `SerializedObject`로 실사용 16개 자산에 슬라이스된 스프라이트 배열을 순서대로 배선
  (Deprecated 정보원 제외 - 위 참고).
- `NpcActorView.cs`: `MoveRoutine()` 시작 시 `StartWalkCycle()` 호출(걷기 프레임을
  `WalkFrameFps=10`으로 순환 재생하는 별도 코루틴 시작), 이동 종료 시 `StopWalkCycle()`로 원래
  idle 스프라이트+원래 스케일로 복귀. 걷기 프레임은 idle 사진과 크롭 크기(피사체 여백)가 서로
  달라, 매 프레임 `idle 사진의 세로 bounds / 현재 프레임의 세로 bounds`로 균일 스케일 보정값을
  다시 계산해 적용한다(`LocationSiteView.FitLabelToNameTag`와 동일한 원리) - 그래야 걷는 동안
  캐릭터가 갑자기 커지거나 작아 보이지 않는다.

**검증**: Play Mode에서 실제 이동 코드(`NpcMovementService.MoveTo`+`NpcRelocatedEvent`)로 경비대장을
이동시켜 확인 — 이동 시작 직후 `body.sprite`가 `Npc_Major_GuardCaptain_Walk_00`으로, 스케일이
자동 보정(1.08→5.63배, 걷기 프레임 크롭이 idle보다 훨씬 작게 잘려 있어서)되는 것을 확인. 이동
완료(0.35초) 후에는 정확히 원래 idle 스프라이트(`Npc_Major_GuardCaptain`)와 원래 스케일(1.08)로
복귀 확인. 중간 프레임(`_15`)을 수동으로 띄워 `Unity_SceneView_Capture2DScene` 스크린샷으로
확인한 결과 걷는 자세가 찌그러짐 없이 정상 크기로 렌더링됨. Console Error/Warning 0. 씬 변경 없음
(스크립트 2개 + `NpcData` 16개 자산 + 신규 이미지만 변경).

⚠️ **참고**: 현재 `MoveDuration=0.35초`(기존 상수, 이번에 안 건드림)로 이동이 매우 짧아 10fps
기준 프레임 3~4장 정도만 보인다 - 걷기 모션 자체는 정상 작동하지만 순식간에 지나가서 눈에 잘 안
띌 수 있다. 더 잘 보이게 하려면 `MoveDuration`을 늘리는 게 필요할 수 있음(이번 범위 밖, 사용자
판단 필요).

**수정/생성한 파일**: `NpcData.cs`(`walkFrames` 필드), `NpcActorView.cs`(`StartWalkCycle`/
`StopWalkCycle`/`WalkCycleRoutine` 신규, `Bind()`에서 `bodyBaseLocalScale` 캡처 추가),
`Assets/Belief/UI/World/NpcWalkSheets/*.png`(17개 신규, 슬라이스됨 - 정보원 포함되지만 미배선),
`Assets/Belief/UI/World/Npcs/Npc_Major_Informant.png`(신규, 임포트만 해둠 - 미배선),
실사용 `NpcData` 16개 자산(`walkFrames` 배선). `Deprecated/Npc_Major_Informant.asset`은 실수로
잠깐 수정했다가 즉시 원복 - 최종적으로 무수정 상태.

### 3-56. 걷기 애니메이션이 "이동 후 도착지에서 재생"되는 것처럼 보이던 버그 — body 스프라이트가 NpcActorView 루트 자신이라 Label/DialogueBubble까지 같이 스케일되고 있었다 + 이름표 제거 (2026-08-04)

사용자 피드백: "이동할때 애니메이션이 나오면서 이동을 해야되는건데 이동은 그냥 쓱 가버리고 도착한
장소에서 걷는 애니메이션이 나와" + "이름태그가 npc마다 있는거 같은데 얘는 그냥 없애줘".

**원인(애니메이션 타이밍)**: `NpcActorView.prefab`의 `body`([SerializeField] SpriteRenderer)가
별도 자식이 아니라 **NpcActorView 루트 GameObject 자기 자신**이었다 - 즉 `body.transform`이
곧 NPC의 `transform`(이동에 쓰이는 바로 그 Transform)과 동일 객체다. 3-55에서 추가한 걷기 프레임
스케일 보정(`WalkCycleRoutine`이 매 프레임 idle 사진 대비 걷기 프레임 크롭 비율로
`body.transform.localScale`을 재계산)이 이 루트 Transform을 직접 건드리면서, 루트의 **자식인
`Label`/`DialogueBubble`까지 같이 스케일이 튀었다**(부모 스케일이 자식에 곱연산되므로). 이동
시작과 동시에 스케일이 갑자기 1.08배→5배 이상으로 치솟는 시각적 "펑" 튐이 0.35초 짧은 이동
구간 안에서 위치 보간(smooth한 이동)보다 훨씬 눈에 띄어서, 사용자에게는 "위치는 순간 이동하듯
쓱 가버리고, 그 뒤에 걷는 그림이 따로 재생되는" 것처럼 보였다(실제로는 위치 보간 자체는 원래도
정상 작동하고 있었음 - 순수하게 스케일 튐이 만든 착시).

**해결**: `NpcActorView.prefab`을 재구성 - 루트 밑에 새 자식 `Body`(로컬 위치/회전/스케일 전부
기본값)를 만들고, 루트에 있던 `SpriteRenderer`(sprite/color/sortingLayer/sortingOrder 그대로
복사)를 `Body`로 옮긴 뒤 루트의 원본 `SpriteRenderer`는 삭제. `NpcActorView.body` 필드를 새
`Body`의 `SpriteRenderer`로 재배선(코드 변경 없음 - `[SerializeField]` 참조 대상만 바뀜).
`BoxCollider2D`(클릭 판정용)는 루트에 그대로 유지 - 걷기 스케일과 무관하게 항상 일정한 클릭 영역을
유지하는 게 오히려 더 안정적이다. 결과적으로 걷기 애니메이션 스케일 보정은 이제 `Body`
자신에게만 적용되고, 루트 Transform(및 그 자식인 Label/DialogueBubble)은 이동 내내 스케일이
전혀 변하지 않는다.

**이름표 제거**: 같은 프리팹 편집 세션에서 `Label`(장소용과 별개로 NPC 머리 위/아래에 뜨는
이름 텍스트) GameObject를 `SetActive(false)`(삭제 아님, 코드가 여전히 `label.text = ...`를
안전하게 호출하므로 비활성만으로 충분).

**검증**: Play Mode에서 실제 이동 코드로 경비대장을 다른 장소로 이동시켜 확인 - 이동 전/중 루트
`transform.localScale`이 `(1.08, 1.08, 1.00)`으로 전혀 안 변함, `Body.localScale`만 걷기 프레임에
맞춰 `(5.21, 5.21, 5.21)`로 독립적으로 변함. `Label.activeSelf`가 `False`로 확인(모든 NPC 공용
프리팹이라 5명 전부 적용됨). 스크린샷으로 이름표가 안 보이고 캐릭터 크기가 정상인 것도 육안 확인.
Console Error/Warning 0. 씬 변경 없음(프리팹만 수정).

**수정한 파일**: `NpcActorView.prefab`(`Body` 자식 신규 생성 + `SpriteRenderer` 이전,
`NpcActorView.body` 필드 재배선, `Label` 비활성화). `NpcActorView.cs`는 무수정(필드 재배선만으로
해결됨).

### 3-57. idle 숨쉬기 애니메이션 추가 + 이동 시간 확대 + 걷기 프레임 6장만 순환하도록 축소 (2026-08-04, 3-56 직후 사용자 재확인)

사용자가 실제 플레이 후 재지적: "idle상태일때는 그냥 몸이 들썩들썩거리기만 하면 되고, 이동할떄
실제 걸어서 이동하는 듯이 캐릭터가 걸어서 도착지까지 가야대 그리고 지금 애니메이션 프레임이
끊기듯이 나오는데, 부드럽게 보이게 조절해줘 스프라이트 이미지를 굳이 다 써야되는건 아니야". 3-56은
"루트 스케일이 자식까지 끌고 가던 버그"만 고쳤을 뿐, ⓐ idle 상태에 애니메이션이 아예 없었던 점
ⓑ `MoveDuration=0.35초`가 여전히 너무 짧아 걷는 느낌이 안 나던 점 ⓒ `walkFrames` 36장 전체를
순서대로 재생하면 시트가 여러 시도를 이어붙인 구조라 프레임이 튀어 보일 수 있는 점은 그대로
남아있었다.

**해결(`NpcActorView.cs`)**:
- **idle 숨쉬기**: `IdleBobRoutine`(신규) - 추가 스프라이트 없이 idle 사진 그대로 `Body`의
  `localPosition.y`만 사인파(`IdleBobAmplitude=0.045`, `IdleBobPeriod=1.3초`)로 미세하게 흔든다.
  같은 프리팹을 쓰는 NPC들이 전부 같은 박자로 들썩이면 부자연스러워서 시작 위상을
  `Random.Range`로 무작위화. `Bind()` 끝에서 자동 시작, `StartWalkCycle()`이 걷기 시작 직전
  끄고(`StopIdleBobVisualOnly`, `Body.localPosition`을 원위치로 복원), `StopWalkCycle()`이 이동
  종료 후 다시 켠다(`StartIdleBob`).
- **이동 시간 확대**: `MoveDuration` `0.35`→`1.0`초 - 걷기 사이클이 최소 한 바퀴 이상 돌 시간을
  확보해야 "걸어서 이동"하는 것처럼 보인다.
- **걷기 프레임 6장만 순환**: `WalkCycleFrameCount=6`(신규 상수) - `WalkCycleRoutine`이 이제
  `frames[i % Mathf.Min(WalkCycleFrameCount, frames.Length)]`로 앞쪽 6장(슬라이싱이 항상 첫 행부터
  채우므로 사실상 "첫 번째 행" = 한 번의 연속 동작에 가까움)만 순환한다. 나머지 프레임은
  `NpcData.walkFrames`에 그대로 남아있지만(재슬라이싱 안 함, 데이터 손실 없음) 재생에는 안 쓰인다.
  `WalkFrameFps`도 `10`→`8`로 살짝 낮춰 한 바퀴(6장/8fps=0.75초)가 새 `MoveDuration`(1.0초) 동안
  자연스럽게 1바퀴 조금 넘게 돈다.

**검증**: Play Mode에서 `Body.localPosition.y`를 두 번 연속 샘플링해 `0.04`→`0.02`로 계속 변하는
것을 확인(숨쉬기 동작 중). 실제 이동 코드로 트리거한 뒤 `Time.timeScale=0.05`(20배 느리게)로
슬로모션 재생시켜 이동 중간 상태를 직접 캐치 - 걷기 프레임(`Npc_Major_GuardCaptain_Walk_04`, 0~5
범위 내)이 재생되는 동시에 루트 위치가 아직 목적지에 도달하지 않은 중간 좌표였음을 확인(=
"이동은 순간이동, 애니메이션은 도착 후 재생" 버그가 완전히 해소되고 위치 보간과 걷기 애니메이션이
실제로 동시에 진행됨). Console Error/Warning 0. 씬 변경 없음(스크립트만 수정, `NpcData` 에셋도
무수정 - 프레임 개수만 코드에서 제한).

**수정한 파일**: `NpcActorView.cs`(`MoveDuration` 0.35→1.0, `WalkFrameFps` 10→8,
`WalkCycleFrameCount`/`IdleBobAmplitude`/`IdleBobPeriod` 신규 상수, `IdleBobRoutine`/
`StartIdleBob`/`StopIdleBobVisualOnly` 신규, `bodyBaseLocalPosition` 필드 추가,
`WalkCycleRoutine`이 프레임 수를 6장으로 제한하도록 수정).

### 3-58. 결과 패널이 이동 애니메이션 끝나기 전에 뜨는 버그 수정 + 걷기 애니메이션 근본 원인 파악(소스 아트 한계) 후 연속 보간(발걸음 통통 튐)으로 재설계 (2026-08-04, 3-57 직후 실제 플레이 재확인)

사용자가 실제 플레이 후: "지금 결과패널 나오는게 이동이 끝나기 전에 나오는데 캐릭터 이동이 다
끝난 후에 결과가 나와야대 그리고 아직도 애니메이션이 뚝뚝 끊기고 있어" - 두 가지 별개 문제.

**결과 패널 타이밍 버그(`HudPresenter.cs`)**: `GameOverEvent`/`ObjectiveCompletedPendingConfirm`/
`StageCompletedPendingConfirm` 핸들러가 전부 `PlaybackDirector.IsPlaying`(현재 재생 중인 연출이
있는지)을 전혀 확인하지 않고 이벤트가 오는 즉시 `ShowResultScreen`/`ShowGatedPopup`을 호출하고
있었다 - `MoveDuration`이 0.35초였을 땐 거의 안 보였는데, 3-57에서 1.0초로 늘리면서 훨씬 눈에
띄게 됐다. **해결**: 세 핸들러 전부 `StartCoroutine(WaitForPlaybackThen(...))`로 감싸서,
`PlaybackDirector.Instance.IsPlaying`이 `false`가 될 때까지(즉 NPC 이동·걷기 애니메이션을 포함해
현재 재생 중인 모든 연출이 끝날 때까지) 한 프레임씩 대기한 뒤에야 실제 팝업을 띄우도록 변경.
**검증**: `Time.timeScale=0.02`로 슬로모션 재생 중 `GameOverEvent`를 발행해 `ResultScreen.activeSelf`가
`PlaybackDirector.IsPlaying=true`인 동안 계속 `False`로 유지되다가, 애니메이션이 끝나
`IsPlaying=false`가 된 직후에만 `True`로 바뀌는 것을 직접 확인.

**걷기 애니메이션이 여전히 끊겨 보이는 근본 원인 재조사**: 3-57에서 앞쪽 6장(행 1)만 쓰도록
줄였는데도 여전히 끊겨 보인다는 지적에, 행 1과 행 2를 나란히 잘라서 육안 비교해봤다 - **결과:
행 1과 행 2가 사실상 거의 동일한 자세였다.** 즉 이 시트는 "여러 걷기 사이클을 이어붙인 것"이
아니라 애초에 프레임 간 자세 차이가 거의 없는(프롬프트의 "in place" 요청대로) 배치 생성물이다 -
몇 장을 쓰든, 어떤 fps로 돌리든 **스프라이트 교체만으로는 매끄러운 보행처럼 보일 수 없는 게
소스 아트 자체의 한계**였다.

**해결(설계 변경)**: "부드러움"을 스프라이트 교체가 아니라 **연속 보간되는 발걸음 통통 튐**이
담당하도록 재설계했다. `WalkCycleRoutine`을 `WaitForSeconds`(코루틴을 프레임 간격만큼 완전히
멈춤) 방식에서 **매 프레임(`yield return null`) 실행되는 루프**로 바꾸고, 그 안에서 스프라이트
교체는 누적 타이머(`frameTimer`)가 `1/WalkFrameFps`를 넘을 때만 낮은 빈도(5fps)로 하되, `Body`의
`localPosition.y`에는 `Mathf.Abs(Sin(...))` 기반 통통 튐(`WalkBobAmplitude=0.06`,
`WalkBobPeriod=0.28초`)을 **매 프레임 없이 끊김 없이** 얹는다. Idle의 숨쉬기(`Sin` 그대로, 위아래로
고르게 오감)와 의도적으로 다른 파형(`Abs(Sin)`, 0 이상만 - "발이 땅을 딛고 튀어오르는" 느낌)을 써서
두 상태가 구분되게 했다. 스프라이트 교체는 이제 "보조 디테일"일 뿐이고 체감 부드러움은 전부 이
연속 보간이 만든다 - fps나 프레임 개수와 무관하게 항상 매끈하다.

**검증**: `Time.timeScale=0.01`(100배 느리게)로 이동을 트리거한 뒤 짧은 간격으로 두 번 샘플링 -
`Body.localPosition.y`가 `0.060`→`0.005`로 계속 변하고(통통 튐 진행 중), 루트 위치도 계속
전진하고, 스프라이트도 `_00`→`_01`로 넘어가는 것을 동시에 확인(위치·통통 튐·스프라이트 3가지가
전부 동시에, 별개의 리듬으로 진행). Console Error/Warning 0. 씬 변경 없음.

**수정한 파일**: `HudPresenter.cs`(`GameOverEvent`/`OnObjectiveCompletedPending`/
`OnStageCompletedPending` 핸들러를 `WaitForPlaybackThen()` 코루틴으로 감쌈, 해당 메서드 신규),
`NpcActorView.cs`(`WalkFrameFps` 8→5, `WalkBobAmplitude`/`WalkBobPeriod` 신규 상수,
`WalkCycleRoutine`을 매 프레임 실행 루프로 재작성 + `ApplyWalkFrame()` 헬퍼 분리).

### 3-59. "도착 후에도 걷기 애니메이션이 계속 나옴" — walkCycleRoutine 고아 코루틴 버그 발견 및 수정 (2026-08-04, 사용자가 실제 스크린샷 5장 + 실시간 플레이로 재현)

사용자가 스크린샷 5장을 순서대로 보내며 재현 시도 → 확인 결과 그 스크린샷들 자체는 **다른 원인**
(장소 정보 패널이 화면 고정 위치에 뜨는데 마침 "경비 초소"의 화면 좌표와 겹쳐서, 그 장소의
NPC가 카드 없이 "떠 있는" 것처럼 보인 것 - 애니메이션과 무관한 UI 레이어링 이슈, 별도로 사용자에게
보고함)였다. 하지만 사용자가 "그 문제 아니고 지금 실제로 플레이모드 돌리는 중인데 도착 후에도
걷기 애니메이션이 계속 나온다"고 재차 확인 → 같은 Unity 세션에 살아있던 Play Mode를 직접 조회해
**진짜 버그를 확인**했다: NPC 5명 전원이 전혀 움직이지 않는 상태(연속 두 번 샘플링해도
`rootPos` 완전히 동일)인데도 `body.sprite`가 걷기 프레임(`_Walk_04`)에, `body.localScale`도
걷기 보정 배율(5.2배 등)에 **멈춰 있었다** - 진짜로 idle로 복귀하지 못하고 있었다.

**원인**: `AnimateTo()`가 이전 `moveRoutine`을 `StopCoroutine`으로 끊을 때, 그 `moveRoutine`
안에서 `StartWalkCycle()`이 이미 시작해 둔 **`walkCycleRoutine`은 별도의 코루틴이라 같이 끊기지
않는다.** `StopCoroutine`은 코루틴을 그 자리에서 즉시 중단시킬 뿐 남은 코드(`StopWalkCycle()`
호출 포함)를 실행하지 않으므로, 이전 이동이 끝나기 전에(1초 이내에) 같은 NPC가 다시
이동 명령을 받으면 그 이전 `walkCycleRoutine`은 **아무도 멈추지 않는 고아 코루틴**이 되어
`while(true)` 루프를 영원히 돈다. 이 고아 루틴은 매 프레임 `body.sprite`/`body.transform.
localPosition`을 계속 걷기 상태로 덮어써서, 나중에 진짜 이동이 끝나 `StopWalkCycle()`(idle 복귀)가
정상 실행돼도 다음 프레임에 고아 루틴이 다시 걷기 상태로 되돌려버린다 - 그래서 겉보기엔 "이동이
끝나도 걷기 애니메이션이 영원히 계속되는" 것처럼 보인다. `MoveDuration`을 3-57에서 0.35초→1.0초로
늘리면서, 짧은 시간 안에 같은 NPC가 재이동 명령을 받아 이 경합 조건에 걸릴 확률이 실질적으로
높아졌다(이전엔 창이 0.35초라 드물게만 겹쳤을 것).

**해결**: `StartWalkCycle()`이 새 걷기를 시작하기 **직전에** 기존 `walkCycleRoutine`을 먼저
확실히 끊도록 방어 코드를 추가했다(`StopWalkCycleCoroutineOnly()` 신규 헬퍼, `StopWalkCycle()`도
이 헬퍼를 공유하도록 리팩터링). 이제 `StartWalkCycle()`이 항상 "이전 걷기 코루틴이 있으면 먼저
정리하고 새로 시작"하는 게 보장되므로, `AnimateTo()`가 몇 번을 연달아 끊겨도 고아가 남지 않는다.

**검증**: 깨끗한 재시작 후 5명 전원 idle 상태(스케일 1.0, idle 사진) 확인. 같은 NPC를 **의도적으로**
1초 미만 간격으로 연속 이동시켜(고아 발생 조건 그대로 재현) 최종 이동이 끝난 뒤 idle로 정확히
복귀하는지 확인 - 성공(스케일 1.0, idle 사진, idle 숨쉬기 정상 작동). 같은 NPC를 5개 장소로
빠르게 연쇄 이동(4번 연속 끊김)시키는 스트레스 테스트도 통과 - 최종적으로 idle 상태 깨끗하게
복귀. Console Error/Warning 0. 씬 변경 없음(스크립트만 수정).

**수정한 파일**: `NpcActorView.cs`(`StopWalkCycleCoroutineOnly()` 신규, `StartWalkCycle()`이
새 코루틴 시작 전 기존 것을 먼저 정리하도록 수정, `StopWalkCycle()`을 새 헬퍼 공유하도록 리팩터링).

### 3-60. "이동할 때 다른 NPC 이미지로 바뀐다" — 경비대장의 걷기 원본 소스 아트가 애초에 기사단장 것이었다 (2026-08-04, 사용자 스크린샷 2장으로 재현)

사용자가 스크린샷 2장(이동 전/후)을 보내며 "npc 이미지가 이동할때 다른 npc로 바껴버려"라고 재현.
실시간 플레이 세션의 `경비대장`/`집사` 등 실제 위치·스프라이트 이름을 직접 조회해 어느 캐릭터가
어느 비주얼인지부터 코드로 확정한 뒤(추측 금지 - `Npc_Major_GuardCaptain.png`를 직접 열어 실제
디자인이 **파란 후드+빨간 목도리 검사**임을 확인), 3-55에서 배선한 16명 전원의 idle 사진과 걷기
시트 첫 프레임을 한 장의 비교 그리드로 만들어 전수 검사했다.

**발견**: 16명 중 **`Npc_Major_GuardCaptain` 단 1명만** idle 사진(파란 후드 검사)과 걷기 시트(검은
망토+흰 옷깃의 완전히 다른 디자인)가 서로 다른 캐릭터였다. 원인을 추적한 결과, `경비대장` 소스
폴더 안의 파일명이 애초에 `기사단장_...`로 돼 있었던 것(3-55에서 이미 발견했던 사실)이 단순
"파일명 오기"가 아니라 **실제로 기사단장(`Npc_Major_KnightCommander`)의 아트가 통째로 잘못
들어있었던 것**이었다 - `경비대장` 폴더와 별도의 `기사단장` 폴더 두 곳의 걷기 시트를 나란히
비교하니 완전히 동일한 디자인(검은 망토 캐릭터)이었다. 즉 아트 준비 단계에서 `경비대장` 폴더에
`기사단장`용 배치 결과물이 잘못 복사된 것으로 보이며, **경비대장 본인의 파란 후드 디자인에
대응하는 걷기 시트 원본은 애초에 어느 폴더에도 존재하지 않는다.** (3-55 당시 "폴더 이름과 이미 있는
idle 사진을 비교해 확인했다"고 기록했는데, 그때 비교가 충분히 꼼꼼하지 않아 이 불일치를 놓쳤다.)

**해결**: 잘못된 아트를 재생하느니 안전하게 idle로만 두는 게 낫다고 판단해, `Npc_Major_GuardCaptain`
NpcData의 `walkFrames` 배열을 **비웠다**(`arraySize = 0`). `NpcActorView.StartWalkCycle()`은 이미
`walkFrames`가 비어 있으면 조용히 생략하고 idle 사진을 유지하도록 방어 코드가 있어(3-55 설계 당시
하위 호환용으로 넣어둔 그대로) 코드 수정 없이 데이터만 비우는 것으로 충분했다 - 경비대장은 이제
이동 중에도 계속 자기 자신의(파란 후드) idle 사진을 유지하며 이동한다(걷기 사이클 애니메이션만
없을 뿐, 위치 이동·숨쉬기 등은 정상).

**검증**: 16명 전원 idle-vs-걷기 비교 그리드에서 나머지 15명은 전부 일치 확인(다른 오류 없음).
사용자 스크린샷의 "여관 근처 초록 후드 캐릭터"는 버그가 아니라 **상인(`Npc_Major_MerchantHead`)의
원래 디자인**이었음도 같은 그리드로 확인. 경비대장을 실제로 이동시켜 스프라이트가 이동 시작
직후부터 끝까지 `Npc_Major_GuardCaptain`(자기 자신)으로 유지되는 것을 확인. Console Error/Warning
0. 씬 변경 없음(`NpcData` 에셋 1개만 수정).

**수정한 파일**: `Npc_Major_GuardCaptain.asset`(`walkFrames` 비움). 코드/씬 무수정.
`NpcActorView.cs`의 `MoveDuration`은 이 턴 중 사용자가 직접 `2.5f`로 조정(코멘트만 동기화).

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
