using UnityEngine;
using UnityEngine.AI;

namespace RooftopContract
{
    public enum GuardWeapon { Smg, Pistol }

    /// <summary>
    /// A guard that takes the roof off the player one row of machinery at a time.
    ///
    /// The old version picked a random point on a ring around the player and ran straight at it,
    /// which is why the defense phase read as a stampede: every attacker crossed the whole roof
    /// in one move and stood in the open. This one only ever moves between cover slots, one
    /// stage at a time, and only when the director says the lane is clear and somebody else is
    /// shooting. Everything else it does — leaning out, firing, ducking back — happens from a
    /// slot it already holds.
    ///
    ///   Deploy   문에서 나와 1선 엄폐로.       (달리는 중, 사격 없음)
    ///   Hold     엄폐 뒤. 주기적으로 옆으로 나와 쏘고 다시 들어간다.
    ///   Advance  다음 단계 엄폐로 도약. 다른 누군가가 엄호사격 중일 때만 허가된다.
    ///   Push     최종 돌입. 후반에, 한 번에 한둘만.
    ///
    /// Marksmanship is still rolled per guard and every round is a real raycast inside a cone,
    /// so the player's cover does the work rather than a hidden hit-probability roll.
    /// </summary>
    [RequireComponent(typeof(Damageable))]
    [RequireComponent(typeof(NavMeshAgent))]
    public class AssaultGuard : MonoBehaviour
    {
        public enum Stance { Deploy, Hold, Advance, Push }

        [Header("Weapon roll")]
        [Range(0f, 1f)] public float smgChance = 0.6f;

        [Header("SMG profile")]
        public float smgDamage = 6f;
        public float smgRate = 0.085f;
        public Vector2 smgBurst = new Vector2(3, 7);
        public Vector2 smgSpread = new Vector2(2.6f, 5.5f);

        [Header("Pistol profile")]
        public float pistolDamage = 11f;
        public float pistolRate = 0.42f;
        public Vector2 pistolBurst = new Vector2(1, 2);
        public Vector2 pistolSpread = new Vector2(1.5f, 3.4f);
        [Tooltip("권총병이 엄폐에서 무릎을 꿇거나 일어나는 동안 이동을 기다리는 시간.")]
        public float pistolPostureTransition = 0.45f;

        [Header("Approach")]
        [Tooltip("각 단계에서 버티는 시간. 이 시간이 지나야 다음 단계 전진을 신청한다.")]
        public Vector2 holdTime = new Vector2(4f, 8f);
        [Tooltip("마지막 단계에서 돌입 신청까지 버티는 시간.")]
        public Vector2 finalStageHold = new Vector2(6f, 13f);
        [Tooltip("엄폐에서 몸을 내밀고 있는 시간.")]
        public Vector2 exposeTime = new Vector2(1.2f, 2.6f);
        [Tooltip("다시 들어가 숨어 있는 시간.")]
        public Vector2 hideTime = new Vector2(1.4f, 3.4f);
        [Tooltip("체력이 이 비율 아래로 떨어지면 한 단계 물러난다. 0이면 절대 물러나지 않는다.")]
        [Range(0f, 1f)] public float fallBackHealth = 0.45f;

        [Header("Engagement")]
        public Vector2 burstRest = new Vector2(0.6f, 1.8f);
        public float moveSpeed = 4.6f;
        public float pushSpeed = 5.4f;
        [Tooltip("최종 돌입 시 플레이어 엄폐물까지 접근하는 거리 (m).")]
        public float pushStandoff = 3.2f;
        public float damageFalloffStart = 25f;
        public float damageFalloffEnd = 60f;

        [Header("Fallback (엄폐 지점이 없는 씬용)")]
        public float preferredRangeMin = 8f;
        public float preferredRangeMax = 20f;
        public float repositionInterval = 6f;

        [Header("Refs")]
        public Transform muzzleOverride;
        [Tooltip("스폰될 때 디렉터가 채운다.")]
        public AssaultDirector director;

        public GuardWeapon Weapon { get; private set; }
        public Stance State { get; private set; } = Stance.Deploy;
        public int Stage { get; private set; }
        public bool IsDead => health != null && health.IsDead;

        /// <summary>
        /// Leaning out with a shot available. The director reads this to decide whether anyone
        /// may cross open ground — covering fire has to be real fire, not a guard hiding behind
        /// a chiller with a clear conscience.
        /// </summary>
        public bool IsSuppressing => exposed && hasLos && !IsDead;

        NavMeshAgent agent;
        Damageable health;
        ActorSetup setup;
        Animator animator;
        PlayerRoot player;

        float skill;              // 0 = sloppy, 1 = sharp. Fixed per guard.
        float fireTimer, restTimer;
        int roundsLeftInBurst;

        CoverPoint cover;
        float stanceTimer;        // hold at this stage, or the advance timeout
        float peekTimer;
        Vector3 peekSlot;
        bool exposed;
        bool hasLos;
        bool fellBack;
        float pistolPostureTimer;
        bool hasPistolModeParameter;
        bool hasPistolKneelParameter;
        bool hasPistolFireParameter;
        bool hasRifleFireParameter;
        Vector3 destination;
        bool hasDestination;

        // Fallback path only.
        float repositionTimer;
        Vector3 firingSlot;
        bool hasSlot;

        void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            health = GetComponent<Damageable>();
            setup = GetComponent<ActorSetup>();
            animator = GetComponentInChildren<Animator>();

            Weapon = Random.value < smgChance ? GuardWeapon.Smg : GuardWeapon.Pistol;
            CachePistolAnimatorParameters();
            SetPistolAnimationState(false);
            skill = Random.value;
            restTimer = Random.Range(0.4f, 1.6f);   // stagger the opening volley
        }

        void Start()
        {
            player = FindFirstObjectByType<PlayerRoot>();
            if (director == null) director = FindFirstObjectByType<AssaultDirector>();
            if (agent != null)
            {
                agent.speed = moveSpeed;
                agent.acceleration = 18f;
                agent.angularSpeed = 640f;
                // Short and precise: a lean-out is a one-metre step, and the default 0.4 m
                // stopping distance is most of that step.
                agent.stoppingDistance = 0.15f;
            }
            if (health != null) health.Damaged += OnDamaged;

            if (UsesCover) EnterDeploy();
            else PickFiringSlot();
        }

        void OnDestroy()
        {
            if (health != null) health.Damaged -= OnDamaged;
            if (director != null)
            {
                director.ReleaseCover(this);
                director.ReleaseAdvance(this);
                director.ReleasePush(this);
            }
        }

        bool UsesCover => director != null && director.coverPoints != null && director.coverPoints.Length > 0;

        void Update()
        {
            if (health.IsDead || player == null)
            {
                // Dying holds a slot until OnDestroy, which may be a while — hand it back now so
                // the queue behind keeps moving.
                if (health != null && health.IsDead && director != null && cover != null)
                {
                    director.ReleaseCover(this);
                    director.ReleaseAdvance(this);
                    director.ReleasePush(this);
                    cover = null;
                }
                return;
            }
            if (MissionDirector.Instance != null && MissionDirector.Instance.Phase == MissionPhase.Result) return;

            Vector3 aimPoint = player.AimPoint;
            hasLos = HasLineOfSight(aimPoint);

            if (UsesCover) TickStaged(aimPoint);
            else TickFallback(aimPoint);

            FaceTarget(aimPoint);
            if (animator != null && agent != null) animator.SetFloat("Speed", agent.velocity.magnitude);
        }

        // ------------------------------------------------------------------ staged approach

        void EnterDeploy()
        {
            State = Stance.Deploy;
            Stage = 0;
            exposed = false;
            stanceTimer = 0f;
        }

        void TickStaged(Vector3 aimPoint)
        {
            switch (State)
            {
                case Stance.Deploy: TickDeploy(); break;
                case Stance.Advance: TickAdvance(); break;
                case Stance.Hold: TickHold(aimPoint); break;
                case Stance.Push: TickPush(aimPoint); break;
            }
        }

        void TickDeploy()
        {
            // Claim the first row of cover and clear the doorway. Nothing else happens until
            // this guard is out of the way — the door is a chokepoint for its own side too.
            if (cover == null)
            {
                cover = director.ClaimCover(this, 1);
                if (cover == null)
                {
                    // First row is full. Try further in rather than standing in the door.
                    for (int s = 2; s <= director.HighestStage && cover == null; s++)
                        cover = director.ClaimCover(this, s);
                }
                if (cover == null) return;
                Stage = cover.stage;
                director.RequestAdvance(this, Stage);
            }

            SetDestination(cover.transform.position);
            if (Arrived(cover.transform.position, 1.0f))
            {
                director.ReleaseAdvance(this);
                EnterHold();
            }
        }

        void EnterHold()
        {
            State = Stance.Hold;
            SetPistolAnimationState(true);
            pistolPostureTimer = Weapon == GuardWeapon.Pistol ? pistolPostureTransition : 0f;
            agent.speed = moveSpeed;
            bool last = Stage >= director.HighestStage;
            Vector2 range = last ? finalStageHold : holdTime;
            stanceTimer = Random.Range(range.x, range.y);
            peekTimer = Random.Range(0.3f, 1.2f);
            exposed = false;
        }

        void TickHold(Vector3 aimPoint)
        {
            if (cover == null) { EnterDeploy(); return; }

            // The pistol clip must visibly finish standing-to-kneeling before this guard peeks
            // or fires. This also prevents a kneeling pose from sliding into the cover slot.
            if (WaitForPistolPosture())
            {
                SetDestination(cover.transform.position);
                return;
            }

            // Peek cycle. Out to the edge of the cover, fire while the shot exists, back in.
            peekTimer -= Time.deltaTime;
            if (peekTimer <= 0f)
            {
                exposed = !exposed;
                Vector2 span = exposed ? exposeTime : hideTime;
                // A sharper guard exposes for longer and hides for less.
                float bias = Mathf.Lerp(0.75f, 1.3f, exposed ? skill : 1f - skill);
                peekTimer = Random.Range(span.x, span.y) * bias;

                // Which side to lean out from is decided once per exposure, not per frame.
                // Re-solving it every frame let the answer flip as the player slid along their
                // wall, and the guard would jitter between the two sides of its own cover
                // instead of shooting from one of them.
                if (exposed)
                {
                    Vector3 requestedPeek = cover.PeekPoint(aimPoint, gameObject);
                    // A generated peek point can sit a few centimetres beyond the baked
                    // NavMesh edge. Some Web builds resolve that edge more strictly, leaving
                    // the agent unable to arrive and therefore unable to fire at all.
                    if (agent != null && NavMesh.SamplePosition(requestedPeek, out NavMeshHit navHit,
                                                                1.5f, agent.areaMask))
                        peekSlot = navHit.position;
                    else
                        peekSlot = requestedPeek;
                    hasDestination = false;
                }
            }

            Vector3 slot = exposed ? peekSlot : cover.transform.position;
            SetDestination(slot);

            // Line of sight is the authoritative firing condition. Requiring the agent to also
            // land within 0.7 m of an exact peek coordinate could suppress every shot when a
            // Web build resolved the NavMesh edge a little differently. The guard may begin
            // firing during the final step out of cover, which also reads more naturally.
            if (exposed && hasLos) TickFiring(aimPoint, Vector3.Distance(transform.position, aimPoint));
            else roundsLeftInBurst = 0;

            // Advance only after holding the line, and only when the director allows it.
            stanceTimer -= Time.deltaTime;
            if (stanceTimer > 0f) return;

            if (Stage < director.HighestStage)
            {
                if (director.RequestAdvance(this, Stage + 1))
                {
                    var next = director.ClaimCover(this, Stage + 1);
                    if (next != null) { cover = next; Stage = next.stage; EnterAdvance(); }
                    else director.ReleaseAdvance(this);
                }
                // Refused: keep holding and shooting, ask again shortly.
                if (State == Stance.Hold) stanceTimer = Random.Range(1.2f, 2.6f);
                return;
            }

            if (director.RequestPush(this)) EnterPush();
            else stanceTimer = Random.Range(2f, 4f);
        }

        void EnterAdvance()
        {
            State = Stance.Advance;
            BeginStandingMove();
            exposed = false;
            roundsLeftInBurst = 0;
            agent.speed = moveSpeed;
            // A bound that never lands would hold the lane permit forever.
            stanceTimer = 8f;
        }

        void TickAdvance()
        {
            if (cover == null) { director.ReleaseAdvance(this); EnterDeploy(); return; }
            if (WaitForPistolPosture()) return;

            SetDestination(cover.transform.position);
            stanceTimer -= Time.deltaTime;

            if (Arrived(cover.transform.position, 1.0f) || stanceTimer <= 0f)
            {
                director.ReleaseAdvance(this);
                EnterHold();
            }
        }

        void EnterPush()
        {
            State = Stance.Push;
            BeginStandingMove();
            exposed = true;                 // no more hiding
            agent.speed = pushSpeed;
            if (cover != null) { director.ReleaseCover(this); cover = null; }
            stanceTimer = 0f;
        }

        /// <summary>Used when the player stays hidden too long. Bypasses the normal late-game
        /// unlock, but still uses the same NavMesh push and combat behaviour.</summary>
        public void ForcePushFromCover()
        {
            if (IsDead || State == Stance.Push) return;
            if (director != null) director.ReleaseAdvance(this);
            EnterPush();
        }

        void TickPush(Vector3 aimPoint)
        {
            if (WaitForPistolPosture()) return;

            Vector3 target = player.transform.position;
            Vector3 from = transform.position;
            Vector3 dir = target - from;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.01f) target -= dir.normalized * pushStandoff;

            SetDestination(target);

            float dist = Vector3.Distance(transform.position, aimPoint);
            if (hasLos) TickFiring(aimPoint, dist);
            else roundsLeftInBurst = 0;
        }

        /// <summary>
        /// A hurt attacker gives ground once. It makes the player's hits legible — something
        /// visibly happens when you land one — and it re-opens the near slot for somebody else.
        /// </summary>
        void OnDamaged(DamageInfo info)
        {
            if (fallBackHealth <= 0f || fellBack || director == null) return;
            if (health == null || health.Normalized > fallBackHealth) return;
            if (State != Stance.Hold || Stage <= 1) return;

            var back = director.ClaimCover(this, Stage - 1);
            if (back == null) return;

            fellBack = true;
            cover = back;
            Stage = back.stage;
            EnterAdvance();
        }

        void SetDestination(Vector3 pos)
        {
            if (agent == null || !agent.isOnNavMesh) return;
            agent.isStopped = false;
            // Re-pathing every frame to the same spot is wasted work and makes the agent stutter.
            if (hasDestination && (destination - pos).sqrMagnitude < 0.04f) return;
            destination = pos;
            hasDestination = true;
            agent.SetDestination(pos);
        }

        bool Arrived(Vector3 pos, float tolerance)
        {
            Vector3 d = transform.position - pos;
            d.y = 0f;
            return d.sqrMagnitude <= tolerance * tolerance;
        }

        // ------------------------------------------------------------------ fallback

        void TickFallback(Vector3 aimPoint)
        {
            float dist = Vector3.Distance(transform.position, aimPoint);

            repositionTimer -= Time.deltaTime;
            if (repositionTimer <= 0f || !hasSlot) PickFiringSlot();
            if (hasSlot) SetDestination(firingSlot);

            bool inRange = dist <= preferredRangeMax * 1.4f;
            bool settled = agent == null || (!agent.pathPending && agent.remainingDistance < 2.5f);
            if (inRange && settled && hasLos) TickFiring(aimPoint, dist);
            else roundsLeftInBurst = 0;
        }

        void PickFiringSlot()
        {
            repositionTimer = repositionInterval * Random.Range(0.6f, 1.5f);
            if (player == null) return;

            Vector3 center = player.transform.position;
            for (int i = 0; i < 8; i++)
            {
                float angle = Random.Range(0f, 360f);
                float radius = Random.Range(preferredRangeMin, preferredRangeMax);
                Vector3 candidate = center + Quaternion.Euler(0f, angle, 0f) * Vector3.forward * radius;
                if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 4f, NavMesh.AllAreas))
                {
                    firingSlot = hit.position;
                    hasSlot = true;
                    return;
                }
            }
            firingSlot = center;
            hasSlot = true;
        }

        // ------------------------------------------------------------------ shooting

        void FaceTarget(Vector3 aimPoint)
        {
            // Face where you are going while bounding, face the player the rest of the time.
            Vector3 look = State == Stance.Advance || State == Stance.Deploy
                         ? agent.velocity
                         : aimPoint - transform.position;
            look.y = 0f;
            if (look.sqrMagnitude < 0.04f)
            {
                look = aimPoint - transform.position;
                look.y = 0f;
                if (look.sqrMagnitude < 0.01f) return;
            }
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(look), 7f * Time.deltaTime);
        }

        void TickFiring(Vector3 aimPoint, float dist)
        {
            if (roundsLeftInBurst <= 0)
            {
                restTimer -= Time.deltaTime;
                if (restTimer > 0f) return;
                Vector2 burst = Weapon == GuardWeapon.Smg ? smgBurst : pistolBurst;
                roundsLeftInBurst = Random.Range((int)burst.x, (int)burst.y + 1);
                fireTimer = 0f;
                if (Weapon == GuardWeapon.Smg && animator != null && hasRifleFireParameter)
                    animator.SetTrigger("RifleFire");
            }

            fireTimer -= Time.deltaTime;
            if (fireTimer > 0f) return;

            FireOnce(aimPoint, dist);
            fireTimer = Weapon == GuardWeapon.Smg ? smgRate : pistolRate;
            roundsLeftInBurst--;
            if (roundsLeftInBurst <= 0)
                restTimer = Random.Range(burstRest.x, burstRest.y);
        }

        void FireOnce(Vector3 aimPoint, float dist)
        {
            if (Weapon == GuardWeapon.Pistol && State == Stance.Hold &&
                animator != null && hasPistolFireParameter)
                animator.SetTrigger("PistolFire");

            // Combat rays must not depend on the visual weapon placement. A bad imported grip
            // or muzzle transform should make a gun look wrong, never disable the AI.
            Vector3 origin = CombatOrigin();
            Vector3 visualOrigin = MuzzlePoint();
            Vector2 spreadRange = Weapon == GuardWeapon.Smg ? smgSpread : pistolSpread;
            // Skill picks where in the weapon's spread band this guard sits, then each round
            // is jittered inside that cone. Two guards with the same gun are not the same threat.
            float spread = Mathf.Lerp(spreadRange.y, spreadRange.x, skill) * Random.Range(0.7f, 1.35f);

            Vector3 dir = PistolRig.Spread((aimPoint - origin).normalized, spread);
            float dmg = (Weapon == GuardWeapon.Smg ? smgDamage : pistolDamage) * Falloff(dist);

            if (ImpactResolver.Raycast(origin, dir, 120f, gameObject, out RaycastHit hit))
                ImpactResolver.Resolve(hit, dmg, dir, gameObject);

            TracerFx.Draw(visualOrigin, origin + dir * 60f, 0.04f);
            MuzzleFlash.Play(visualOrigin, dir);
            GameAudio.PlayEnemyShot(origin, Weapon == GuardWeapon.Smg);
        }

        void CachePistolAnimatorParameters()
        {
            if (animator == null) return;
            foreach (var parameter in animator.parameters)
            {
                if (parameter.name == "IsPistol") hasPistolModeParameter = true;
                else if (parameter.name == "PistolKneel") hasPistolKneelParameter = true;
                else if (parameter.name == "PistolFire") hasPistolFireParameter = true;
                else if (parameter.name == "RifleFire") hasRifleFireParameter = true;
            }
        }

        void SetPistolAnimationState(bool kneeling)
        {
            if (animator == null) return;
            if (hasPistolModeParameter) animator.SetBool("IsPistol", Weapon == GuardWeapon.Pistol);
            if (hasPistolKneelParameter)
                animator.SetBool("PistolKneel", Weapon == GuardWeapon.Pistol && kneeling);
        }

        void BeginStandingMove()
        {
            SetPistolAnimationState(false);
            pistolPostureTimer = Weapon == GuardWeapon.Pistol ? pistolPostureTransition : 0f;
            hasDestination = false;
        }

        bool WaitForPistolPosture()
        {
            if (Weapon != GuardWeapon.Pistol || pistolPostureTimer <= 0f) return false;
            pistolPostureTimer -= Time.deltaTime;
            if (agent != null && agent.isOnNavMesh)
            {
                agent.isStopped = true;
                agent.ResetPath();
            }
            hasDestination = false;
            return pistolPostureTimer > 0f;
        }

        float Falloff(float dist)
        {
            if (dist <= damageFalloffStart) return 1f;
            float t = Mathf.InverseLerp(damageFalloffStart, damageFalloffEnd, dist);
            return Mathf.Lerp(1f, 0.35f, t);
        }

        Vector3 MuzzlePoint()
        {
            if (muzzleOverride != null) return muzzleOverride.position;
            float h = setup != null ? setup.ActorHeight : 1.8f;
            return transform.position + Vector3.up * (h * 0.72f) + transform.forward * 0.45f;
        }

        /// <summary>
        /// Stable origin for visibility and damage. It belongs to the actor, not the imported
        /// weapon, so animation, grip offsets and an incorrectly positioned muzzle cannot put
        /// the ray inside a wall or below the roof.
        /// </summary>
        Vector3 CombatOrigin()
        {
            float h = setup != null ? setup.ActorHeight : 1.8f;
            return transform.position + Vector3.up * (h * 0.74f) + transform.forward * 0.12f;
        }

        bool HasLineOfSight(Vector3 aimPoint)
        {
            Vector3 origin = CombatOrigin();
            Vector3 dir = aimPoint - origin;
            float d = dir.magnitude;
            if (d < 0.1f) return true;
            if (!ImpactResolver.Raycast(origin, dir / d, d, gameObject, out RaycastHit hit))
                return true;
            // Anything belonging to the player counts as visible; anything else is cover.
            return player != null && hit.collider.GetComponentInParent<PlayerRoot>() == player;
        }
    }

    /// <summary>
    /// A very small pooled muzzle flare for the defense phase. Four instances cover overlapping
    /// SMG fire without allocating and destroying a Light for every bullet, which matters in a
    /// Web build. The Lines provide the visible flare; the shadowless Point Light only adds a
    /// brief glow to nearby surfaces.
    /// </summary>
    public static class MuzzleFlash
    {
        const int PoolSize = 4;
        static MuzzleFlashPulse[] pool;
        static int cursor;

        public static void Play(Vector3 pos, Vector3 dir)
        {
            var director = MissionDirector.Instance;
            if (director == null || director.Phase != MissionPhase.Defense) return;

            EnsurePool();
            var pulse = pool[cursor++ % PoolSize];
            if (pulse == null)
            {
                pool = null;
                EnsurePool();
                pulse = pool[cursor++ % PoolSize];
            }
            pulse.Play(pos, dir);
        }

        static void EnsurePool()
        {
            if (pool != null && pool.Length == PoolSize && pool[0] != null) return;
            pool = new MuzzleFlashPulse[PoolSize];
            cursor = 0;
            for (int i = 0; i < PoolSize; i++)
            {
                var go = new GameObject($"MuzzleFlash_Pooled_{i}");
                pool[i] = go.AddComponent<MuzzleFlashPulse>();
            }
        }
    }

    public sealed class MuzzleFlashPulse : MonoBehaviour
    {
        Light pointLight;
        LineRenderer horizontal;
        LineRenderer vertical;
        float disableAt;

        void Awake()
        {
            pointLight = gameObject.AddComponent<Light>();
            pointLight.type = LightType.Point;
            pointLight.color = new Color(1f, 0.78f, 0.42f);
            pointLight.intensity = 3.2f;
            pointLight.range = 3.5f;
            pointLight.shadows = LightShadows.None;
            pointLight.enabled = false;

            horizontal = CreateLine("FlareHorizontal");
            vertical = CreateLine("FlareVertical");
        }

        LineRenderer CreateLine(string objectName)
        {
            var child = new GameObject(objectName);
            child.transform.SetParent(transform, false);
            var line = child.AddComponent<LineRenderer>();
            line.material = MaterialLibrary.Tracer;
            line.positionCount = 2;
            line.useWorldSpace = true;
            line.widthMultiplier = 0.055f;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.enabled = false;
            return line;
        }

        public void Play(Vector3 pos, Vector3 direction)
        {
            Vector3 forward = direction.sqrMagnitude > 0.001f ? direction.normalized : transform.forward;
            Vector3 center = pos + forward * 0.12f;
            transform.position = center;

            Vector3 right = Vector3.Cross(forward, Vector3.up);
            if (right.sqrMagnitude < 0.01f) right = Vector3.right;
            right.Normalize();
            Vector3 up = Vector3.Cross(right, forward).normalized;

            horizontal.SetPosition(0, center - right * 0.16f);
            horizontal.SetPosition(1, center + right * 0.16f);
            vertical.SetPosition(0, center - up * 0.12f);
            vertical.SetPosition(1, center + up * 0.12f);

            pointLight.enabled = true;
            horizontal.enabled = true;
            vertical.enabled = true;
            disableAt = Time.time + 0.04f;
        }

        void Update()
        {
            if (!pointLight.enabled || Time.time < disableAt) return;
            pointLight.enabled = false;
            horizontal.enabled = false;
            vertical.enabled = false;
        }
    }
}
