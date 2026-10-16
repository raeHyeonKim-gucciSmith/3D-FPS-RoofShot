using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RooftopContract
{
    /// <summary>
    /// The sniper phase. Aim is stored as a settled yaw/pitch pair; recoil is a separate
    /// additive offset that decays back to zero. That separation is what makes the reticle
    /// return to *exactly* where the player was aiming before the shot.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class SniperRig : MonoBehaviour
    {
        [Header("Aim")]
        public float sensitivity = 0.14f;
        public float pitchMin = -60f, pitchMax = 35f;

        [Header("Zoom — RMB cycles 1x / 8x / 16x")]
        public float baseFov = 60f;
        // 4x put the target at 250m too small to read; 8x is where a voxel head becomes a
        // distinct shape, and 16x is where the shield formation's gaps become aimable.
        public float[] magnifications = { 1f, 8f, 16f };
        public float zoomLerpSpeed = 14f;

        [Header("Ammo")]
        public int magazine = 5;
        public float boltCycleTime = 1.2f;

        [Header("Ballistics")]
        // Deliberately slower than a real rifle round: at the game's 210-300 m engagement
        // ranges this creates a readable fire -> recover -> impact beat without turning the
        // projectile into a visibly floating pellet.
        public float muzzleVelocity = 260f;
        public float bulletGravity = 1f;
        public float damage = 95f;

        [Header("Zero — makes the crosshair honest at the contract's range")]
        // Without this the bullet lands one full head-height low at 250m (0.44m drop against a
        // 0.43m head box), so a crosshair placed on the head hits the neck. The scene builder
        // bakes the contract's own slant range here, exactly as a rifle is zeroed for a known
        // distance — the drop physics are untouched, the sight line is simply tilted to meet it.
        [Tooltip("영점 사거리(m). 0이면 보정 없음. 씬 생성 시 계약의 실제 사거리로 구워진다.")]
        public float zeroRange;

        [Header("Recoil — kicks up hard, then returns to the aimed point")]
        // 2.2 deg, not the original 6.5. At 8x the whole scope is 7.5 deg wide, so a 6.5 deg
        // kick threw the target clean off screen for 0.65s of a 0.96s flight — the player never
        // saw the round travel. The lost punch moves to the rifle model, which recoils visibly
        // without taking the target with it.
        public float recoilPitchKick = 2.2f;
        public float recoilYawKick = 1.2f;
        public float recoilRiseTime = 0.10f;
        public float recoilRecoverTime = 0.55f;

        [Header("Sway (scales with magnification)")]
        // Kept small on purpose: it is multiplied by sqrt(magnification), so 0.06 here becomes
        // ~0.17 deg at 8x — about 0.75m of wander at 250m. Enough to feel, not enough to rob.
        public float swayAmplitude = 0.06f;
        public float swayFrequency = 0.55f;

        [Header("Refs")]
        public Transform muzzle;

        public int AmmoLeft { get; private set; }
        public float Magnification => magnifications[zoomIndex];
        public bool IsScoped => zoomIndex > 0;
        public bool CanFire => IsScoped && AmmoLeft > 0 && boltTimer <= 0f && enabled;
        public float BoltProgress => boltCycleTime <= 0f ? 1f : 1f - Mathf.Clamp01(boltTimer / boltCycleTime);

        /// <summary>
        /// How far above the sight line the barrel points, in degrees. Derived from the same
        /// ballistics the projectile integrates, so the two can never disagree: over zeroRange
        /// the round falls 0.5*g*t^2, and this is the angle that cancels exactly that.
        /// </summary>
        public float ZeroElevationDeg
        {
            get
            {
                if (zeroRange <= 0f || muzzleVelocity <= 0f || bulletGravity <= 0f) return 0f;
                float t = zeroRange / muzzleVelocity;
                float drop = 0.5f * bulletGravity * t * t;
                return Mathf.Atan2(drop, zeroRange) * Mathf.Rad2Deg;
            }
        }

        public event Action Fired;
        public event Action OutOfAmmo;

        Camera cam;
        float aimYaw, aimPitch;          // where the player has settled the crosshair
        float recoilPitch, recoilYaw;    // additive, decays to 0
        float recoilVelPitch, recoilVelYaw;
        float kickRemaining;
        float boltTimer;
        int zoomIndex;
        float swaySeed;
        bool holdingBreath;

        void Awake()
        {
            cam = GetComponent<Camera>();
            AmmoLeft = magazine;
            swaySeed = UnityEngine.Random.value * 100f;
            Vector3 e = transform.eulerAngles;
            aimYaw = e.y;
            aimPitch = Mathf.DeltaAngle(0f, e.x);
            cam.fieldOfView = baseFov;
        }

        void OnEnable()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        void Update()
        {
            if (Time.timeScale <= 0.01f) return;

            if (boltTimer > 0f) boltTimer -= Time.deltaTime;

            ReadLook();
            ReadZoom();
            ReadFire();
            IntegrateRecoil();
            ApplyRotation();
        }

        void ReadLook()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;

            // Slowing the mouse with magnification is what makes 8x feel like 8x.
            float scale = sensitivity / Mathf.Max(1f, Magnification * 0.7f);
            Vector2 d = mouse.delta.ReadValue();
            aimYaw += d.x * scale;
            aimPitch = Mathf.Clamp(aimPitch - d.y * scale, pitchMin, pitchMax);

            var kb = Keyboard.current;
            holdingBreath = kb != null && kb.leftShiftKey.isPressed;
        }

        void ReadZoom()
        {
            var mouse = Mouse.current;
            if (mouse != null && mouse.rightButton.wasPressedThisFrame)
                zoomIndex = (zoomIndex + 1) % magnifications.Length;

            float target = baseFov / Magnification;
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, target, 1f - Mathf.Exp(-zoomLerpSpeed * Time.deltaTime));
        }

        void ReadFire()
        {
            var mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return;
            if (!IsScoped) return;

            if (AmmoLeft <= 0) { OutOfAmmo?.Invoke(); return; }
            if (boltTimer > 0f) return;

            Fire();
        }

        void Fire()
        {
            AmmoLeft--;
            boltTimer = boltCycleTime;

            // Gameplay ballistics start on the camera axis. Starting at the lower, offset visual
            // muzzle made an otherwise clear reticle shot run parallel into the nest parapet.
            Vector3 origin = transform.position;
            Vector3 visualOrigin = muzzle != null ? muzzle.position : origin;

            // Zeroed: the barrel sits a fraction of a degree above the sight line so the arc
            // crosses it again at zeroRange. Negative angle around the camera's right axis
            // raises the muzzle in Unity's left-handed frame.
            Vector3 dir = transform.forward;
            float zero = ZeroElevationDeg;
            if (zero > 0f) dir = Quaternion.AngleAxis(-zero, transform.right) * dir;

            Projectile.Spawn(origin, dir, muzzleVelocity, bulletGravity, damage,
                             gameObject, MaterialLibrary.Tracer, 45f, visualOrigin, true);

            kickRemaining = recoilRiseTime;
            recoilVelPitch = -recoilPitchKick / Mathf.Max(0.001f, recoilRiseTime);
            recoilVelYaw = UnityEngine.Random.Range(-recoilYawKick, recoilYawKick) / Mathf.Max(0.001f, recoilRiseTime);

            Fired?.Invoke();
            MissionDirector.Instance?.ReportSniperShot(transform.position);
        }

        void IntegrateRecoil()
        {
            if (kickRemaining > 0f)
            {
                float step = Mathf.Min(kickRemaining, Time.deltaTime);
                recoilPitch += recoilVelPitch * step;
                recoilYaw += recoilVelYaw * step;
                kickRemaining -= step;
            }
            else
            {
                // Critically-damped return: no overshoot, lands exactly on the original aim.
                float t = 1f - Mathf.Exp(-(5.5f / Mathf.Max(0.05f, recoilRecoverTime)) * Time.deltaTime);
                recoilPitch = Mathf.Lerp(recoilPitch, 0f, t);
                recoilYaw = Mathf.Lerp(recoilYaw, 0f, t);
                if (Mathf.Abs(recoilPitch) < 0.002f) recoilPitch = 0f;
                if (Mathf.Abs(recoilYaw) < 0.002f) recoilYaw = 0f;
            }
        }

        void ApplyRotation()
        {
            float swayScale = swayAmplitude * (holdingBreath ? 0.18f : 1f) * Mathf.Sqrt(Magnification);
            float t = Time.time * swayFrequency;
            float swayX = (Mathf.PerlinNoise(swaySeed, t) - 0.5f) * 2f * swayScale;
            float swayY = (Mathf.PerlinNoise(t, swaySeed + 31.7f) - 0.5f) * 2f * swayScale;

            transform.rotation = Quaternion.Euler(aimPitch + recoilPitch + swayY,
                                                  aimYaw + recoilYaw + swayX, 0f);
        }

        /// <summary>Distance to whatever the crosshair is on — the HUD shows it as a range readout.</summary>
        public float RangeToCrosshair()
        {
            if (ImpactResolver.Raycast(transform.position, transform.forward, 2000f, gameObject, out RaycastHit hit))
                return hit.distance;
            return 0f;
        }
    }
}
