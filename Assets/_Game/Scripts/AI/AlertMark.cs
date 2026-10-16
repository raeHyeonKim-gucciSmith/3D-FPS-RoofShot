using UnityEngine;

namespace RooftopContract
{
    /// <summary>
    /// The "!" that pops over a guard's head the moment the player takes the first shot.
    /// Built from two unlit quads rather than a font so it stays crisp at 250m through an 8x scope,
    /// and it scales with distance to hold a constant screen size.
    /// </summary>
    public class AlertMark : MonoBehaviour
    {
        public Color color = new Color(1f, 0.86f, 0.15f);
        [Tooltip("화면 높이 대비 마커 크기. 0.05 = 화면의 5%.")]
        public float screenSize = 0.05f;
        public float popDuration = 0.28f;
        public float bobAmplitude = 0.08f;

        Transform bar, dot;
        Camera cam;
        float age;
        Vector3 baseLocalPos;

        public static AlertMark Attach(Transform anchor, float heightOffset)
        {
            var go = new GameObject("AlertMark");
            go.transform.SetParent(anchor, false);
            go.transform.localPosition = Vector3.up * heightOffset;
            return go.AddComponent<AlertMark>();
        }

        void Awake()
        {
            baseLocalPos = transform.localPosition;
            var mat = MaterialLibrary.Unlit(color);

            bar = MakeQuad("Bar", mat);
            bar.localPosition = new Vector3(0f, 0.26f, 0f);
            bar.localScale = new Vector3(0.22f, 0.62f, 1f);

            dot = MakeQuad("Dot", mat);
            dot.localPosition = new Vector3(0f, -0.20f, 0f);
            dot.localScale = new Vector3(0.22f, 0.22f, 1f);
        }

        Transform MakeQuad(string name, Material mat)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = name;
            // Destroy() is deferred to end of frame, and the marker pops exactly when rounds are
            // in the air — so put it on Ignore Raycast too rather than trusting the timing.
            q.layer = 2;
            Destroy(q.GetComponent<Collider>());
            var r = q.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            q.transform.SetParent(transform, false);
            return q.transform;
        }

        void OnEnable() { age = 0f; }

        void LateUpdate()
        {
            if (cam == null) cam = Camera.main;
            if (cam == null) return;

            age += Time.deltaTime;

            // Billboard.
            transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position, Vector3.up);

            // Constant screen size. The world height that fills the viewport at distance d is
            // 2 * d * tan(fov/2), so a marker covering `screenSize` of the screen is exactly
            // that times the fraction — no fudge factor. The old code multiplied by 6 instead
            // of 2, which is why the mark rendered three times the intended size.
            float dist = Vector3.Distance(cam.transform.position, transform.position);
            float fovScale = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float pop = Mathf.Clamp01(age / Mathf.Max(0.01f, popDuration));
            float overshoot = 1f + 0.55f * Mathf.Sin(pop * Mathf.PI) * (1f - pop);
            float s = screenSize * 2f * dist * fovScale * overshoot;

            Vector3 lossy = transform.parent != null ? transform.parent.lossyScale : Vector3.one;
            float inv = 1f / Mathf.Max(0.0001f, lossy.x);
            transform.localScale = Vector3.one * s * inv;

            transform.localPosition = baseLocalPos + Vector3.up * (Mathf.Sin(Time.time * 3.1f) * bobAmplitude);
        }
    }
}
