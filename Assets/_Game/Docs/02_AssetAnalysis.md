# 에셋 분석 및 적용 결과

> 갱신: 2026-09-03. 현행 에셋 **목록**은 `00_HANDOFF.md` 8절이 요약본이고,
> 이 문서는 **각 팩이 어떤 함정을 갖고 있었고 그걸 어떻게 처리했는지**의 기록입니다.

프로젝트: Unity **6000.3.18f1** / **URP 17.3** / **Input System 1.19 (New only)** / **AI Navigation 2.0.13**
빌드 타깃: **WebGL**

---

## 1. 보유 에셋 전수 조사

### 1-1. 본인 제작 복셀 캐릭터 — 전 캐릭터

| 항목 | 결과 |
|---|---|
| 위치 | `Assets/VoxelCharacter/` — `Vip1~3` / `Guard1~2` / `Assault` / `Player` |
| 파이프라인 | **MagicaVoxel → 믹사모 오토리깅 → Unity** |
| 리그 | **Humanoid** — 클립 1개가 전 캐릭터에 리타게팅됨 |
| 배역 결정 | **폴더명** (`RC_AutoCast` 가 폴더명으로 자동 배정) |
| 텍스처 | 256×1 팔레트 PNG |
| 콜라이더 | **없음** → 런타임 생성 |

**→ 판단 및 대응**
- **Humanoid로 통일한 게 가장 큰 수확입니다.** 믹사모 클립 하나를 받으면 6종 전원이 씁니다.
  Generic이었으면 캐릭터마다 클립을 따로 받아야 했습니다.
- 콜라이더가 없어 히트박스를 **런타임에 생성**합니다 (`ActorSetup`). 메시 정점을 실측해
  머리/몸통 AABB를 각각 잡고, 머리에 **4배 데미지**를 겁니다.
- **팔레트 텍스처에 mipmap을 끕니다** (`RC_VoxelTextureFixer`).
  256×1 텍스처는 최하위 밉이 **전체 색의 평균**이라, 250m 밖 캐릭터가 단색 덩어리가 됩니다.
- `Player/` 는 **의도적으로 미사용**입니다 (1인칭).

> **삭제된 것**: 예전엔 `VoxelCharacterCollection` 샘플 팩(42종, Generic 리그, 클립 6종)을 썼습니다.
> 지금은 폴더째 삭제됐고 **코드의 폴백도 제거**했습니다 — 배역이 비면 조용히 대체하지 않고 에러를 냅니다.
> "다른 사람이 만든 사람 에셋은 쓰지 않는다"가 이 프로젝트의 원칙입니다.

### 1-2. Polyeler SimpleBuildingPack — 건물

| 항목 | 결과 |
|---|---|
| Building 프리팹 | **20종** (`Building1` ~ `Building20`) |
| 구조 | 루트 → `Building` 자식 (MeshFilter + MeshRenderer + **BoxCollider ×3**) |
| 셰이더 | **`Standard` (built-in)** → URP에서 **마젠타** |

**→ 판단 및 대응**
- 콜라이더가 이미 있어 저격 차폐·NavMesh 베이크 양쪽에 바로 쓸 수 있습니다. 최고의 수확.
- 셰이더는 `에셋 — URP 머티리얼 변환` 으로 일괄 URP/Lit 변환 (색·텍스처·metallic·smoothness 보존).
- **치수가 문서화되어 있지 않습니다.** 추측 대신 `RC_BuildUtil.MeasurePrefab()` 이
  프리팹을 임시 인스턴스화해 렌더러 바운드를 실측하고, 원하는 높이에 맞춰 스케일을 역산합니다.
  → `PlaceScaledToHeight(prefab, pos, 64f, yaw)` 한 줄로 "높이 64m 건물"이 나옵니다.
- ⚠️ **스카이라인 건물은 덱보다 0.6m 아래로 내려야 합니다.** 덱 높이에 맞춰 놓으면
  팩 건물의 **지붕면과 덱 슬래브 윗면이 같은 y**에 와서 Z-fighting이 납니다.

### 1-3. KayKit City Builder Bits — 도시 소품

| 항목 | 결과 |
|---|---|
| 내용 | 차량 5종, 도로 6종, 가로등, 신호등, 벤치, 쓰레기통, 물탱크, 건물 A~H |
| 텍스처 | `citybits_texture.png` **단일 아틀라스** |
| 머티리얼 | `materialLocation: 1` = **FBX 내장** → Standard → 마젠타 |

**→ 판단 및 대응**
- FBX 내장 머티리얼은 `AssetDatabase.FindAssets("t:Material")` 로 잡히지 않아
  **일괄 변환 스크립트가 닿지 않습니다.**
- 킷 전체가 아틀라스 하나를 공유하므로 **URP/Lit + 그 아틀라스** 머티리얼 1개를 만들어
  배치 시점에 전 렌더러에 덮어씌웁니다 (`RC_Palette.CityBits` + `Retexture()`).
- 킷 스케일이 작아 지상 소품은 **×3.2**로 확대합니다.

### 1-4. Low Poly Weapon Pack 4 (MW) — 주력 무기

| 역할 | 프리팹 |
|---|---|
| 플레이어 저격총 | `Weapons_PreSet/**Recon_P_PreSet**.prefab` (스코프 포함 완성형) |
| 경호원 기관단총 | `**SMG_P_PreSet**.prefab` |
| 특공대 연사 주무기 | `**AR_U_PreSet**.prefab` |
| 셰이더 | Standard → URP 변환 필요 |

`Recon_P_PreSet` 은 **`Recon_P_Bipod` 자식**을 갖고 있고, 거치 소총이 그 지점을 회전축으로 씁니다
(`MountedRifleView`). 저격총을 다른 모델로 바꾸면 이 자식 이름을 같이 확인해야 합니다.

### 1-5. Low Poly Pistol Weapon Pack 2 — 권총

권총 5종(`Pistol_F`~`J`) 중 셋을 씁니다 — 경호원 `Pistol_I`, 특공대 `Pistol_H`,
플레이어 방어전 `Pistol_J`. Pack 4의 `Pistol_P_PreSet` 은 배정에서 제외했습니다.

`Pistol_J` 는 뷰모델이 부품 단위로 움직입니다 — `Pistol_J_BarrelGuard`(슬라이드),
`Pistol_J_Sight`(팔로워), `*_Mag`(탄창). **부품 이름이 바뀌면 재장전 연출이 멈춥니다.**

### 1-6. Low Poly Helicopters Pack Free — 헬기

`Heli_2` 를 EXFIL 탈출 헬기로 씁니다 (동축 반전 로터 — `Rotor_Twin_Upper/Lower` 를 반대로 회전).

**URP 변환도 팔레트 텍스처 수정도 안 된 상태로** 들어왔으므로 두 도구의 대상 폴더에 추가했습니다.
안 돌리면 헬기가 **마젠타**로 뜨거나 멀리서 단색 덩어리가 됩니다.

### 1-7. Staggart Skyboxes — 하늘 (임베디드 패키지)

`Packages/xyz.staggartcreations.skyboxes/` 에 있습니다. **`Assets/` 가 아니라 패키지**라,
`manifest.json` 에 항목이 없어도 `Packages/` 안에 있으면 자동으로 잡힙니다.
미션별로 `Sky_Sunset` / `Sky_Dusk` / `Sky_NightMoon`. 경로가 깨지면 하늘이 **회색 단색**이 됩니다.

### 1-8. 사운드 — `Assets/Sound/` wav 19종

사격·재장전·착탄·앰비언스·사무실 UI. **생성기가 전부 배선**하므로 인스펙터 배선은 0입니다
(재생 시점 표는 `00_HANDOFF.md` 4절 "사운드").

---

## 2. 최종 캐스팅

| 게임 내 역할 | 에셋 | HP | 비고 |
|---|---|---|---|
| VIP (암살 대상) | `Vip1` / `Vip2` / `Vip3` | 90 | **계약마다 다른 인물** |
| 근접 경호원 3~5 | `Guard1` / `Guard2` 혼합 | 90 | 무기 `SMG_P` : `Pistol_I` = **1:1 교대** |
| 돌격대 (런타임 스폰) | `Assault` | 70 | `AR_U` : `Pistol_H` = **60:40 랜덤** |
| 민간인 | 복셀 캐릭터 3종 | 60 | 연출용, 콜라이더/히트박스 제거 |
| 플레이어 저격총 | `Recon_P_PreSet` | — | **배율 진입 시 자동 숨김** |
| 플레이어 권총 | `Pistol_J` | — | DEFENSE 페이즈 |
| 스카이라인 | `Building1`~`Building20` | — | 실측 후 스케일, 덱보다 0.6m 아래 |
| 지상 소품 | KayKit 차량·가로등 | — | ×3.2, 아틀라스 재적용 |
| 옥상 구조물 | **프리미티브 직접 생성** | — | 아래 3번 |

> **세 계약의 표적이 전부 같은 얼굴이었던 적이 있습니다.** 폴백이 `Pick(vipPrefabs, 0)` 이었고,
> **사진을 사무실 책상에 올리기 전까지 아무도 눈치채지 못했습니다.** 지금은 `VipPrefabFor(mission, index)`.

---

## 3. 왜 옥상만 프리미티브로 지었는가

건물 팩의 지붕은 장식 형상이라 **평평하다는 보장도, NavMesh가 깔린다는 보장도 없습니다.**
그런데 이 게임은 두 페이즈 모두 옥상 바닥과 엄폐물에 전적으로 의존합니다.

- 저격 페이즈: 경호원이 VIP 주위로 **정확한 호를 그리며** 이동해야 함 → 신뢰할 수 있는 NavMesh
- 방어 페이즈: 엄폐가 **실제 레이캐스트를 막아야** 함 → 신뢰할 수 있는 콜라이더

그래서 스카이라인(보이기만 하는 곳)은 에셋 그대로, **플레이하는 두 옥상만** 박스/실린더로 지었습니다.
로우폴리 아트 방향과도 어긋나지 않습니다 — 실외기도 냉각탑도 원래 박스와 원통입니다.

---

## 4. 발견한 함정과 처리

| 함정 | 처리 |
|---|---|
| Standard 셰이더 → URP 마젠타 | `에셋 — URP 머티리얼 변환` (일괄) |
| KayKit 내장 머티리얼은 변환 불가 | 아틀라스 공유 → 단일 URP 머티리얼 덮어쓰기 |
| **250m에서 캐릭터가 단색 덩어리** | 256×1 팔레트에 **mipmap ON** → 최하위 밉 = 전체 색 평균. 끌 것 |
| 팔레트 텍스처 수정이 되돌아감 | 에디터가 열린 채 `.meta` 를 직접 고치면 Unity가 덮어씀. **반드시 메뉴로** |
| `activeInputHandler: 1` (New Input 전용) | `Input.GetAxis` 불가 → `Mouse.current` / `Keyboard.current` 직접 폴링 |
| 캐릭터 콜라이더 없음 | 런타임 히트박스 생성 (`ActorSetup`) |
| 건물 치수 미상 | 렌더러 바운드 실측 후 스케일 역산 |
| 스킨드 메시 키가 11% 크게 나옴 | `Renderer.bounds` 는 **패딩된 값**. `BakeMesh` 정점으로 실측할 것 |
| 총구가 자기 히트박스 안에 있음 | `ImpactResolver.Raycast()` 가 슈터 root 소속을 전부 스킵 |
| NavMesh가 두 옥상을 공중으로 연결 | 옥상별 **Volume** 서피스 2개로 분리 베이크 |
| **헬기를 옥상 계층에 넣으면 NavMesh가 깨짐** | 베이크가 하위 모든 MeshFilter를 지면으로 수집 → **씬 루트**(탈출 헬기) 또는 `MAP` 루트(표적 헬기)에 둘 것 |
| 옥상 바닥 Z-fighting | 스카이라인 건물을 덱 아래 0.6m로 |
| 웹 빌드에서 한글이 깨짐 | OS 폰트 폴백이 없음 → 폰트를 `Resources/` 에 번들 (`UIFontProvider`) |
| 웹에서 IMGUI 원이 찌그러짐 | 분수 픽셀 반올림 → 안티에일리어싱 텍스처로 그릴 것 |
| 커스텀 레이어 등록 실패 시 마스크가 Default를 배제 | 레이어 미해결이면 `~0` 폴백 (빈 NavMesh 방지) |

> NavMesh·무기 그립·AI 쪽 함정의 **전체 목록**은 `00_HANDOFF.md` 6절 "이미 밟은 지뢰"에 있습니다.
> 이 표는 **에셋 임포트 단계**에서 걸린 것만 모았습니다.
