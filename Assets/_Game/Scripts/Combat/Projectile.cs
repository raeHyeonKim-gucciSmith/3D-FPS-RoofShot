using UnityEngine;

namespace RooftopContract
{
    /// <summary>
    /// A bullet that actually travels. The delay between the muzzle flash and the impact
    /// is what makes the "fire -> recoil -> settle -> watch it land" beat work at 250m.
    /// </summary>
    public class Projectile : MonoBehaviour
    {
        public static Projectile Spawn(Vector3 origin, Vector3 direction, float speed, float gravity,
                                       float damage, GameObject shooter, Material tracerMaterial,
                                       float tracerLength = 6f, Vector3? visualOrigin = null,
                                       bool reportSniperImpact = false)
        {
            var go = new GameObject("Projectile");
            go.transform.position = origin;
            var p = go.AddComponent<Projectile>();
            p.velocity = direction.normalized * speed;
            p.gravity = gravity;
            p.damage = damage;
            p.shooter = shooter;
            p.shotOrigin = origin;
            p.visualOrigin = visualOrigin ?? origin;
            p.reportSniperImpact = reportSniperImpact;
            p.BuildTracer(tracerMaterial, tracerLength);
            return p;
        }

        Vector3 velocity;
        float gravity;
        float damage;
        GameObject shooter;
        Vector3 shotOrigin;
        Vector3 visualOrigin;
        bool reportSniperImpact;
        float distanceTravelled;
        float life = 8f;
        LineRenderer tracer;
        float tracerLength;
        Camera viewCamera;

        // A world-space width is invisible at range: 0.09m at 250m through an 8x scope is three
        // pixels, and half that at 16x — the round the player is meant to watch was thinner than
        // the reticle. Width is therefore driven to a constant *apparent* size instead, so the
        // tracer reads the same whatever the zoom or the distance.
        const float TracerPixels = 6f;
        const float MinTracerWidth = 0.05f;

        void BuildTracer(Material mat, float length)
        {
            if (mat == null) return;
            tracerLength = length;
            tracer = gameObject.AddComponent<LineRenderer>();
            tracer.material = mat;
            tracer.widthMultiplier = MinTracerWidth;
            tracer.positionCount = 2;
            tracer.useWorldSpace = true;
            tracer.numCapVertices = 2;
            tracer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        void FixedUpdate()
        {
            life -= Time.fixedDeltaTime;
            if (life <= 0f) { Destroy(gameObject); return; }

            velocity += Vector3.down * gravity * Time.fixedDeltaTime;
            Vector3 step = velocity * Time.fixedDeltaTime;
            Vector3 from = transform.position;

            if (ImpactResolver.Raycast(from, step.normalized, step.magnitude + 0.05f, shooter, out RaycastHit hit))
            {
                if (reportSniperImpact)
                {
                    MissionDirector.Instance?.ReportSniperImpact(shotOrigin);
                    if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "Mission1")
                    {
                        Debug.Log($"[Mission1 Bullet Impact] collider='{HierarchyPath(hit.collider.transform)}' " +
                                  $"type={hit.collider.GetType().Name} layer={LayerMask.LayerToName(hit.collider.gameObject.layer)} " +
                                  $"distance={Vector3.Distance(shotOrigin, hit.point):0.00}m point={hit.point}");
                    }
                }
                ImpactResolver.Resolve(hit, damage, step.normalized, shooter);
                Destroy(gameObject);
                return;
            }

            transform.position = from + step;
            distanceTravelled += step.magnitude;
            if (tracer != null)
            {
                Vector3 back = distanceTravelled < tracerLength
                    ? visualOrigin
                    : transform.position - velocity.normalized * tracerLength;
                tracer.SetPosition(0, back);
                tracer.SetPosition(1, transform.position);
            }
        }

        void LateUpdate()
        {
            if (tracer == null) return;
            if (viewCamera == null) viewCamera = Camera.main;
            if (viewCamera == null) return;

            float distance = Vector3.Distance(viewCamera.transform.position, transform.position);
            float radiansPerPixel = viewCamera.fieldOfView * Mathf.Deg2Rad / Mathf.Max(1, Screen.height);
            tracer.widthMultiplier = Mathf.Max(MinTracerWidth, distance * radiansPerPixel * TracerPixels);
        }

        static string HierarchyPath(Transform current)
        {
            if (current == null) return "<missing>";
            string path = current.name;
            while (current.parent != null)
            {
                current = current.parent;
                path = current.name + "/" + path;
            }
            return path;
        }
    }

}
