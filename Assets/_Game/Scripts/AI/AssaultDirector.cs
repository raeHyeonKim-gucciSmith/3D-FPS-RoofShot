using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace RooftopContract
{
    /// <summary>
    /// Runs the defense phase as a staged assault rather than a stampede.
    ///
    /// Everyone comes out of one door — the stairwell head at the back of the roof — and from
    /// there the approach is rationed by three things, none of which live in the individual
    /// guards:
    ///
    ///   • Slots. Each stage has a fixed number of cover positions and they are exclusive.
    ///     Stage 3 has two, so two is the most that can ever be near the player, however many
    ///     are alive. Attackers queue up behind the plant instead of arriving all at once.
    ///   • Permits. Only one guard (two, late) may be crossing open ground at any moment.
    ///     Everyone else is behind cover while it happens.
    ///   • Covering fire. That permit is only issued while somebody else is actually leaning out
    ///     and shooting. With nobody shooting, nobody moves — so the fight opens with a firing
    ///     line at twenty metres and closes only as it builds up enough guns to bound forward.
    ///
    /// The result is bounding overwatch, and it reads from the player's side as an assault that
    /// takes the roof one row of machinery at a time.
    /// </summary>
    public class AssaultDirector : MonoBehaviour
    {
        [Header("Spawning — 옥상 출입문")]
        [Tooltip("계단실 문 앞. 보통 하나다 — 문이 하나라서 플레이어가 어디를 볼지 배울 수 있다.")]
        public Transform[] spawnPoints;
        [Tooltip("돌격대 템플릿들. 스폰마다 무작위로 하나 고른다.")]
        public GameObject[] guardPrefabs;
        public Vector2 spawnInterval = new Vector2(4f, 8f);
        public int maxAlive = 6;
        public int firstWaveCount = 3;
        [Tooltip("문 앞이 비어야 다음 인원이 나온다. 겹쳐서 튀어나오는 걸 막는 반경 (m).")]
        public float doorClearance = 1.3f;

        [Header("Door")]
        [Tooltip("문이 열릴 때 번쩍이는 조명. 없어도 된다.")]
        public Light doorLight;
        public float doorLightIdle = 1.2f;
        public float doorLightFlash = 7f;

        [Header("Staged approach")]
        [Tooltip("엄폐 지점들. 맵 생성기가 채운다. stage 1 = 문 쪽, 3 = 플레이어 코앞.")]
        public CoverPoint[] coverPoints;
        [Tooltip("동시에 개활지를 건널 수 있는 인원. 후반에는 늘어난다.")]
        public int maxAdvancing = 1;
        public int lateGameMaxAdvancing = 2;
        [Tooltip("아무도 사격하지 않으면 아무도 전진하지 않는다. 첫 진입선(stage 1)만 예외.")]
        public bool requireCoveringFire = true;

        [Header("Final push")]
        [Tooltip("생존 시간이 이 비율을 넘겨야 최종 돌입이 허용된다.")]
        [Range(0f, 1f)] public float pushUnlockProgress = 0.55f;
        public int maxPushing = 1;
        public int lateGameMaxPushing = 2;

        [Header("Anti-camp pressure")]
        [Tooltip("이 시간 동안 계속 엄폐하면 가장 가까운 한 명이 돌입한다.")]
        public float hiddenRushDelay = 3f;

        [Header("Pressure ramp")]
        [Tooltip("생존 시간이 지날수록 스폰 간격이 이 배율까지 짧아진다")]
        public float lateGameIntervalScale = 0.55f;
        [Tooltip("후반에 허용되는 최대 동시 인원")]
        public int lateGameMaxAlive = 8;

        [Header("Weapon models (optional)")]
        public GameObject smgModel;
        public GameObject pistolModel;

        [Header("Weapon attach — copied from CastingConfig by the scene generator")]
        public Vector3 weaponGripOffset = new Vector3(0f, 0.03f, 0f);
        public Vector3 weaponExtraEuler = Vector3.zero;
        public bool weaponGripManual;
        public Vector3 weaponHandOffset;
        public Vector3 weaponHandEuler;
        public bool leftHandIK = true;
        [Range(0f, 1f)] public float leftHandIKPositionWeight = 1f;
        [Range(0f, 1f)] public float leftHandIKRotationWeight = 0.7f;
        public bool weaponSupportCaptured;
        public Vector3 weaponSupportOffset;
        public Vector3 weaponSupportEuler;
        public bool pistolGripManual;
        public Vector3 pistolHandOffset;
        public Vector3 pistolHandEuler;
        public bool pistolLeftHandIK = true;
        public bool pistolSupportCaptured;
        public Vector3 pistolSupportOffset;
        public Vector3 pistolSupportEuler;
        public bool assaultPrimaryGripManual;
        public Vector3 assaultPrimaryHandOffset;
        public Vector3 assaultPrimaryHandEuler;
        public bool assaultPrimaryLeftHandIK = true;
        public bool assaultPrimarySupportCaptured;
        public Vector3 assaultPrimarySupportOffset;
        public Vector3 assaultPrimarySupportEuler;
        public float assaultPrimaryLength = 0.9f;
        [Range(0.3f, 0.95f)] public float weaponFallbackHeightFraction = 0.62f;
        public float smgLength = 0.6f;
        public float pistolLength = 0.24f;

        public const int TopStage = 3;

        readonly List<AssaultGuard> alive = new List<AssaultGuard>();
        readonly List<AssaultGuard> advancing = new List<AssaultGuard>();
        readonly List<AssaultGuard> pushing = new List<AssaultGuard>();
        float timer;
        bool running;
        float doorFlash;
        float hiddenClock;
        bool hiddenRushTriggered;
        PlayerRoot player;

        /// <summary>Highest stage that has any cover at all. Lets the map define fewer stages.</summary>
        int topStageAvailable;

        void Start()
        {
            player = FindFirstObjectByType<PlayerRoot>();
            foreach (var c in coverPoints)
                if (c != null) topStageAvailable = Mathf.Max(topStageAvailable, c.stage);
            if (topStageAvailable == 0) topStageAvailable = TopStage;

            var director = MissionDirector.Instance;
            if (director != null)
            {
                director.PhaseChanged += OnPhaseChanged;
                director.MissionEnded += _ => running = false;
                // Exfil inbound: stop feeding the roof, but leave whoever is already on it
                // fighting. Reinforcements arriving while the helicopter is landing would read
                // as the game refusing to let the player finish.
                director.ExtractionStarted += () => running = false;
            }
            if (doorLight != null) doorLight.intensity = doorLightIdle;
        }

        void OnDestroy()
        {
            var director = MissionDirector.Instance;
            if (director != null) director.PhaseChanged -= OnPhaseChanged;
        }

        void OnPhaseChanged(MissionPhase phase)
        {
            if (phase != MissionPhase.Defense) return;
            running = true;
            for (int i = 0; i < firstWaveCount; i++) SpawnOne();
            timer = Random.Range(spawnInterval.x, spawnInterval.y);
        }

        void Update()
        {
            if (doorLight != null && doorFlash > 0f)
            {
                doorFlash -= Time.deltaTime * 2.5f;
                doorLight.intensity = Mathf.Lerp(doorLightIdle, doorLightFlash, Mathf.Clamp01(doorFlash));
            }

            if (!running) return;

            alive.RemoveAll(IsGone);
            advancing.RemoveAll(IsGone);
            pushing.RemoveAll(IsGone);
            foreach (var c in coverPoints) if (c != null) c.DropDeadOccupant();

            TickHiddenPressure();

            float progress = Progress();
            int cap = Mathf.RoundToInt(Mathf.Lerp(maxAlive, lateGameMaxAlive, progress));
            if (alive.Count >= cap) return;

            timer -= Time.deltaTime;
            if (timer > 0f) return;

            // Never stack two bodies in the doorway; wait a beat instead.
            if (!DoorIsClear()) { timer = 0.4f; return; }

            SpawnOne();
            float scale = Mathf.Lerp(1f, lateGameIntervalScale, progress);
            timer = Random.Range(spawnInterval.x, spawnInterval.y) * scale;
        }

        void TickHiddenPressure()
        {
            if (player == null) return;

            if (player.IsPeeking)
            {
                hiddenClock = 0f;
                hiddenRushTriggered = false;
                return;
            }

            if (hiddenRushTriggered || pushing.Count > 0) return;
            hiddenClock += Time.deltaTime;
            if (hiddenClock < hiddenRushDelay) return;

            AssaultGuard closest = null;
            float closestDistance = float.PositiveInfinity;
            foreach (var guard in alive)
            {
                if (guard == null || guard.IsDead || guard.State == AssaultGuard.Stance.Push) continue;
                float distance = (guard.transform.position - player.transform.position).sqrMagnitude;
                if (distance >= closestDistance) continue;
                closestDistance = distance;
                closest = guard;
            }

            if (closest == null) return;
            pushing.Add(closest);
            closest.ForcePushFromCover();
            hiddenRushTriggered = true;
        }

        static bool IsGone(AssaultGuard g)
        {
            if (g == null) return true;
            var d = g.GetComponent<Damageable>();
            return d == null || d.IsDead;
        }

        float Progress()
        {
            var d = MissionDirector.Instance;
            if (d == null || d.survivalDuration <= 0f) return 0f;
            return 1f - Mathf.Clamp01(d.SurvivalCountdown / d.survivalDuration);
        }

        // ------------------------------------------------------------------ cover slots

        /// <summary>
        /// Hands out the free slot in a stage that is nearest the asker, and takes back whatever
        /// they were holding. Returns null when the stage is full — which is the whole point:
        /// a full stage is a queue, and a queue is what keeps the roof from filling up.
        /// </summary>
        public CoverPoint ClaimCover(AssaultGuard guard, int stage)
        {
            if (guard == null || coverPoints == null) return null;

            CoverPoint best = null;
            float bestDist = float.PositiveInfinity;
            foreach (var c in coverPoints)
            {
                if (c == null || c.stage != stage || !c.IsFree) continue;
                float d = (c.transform.position - guard.transform.position).sqrMagnitude;
                if (d >= bestDist) continue;
                bestDist = d;
                best = c;
            }

            if (best == null) return null;
            ReleaseCover(guard);
            best.TryClaim(guard);
            return best;
        }

        public void ReleaseCover(AssaultGuard guard)
        {
            if (coverPoints == null) return;
            foreach (var c in coverPoints) if (c != null) c.Release(guard);
        }

        public bool StageHasRoom(int stage)
        {
            if (coverPoints == null) return false;
            foreach (var c in coverPoints)
                if (c != null && c.stage == stage && c.IsFree) return true;
            return false;
        }

        public int HighestStage => topStageAvailable;

        // ------------------------------------------------------------------ permits

        /// <summary>
        /// Permission to cross open ground. Refused while the crossing lane is busy, while the
        /// destination stage is full, and — for anything past the first bound — while nobody is
        /// shooting to cover the move.
        /// </summary>
        public bool RequestAdvance(AssaultGuard guard, int toStage)
        {
            if (guard == null) return false;
            if (advancing.Contains(guard)) return true;

            int limit = Mathf.RoundToInt(Mathf.Lerp(maxAdvancing, lateGameMaxAdvancing, Progress()));
            if (advancing.Count >= Mathf.Max(1, limit)) return false;
            if (!StageHasRoom(toStage)) return false;

            // Leaving the doorway is always allowed: standing in an open door is worse than
            // crossing to the first row of cover, and there is nobody to cover you yet anyway.
            if (requireCoveringFire && toStage > 1 && !CoveringFireAvailable(guard)) return false;

            advancing.Add(guard);
            return true;
        }

        public void ReleaseAdvance(AssaultGuard guard) => advancing.Remove(guard);

        /// <summary>Is anyone other than the asker currently leaning out with a shot?</summary>
        public bool CoveringFireAvailable(AssaultGuard except)
        {
            foreach (var g in alive)
            {
                if (g == null || g == except) continue;
                if (g.IsSuppressing) return true;
            }
            return false;
        }

        public bool RequestPush(AssaultGuard guard)
        {
            if (guard == null) return false;
            if (pushing.Contains(guard)) return true;
            if (Progress() < pushUnlockProgress) return false;

            int limit = Mathf.RoundToInt(Mathf.Lerp(maxPushing, lateGameMaxPushing, Progress()));
            if (pushing.Count >= Mathf.Max(1, limit)) return false;

            pushing.Add(guard);
            return true;
        }

        public void ReleasePush(AssaultGuard guard) => pushing.Remove(guard);

        // ------------------------------------------------------------------ spawning

        bool DoorIsClear()
        {
            if (spawnPoints == null || spawnPoints.Length == 0) return true;
            foreach (var g in alive)
            {
                if (g == null) continue;
                foreach (var p in spawnPoints)
                {
                    if (p == null) continue;
                    if ((g.transform.position - p.position).sqrMagnitude < doorClearance * doorClearance)
                        return false;
                }
            }
            return true;
        }

        void SpawnOne()
        {
            if (guardPrefabs == null || guardPrefabs.Length == 0 || spawnPoints == null || spawnPoints.Length == 0) return;

            var template = guardPrefabs[Random.Range(0, guardPrefabs.Length)];
            if (template == null) return;

            Transform point = spawnPoints[Random.Range(0, spawnPoints.Length)];
            if (point == null) return;

            // A little lateral spread inside the doorway so a stack of three does not come out
            // along one exact line.
            Vector3 pos = point.position + point.right * Random.Range(-0.5f, 0.5f);
            if (NavMesh.SamplePosition(pos, out NavMeshHit hit, 5f, NavMesh.AllAreas)) pos = hit.position;

            var go = Instantiate(template, pos, point.rotation);
            go.SetActive(true);
            go.name = "AssaultGuard";

            var guard = go.GetComponent<AssaultGuard>();
            if (guard == null) guard = go.AddComponent<AssaultGuard>();
            guard.director = this;
            alive.Add(guard);

            doorFlash = 1f;

            AttachWeaponModel(go, guard);
        }

        void AttachWeaponModel(GameObject actor, AssaultGuard guard)
        {
            bool isSmg = guard.Weapon == GuardWeapon.Smg;
            GameObject source = isSmg ? smgModel : pistolModel;
            if (source == null) return;

            var setup = actor.GetComponent<ActorSetup>();
            bool hasAnchor = setup != null && setup.RightHand != null;
            Transform hand = hasAnchor ? setup.RightHand : actor.transform;

            // Configured while inactive, activated last. A component wakes the instant it is
            // added to a live object, which would have WeaponGrip reading its fields before they
            // are assigned — and in manual mode that costs a frame with the weapon at its raw
            // prefab size, hanging off the wrist.
            var model = Instantiate(source, hand);
            model.SetActive(false);

            var muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(model.transform, false);
            muzzle.localPosition = new Vector3(0f, 0f, 0.5f);
            guard.muzzleOverride = muzzle;

            if (hasAnchor)
            {
                // Solved on the posed skeleton a couple of frames from now — see WeaponGrip.
                var grip = model.AddComponent<WeaponGrip>();
                grip.actorRoot = actor.transform;
                grip.gripOffset = weaponGripOffset;
                grip.extraEuler = weaponExtraEuler;
                grip.muzzle = muzzle;
                grip.targetLength = isSmg ? assaultPrimaryLength : pistolLength;

                // SMG and pistol have different trigger/grip geometry, so each weapon keeps its
                // own hand-relative placement and its own captured support-hand target.
                grip.manual = isSmg ? assaultPrimaryGripManual : pistolGripManual;
                grip.handOffset = isSmg ? assaultPrimaryHandOffset : pistolHandOffset;
                grip.handEuler = isSmg ? assaultPrimaryHandEuler : pistolHandEuler;

                if (isSmg)
                    AttachSupportIK(actor, model, assaultPrimaryLeftHandIK, assaultPrimarySupportCaptured,
                                    assaultPrimarySupportOffset, assaultPrimarySupportEuler);
                else
                    AttachSupportIK(actor, model, pistolLeftHandIK, pistolSupportCaptured,
                                    pistolSupportOffset, pistolSupportEuler);
            }
            else
            {
                // No hand bone and no marker — an unrigged voxel mesh. Hold the weapon out at
                // chest height on the actor's right so the silhouette still reads as armed.
                float h = setup != null ? setup.ActorHeight : 1.8f;
                model.transform.localPosition = new Vector3(0.28f, h * weaponFallbackHeightFraction, 0.3f);
                model.transform.localRotation = Quaternion.identity;
            }

            model.SetActive(true);
        }

        /// <summary>
        /// Support-hand IK, on the Animator's GameObject — see WeaponSupportIK. Added fresh per
        /// spawn because the assault guards are cloned from a template that has no weapon yet.
        /// </summary>
        void AttachSupportIK(GameObject actor, GameObject weapon, bool enabled, bool captured,
                             Vector3 supportOffset, Vector3 supportEuler)
        {
            if (!enabled || !captured) return;

            var animator = actor.GetComponentInChildren<Animator>();
            if (animator == null || !animator.isHuman) return;

            var existing = animator.GetComponent<WeaponSupportIK>();
            var ik = existing != null ? existing : animator.gameObject.AddComponent<WeaponSupportIK>();
            ik.weapon = weapon.transform;
            ik.supportOffset = supportOffset;
            ik.supportEuler = supportEuler;
            ik.captured = captured;
            ik.positionWeight = leftHandIKPositionWeight;
            ik.rotationWeight = leftHandIKRotationWeight;
        }
    }
}
