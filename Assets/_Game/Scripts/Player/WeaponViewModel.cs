using UnityEngine;

namespace RooftopContract
{
    /// <summary>
    /// Keeps the first-person weapon out of the way. The rifle sits low-right while the player
    /// is scanning at 1x and vanishes the moment they go to scope, where the eyepiece is the
    /// whole picture. It also carries the shot kick so the gun and the reticle move together.
    /// </summary>
    public class WeaponViewModel : MonoBehaviour
    {
        public SniperRig sniper;
        public PistolRig pistol;
        public Vector3 restPosition = new Vector3(0.28f, -0.24f, 0.45f);
        public Vector3 restEuler = new Vector3(0f, 0f, 0f);
        public float kickBack = 0.16f;
        public float kickPitch = 9f;
        public float recoverSpeed = 7f;

        float kick;

        void Start()
        {
            transform.localPosition = restPosition;
            transform.localEulerAngles = restEuler;
            if (sniper != null) sniper.Fired += () => kick = 1f;
            if (pistol != null) pistol.Fired += () => kick = 0.55f;
        }

        void LateUpdate()
        {
            bool scoped = sniper != null && sniper.enabled && sniper.IsScoped;
            foreach (var r in GetComponentsInChildren<Renderer>(true))
                r.enabled = !scoped;

            kick = Mathf.Lerp(kick, 0f, 1f - Mathf.Exp(-recoverSpeed * Time.deltaTime));
            transform.localPosition = restPosition - Vector3.forward * (kick * kickBack);
            transform.localEulerAngles = restEuler + new Vector3(-kick * kickPitch, 0f, 0f);
        }
    }
}
