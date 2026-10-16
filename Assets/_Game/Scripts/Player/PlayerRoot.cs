using UnityEngine;
using UnityEngine.InputSystem;

namespace RooftopContract
{
    /// <summary>
    /// Owns the player's body, health, and the swap from sniper to sidearm.
    /// The player never walks freely: in the sniper phase they are locked to the nest,
    /// in the defense phase they slide along the parapet between cover and peek height.
    /// </summary>
    public class PlayerRoot : MonoBehaviour
    {
        [Header("Refs")]
        public Camera playerCamera;
        public SniperRig sniper;
        public PistolRig pistol;
        public PlayerIntro intro;
        public Transform sniperNest;      // camera pose while sniping
        public Transform coverAnchor;     // waist-height behind the parapet
        public GameObject sniperModel;
        public GameObject pistolModel;

        [Header("Defense movement")]
        public float lateralSpeed = 3.2f;
        public float lateralLimit = 6f;
        public float peekHeight = 0.85f;
        public float postureLerpSpeed = 9f;

        [Header("Defense look")]
        public float sensitivity = 0.11f;
        public float pitchMin = -55f, pitchMax = 45f;

        [Header("Health")]
        public Damageable health;

        public bool IsPeeking { get; private set; }
        /// <summary>Where guards should aim. Follows the camera so cover geometry actually matters.</summary>
        public Vector3 AimPoint => playerCamera != null ? playerCamera.transform.position : transform.position + Vector3.up * 1.5f;

        float lateralOffset;
        float yaw, pitch;
        float postureBlend;      // 0 = behind cover, 1 = peeking
        Vector3 coverBasePos;
        Vector3 lateralAxis;
        bool defenseActive;

        void Awake()
        {
            if (health == null) health = GetComponent<Damageable>();
            HitFeedback.SetPlayer(gameObject);
        }

        void Start()
        {
            if (health != null) health.Died += _ => MissionDirector.Instance?.ReportPlayerDead();

            var director = MissionDirector.Instance;
            if (director != null)
            {
                director.PhaseChanged += OnPhaseChanged;
                director.MissionEnded += _ => ReleaseCursor();
            }
            EnterSniperMode();
        }

        void OnDestroy()
        {
            var director = MissionDirector.Instance;
            if (director != null) director.PhaseChanged -= OnPhaseChanged;
        }

        void OnPhaseChanged(MissionPhase phase)
        {
            if (phase == MissionPhase.Defense) EnterDefenseMode();
            else if (phase == MissionPhase.Result) ReleaseCursor();
        }

        void EnterSniperMode()
        {
            defenseActive = false;
            if (sniper != null) sniper.enabled = intro == null || !intro.BlocksInput;
            if (pistol != null) pistol.enabled = false;
            if (sniperModel != null) sniperModel.SetActive(true);
            if (pistolModel != null) pistolModel.SetActive(false);

            if (sniperNest != null && playerCamera != null)
            {
                playerCamera.transform.SetPositionAndRotation(sniperNest.position, sniperNest.rotation);
            }
        }

        void EnterDefenseMode()
        {
            defenseActive = true;
            IsPeeking = true;
            postureBlend = 1f;
            if (sniper != null) sniper.enabled = false;
            if (pistol != null) pistol.enabled = true;
            if (sniperModel != null) sniperModel.SetActive(false);
            if (pistolModel != null) pistolModel.SetActive(true);

            if (coverAnchor != null)
            {
                coverBasePos = coverAnchor.position;
                lateralAxis = coverAnchor.right;
                // Face away from the parapet, back toward the stairwells the guards come from.
                yaw = coverAnchor.eulerAngles.y;
                pitch = 0f;
            }
            if (playerCamera != null) playerCamera.fieldOfView = 68f;
        }

        void Update()
        {
            if (!defenseActive) return;
            if (MissionDirector.Instance != null && MissionDirector.Instance.Phase == MissionPhase.Result) return;

            var mouse = Mouse.current;
            var kb = Keyboard.current;

            if (mouse != null)
            {
                Vector2 d = mouse.delta.ReadValue();
                yaw += d.x * sensitivity;
                pitch = Mathf.Clamp(pitch - d.y * sensitivity, pitchMin, pitchMax);
                // Standing is the default so the player can watch the assault arrive. RMB is
                // the deliberate defensive action: hold to duck, release to stand and fire.
                IsPeeking = !mouse.rightButton.isPressed;
            }

            if (pistol != null) pistol.firingAllowed = IsPeeking;

            float move = 0f;
            if (kb != null)
            {
                if (kb.aKey.isPressed) move -= 1f;
                if (kb.dKey.isPressed) move += 1f;
            }
            lateralOffset = Mathf.Clamp(lateralOffset + move * lateralSpeed * Time.deltaTime, -lateralLimit, lateralLimit);

            float targetPosture = IsPeeking ? 1f : 0f;
            postureBlend = Mathf.Lerp(postureBlend, targetPosture, 1f - Mathf.Exp(-postureLerpSpeed * Time.deltaTime));

            if (playerCamera != null)
            {
                Vector3 pos = coverBasePos + lateralAxis * lateralOffset + Vector3.up * (peekHeight * postureBlend);
                playerCamera.transform.position = pos;
                float recoil = pistol != null ? pistol.RecoilPitch : 0f;
                playerCamera.transform.rotation = Quaternion.Euler(pitch + recoil, yaw, 0f);
            }
        }

        void ReleaseCursor()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}
