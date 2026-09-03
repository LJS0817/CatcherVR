# 설계도 ↔ 코드 대조 감사표

`Baseball_Defense_AI_Design.md` 전 조항을 실제 코드와 1:1로 대조한 결과입니다.
**이번 감사에서 미구현 7건이 발견되어 모두 구현했습니다.**

범례 : ✅ 구현 완료 · 🆕 이번 감사에서 새로 구현 · ↔ 기존 타입으로 대응

---

## 1~2장 · 아키텍처 / FSM

| 조항 | 상태 | 구현 위치 |
| :--- | :--- | :--- |
| 포수 = 플레이어, AI 8명 | ✅ | 씬의 수비팀은 8명(TeamA), 포수는 VR 플레이어 |
| 수비 4대 원칙 (Ball·Base·Backup·Command) | ✅ | `FieldingCoordinator.Evaluate` 1~8단계 |
| FSM 상태 기계 | ✅ | `FIELDER_STATE` 10상태 + `Defencer` Tick 분기 |
| `FielderRole` 8종 | 🆕 | `DEFENSIVE_ROLE` 에 `ASSIST` · `RELAY` · `OBSERVER` 추가 |

## 3장 · 타구 판독 및 우선권

| 조항 | 상태 | 구현 위치 |
| :--- | :--- | :--- |
| 3.1 ETA 기반 Primary 선정 | ✅ | `InterceptSolver.Solve` (반응+가속+후진+글러브 리치까지 반영) |
| 3.2 외야수 > 내야수 / CF > LF·RF / SS > 2B / 키스톤 > 코너 | ✅ | `FielderAttributes.CallPriority` (CF 0 · LF·RF 1 · SS 3 · 2B 4 · 3B 5 · 1B 6) |
| 3.2-5 플레이어(포수) 경합 시 AI 양보 + 투수 홈 커버 | ✅ | `FieldingCoordinator.ShouldYieldToPlayer` / `_coverHomeByPitcher` |

## 4장 · 포지션 · 좌표 · 시프트

| 조항 | 상태 | 구현 위치 |
| :--- | :--- | :--- |
| 4.2 기본 위치 배치도(9포지션 좌표) | 🆕 | `PositionCoverage` 테이블 |
| 4.3 1차 유효 반경 / X·Z 커버리지 경계 | 🆕 | `PositionCoverageConfig` + `Priority` · `WithinPrimaryRadius` |
| 4.3 중첩 구역 충돌 해결 | 🆕 | `FieldingCoordinator.ZoneBonus` 가 커버리지 스펙 기반으로 교체됨 |
| 4.4 표준 / 전진 / 병살 / 노-다블 시프트 | ✅ | `DefensiveAlignment` (+ 후반 근소 리드 노-다블 조건 추가) |

> **축소형 구장 대응** — 이 프로젝트는 내야가 실제 규격, 외야는 얕습니다.
> `PositionCoverage.Calibrate` 가 실제 외야수 위치를 재서 외야 깊이만 비율로 눌러
> 설계도 수치를 그대로 유지하면서 씬과 어긋나지 않게 합니다.

## 5장 · 상황별 수비

| 조항 | 상태 | 구현 위치 |
| :--- | :--- | :--- |
| 5.1 주자 없음 내야 땅볼 / RF 1루 백업 / P-1B 토스 커버 | ✅ | `_coverFirstByPitcher`, `FixedOutfieldBackup` |
| 5.2 병살 · 키스톤 2루 커버 규칙 | ✅ | `PreferenceFor(E_SECOND_BASE)`, `ThrowDecision` 병살 분기 |
| 5.3 홈 릴레이 · 동적 1자 정렬 · Cut / Let-go | ✅ | `AssignCutoff` 가 플레이어 포수 위치로 정렬, `DefenseCommand` 콜 반영 |
| 5.4 번트 수비 · 포수 쇄도 시 투수 홈 커버 | ✅ | `_buntCoverFirst/Third`, `IsCatcherCharging` |
| **5.5 12개 코스 × 8인 행동 매트릭스** | 🆕 | `HitCourseMatrix` — 표 원문을 룩업 테이블로 구현, 조율자가 이행 |

## 6장 · 송구 판단 엔진

| 조항 | 상태 | 구현 위치 |
| :--- | :--- | :--- |
| 6.1 의사결정 순서도 | ✅ | `ThrowDecision.Decide` 0~6단계 |
| 6.2 사용자 지정 최우선 오버라이드 | ✅ | `TryUserCommandedThrow` |
| 6.2 커버 요원 매핑 · 리드 송구 | ✅ | `AimPointFor` |
| 6.3 ΔT 기반 자동 판단 | ✅ | `BuildCandidate` (주자 ETA vs 송구 도착) |

## 7장 · 커버 / 백업 기하학

| 조항 | 상태 | 구현 위치 |
| :--- | :--- | :--- |
| 7.1 포스 = 모서리 터치 / 태그 = 베이스 앞 | ✅ | `FieldingCoordinator.BaseCoverSpot` |
| 7.2 BackupDistance 12~18m | 🆕 | 상수 11m → 15m, 12~18m 범위 클램프 |
| 7.2 허용 각도 ±15° | 🆕 | `ClampToBackupCone` |

## 8~9장 · 포구 자세 / 런다운

| 조항 | 상태 | 구현 위치 |
| :--- | :--- | :--- |
| 8장 포구 자세 7종 딜레이·파워 | ✅ | `CatchPostureTable` |
| 8장 송구 정확도 보정 | ✅(미적용) | `AccuracyScale` 보유. 11.2 「AI→AI 무오차」 규정에 따라 의도적으로 미적용 |
| 9장 하위 베이스 몰이 / 4~5m 압축 / 2회 제한 | ✅ | `RundownPlay` |
| 9장 펌프 페이크 | 🆕 | `FielderMotion.PlayPumpFake` + `ApplyPumpFake` |

## 10장 · 포스 / 태그 SOP

| 조항 | 상태 | 구현 위치 |
| :--- | :--- | :--- |
| 10.1 모서리 터치 · 최대 스트레치 · 볼 집중 | ✅ | `BaseCoverSpot`, `PlayStretch` |
| 10.1-4 **2루 병살 피벗 스텝** | 🆕 | `FielderMotion.PlayPivot` + `ApplyPivot`, `CheckBaseForceOut` 에서 발동 |
| 10.2 스트래들 · 다운앤스위프 · 링거 태그 | ✅ | `BaseCoverSpot`, `PlaySweepTag`, `TAG_LINGER_TIME` |
| 10.3 포스 해제 → 태그 전환 | ✅ | `RunnerTracker.ResolveForceState` + `CheckBaseForceOut` |
| **10.4 콜라이더 / 타임스탬프 정밀 판정** | 🆕 | `OutJudge.EvaluateForceOut` · `EvaluateTagOut` |

> **설계도와 의도적으로 다른 점 1곳** — 설계도 10.4-B 는 `tCatch < tRunner` 로
> **포구 시각**을 씁니다. 그대로 쓰면 *멀리서 먼저 잡고 뒤늦게 베이스를 밟은* 경우가
> 아웃으로 오판됩니다. 그래서 **포구 시각과 베이스 접촉 시각 중 늦은 쪽**을
> 기준 시각으로 넘기도록 고쳤습니다(야구 규칙에 맞는 동작).

## 11장 · 포구 / 송구 정확도

| 조항 | 상태 | 구현 위치 |
| :--- | :--- | :--- |
| 11.1 캐치 반경 100% 포구 (1.2~1.5m, 확장 1.8m) | ✅ | `Defencer.TryGuaranteedCatch` |
| 11.2 AI→AI 무오차 / Player→AI 조작 종속 | ✅ | `Ball.ThrowByFielder` 정확해 + `ThrowReceiver` 가로채기 방지 |
| 11.3 송구 미스 → 유동적 루즈볼 수비 | ✅ | `Team.OnLooseBall` |

## 12장 · 종료 / 반환 / 리셋 / 페일세이프

| 조항 | 상태 | 구현 위치 |
| :--- | :--- | :--- |
| 12.1 종료 판정 4조건 AND | 🆕 | `Team.EvaluateTermination` — 기존엔 `NoFurtherOutPossible` 이 **죽은 코드**였음 |
| 12.2 외야→내야 24m/s → 투수 16m/s 릴레이 | ✅ | `ThrowDecision.BuildReturnToPitcher` |
| 12.3 걷기 복귀 3m/s → 0.5m 이내 READY | ✅ | `Defencer.TickReturn`, `Team.TickResetSequence` |
| 12.4 8초 / 3초 타임아웃 페일세이프 | ✅ | `Team.TickFailsafe` |

## 13장 · 데이터 구조

| 설계도 타입 | 상태 | 대응 |
| :--- | :--- | :--- |
| `PositionCoverageConfig` | 🆕 | `PositionCoverage.cs` |
| `GameTacticalContext` | 🆕 | `GameTacticalContext.cs` — 시프트 판단이 실제로 이걸 사용 |
| `DefensiveShiftType` | 🆕 | `GameTacticalContext.cs` + `DefensiveAlignment.ActiveShift` |
| `PlayerCatcherState` | 🆕 | `GameTacticalContext.cs` (`DefenseCommand` 의 읽기 뷰) |
| `FielderRole` | 🆕 | `DEFENSIVE_ROLE` 확장 |
| `BaseIndex` | ↔ | 기존 `BASE_TYPE` 과 1:1 대응 — 중복 정의하지 않음 |
| `HitType` | ↔ | 기존 `BATTED_BALL_TYPE` 과 1:1 대응 (BUNT 포함, 더 상세) |
| `FielderAgent` | ↔ | `Defencer` + `FielderAttributes` + `FieldAssignment` 로 분리 구현 |

## 14장 · QA 체크리스트 18항목

18항목 전부 구현되어 있습니다. 위 표의 해당 조항 참조.

---

## 이번 감사에서 새로 만든 파일

| 파일 | 설계도 |
| :--- | :--- |
| `Defense/PositionCoverage.cs` | 4.2 · 4.3 · 13.1 |
| `Defense/HitCourseMatrix.cs` | 5.5 |
| `Defense/OutJudge.cs` | 10.4 |
| `Defense/GameTacticalContext.cs` | 13.1 |
