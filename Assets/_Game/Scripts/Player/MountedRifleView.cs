using UnityEngine;

namespace RooftopContract
{
    /// <summary>Keeps the bipod rifle planted at the parapet while its barrel follows the aim.</summary>
    public class MountedRifleView : MonoBehaviour
    {
        public SniperRig sniper;
        public Camera aimCamera;
        [Tooltip("월드에 고정할 Recon_P_Bipod 루트.")]
        public Transform bipod;
        public float pickupDuration = 0.75f;
        // Carries the punch the camera gave up when SniperRig.recoilPitchKick dropped to 2.2 deg.
        // The rifle jolting on its bipod reads as recoil without moving the sight off the target.
        public float recoilBack = 0.17f;
        public float recoilRecover = 9f;

        Vector3 mountPosition;
        Quaternion mountRotation;
        Vector3 bipodAnchorPosition;
        Quaternion bipodAnchorRotation;
        Vector3 bipodLocalPoint;
        Renderer[] renderers;
        float recoil;
        float pickupBlend;
        bool ready;

        void Start()
        {
            mountPosition = transform.position;
            mountRotation = transform.rotation;
            if (bipod != null)
            {
                bipodAnchorPosition = bipod.position;
                bipodAnchorRotation = bipod.rotation;
                bipodLocalPoint = transform.InverseTransformPoint(bipodAnchorPosition);
            }
            renderers = GetComponentsInChildren<Renderer>(true);
            if (sniper != null) sniper.Fired += OnFired;
            ready = true;
        }

        void OnDestroy()
        {
            if (sniper != null) sniper.Fired -= OnFired;
        }

        void OnFired() => recoil = 1f;

        void LateUpdate()
        {
            if (!ready || aimCamera == null) return;

            bool scoped = sniper != null && sniper.enabled && sniper.IsScoped;
            if (renderers != null)
                foreach (var r in renderers) if (r != null) r.enabled = !scoped;

            recoil = Mathf.Lerp(recoil, 0f, 1f - Mathf.Exp(-recoilRecover * Time.deltaTime));
            bool held = sniper != null && sniper.enabled;
            pickupBlend = Mathf.MoveTowards(pickupBlend, held ? 1f : 0f,
                Time.deltaTime / Mathf.Max(0.05f, pickupDuration));
            float eased = pickupBlend * pickupBlend * (3f - 2f * pickupBlend);
            Quaternion aim = Quaternion.Slerp(mountRotation, aimCamera.transform.rotation, eased);

            Vector3 position;
            if (bipod != null)
            {
                // Rotate the rifle around the planted bipod point, not around the camera or the
                // stock. Then counter-transform the bipod so its feet never leave the ledge.
                position = bipodAnchorPosition - aim * bipodLocalPoint;
                position -= aim * Vector3.forward * (recoil * recoilBack);
                transform.SetPositionAndRotation(position, aim);
                bipod.SetPositionAndRotation(bipodAnchorPosition, bipodAnchorRotation);
            }
            else
            {
                position = mountPosition - aim * Vector3.forward * (recoil * recoilBack);
                transform.SetPositionAndRotation(position, aim);
            }
        }
    }
}
