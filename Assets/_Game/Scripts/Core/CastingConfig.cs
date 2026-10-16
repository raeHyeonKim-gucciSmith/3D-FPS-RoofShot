using UnityEngine;

namespace RooftopContract
{
    /// <summary>
    /// Which character prefab plays which role, plus the import corrections a MagicaVoxel
    /// export usually needs. The scene generator reads this instead of hardcoding asset paths,
    /// so swapping in your own characters is a drag-and-drop, not a code edit.
    /// </summary>
    [CreateAssetMenu(fileName = "CastingConfig", menuName = "Rooftop Contract/Casting Config")]
    public class CastingConfig : ScriptableObject
    {
        [Header("Cast — 여러 개 넣으면 섞여서 등장한다")]
        [Tooltip("암살 대상. 여러 종을 넣으면 플레이마다 한 명이 뽑힌다.")]
        public GameObject[] vipPrefabs;
        [Tooltip("타겟 옥상의 근접 경호원 4명. 여러 종을 넣으면 섞여서 배치된다.")]
        public GameObject[] protectorPrefabs;
        [Tooltip("DEFENSE 페이즈에 몰려오는 돌격 경호대. 여러 종을 넣으면 스폰마다 섞인다.")]
        public GameObject[] assaultPrefabs;
        [Tooltip("연출용 민간인. 비워도 된다.")]
        public GameObject[] civilianPrefabs;

        /// <summary>Deterministic pick, so a rebuild with the same seed produces the same cast.</summary>
        public static GameObject Pick(GameObject[] pool, int index)
        {
            if (pool == null || pool.Length == 0) return null;

            // Skip empty slots rather than returning null for them: an array with a hole in the
            // middle is a mis-click in the inspector, not an instruction to spawn nothing.
            int count = 0;
            foreach (var p in pool) if (p != null) count++;
            if (count == 0) return null;

            int target = ((index % count) + count) % count;
            foreach (var p in pool)
            {
                if (p == null) continue;
                if (target-- == 0) return p;
            }
            return null;
        }

        [Header("Animation")]
        [Tooltip("이동 애니메이션. 비워두면 캐스팅된 캐릭터의 FBX 안에서 이름에 'walk'가 들어간 클립을 자동으로 찾는다.")]
        public AnimationClip walkClip;
        [Tooltip("대기 애니메이션 폴백. Humanoid 리그는 Idle 클립이 없으면 자세가 무너져 주저앉는다.")]
        public AnimationClip idleClip;

        [Header("Import correction")]
        [Tooltip("MagicaVoxel 익스포트는 1복셀=1유닛인 경우가 많아 키가 30배쯤 나온다. 켜면 targetHeight로 맞춘다.")]
        public bool normalizeHeight = true;
        [Tooltip("정규화 목표 키 (m). 사람 기준 1.7~1.9.")]
        public float targetHeight = 1.8f;
        [Tooltip("피벗이 발밑이 아니라 모델 중심에 있는 경우를 보정한다.")]
        public bool groundPivot = true;
        [Tooltip("모델이 +Z를 안 보고 있으면 여기서 돌린다. 90 단위로 시험해 보면 된다.")]
        public float modelYawOffset = 0f;

        [Header("Head hitbox")]
        [Tooltip("리그가 없을 때 상단 몇 %를 머리로 볼지. 머리가 큰 치비 비율이면 0.3 근처.")]
        [Range(0.08f, 0.45f)] public float headHeightFraction = 0.24f;
        [Tooltip("머리 히트박스 여유. 1보다 크면 헤드샷이 관대해진다.")]
        [Range(0.5f, 1.6f)] public float headPadding = 1f;
        [Tooltip("몸통 히트박스 여유. 팔이 크게 벌어진 모델은 줄여도 된다.")]
        [Range(0.5f, 1.4f)] public float bodyPadding = 1f;
        [Tooltip("몸통 박스 가로 폭 상한 (키 대비). T포즈 바인드 포즈에서 팔 길이만큼 벌어지는 것을 막는다.")]
        [Range(0.2f, 1.5f)] public float maxBodyWidthFraction = 0.5f;
        public float headDamageMultiplier = 4f;

        [Header("Protector SMG_P attach — 손 기준 수동 배치 (메뉴 8)")]
        [Tooltip("SMG에 아래 두 값을 그대로 쓴다. 메뉴 '8b. Save Weapon Grip' 이 자동으로 켠다. " +
                 "끄면 WeaponGrip 이 총 부품 이름과 양손 위치로 자동 계산한다 (자동은 자세를 못 이긴다).")]
        public bool weaponGripManual;
        [Tooltip("손뼈 기준 무기 위치 (m). 캐릭터 정규화 스케일과 무관하게 미터로 저장된다.")]
        public Vector3 weaponHandOffset;
        [Tooltip("손뼈 기준 무기 회전. 믹사모 리그가 전부 같은 손 축을 쓰므로 캐릭터끼리 그대로 통한다.")]
        public Vector3 weaponHandEuler;

        [Header("SMG left hand IK — 왼손을 총열에 붙인다")]
        [Tooltip("SMG를 든 액터의 걷기·달리기에서 왼손이 허공을 젓는 걸 막는다. 권총에는 적용하지 않는다.")]
        public bool leftHandIK = true;
        [Range(0f, 1f)] public float leftHandIKPositionWeight = 1f;
        [Tooltip("손목 각도까지 맞출 비중. 워크벤치에서 캡처한 값이 있을 때만 쓰인다.")]
        [Range(0f, 1f)] public float leftHandIKRotationWeight = 0.7f;
        [Tooltip("메뉴 8b 가 채운다 — 소총 파지 자세에서의 '총 대비 왼손' 관계.")]
        public bool weaponSupportCaptured;
        [Tooltip("총 기준 왼손 위치 (m).")]
        public Vector3 weaponSupportOffset;
        [Tooltip("총 기준 왼손 회전.")]
        public Vector3 weaponSupportEuler;

        [Header("Assault Pistol_H attach — Pistol Idle 기준")]
        [Tooltip("특공대 Pistol_H에만 적용되는 오른손 그립.")]
        public bool pistolGripManual;
        public Vector3 pistolHandOffset;
        public Vector3 pistolHandEuler;

        [Header("Pistol left hand IK — 양손 권총 파지")]
        [Tooltip("Pistol Idle에서 캡처한 왼손을 권총에 고정해 모든 권총 자세를 양손 파지로 유지한다.")]
        public bool pistolLeftHandIK = true;
        public bool pistolSupportCaptured;
        public Vector3 pistolSupportOffset;
        public Vector3 pistolSupportEuler;

        [Header("Protector Pistol_I attach — Pistol Idle 기준")]
        public bool protectorPistolGripManual;
        public Vector3 protectorPistolHandOffset;
        public Vector3 protectorPistolHandEuler;
        public bool protectorPistolLeftHandIK = true;
        public bool protectorPistolSupportCaptured;
        public Vector3 protectorPistolSupportOffset;
        public Vector3 protectorPistolSupportEuler;
        public float protectorPistolLength = 0.24f;

        [Header("Assault AR_U attach — 소총 파지 기준")]
        public bool assaultPrimaryGripManual;
        public Vector3 assaultPrimaryHandOffset;
        public Vector3 assaultPrimaryHandEuler;
        public bool assaultPrimaryLeftHandIK = true;
        public bool assaultPrimarySupportCaptured;
        public Vector3 assaultPrimarySupportOffset;
        public Vector3 assaultPrimarySupportEuler;
        public float assaultPrimaryLength = 0.9f;

        [Header("Weapon attach — 자동 배치 보정")]
        [Tooltip("손 기준 미세 조정 (조준 방향 기준). WeaponGrip이 방아쇠 부품을 찾아 손에 맞추므로 z는 보통 0이면 된다. " +
                 "y는 총몸을 손목보다 살짝 올려 주먹이 손잡이를 감싼 것처럼 보이게 하는 값.")]
        public Vector3 weaponGripOffset = new Vector3(0f, 0.03f, 0f);
        [Tooltip("자동 정렬 후 추가 회전. 보통 0으로 두면 된다.")]
        public Vector3 weaponExtraEuler = Vector3.zero;
        [Tooltip("앵커가 아예 없을 때 쓰는 대체 위치. 키에 대한 비율 (가슴 높이 ≈ 0.62).")]
        [Range(0.3f, 0.95f)] public float weaponFallbackHeightFraction = 0.62f;
        [Tooltip("기관단총 실제 길이 (m). 캐릭터 정규화 스케일과 무관하게 이 크기로 맞춘다.")]
        public float smgLength = 0.6f;
        [Tooltip("권총 실제 길이 (m)")]
        public float pistolLength = 0.24f;

        [Header("Health")]
        [Tooltip("저격 데미지 95 기준. 이하로 두면 몸 어디를 맞아도 1발 사살.")]
        public float vipHealth = 90f;
        public float protectorHealth = 90f;
        public float assaultHealth = 70f;
        public float civilianHealth = 60f;

        [Header("Navigation")]
        [Tooltip("에이전트 반경. 밀착 대형이 서로 밀어내지 않도록 작게 잡는다.")]
        public float agentRadius = 0.32f;

        /// <summary>True when every required role has at least one prefab assigned.</summary>
        public bool IsComplete => Has(vipPrefabs) && Has(protectorPrefabs) && Has(assaultPrefabs);

        static bool Has(GameObject[] pool)
        {
            if (pool == null) return false;
            foreach (var p in pool) if (p != null) return true;
            return false;
        }

        public string MissingRoles()
        {
            var missing = new System.Collections.Generic.List<string>();
            if (!Has(vipPrefabs)) missing.Add("vipPrefabs");
            if (!Has(protectorPrefabs)) missing.Add("protectorPrefabs");
            if (!Has(assaultPrefabs)) missing.Add("assaultPrefabs");
            return string.Join(", ", missing);
        }
    }
}
