using UnityEngine;

namespace RooftopContract
{
    public class PistolViewModel : MonoBehaviour
    {
        public PistolRig pistol;
        public Transform slide;
        public Transform slideFollower;
        public Transform magazine;
        public float slideTravel = 0.065f;
        public float slideReturnSpeed = 32f;
        public float reloadPullBack = 0.16f;

        Vector3 restPosition, slideRest, slideFollowerRest, magazineRest;
        Quaternion restRotation, magazineRestRotation;
        Transform freshMagazine;
        Renderer[] emptyMagazineRenderers, freshMagazineRenderers;
        float slideKick;
        float shotSlideTimer;
        bool reloading, reloadStartedEmpty;

        void Start()
        {
            restPosition = transform.localPosition;
            restRotation = transform.localRotation;
            // Pistol_J's visible slide is split into two sibling meshes. The asset's "Bolt"
            // is not the outer slide; BarrelGuard is the slide body and Sight rides on it.
            Transform visibleSlide = FindExact(transform, "Pistol_J_BarrelGuard");
            Transform visibleSight = FindExact(transform, "Pistol_J_Sight");

            // Always replace serialized scene references. This prevents an older scene or
            // an accidental Inspector assignment from ever moving Pistol_J_Barrel.
            slide = visibleSlide;
            slideFollower = visibleSight;
            if (slide != null) slideRest = slide.localPosition;
            if (slideFollower != null) slideFollowerRest = slideFollower.localPosition;
            if (magazine != null)
            {
                magazineRest = magazine.localPosition;
                magazineRestRotation = magazine.localRotation;
                emptyMagazineRenderers = magazine.GetComponentsInChildren<Renderer>(true);
                var clone = Instantiate(magazine.gameObject, magazine.parent);
                clone.name = "Pistol_J_FreshMag";
                freshMagazine = clone.transform;
                freshMagazineRenderers = clone.GetComponentsInChildren<Renderer>(true);
                SetVisible(freshMagazineRenderers, false);
            }
            if (pistol != null)
            {
                pistol.Fired += OnFired;
                pistol.ReloadStarted += OnReload;
            }
        }

        void OnDestroy()
        {
            if (pistol == null) return;
            pistol.Fired -= OnFired;
            pistol.ReloadStarted -= OnReload;
        }

        void OnFired()
        {
            // Keep the slide fully rearward for a few frames. The old single MoveTowards
            // impulse had already lost most of its travel in the first rendered frame.
            shotSlideTimer = 0.12f;
            slideKick = 1f;
        }

        void OnReload()
        {
            reloading = true;
            reloadStartedEmpty = pistol != null && pistol.AmmoInMag <= 0;
        }

        void LateUpdate()
        {
            if (pistol == null) return;
            float t = reloading && pistol.IsReloading ? Mathf.Clamp01(pistol.ReloadProgress) : 0f;
            AnimateSlide(t);
            if (reloading && pistol.IsReloading) AnimateReload(t);
            else ReturnToReady();
        }

        void AnimateSlide(float t)
        {
            float amount;
            if (reloading && pistol.IsReloading && reloadStartedEmpty)
                amount = t < 0.80f ? 1f : 1f - Mathf.SmoothStep(0f, 1f, (t - 0.80f) / 0.09f);
            else if (pistol.AmmoInMag <= 0 && !pistol.IsReloading)
                amount = 1f;
            else
            {
                if (shotSlideTimer > 0f)
                {
                    shotSlideTimer = Mathf.Max(0f, shotSlideTimer - Time.deltaTime);
                    // 45 ms locked to the rear, followed by a visible 75 ms return.
                    slideKick = shotSlideTimer > 0.075f ? 1f : shotSlideTimer / 0.075f;
                }
                else
                {
                    slideKick = 0f;
                }
                amount = slideKick;
            }
            if (slide != null) slide.localPosition = slideRest - Vector3.forward * (slideTravel * amount);
            if (slideFollower != null)
                slideFollower.localPosition = slideFollowerRest - Vector3.forward * (slideTravel * amount);
        }

        void AnimateReload(float t)
        {
            // Bring the pistol close, stand it up and roll it far enough that the magazine
            // well is readable. Hold that presentation through the entire magazine exchange,
            // then return only after the slide has gone forward.
            float present = SmoothSegment(t, 0.00f, 0.20f);
            float returnToAim = SmoothSegment(t, 0.86f, 1.00f);
            float pose = present * (1f - returnToAim);

            Vector3 reloadOffset = new Vector3(0.055f, 0.075f, -reloadPullBack);
            transform.localPosition = Vector3.Lerp(restPosition, restPosition + reloadOffset, pose);
            transform.localRotation = restRotation * Quaternion.Slerp(
                Quaternion.identity, Quaternion.Euler(-31f, -48f, -23f), pose);

            AnimateEmptyMagazine(t);
            AnimateFreshMagazine(t);

            if (reloadStartedEmpty && t >= 0.80f && t <= 0.94f)
            {
                float impact = 1f - Mathf.Clamp01((t - 0.80f) / 0.14f);
                float vibration = Mathf.Sin((t - 0.80f) * 150f) * impact;
                transform.localPosition += new Vector3(vibration * 0.006f, vibration * 0.009f, -impact * 0.025f);
                transform.localRotation *= Quaternion.Euler(vibration * 1.8f, 0f, vibration * 0.8f);
            }
        }

        void AnimateEmptyMagazine(float t)
        {
            if (magazine == null) return;
            if (t < 0.23f)
            {
                SetVisible(emptyMagazineRenderers, true);
                magazine.localPosition = magazineRest;
                magazine.localRotation = magazineRestRotation;
                return;
            }
            float eject = Mathf.Clamp01((t - 0.23f) / 0.18f);
            SetVisible(emptyMagazineRenderers, eject < 0.98f);
            magazine.localPosition = magazineRest
                + Vector3.down * Mathf.SmoothStep(0f, 0.48f, eject)
                + Vector3.back * Mathf.SmoothStep(0f, 0.045f, eject);
            magazine.localRotation = magazineRestRotation * Quaternion.Euler(9f * eject, 0f, 7f * eject);
        }

        void AnimateFreshMagazine(float t)
        {
            if (freshMagazine == null) return;
            if (t < 0.36f || t > 0.91f)
            {
                SetVisible(freshMagazineRenderers, false);
                return;
            }
            SetVisible(freshMagazineRenderers, true);
            float insert = SmoothSegment(t, 0.36f, 0.72f);
            freshMagazine.localPosition = Vector3.Lerp(
                magazineRest + Vector3.down * 0.50f + Vector3.back * 0.055f, magazineRest, insert);
            freshMagazine.localRotation = Quaternion.Slerp(
                magazineRestRotation * Quaternion.Euler(-11f, 0f, -7f), magazineRestRotation, insert);

            // A short seating hit makes the new magazine feel pushed firmly into the grip.
            if (t >= 0.72f && t < 0.80f)
            {
                float seat = 1f - Mathf.Clamp01((t - 0.72f) / 0.08f);
                transform.localPosition += Vector3.up * (0.012f * seat);
            }
        }

        static float SmoothSegment(float value, float from, float to)
        {
            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(from, to, value));
        }

        void ReturnToReady()
        {
            if (reloading)
            {
                reloading = false;
                SetVisible(emptyMagazineRenderers, true);
                SetVisible(freshMagazineRenderers, false);
                if (magazine != null)
                {
                    magazine.localPosition = magazineRest;
                    magazine.localRotation = magazineRestRotation;
                }
            }
            transform.localPosition = Vector3.Lerp(transform.localPosition, restPosition,
                1f - Mathf.Exp(-20f * Time.deltaTime));
            transform.localRotation = Quaternion.Slerp(transform.localRotation, restRotation,
                1f - Mathf.Exp(-20f * Time.deltaTime));
        }

        static void SetVisible(Renderer[] renderers, bool visible)
        {
            if (renderers == null) return;
            foreach (var r in renderers) if (r != null) r.enabled = visible;
        }

        static Transform FindExact(Transform root, string objectName)
        {
            if (root == null) return null;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == objectName) return t;
            return null;
        }
    }
}
