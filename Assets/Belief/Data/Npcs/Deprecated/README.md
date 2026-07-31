# Deprecated NPC 자산

## Npc_Major_Informant.asset

- **상태**: Deprecated. 어떤 StageData/씬/미션 조건에서도 참조하지 않는다(2026-07-31 기준 참조 0건, guid
  `5485e85a54854fcc967165f26956853f`).
- **왜 존재하는가**: "정보원"은 게임 내에서 플레이어의 카드 전달 행위(`TargetingController.DeliverByInformant`)를
  가리키는 세계관상의 명칭/시스템 인터페이스일 뿐, 독립적으로 사고하거나 행동하는 NPC가 아니다.
  이 자산은 정보원을 실제 NPC로 구현하려던 과거 시도의 산물로 보이며, 코드 어디에서도 이 자산을 로드하거나
  참조하지 않는다.
- **금지 사항**: 이 자산을 StageData의 `npcPlacements`나 씬의 `GameInstaller.allNpcs`에 다시 배치하지 않는다.
  "정보원"을 실제 게임플레이 NPC로 만들고 싶다면 이 자산을 재활용하지 말고 별도 설계 검토를 먼저 거친다
  (전달 지연/왜곡/신뢰도 가공 등 정보원 전용 로직은 현재 어디에도 구현되어 있지 않다).
- **데이터**: 자산 내부 필드(trustBias/skepticism/goal/loyalty/relationships/availableActions)는 삭제 시
  참고할 수 있도록 수정하지 않고 원본 그대로 보존한다.
- **삭제하지 않는 이유**: 참조 0건이라 삭제해도 당장 안전하지만, 향후 "정보원" 개념을 실제로 구현할 때
  과거에 어떤 수치로 설계됐는지 참고 자료로 남겨두기 위해 보존한다.
