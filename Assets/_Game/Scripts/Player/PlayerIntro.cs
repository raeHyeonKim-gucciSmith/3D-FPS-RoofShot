using UnityEngine;

namespace RooftopContract
{
    /// <summary>Short first-person walk from the rooftop to the rifle already set on its bipod.</summary>
    public class PlayerIntro : MonoBehaviour
    {
        public bool BlocksInput { get; private set; } = true;
        public Camera playerCamera;
        public SniperRig sniper;
        public Transform sniperNest;
        public Transform weaponHolder;
        public Transform rifle;
        public float walkDistance = 4.5f;
        public float duration = 3.2f;

        Vector3 startPosition;
        Quaternion startRotation;
        Vector3 targetPosition;
        Quaternion targetRotation;
        float clock;
        bool running;

        void Start()
        {
            if (playerCamera == null || sniperNest == null || rifle == null) return;

            targetPosition = sniperNest.position;
            targetRotation = sniperNest.rotation;
            startPosition = targetPosition - targetRotation * Vector3.forward * walkDistance;
            startRotation = targetRotation;

            // Calculate its final camera-relative pose, then leave it behind in world space.
            playerCamera.transform.SetPositionAndRotation(targetPosition, targetRotation);
            Vector3 installedPosition = rifle.position;
            Quaternion installedRotation = rifle.rotation;
            rifle.SetParent(null, true);
            rifle.SetPositionAndRotation(installedPosition, installedRotation);

            playerCamera.transform.SetPositionAndRotation(startPosition, startRotation);
            if (sniper != null) sniper.enabled = false;
            running = true;
        }

        void LateUpdate()
        {
            if (!running) return;
            clock += Time.deltaTime;
            float t = Mathf.Clamp01(clock / Mathf.Max(0.01f, duration));
            float eased = t * t * (3f - 2f * t);

            Vector3 position = Vector3.Lerp(startPosition, targetPosition, eased);
            float fade = Mathf.Sin(t * Mathf.PI);
            position += Vector3.up * (Mathf.Sin(t * Mathf.PI * 8f) * 0.035f * fade);
            playerCamera.transform.SetPositionAndRotation(position, Quaternion.Slerp(startRotation, targetRotation, eased));

            if (t < 1f) return;
            running = false;
            BlocksInput = false;
            playerCamera.transform.SetPositionAndRotation(targetPosition, targetRotation);
            // Do not parent the rifle back to the camera. It remains planted on the parapet;
            // arriving only enables control, which reads as taking hold of an installed weapon.
            if (sniper != null) sniper.enabled = true;
        }
    }
}
