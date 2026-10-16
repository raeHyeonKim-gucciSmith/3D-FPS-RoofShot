using UnityEngine;

namespace RooftopContract
{
    /// <summary>
    /// The exfil bird. Flies in when the survival clock runs out and holds off the front parapet
    /// until the mission closes.
    ///
    /// It is scenery, deliberately. It carries no weapons and kills nothing: the player is meant
    /// to still be fighting when it arrives, so the last seconds of a contract are the loudest
    /// ones. If the helicopter cleared the roof the ending would go quiet exactly when it should
    /// not.
    ///
    /// It also never lands. The rooftop it would land on is the fight — the plant deck is packed
    /// with the cover the assault is using — so it comes alongside the front edge instead, over
    /// the city, which puts the extraction on the opposite side of the roof from the door the
    /// attackers keep coming out of.
    /// </summary>
    public class HeliExtraction : MonoBehaviour
    {
        [Header("Refs — 씬 생성기가 채운다")]
        [Tooltip("헬기 모델 루트. 시작할 때 꺼져 있다가 탈출 시작에 켜진다.")]
        public GameObject heli;
        [Tooltip("진입 시작 지점 (멀리, 낮게).")]
        public Transform approachPoint;
        [Tooltip("최종 호버 지점 (앞 난간 너머).")]
        public Transform hoverPoint;
        [Tooltip("회전할 로터들. 동축 반전 로터라 하나씩 방향이 뒤집힌다.")]
        public Transform[] rotors;

        [Header("Flight")]
        [Tooltip("진입에 걸리는 시간 (초). MissionDirector.extractionDuration 보다 짧아야 도착이 보인다.")]
        public float travelTime = 5.5f;
        [Tooltip("직선으로 오지 않도록 경로를 옆으로 부풀리는 양 (m).")]
        public float approachBow = 45f;
        [Tooltip("선회할 때 기울어지는 최대 각도.")]
        public float maxBank = 22f;
        [Tooltip("호버 중 위아래 흔들림 (m).")]
        public float hoverBob = 0.22f;
        public float hoverBobSpeed = 1.5f;

        [Header("Rotors")]
        [Tooltip("로터 회전 속도 (deg/s). 프레임에 안 걸리는 속도라 어차피 뭉개져 보인다.")]
        public float rotorSpeed = 1400f;
        [Tooltip("진입 전에도 로터가 돌고 있는가. 켜두면 멀리서 실루엣이 살아 있다.")]
        public bool spinBeforeArrival = true;

        bool running;
        float clock;
        Vector3 bezierControl;
        Quaternion hoverRotation;

        void Start()
        {
            if (heli != null) heli.SetActive(false);

            var director = MissionDirector.Instance;
            if (director != null) director.ExtractionStarted += Begin;

            // A quadratic control point, offset sideways and up, so the approach reads as a
            // banking curve rather than a prop sliding down a rail.
            if (approachPoint != null && hoverPoint != null)
            {
                Vector3 a = approachPoint.position, b = hoverPoint.position;
                Vector3 flat = b - a;
                flat.y = 0f;
                Vector3 side = flat.sqrMagnitude > 0.01f
                             ? Vector3.Cross(Vector3.up, flat.normalized)
                             : Vector3.right;
                bezierControl = Vector3.Lerp(a, b, 0.55f) + side * approachBow + Vector3.up * (approachBow * 0.35f);
                hoverRotation = hoverPoint.rotation;
            }
        }

        void OnDestroy()
        {
            var director = MissionDirector.Instance;
            if (director != null) director.ExtractionStarted -= Begin;
        }

        void Begin()
        {
            if (running || heli == null || approachPoint == null || hoverPoint == null) return;
            running = true;
            clock = 0f;
            heli.SetActive(true);
            heli.transform.SetPositionAndRotation(approachPoint.position, approachPoint.rotation);
        }

        void Update()
        {
            if (heli == null) return;
            if (!running && !spinBeforeArrival) return;

            SpinRotors();
            if (!running) return;

            clock += Time.deltaTime;
            float t = travelTime > 0.01f ? Mathf.Clamp01(clock / travelTime) : 1f;

            // Ease out: it arrives decelerating into the hover instead of stopping dead.
            float eased = 1f - Mathf.Pow(1f - t, 2.4f);

            Vector3 previous = heli.transform.position;
            Vector3 target = Bezier(approachPoint.position, bezierControl, hoverPoint.position, eased);

            if (t >= 1f)
            {
                // Settled. Hold station with a slow bob so it does not read as a parked prop.
                float bob = Mathf.Sin(Time.time * hoverBobSpeed) * hoverBob;
                heli.transform.position = hoverPoint.position + Vector3.up * bob;
                heli.transform.rotation = Quaternion.Slerp(heli.transform.rotation, hoverRotation,
                                                           1f - Mathf.Exp(-2f * Time.deltaTime));
                return;
            }

            heli.transform.position = target;

            Vector3 velocity = target - previous;
            if (velocity.sqrMagnitude < 0.0001f) return;

            // Bank into the turn: how far the heading changed this frame, signed around up.
            Vector3 heading = velocity.normalized;
            Vector3 flatHeading = new Vector3(heading.x, 0f, heading.z);
            if (flatHeading.sqrMagnitude < 0.0001f) return;
            flatHeading.Normalize();

            float turn = Vector3.SignedAngle(heli.transform.forward, flatHeading, Vector3.up);
            float bank = Mathf.Clamp(turn * 2.5f, -maxBank, maxBank);

            Quaternion want = Quaternion.LookRotation(heading, Vector3.up) * Quaternion.Euler(0f, 0f, -bank);
            heli.transform.rotation = Quaternion.Slerp(heli.transform.rotation, want,
                                                       1f - Mathf.Exp(-4f * Time.deltaTime));
        }

        void SpinRotors()
        {
            if (rotors == null) return;
            for (int i = 0; i < rotors.Length; i++)
            {
                if (rotors[i] == null) continue;
                // Coaxial rotors turn against each other; alternating the sign is free and it is
                // the difference between "a helicopter" and "a model with a spinning disc".
                float dir = (i % 2 == 0) ? 1f : -1f;
                rotors[i].Rotate(Vector3.up, rotorSpeed * dir * Time.deltaTime, Space.Self);
            }
        }

        static Vector3 Bezier(Vector3 a, Vector3 control, Vector3 b, float t)
        {
            float u = 1f - t;
            return u * u * a + 2f * u * t * control + t * t * b;
        }
    }
}
