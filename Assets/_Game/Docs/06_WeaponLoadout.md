# 확정 무기 배정표

> 최종 갱신: 2026-09-03 (메뉴 이름 개편 반영)  
> 이 문서가 무기 모델 선택의 기준이다. 다른 문서나 코드의 과거 `Pistol_P` 배정보다 이 표를 우선한다.

## 역할별 무기

| 역할 | 상황/종류 | 확정 모델 | 프리팹 경로 |
|---|---|---|---|
| 플레이어 | 저격 단계 | `Recon_P_PreSet` | `Assets/Low Poly Weapon Pack 4_MW_1/Prefabs/Weapons/Weapons_PreSet/Recon_P_PreSet.prefab` |
| 플레이어 | 근접 방어전 권총 | `Pistol_J` | `Assets/Low Poly Pistol Weapon Pack 2/Prefabs/Weapons/Pistol_J.prefab` |
| 경호원 | 권총 | `Pistol_I` | `Assets/Low Poly Pistol Weapon Pack 2/Prefabs/Weapons/Pistol_I.prefab` |
| 경호원 | SMG | `SMG_P_PreSet` | `Assets/Low Poly Weapon Pack 4_MW_1/Prefabs/Weapons/Weapons_PreSet/SMG_P_PreSet.prefab` |
| 특공대 | 권총 | `Pistol_H` | `Assets/Low Poly Pistol Weapon Pack 2/Prefabs/Weapons/Pistol_H.prefab` |
| 특공대 | 주무기(SMG 슬롯) | `AR_U_PreSet` | `Assets/Low Poly Weapon Pack 4_MW_1/Prefabs/Weapons/Weapons_PreSet/AR_U_PreSet.prefab` |

- 경호원 배정 비율은 현재 `SMG_P : Pistol_I = 1 : 1`이다. 생성 순서대로 번갈아 배정한다.
- 특공대의 기존 SMG 전투 수치/슬롯은 유지하지만 표시 모델은 `AR_U_PreSet`을 사용한다.

## 애니메이션·그립 기준

- 권총 오른손 그립 및 왼손 IK 캡처 기준 자세: `Pistol Idle`
- 권총 사격 재생: `pistolShooting`
- 특공대 `AR_U` 사격 재생: 버스트가 시작될 때 `RifleShootingplay` 1회
- 권총병 엄폐: 서서 이동 → 엄폐에서 무릎 → 무릎 사격 → 일어난 뒤 이동
- 저장된 실제 길이: 경호원 `SMG_P` `0.7855m`, 경호원 `Pistol_I` `0.2517m`, 특공대 `Pistol_H` `0.2275m`, 특공대 `AR_U` `0.9000m`
- 위 네 무기는 오른손 수동 그립과 왼손 IK 캡처가 모두 완료된 상태다.
- 서로 형상이 다른 `SMG_P`, `AR_U`, `Pistol_I`, `Pistol_H`, `Pistol_J`는 같은 그립 위치나 왼손 IK 캡처값을 공유하지 않는다.

## 명칭 주의

- 코드의 `GuardWeapon.Smg`는 특공대의 연사 주무기 유형을 뜻한다. 최종 외형은 `SMG_P`가 아니라 `AR_U_PreSet`이다.
- `Pistol_P_PreSet`은 최종 배정에서 제외한다.
- `Recon_P_PreSet`은 저격 단계에서 그대로 유지한다.

## 무기별 그립 워크벤치

| Unity 메뉴 (`Rooftop Contract ▸ 도구 ▸`) | 대상 무기 | 저장되는 `CastingConfig` 필드 |
|---|---|---|
| `무기 — 그립 워크벤치 (SMG_P)` | 경호원 `SMG_P` | `weapon*` |
| `무기 — Pistol_H 워크벤치` | 특공대 `Pistol_H` | `pistol*` |
| `무기 — Pistol_I 워크벤치` | 경호원 `Pistol_I` | `protectorPistol*` |
| `무기 — AR_U 워크벤치` | 특공대 `AR_U` | `assaultPrimary*` |

각 작업대에서 위치를 맞춘 뒤 공통 메뉴 `무기 — 그립 저장`으로 저장한다.
저장값은 **해당 무기 프로필에만** 기록된다. 다른 자세에서 확인하려면 `무기 — 포즈 순환`.

> **플레이어 저격총은 여기가 아닙니다.** 저격총은 계약마다 위치가 달라서
> (`MissionDefinition.playerRifleLocalPosition/Euler`) 미션 씬에서 직접 맞춘 뒤
> `무기 — 플레이어 소총 배치 저장` 으로 **그 미션의 MissionSet 항목에** 기록합니다.

> 메뉴 번호 체계(`8`, `8b`, `8d`…)는 폐기됐습니다. 전체 메뉴 구조는 `00_HANDOFF.md` 2절.
