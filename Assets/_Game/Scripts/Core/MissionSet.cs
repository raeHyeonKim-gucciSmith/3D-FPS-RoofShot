using System;
using UnityEngine;

namespace RooftopContract
{
    /// <summary>
    /// Where the target is. The player's nest never moves — the whole defense phase is built
    /// into that one roof — so a mission is made distinct by changing what sits at the other
    /// end of the scope, not by moving the scope.
    /// </summary>
    public enum TargetVenue
    {
        /// <summary>A rooftop terrace 34m up. Static target, loose patrol. The tutorial shot.</summary>
        RooftopTerrace,
        /// <summary>Street level. The target walks a pavement between cover, so the shot has to be led.</summary>
        StreetWalk,
        /// <summary>A helipad with a bird already turning. The target is walking towards the exit.</summary>
        Helipad,
    }

    /// <summary>
    /// One contract. Everything that makes a mission feel like its own job rather than a reskin
    /// lives here: who the target is, how far away the nest sits, how many rounds you get, and
    /// how hard the roof gets defended afterwards.
    /// </summary>
    [Serializable]
    public class MissionDefinition
    {
        [Header("Identity")]
        public string codename = "CONTRACT";
        [TextArea(2, 3)] public string brief = "";
        [Tooltip("이 미션의 암살 대상")] public GameObject vipPrefab;

        [Header("Dossier (사무실 브리핑에 표시)")]
        [Tooltip("계약 보드에 찍히는 난이도. 1 보통 / 2 어려움 / 3 극한.")]
        [Range(1, 3)] public int difficulty = 1;
        [Tooltip("서류상의 표적 이름.")] public string targetName = "UNKNOWN";
        [Tooltip("한 줄 신상. 사진 밑에 붙는다.")] public string targetNote = "";
        [Tooltip("표적이 현장을 뜨는 시각. 연출용 시계 표기.")] public string strikeByClock = "19:40";
        [Tooltip("헬기 픽업 시각. 연출용 시계 표기.")] public string pickupClock = "19:47";

        [Header("Geometry")]
        [Tooltip("스카이라인 배치 시드. 미션마다 다르면 도시가 달라 보인다.")]
        public int mapSeed = 20260901;
        [Tooltip("표적이 있는 장소의 종류. 저격 포인트는 어떤 경우에도 같은 옥상이다.")]
        public TargetVenue venue = TargetVenue.RooftopTerrace;
        [Tooltip("표적이 서 있는 지면의 높이 (m). 옥상이면 34, 도로면 0.")]
        public float targetGroundY = 34f;
        [Tooltip("저격 거리 (m)")] public float engagementRange = 250f;
        [Tooltip("타겟 지면 대비 저격 위치 고도차 (m). 낮을수록 부감이 얕아 방어 대형을 넘겨보기 어렵다.")]
        public float elevationAdvantage = 30f;

        [Header("Sniper phase")]
        [Tooltip("이 미션 전용 저격총 위치. Mission 씬에서 배치 후 메뉴 8g로 저장한다.")]
        public Vector3 playerRifleLocalPosition = new Vector3(0.10f, -0.499f, 1.156f);
        [Tooltip("이 미션 전용 저격총 회전.")]
        public Vector3 playerRifleLocalEuler = new Vector3(324.33368f, 0f, 0f);
        public int sniperAmmo = 6;
        [Range(2, 6)] public int protectorCount = 4;
        [Tooltip("밀착 대형이 좁혀지는 시간 = 2번째 사격 기회 창")]
        public float shieldTightenTime = 2.6f;
        [Tooltip("0이면 무제한. 그 외에는 이 초가 지나면 표적이 현장을 뜬다 (HUD 카운트다운).")]
        public float strikeWindow = 0f;

        [Header("Defense phase")]
        public float assaultDelay = 17f;
        public float survivalDuration = 40f;
        public int assaultMaxAlive = 6;
        public Vector2 assaultSpawnInterval = new Vector2(3f, 7f);
    }

    /// <summary>
    /// The campaign: three contracts, one per target. The scene generator produces one scene per
    /// entry, and finishing a mission loads the next.
    /// </summary>
    [CreateAssetMenu(fileName = "MissionSet", menuName = "Rooftop Contract/Mission Set")]
    public class MissionSet : ScriptableObject
    {
        /// <summary>
        /// Bumped whenever <see cref="BuildDefaults"/> gains fields an existing asset cannot
        /// have values for.
        ///
        /// Unity deserialises a missing field to its C# initialiser, so an asset written before
        /// the venue system existed comes back claiming all three contracts are rooftop
        /// terraces — silently, and with no way to tell that apart from someone having chosen
        /// that. The version stamp is the difference between "not set" and "set to the default",
        /// and the generator uses it to re-seed the contracts instead of building three copies
        /// of mission 1.
        /// </summary>
        public const int CurrentVersion = 7;

        [HideInInspector] public int assetVersion;

        public MissionDefinition[] missions = new MissionDefinition[0];

        public bool NeedsDefaultsRefresh => assetVersion < CurrentVersion;

        public int Count => missions == null ? 0 : missions.Length;
        public MissionDefinition Get(int index) =>
            missions != null && index >= 0 && index < missions.Length ? missions[index] : null;

        /// <summary>Scene path the generator writes for a given mission index.</summary>
        public static string ScenePathFor(int index) =>
            $"Assets/_Game/Scenes/Mission{index + 1}.unity";

        public static string SceneNameFor(int index) => $"Mission{index + 1}";

        /// <summary>The briefing room. Build index 0, and the campaign always returns here.</summary>
        public const string OfficeScenePath = "Assets/_Game/Scenes/Office.unity";
        public const string OfficeSceneName = "Office";

        /// <summary>
        /// The default three-contract ramp. Each mission takes something away and adds pressure:
        /// fewer rounds, one more bodyguard, and a denser assault once the roof is stormed.
        ///
        /// **The two clocks are identical in all three, on purpose.** assaultDelay (17s) and
        /// survivalDuration (40s) used to ramp 30/25/20 and 60/75/90, which encoded an
        /// assumption that died the day the contracts went onto a board the player picks from
        /// in any order: a 12-second fuse is a fine finale and a terrible first impression, and
        /// nothing stops someone opening with mission 3. Uniform clocks make "17 seconds after
        /// the first shot, then hold for 40" a rule the player learns once and carries into
        /// every contract.
        ///
        /// The ramp did not go away, it moved to where the difficulty pips actually point:
        /// rounds (5/4/3), bodyguards (3/4/5), the venue, and the assault's *density* —
        /// assaultMaxAlive (5/6/8) and assaultSpawnInterval. Contracts get harder by being
        /// denser, not by being longer.
        ///
        /// Shortening the hold is safe because every knob in AssaultDirector is normalised
        /// against survivalDuration (see its Progress()): the pressure curve compresses rather
        /// than truncating, so no stage of the advance is silently cut off. At 40s the final
        /// push unlocks at 24.8s, which is just as the first attackers reach stage 3 — at 60s
        /// it unlocked at 33s and both sides spent the gap staring at each other.
        ///
        /// The three shots are deliberately three different *kinds* of shot, because the nest
        /// never changes:
        ///
        ///   1. A static target on a terrace. Range and hold-your-breath, nothing else.
        ///   2. A target on foot at street level. Now the shot has to be led, and the pavement
        ///      furniture opens and closes the window while he walks.
        ///   3. A target walking to a helicopter that is already turning. A hard deadline you
        ///      can watch approach, three rounds, and five bodyguards walling him in.
        ///
        /// Mission 2 also flattens nothing — it does the opposite, dropping the target 34m to
        /// the street so the depression angle goes to 11 degrees. The difficulty there is the
        /// lead, not the angle.
        ///
        /// **Codenames must not imply an order.** These were FIRST LIGHT and LAST FLIGHT while
        /// the campaign was a queue; the moment all three went onto a board the player picks
        /// from, a contract called "first" that you might take third is just wrong. Each name
        /// now describes its own job — a rooftop meeting, a pavement, a departure — and the
        /// difficulty pips carry the ranking the names used to imply.
        /// </summary>
        public static MissionDefinition[] BuildDefaults()
        {
            return new[]
            {
                new MissionDefinition
                {
                    codename = "HIGH TABLE", difficulty = 1,
                    brief = "표적이 옥상 테라스에서 접선 중이다. 경호는 느슨하다.",
                    targetName = "ARMAND VOSS",
                    targetNote = "무기 중개인 · 옥상 회합",
                    strikeByClock = "19:40", pickupClock = "19:47",
                    mapSeed = 20260901,
                    venue = TargetVenue.RooftopTerrace, targetGroundY = 34f,
                    engagementRange = 250f, elevationAdvantage = 30f,
                    sniperAmmo = 6, protectorCount = 3, shieldTightenTime = 2.6f,
                    strikeWindow = 0f,
                    assaultDelay = 17f, survivalDuration = 40f,
                    assaultMaxAlive = 5, assaultSpawnInterval = new Vector2(3.5f, 7f),
                },
                new MissionDefinition
                {
                    codename = "STREET LEVEL", difficulty = 2,
                    brief = "표적이 차량에서 내려 로비로 이동한다. 이동 경로를 예측해 조준하라.",
                    targetName = "DEAN MARLOWE",
                    targetNote = "자금 세탁 · 도보 이동 중",
                    strikeByClock = "20:05", pickupClock = "20:13",
                    mapSeed = 771903,
                    venue = TargetVenue.StreetWalk, targetGroundY = 0f,
                    engagementRange = 300f, elevationAdvantage = 58f,
                    sniperAmmo = 5, protectorCount = 4, shieldTightenTime = 2.3f,
                    strikeWindow = 0f,
                    assaultDelay = 17f, survivalDuration = 40f,
                    assaultMaxAlive = 6, assaultSpawnInterval = new Vector2(3f, 6f),
                },
                new MissionDefinition
                {
                    codename = "WHEELS UP", difficulty = 3,
                    brief = "표적이 헬기로 이동 중이다. 탑승하기 전에 제거하라.",
                    targetName = "KAREL SIDOROV",
                    targetNote = "출국 예정 · 헬리패드 대기",
                    strikeByClock = "21:12", pickupClock = "21:20",
                    mapSeed = 44127,
                    venue = TargetVenue.Helipad, targetGroundY = 34f,
                    engagementRange = 210f, elevationAdvantage = 42f,
                    sniperAmmo = 4, protectorCount = 5, shieldTightenTime = 2.0f,
                    // The walk to the bird is roughly this long. The HUD counts it down because
                    // the briefing promised a deadline and a deadline you cannot see is a rumour.
                    strikeWindow = 75f,
                    assaultDelay = 17f, survivalDuration = 40f,
                    assaultMaxAlive = 8, assaultSpawnInterval = new Vector2(2.2f, 5f),
                },
            };
        }
    }
}
