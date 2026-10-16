using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RooftopContract
{
    /// <summary>Defense phase sidearm. Hitscan, tight spread, rewards headshots on approaching guards.</summary>
    public class PistolRig : MonoBehaviour
    {
        [Header("Ballistics")]
        public float damage = 34f;
        public float fireInterval = 0.22f;
        public float spreadDegrees = 0.9f;
        public float range = 120f;

        [Header("Magazine")]
        public int magazineSize = 15;
        public float reloadTime = 1.4f;

        [Header("Recoil")]
        public float recoilKick = 2.4f;
        public float recoilRecoverTime = 0.18f;

        public int AmmoInMag { get; private set; }
        public bool IsReloading => reloadTimer > 0f;
        public float ReloadProgress => reloadTime <= 0f ? 1f : 1f - Mathf.Clamp01(reloadTimer / reloadTime);

        public event Action Fired;
        public event Action ReloadStarted;

        [Header("Refs")]
        public Camera aimCamera;
        public Transform muzzle;
        /// <summary>Set by PlayerDefenseController — firing is only allowed while peeking out of cover.</summary>
        public bool firingAllowed = true;

        float cooldown, reloadTimer;
        public float RecoilPitch { get; private set; }
        public float AimBloom { get; private set; }

        void Awake() => AmmoInMag = magazineSize;

        void Update()
        {
            if (Time.timeScale <= 0.01f) return;

            if (cooldown > 0f) cooldown -= Time.deltaTime;

            if (reloadTimer > 0f)
            {
                reloadTimer -= Time.deltaTime;
                if (reloadTimer <= 0f) AmmoInMag = magazineSize;
            }

            var kb = Keyboard.current;
            if (kb != null && kb.rKey.wasPressedThisFrame) BeginReload();

            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.isPressed && firingAllowed) TryFire();

            RecoilPitch = Mathf.Lerp(RecoilPitch, 0f,
                1f - Mathf.Exp(-(5f / Mathf.Max(0.05f, recoilRecoverTime)) * Time.deltaTime));
            AimBloom = Mathf.MoveTowards(AimBloom, 0f, 1.8f * Time.deltaTime);
        }

        public void BeginReload()
        {
            if (IsReloading || AmmoInMag >= magazineSize) return;
            reloadTimer = reloadTime;
            ReloadStarted?.Invoke();
        }

        void TryFire()
        {
            if (cooldown > 0f || IsReloading) return;
            if (AmmoInMag <= 0) { BeginReload(); return; }

            AmmoInMag--;
            cooldown = fireInterval;

            Camera c = aimCamera != null ? aimCamera : Camera.main;
            Vector3 origin = c.transform.position;
            Vector3 dir = Spread(c.transform.forward, spreadDegrees + AimBloom * 1.35f);

            if (ImpactResolver.Raycast(origin, dir, range, gameObject, out RaycastHit hit))
                ImpactResolver.Resolve(hit, damage, dir, gameObject);

            TracerFx.Draw(muzzle != null ? muzzle.position : origin,
                          origin + dir * range, 0.05f);

            // The sidearm only exists during Defense. MuzzleFlash also checks the phase so
            // this inexpensive pooled light can never leak into the sniper portion.
            Vector3 flashOrigin = muzzle != null ? muzzle.position : origin;
            MuzzleFlash.Play(flashOrigin, dir);

            RecoilPitch -= recoilKick;
            AimBloom = Mathf.Clamp01(AimBloom + 0.30f);
            Fired?.Invoke();
        }

        public static Vector3 Spread(Vector3 forward, float degrees)
        {
            if (degrees <= 0f) return forward;
            float yaw = UnityEngine.Random.Range(-degrees, degrees);
            float pitch = UnityEngine.Random.Range(-degrees, degrees);
            return Quaternion.Euler(pitch, yaw, 0f) * forward;
        }
    }

    /// <summary>Throwaway line renderers for hitscan tracers.</summary>
    public static class TracerFx
    {
        public static void Draw(Vector3 a, Vector3 b, float life)
        {
            var go = new GameObject("Tracer");
            var lr = go.AddComponent<LineRenderer>();
            lr.material = MaterialLibrary.Tracer;
            lr.widthMultiplier = 0.035f;
            lr.positionCount = 2;
            lr.useWorldSpace = true;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.SetPosition(0, a);
            lr.SetPosition(1, b);
            UnityEngine.Object.Destroy(go, life);
        }
    }
}
