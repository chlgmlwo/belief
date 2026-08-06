# BELIEF NPC AI Execution Architecture V1 — Frozen

확정일: 2026-08-06
검증: `BELIEF/Diagnostics/NPC AI 실행 구조 검증 (호출 0회)` 20/20 PASS

이 문서는 새 구조 제안이 아니라 **현재 코드가 이미 하고 있는 일을 고정한 기록**이다.
이후 재현 가능한 버그나 진행 불능이 아닌 이상 이 구조를 다시 설계하지 않는다.

---

## 1. 두 계층

| 계층 | 담당 | 코드 |
|---|---|---|
| **사건 판단** | 새 판단이 필요한 NPC의 해석·믿음·목표·행동·목적지·대사 | `IntegratedLlmThinker` |
| **평상시 행동** | 그 외 전원의 일상 이동 | `RuleBasedMajorThinker.DecideMoveAsync` |

> 현재 사건을 새롭게 해석해야 하는 NPC만 LLM을 사용하고,
> 그 외 NPC는 **같은 월드 상태를 공유하는** RuleBased 행동으로 움직인다.

---

## 2. LLM 호출 대상이 정해지는 단 하나의 경로

```
플레이어 카드 사용
  └ InfoDeliverySystem
      ├ DeliverCardToNpcAsync      → 지정 NPC 1명
      └ ExposeCardAtLocationAsync  → 그 장소 재실 NPC
                                     − 확산 주체 본인
                                     − (재확산 시) 이미 같은 카드를 받은 NPC
                                     − npcDensity 상한 초과분
        └ NpcThinkingSystem.HandleExposureAsync
            └ IntegratedLlmThinker.DecideAsync   ← LLM 호출은 여기 한 곳뿐
                └ Belief가 실제로 바뀌면 NpcState.MarkBeliefChanged()
```

**LLM 호출은 "정보를 새로 받은 NPC 1명당 1회"뿐이다.** 다른 진입점은 없다.

이동 단계에서는 LLM이 호출되지 않는다 — `GameInstaller`가 IntegratedLlm 모드일 때
이동용 `IMajorNpcThinker`를 **명시적으로 RuleOnly로 생성**하기 때문에
(`GameInstaller.cs`의 `integratedThinker != null ? ThinkerMode.RuleOnly : effectiveMode`),
이동 때문에 Transport가 호출되는 경로 자체가 존재하지 않는다.

## 3. 평상시 이동 선별

`NpcMovementSystem.Dispatch`가 NPC마다 세 갈래로 나눈다.

1. **예약 있음** (`DestinationReservation.TryConsume != None`)
   → 판단 경로도 RuleBased 경로도 타지 않고 예약대로 처리. Stay도 하나의 예약이다.
2. **`NpcState.NeedsFreshDecision == true`** → `thinker` 경로
   (IntegratedLlm 모드에서는 이 `thinker`도 RuleOnly라 Transport 호출 0)
3. **그 외 전원** → `ruleBased` 경로

`NeedsFreshDecision`은 `BeliefChangedThisTurn || GoalChangedThisTurn`이며,
마커는 `MoveNpcsAsync`의 `finally`에서만 내려간다.

**"정보를 받았다"가 아니라 "받은 결과 판단이 실제로 달라졌다"만 대상이다** —
같은 값으로 재확정된 경우는 호출하지 않는다.

## 4. 공유 월드 상태

`GameInstaller`가 만든 `locationStates` / `npcStates` 딕셔너리 **하나**를
BeliefSystem · NpcThinkingSystem · InfoDeliverySystem · NpcMovementSystem ·
ActionResolutionSystem · DestinationReservation · MissionSystem이 전부 공유한다.
LLM NPC와 RuleBased NPC가 서로 다른 세계를 보는 지점은 없다.

**관계는 RuleBased 판단에 들어가지 않는다.** `BuildBeliefSystem`의 evaluator 7종에
관계 항목이 없고, 관계는 통합 판단 프롬프트에만 제시된다. 이는 §5의
"RuleBased가 관계를 깊게 해석할 필요는 없다"에 부합하며, 별도 세계가 아니라
**같은 데이터를 한쪽만 읽는 것**이다.

## 5. Destination 배타성

- LLM이 유효한 목적지를 반환 → `DestinationReservation.TryReserve`
- 같은 사이클의 `Dispatch`가 그 예약을 소비하고 **RuleBased 이동을 건너뜀**
- 예약은 `TryConsume`이 즉시 제거하므로 두 번 소비되지 않음
- 미등록 장소는 예약 단계와 `ActionResolutionSystem.MoveNpc` 양쪽에서 차단

## 6. NPC 간 만남(InteractionIntent) — 현재 해당 없음

현재 빌드에는 **"다른 NPC를 만나러 간다"는 개념이 존재하지 않는다.**

- 행동 Intent 5종: `Comply / Escalate / Verify / Ignore / Wait` — 대인 대상 없음
- 통합 판단 응답 스키마에 상호작용 대상 필드 없음
- 행동 22개 중 NPC를 목표로 예약하는 것 없음

따라서 엇갈림·교착이 구조적으로 발생할 수 없고, **없는 기능을 위해 예약 구조를
미리 만들지 않는다**(§18 최소 변경). 만남 기능이 추가되면 그때
`DestinationReservation`을 재사용해 최소 구현한다.

## 7. 중복·늦은 응답 차단

- `JudgmentApplicationSystem.appliedKeys` — 동일 NPC·턴·카드 중복 적용 차단
- `ApplicationKey = {attemptId}|{turn}|{npcId}|{cardId}`
- `id.MissionAttemptId != missionAttemptId` → `"StaleAttempt"`로 폐기
- `DestinationReservation`이 같은 턴 재예약 거부
- 미션 재시작·씬 전환 시 `TurnStartedEvent(Turn==1)`로 attemptId 증가 + 이력 초기화

## 8. Fresh Completion

LLM NPC와 RuleBased NPC의 변화가 **같은 `WorldChangeClock` 스탬프 체계**를 쓴다.
`MissionStartBaseline`이 미션 시작 스탬프 하나와 조건별 시작 만족 여부만 보관하고,
시작부터 만족 중이던 조건은 관련 상태가 새로 변해야 성공으로 인정된다.

## 9. 실측 호출 수

전 구역 LLM 플레이(판단 100건 / 이동 758건) 기준 턴당:

| 구역·턴 | 전체 NPC | LLM 호출 | RuleBased 이동 |
|---|---|---|---|
| STAGE_01 T1 | 5 | 2 | 3 |
| STAGE_02 T1 | 5 | 4 | 6 |
| STAGE_03 T11 | 6 | **0** | 6 |
| STAGE_03 T13 | 6 | 2 | 10 |

**정보 사건이 없는 턴은 호출 0**이고, 호출 수는 NPC 수가 아니라
그 턴의 정보 수신 건수를 따른다. 한 턴에 NPC 수를 넘는 경우는
재확산으로 같은 NPC가 서로 다른 카드를 여러 장 받은 경우이며, 이는 §2에 부합한다.

## 10. 시간 표현

내부 코드는 Turn 명칭을 유지한다(`TurnSystem` / `CurrentTurn` / `turnLimit` /
`FinishTurnAsync`). 플레이어에게 보이는 문구만 일수로 표기한다
(1일차 / 다음 날 / 제한 기간 / 남은 기간). **완전한 턴 사이클 1회 = 게임 세계의 1일.**
UI 표기 작업은 이 AI 구조와 별개로 관리한다.

## 11. 알려진 비차단 한계

이번 감사에서 확인했으나 **버그가 아니며 이 Frozen 범위에서 고치지 않는 것들**:

1. **Verify 결과와 기존 Belief의 충돌 감지 없음** — Verify는 기억·조사기록만 남기고
   재판단을 유발하지 않는다. §11의 "단순 기록 추가는 다음 관련 판단에 맥락으로 포함"에 해당한다.
2. **`GoalEvaluator`는 항상 0을 반환** — RuleBased Belief 계산에서 목표는 죽은 항이다.
3. **`GoalChangedThisTurn`은 실제로 참이 되지 않음** — `SetGoal`을 부르는 곳이
   통합 판단 경로뿐이고, 그 경로는 이미 판단을 마친 뒤다. 구조만 열려 있다.
4. **장소 신뢰도 보정이 LLM 프롬프트에 전달되지 않음** — `AppendLocationDetail`이
   확산 속도·밀집도·민감 유형은 넘기지만 `credibilityModifier`는 빠져 있다.
   RuleBased는 같은 값을 읽어 ±0.10~0.20을 적용한다. 밸런싱 영역의 별도 판단 사항이다.

---

## 검증

| 항목 | 결과 |
|---|---|
| NPC AI 실행 구조 검증 | **20/20 PASS** |
| Fresh Completion 결정적 검증 | 46/46 PASS |
| Judgment Application / Integrated Pilot / Fallback / RuleBased Parity | 무회귀 |
| Console Error / Warning | **0** |

---

## 변경 규칙

**금지** — 전 NPC 매일 호출, LLM·RuleBased 결과 맞추기, RuleBased 공식의 프롬프트 강제,
관계로 인한 전 NPC 호출, 새 관계 점수 공식, 성향 평준화, 정답 카드 지정,
반복 전달 강제, `clearMode`·`TargetCount` 변경, 최소 턴 강제,
이동 공식·NPC Profile·폴백·Fresh Completion 재설계.

**허용** — 재현 가능한 버그, 진행 불능, 잘못된 NPC·장소 참조, 중복 적용,
stale 응답 적용, 무한 이동·대기, InteractionIntent 교착, API 실패 시 정지,
Snapshot Restore 불일치, Fresh Completion 오판정, 비용 폭증, 미검증 목적지 이동.

결과가 취향과 다르거나 LLM과 RuleBased가 다르다는 이유로는 변경하지 않는다.

**BELIEF NPC AI Execution Architecture V1 — Frozen**
