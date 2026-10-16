using UnityEngine;
using UnityEngine.SceneManagement;

namespace RooftopContract
{
    /// <summary>
    /// Mission 1's skyline buildings ship with coarse box colliders that cover empty parts of
    /// their silhouettes. Replace only the four authored sightline buildings with colliders made
    /// from their visible meshes. Positions, rotations and scales are left untouched.
    /// </summary>
    public static class SightlineColliderFitter
    {
        static readonly string[] MissionOneObjects =
        {
            "Tower_Target", "Tower_CorridorL", "Tower_CorridorR", "Tower_Occluder", "Bld_1_5"
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void FitLoadedMissionOne()
        {
            if (SceneManager.GetActiveScene().name != "Mission1") return;
            int found = 0;
            int installed = 0;
            var scene = SceneManager.GetActiveScene();
            var roots = scene.GetRootGameObjects();
            foreach (string objectName in MissionOneObjects)
            {
                GameObject go = FindInScene(roots, objectName);
                if (go == null) continue;
                found++;
                installed += FitToVisibleMeshes(go, false);
            }
            Debug.Log($"[Mission1 Collider Fit] found={found}/{MissionOneObjects.Length} meshColliders={installed}");
        }

        static GameObject FindInScene(GameObject[] roots, string objectName)
        {
            foreach (var root in roots)
            {
                foreach (var child in root.GetComponentsInChildren<Transform>(true))
                    if (child.name == objectName) return child.gameObject;
            }
            return null;
        }

        /// <summary>Returns the number of visible mesh colliders installed.</summary>
        public static int FitToVisibleMeshes(GameObject root, bool immediate)
        {
            if (root == null) return 0;

            foreach (var box in root.GetComponentsInChildren<BoxCollider>(true))
            {
                // Destroy is deferred in play mode. Disable first so physics stops seeing the
                // oversized prefab box immediately, even before the end of this frame.
                box.enabled = false;
                if (immediate) Object.DestroyImmediate(box);
                else Object.Destroy(box);
            }

            int installed = 0;
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) continue;
                var renderer = filter.GetComponent<Renderer>();
                if (renderer == null || !renderer.enabled) continue;

                var collider = filter.GetComponent<MeshCollider>();
                if (collider == null) collider = filter.gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh = filter.sharedMesh;
                collider.convex = false;
                collider.enabled = true;
                installed++;
            }
            return installed;
        }
    }

    /// <summary>Shared hit handling for both traveling projectiles and instant raycasts.</summary>
    public static class ImpactResolver
    {
        static readonly RaycastHit[] buffer = new RaycastHit[16];

        /// <summary>
        /// Raycast that skips anything belonging to the shooter. Every actor carries trigger
        /// hitboxes and muzzles sit inside them, so a naive raycast shoots itself in the chest.
        /// </summary>
        public static bool Raycast(Vector3 origin, Vector3 direction, float distance,
                                   GameObject shooter, out RaycastHit best)
        {
            best = default;
            int count = Physics.RaycastNonAlloc(origin, direction, buffer, distance,
                                                GameLayers.BulletMask, QueryTriggerInteraction.Collide);
            Transform selfRoot = shooter != null ? shooter.transform.root : null;

            float nearest = float.PositiveInfinity;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                var h = buffer[i];
                if (selfRoot != null && h.collider.transform.root == selfRoot) continue;
                // Hard safety net for Mission 1. Bld_1_5's prefab box covers a large empty part
                // of its silhouette. Its fitted mesh collider may share the same ray, but this
                // coarse box must never win the bullet hit.
                if (SceneManager.GetActiveScene().name == "Mission1" &&
                    h.collider is BoxCollider && HasAncestorNamed(h.collider.transform, "Bld_1_5"))
                    continue;
                if (h.distance >= nearest) continue;
                nearest = h.distance;
                best = h;
                found = true;
            }
            return found;
        }

        static bool HasAncestorNamed(Transform current, string objectName)
        {
            while (current != null)
            {
                if (current.name == objectName) return true;
                current = current.parent;
            }
            return false;
        }

        public static void Resolve(RaycastHit hit, float damage, Vector3 direction, GameObject shooter)
        {
            var hitbox = hit.collider.GetComponent<Hitbox>();
            if (hitbox != null)
            {
                hitbox.Take(damage, hit.point, direction, shooter);
                ImpactFx.Play(hit.point, hit.normal, true, hitbox.isHead);
                if (HitFeedback.IsPlayer(shooter)) HitFeedback.Report(hitbox.isHead);
                return;
            }

            var dmg = hit.collider.GetComponentInParent<Damageable>();
            if (dmg != null)
            {
                dmg.ApplyDamage(new DamageInfo { amount = damage, point = hit.point, direction = direction, source = shooter });
                ImpactFx.Play(hit.point, hit.normal, true);
                if (HitFeedback.IsPlayer(shooter)) HitFeedback.Report(false);
                return;
            }

            ImpactFx.Play(hit.point, hit.normal, false);
            GameAudio.PlayMetalImpact(hit.point);
        }
    }

    /// <summary>Lets the HUD light up a hitmarker without knowing anything about the shooter.</summary>
    public static class HitFeedback
    {
        public static System.Action<bool> OnPlayerHitConfirmed;
        static GameObject playerShooter;
        public static void SetPlayer(GameObject go) => playerShooter = go;
        public static void Report(bool headshot) => OnPlayerHitConfirmed?.Invoke(headshot);
        public static bool IsPlayer(GameObject go) => go != null && go == playerShooter;
    }
}
