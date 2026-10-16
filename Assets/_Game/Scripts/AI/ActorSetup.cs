using System.Collections.Generic;
using UnityEngine;

namespace RooftopContract
{
    /// <summary>
    /// Builds hit volumes for any voxel character, rigged or not.
    ///
    /// Hitboxes are measured from the actual mesh vertices rather than assumed from a rig,
    /// because a MagicaVoxel export may arrive as a bare .obj with no bones at all. The head
    /// and body volumes are boxes, not capsules: voxel characters are boxes, and a box fitted
    /// to the real vertex extents is both more accurate and more honest to the art than a
    /// capsule guessing at proportions.
    ///
    /// The head anchor resolves in this order, most explicit first:
    ///   1. headAnchorOverride assigned in the inspector
    ///   2. a child transform named HITBOX_HEAD  (drop an empty on the head, works with no rig)
    ///   3. a bone whose name matches headBoneHints  (rigged models — follows animation)
    ///   4. the top slice of the mesh silhouette  (fallback, always works)
    /// </summary>
    [RequireComponent(typeof(Damageable))]
    public class ActorSetup : MonoBehaviour
    {
        [Header("Damage")]
        public float headDamageMultiplier = 4f;

        [Header("Head anchor — override > marker > bone > bounds")]
        public Transform headAnchorOverride;
        public string headMarkerName = "HITBOX_HEAD";
        public string[] headBoneHints = { "head", "skull", "neck" };

        [Tooltip("리그가 없을 때 상단 몇 %를 머리로 볼지. 치비 비율이면 0.3 근처, 사실적 비율이면 0.15 근처.")]
        [Range(0.08f, 0.45f)] public float headHeightFraction = 0.24f;

        [Tooltip("측정된 머리 박스에 곱하는 여유. 1보다 크면 헤드샷이 관대해진다.")]
        [Range(0.5f, 1.6f)] public float headPadding = 1.0f;

        [Header("Body")]
        [Tooltip("측정된 몸통 박스에 곱하는 여유. 팔이 벌어진 모델은 1보다 작게 줄여도 된다.")]
        [Range(0.5f, 1.4f)] public float bodyPadding = 1.0f;

        [Tooltip("몸통 박스의 가로 폭 상한 (키 대비 비율). T포즈 바인드 포즈에서 팔 길이만큼 " +
                 "박스가 벌어지는 것을 막는다. 0.5 = 키의 절반까지만 허용.")]
        [Range(0.2f, 1.5f)] public float maxBodyWidthFraction = 0.5f;

        [Header("Weapon attach — override > marker > bone")]
        public Transform handAnchorOverride;
        public string handMarkerName = "ATTACH_WEAPON";
        // Mixamo rigs expose "mixamorig:RightHand"; the sample pack uses "R_Elbow_jnt".
        public string[] handBoneHints = { "righthand", "hand_r", "r_hand", "r_elbow", "rightforearm", "rightarm" };

        [Header("Debug")]
        public bool drawGizmos = true;

        public Transform HeadAnchor { get; private set; }
        public Transform RightHand { get; private set; }
        public float ActorHeight { get; private set; } = 1.8f;
        public Hitbox HeadBox { get; private set; }
        public Hitbox BodyBox { get; private set; }

        /// <summary>Where other actors aim. Centre of the measured body box.</summary>
        public Vector3 CenterMass => transform.TransformPoint(bodyCenterLocal);
        public Vector3 HeadPoint => HeadAnchor != null && headBoxGo != null
            ? headBoxGo.transform.position
            : transform.TransformPoint(headCenterLocal);

        Damageable damageable;
        GameObject headBoxGo, bodyBoxGo;

        // Local-space (relative to this transform) measurements.
        Vector3 headCenterLocal, headSizeLocal;
        Vector3 bodyCenterLocal, bodySizeLocal;
        bool measured;

        void Awake()
        {
            damageable = GetComponent<Damageable>();
            ResolveAnchors();
            Measure();
            BuildHitboxes();
        }

        // ------------------------------------------------------------------ anchors

        /// <summary>Public so the scene generator can resolve the weapon anchor at edit time.</summary>
        public void ResolveAnchors()
        {
            var animator = GetComponentInChildren<Animator>();
            bool humanoid = animator != null && animator.avatar != null && animator.isHuman;

            HeadAnchor = headAnchorOverride
                      ?? FindChildNamed(headMarkerName)
                      ?? (humanoid ? animator.GetBoneTransform(HumanBodyBones.Head) : null)
                      ?? FindBoneByHints(headBoneHints);

            RightHand = handAnchorOverride
                     ?? FindChildNamed(handMarkerName)
                     ?? (humanoid ? animator.GetBoneTransform(HumanBodyBones.RightHand) : null)
                     ?? FindBoneByHints(handBoneHints);
        }

        Transform FindChildNamed(string exactName)
        {
            if (string.IsNullOrEmpty(exactName)) return null;
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (t.name == exactName) return t;
            return null;
        }

        Transform FindBoneByHints(string[] hints)
        {
            if (hints == null || hints.Length == 0) return null;

            // Only consider transforms that look like bones: no renderer of their own, and
            // part of a skinned hierarchy. A mesh object literally named "head" is handled by
            // the marker path above, so this stays conservative.
            var skinned = GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (skinned.Length == 0) return null;

            foreach (var hint in hints)
            {
                string needle = hint.ToLowerInvariant();
                Transform best = null;

                foreach (var smr in skinned)
                {
                    if (smr.bones == null) continue;
                    foreach (var bone in smr.bones)
                    {
                        if (bone == null) continue;
                        string n = bone.name.ToLowerInvariant();
                        if (!n.Contains(needle) || n.Contains("end")) continue;

                        // Shortest match wins. Mixamo rigs contain both "mixamorig:RightHand"
                        // and "mixamorig:RightHandIndex1"; bone order is not guaranteed, so
                        // "first hit" would sometimes bolt the rifle to a finger joint.
                        if (best == null || bone.name.Length < best.name.Length) best = bone;
                    }
                }
                if (best != null) return best;
            }
            return null;
        }

        // ------------------------------------------------------------------ measurement

        /// <summary>
        /// Splits the silhouette into a head region and a body region and fits an exact box to
        /// each. Works off baked vertices so it is identical for static meshes and skinned ones.
        /// </summary>
        public void Measure()
        {
            var verts = CollectLocalVertices();
            if (verts.Count == 0)
            {
                // Nothing to measure — fall back to a plain 1.8m humanoid so the actor is still
                // shootable rather than silently invulnerable.
                ActorHeight = 1.8f;
                headSizeLocal = new Vector3(0.45f, 0.45f, 0.45f);
                headCenterLocal = new Vector3(0f, 1.55f, 0f);
                bodySizeLocal = new Vector3(0.6f, 1.35f, 0.4f);
                bodyCenterLocal = new Vector3(0f, 0.67f, 0f);
                measured = true;
                return;
            }

            Bounds all = new Bounds(verts[0], Vector3.zero);
            for (int i = 1; i < verts.Count; i++) all.Encapsulate(verts[i]);

            ActorHeight = Mathf.Max(0.2f, all.size.y);
            float splitY = all.max.y - ActorHeight * headHeightFraction;

            Bounds head = default, body = default;
            bool hasHead = false, hasBody = false;
            foreach (var v in verts)
            {
                if (v.y >= splitY)
                {
                    if (!hasHead) { head = new Bounds(v, Vector3.zero); hasHead = true; }
                    else head.Encapsulate(v);
                }
                else
                {
                    if (!hasBody) { body = new Bounds(v, Vector3.zero); hasBody = true; }
                    else body.Encapsulate(v);
                }
            }

            if (!hasHead) { head = all; }
            if (!hasBody) { body = all; }

            headCenterLocal = head.center;
            headSizeLocal = Vector3.Max(head.size, Vector3.one * 0.05f) * headPadding;

            bodyCenterLocal = body.center;
            bodySizeLocal = Vector3.Max(body.size, Vector3.one * 0.05f) * bodyPadding;

            // A rigged character sits in its bind pose until the Animator plays a frame, and a
            // Mixamo bind pose is a T-pose. Left unclamped the body box would span the whole arm
            // span — you could kill a guard by shooting a metre to the side of them. Clamp the
            // horizontal extents around the torso axis instead.
            float maxWidth = ActorHeight * maxBodyWidthFraction;
            if (bodySizeLocal.x > maxWidth)
            {
                bodyCenterLocal.x = headCenterLocal.x;
                bodySizeLocal.x = maxWidth;
            }
            if (bodySizeLocal.z > maxWidth)
            {
                bodyCenterLocal.z = headCenterLocal.z;
                bodySizeLocal.z = maxWidth;
            }

            measured = true;
        }

        List<Vector3> CollectLocalVertices()
        {
            var world = CollectWorldVertices(gameObject);
            Matrix4x4 toLocal = transform.worldToLocalMatrix;
            for (int i = 0; i < world.Count; i++) world[i] = toLocal.MultiplyPoint3x4(world[i]);
            return world;
        }

        /// <summary>
        /// Every vertex of every renderer under <paramref name="root"/>, in world space.
        ///
        /// This is deliberately NOT Renderer.bounds. A SkinnedMeshRenderer reports a padded,
        /// conservative volume computed from the bind pose — for this Mixamo guard it was about
        /// 11% taller than the mesh actually is. Measuring the real vertices is what keeps
        /// "1.8m" meaning 1.8m for every character regardless of how its bounds were authored.
        /// </summary>
        public static List<Vector3> CollectWorldVertices(GameObject root)
        {
            var result = new List<Vector3>();
            if (root == null) return result;

            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = mf.sharedMesh;
                if (mesh == null) continue;
                Matrix4x4 m = mf.transform.localToWorldMatrix;
                foreach (var v in mesh.vertices) result.Add(m.MultiplyPoint3x4(v));
            }

            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                AppendSkinned(smr, result);

            return result;
        }

        /// <summary>
        /// BakeMesh's output space is not something to guess at. Whether the hierarchy's scale is
        /// already baked into the vertices depends on how the rig is parented — on this Mixamo
        /// guard it was, so multiplying by the full localToWorldMatrix applied scale twice and a
        /// 1.8m normalisation landed at 2.75m.
        ///
        /// So don't assume: transform the vertices both ways and keep whichever actually lands on
        /// the renderer's own reported bounds. Self-calibrating, and correct for any rig.
        /// </summary>
        static void AppendSkinned(SkinnedMeshRenderer smr, List<Vector3> result)
        {
            if (smr.sharedMesh == null) return;

            var baked = new Mesh();
            smr.BakeMesh(baked);
            var verts = baked.vertices;
            if (verts.Length == 0)
            {
                if (Application.isPlaying) Destroy(baked); else DestroyImmediate(baked);
                return;
            }

            Matrix4x4 withScale = smr.transform.localToWorldMatrix;
            Matrix4x4 withoutScale = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);

            Bounds reference = smr.bounds;   // world space, padded but positioned correctly
            Matrix4x4 chosen = Mismatch(verts, withScale, reference) <= Mismatch(verts, withoutScale, reference)
                ? withScale : withoutScale;

            foreach (var v in verts) result.Add(chosen.MultiplyPoint3x4(v));
            if (Application.isPlaying) Destroy(baked); else DestroyImmediate(baked);
        }

        /// <summary>How far a candidate transform lands from the renderer's own world bounds.</summary>
        static float Mismatch(Vector3[] verts, Matrix4x4 m, Bounds reference)
        {
            Vector3 min = m.MultiplyPoint3x4(verts[0]), max = min;
            for (int i = 1; i < verts.Length; i++)
            {
                Vector3 p = m.MultiplyPoint3x4(verts[i]);
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
            Vector3 size = max - min;
            Vector3 center = (max + min) * 0.5f;
            return (size - reference.size).magnitude + (center - reference.center).magnitude;
        }

        /// <summary>True bounds from baked vertices. Used by the generator to normalise height.</summary>
        public static bool TryMeasureWorldBounds(GameObject root, out Bounds bounds)
        {
            var verts = CollectWorldVertices(root);
            if (verts.Count == 0) { bounds = default; return false; }

            bounds = new Bounds(verts[0], Vector3.zero);
            for (int i = 1; i < verts.Count; i++) bounds.Encapsulate(verts[i]);
            return true;
        }

        /// <summary>
        /// Scales a model so that the height reported by this very measurement equals
        /// targetHeight, then drops its feet onto footY and centres it horizontally.
        ///
        /// Deliberately iterative rather than one division: measuring a skinned mesh is not
        /// guaranteed to be linear in the transform's scale, and a single-shot correction that
        /// silently lands 50% out is worse than three cheap passes that provably converge.
        /// </summary>
        public static float NormalizeToHeight(Transform model, float targetHeight, bool groundAndCentre,
                                              Vector3 anchor, int maxIterations = 6)
        {
            float measured = 0f;

            for (int i = 0; i < maxIterations; i++)
            {
                if (!TryMeasureWorldBounds(model.gameObject, out Bounds b) || b.size.y < 0.0001f) break;
                measured = b.size.y;

                float correction = targetHeight / measured;
                if (Mathf.Abs(correction - 1f) < 0.002f) break;

                model.localScale *= correction;
            }

            if (TryMeasureWorldBounds(model.gameObject, out Bounds final))
            {
                measured = final.size.y;
                if (groundAndCentre)
                    model.position += new Vector3(anchor.x - final.center.x,
                                                  anchor.y - final.min.y,
                                                  anchor.z - final.center.z);
            }

            return measured;
        }

        /// <summary>Measured box extents, for tooling that wants to report them.</summary>
        public Vector3 HeadBoxSize => headSizeLocal;
        public Vector3 BodyBoxSize => bodySizeLocal;

        // ------------------------------------------------------------------ hitboxes

        void BuildHitboxes()
        {
            int layer = GameLayers.Hitbox;

            // Head. Parented to the anchor when one exists so it tracks animation; otherwise it
            // rides the actor root, which is correct for an unrigged static mesh.
            headBoxGo = new GameObject("Hitbox_Head") { layer = layer };
            Transform headParent = HeadAnchor != null ? HeadAnchor : transform;
            headBoxGo.transform.SetParent(headParent, false);
            headBoxGo.transform.localRotation = Quaternion.identity;
            headBoxGo.transform.localScale = Vector3.one;
            headBoxGo.transform.position = transform.TransformPoint(headCenterLocal);

            var headCol = headBoxGo.AddComponent<BoxCollider>();
            headCol.size = DivideScale(headSizeLocal, headParent);
            headCol.isTrigger = true;
            HeadBox = headBoxGo.AddComponent<Hitbox>();
            HeadBox.owner = damageable;
            HeadBox.damageMultiplier = headDamageMultiplier;
            HeadBox.isHead = true;

            // Body.
            bodyBoxGo = new GameObject("Hitbox_Body") { layer = layer };
            bodyBoxGo.transform.SetParent(transform, false);
            bodyBoxGo.transform.localPosition = bodyCenterLocal;
            var bodyCol = bodyBoxGo.AddComponent<BoxCollider>();
            bodyCol.size = bodySizeLocal;
            bodyCol.isTrigger = true;
            BodyBox = bodyBoxGo.AddComponent<Hitbox>();
            BodyBox.owner = damageable;
            BodyBox.damageMultiplier = 1f;
        }

        /// <summary>Box sizes are local; undo the anchor's scale so the volume matches the mesh.</summary>
        Vector3 DivideScale(Vector3 size, Transform parent)
        {
            Vector3 rel = parent == transform
                ? Vector3.one
                : new Vector3(
                    SafeDiv(parent.lossyScale.x, transform.lossyScale.x),
                    SafeDiv(parent.lossyScale.y, transform.lossyScale.y),
                    SafeDiv(parent.lossyScale.z, transform.lossyScale.z));
            return new Vector3(SafeDiv(size.x, rel.x), SafeDiv(size.y, rel.y), SafeDiv(size.z, rel.z));
        }

        static float SafeDiv(float a, float b) => Mathf.Abs(b) < 0.0001f ? a : a / b;

        public void DisableHitboxes()
        {
            if (HeadBox != null) HeadBox.GetComponent<Collider>().enabled = false;
            if (BodyBox != null) BodyBox.GetComponent<Collider>().enabled = false;
        }

        // ------------------------------------------------------------------ gizmos

        void OnDrawGizmosSelected()
        {
            if (!drawGizmos) return;
            if (!measured || !Application.isPlaying)
            {
                ResolveAnchors();
                Measure();
            }

            Gizmos.matrix = transform.localToWorldMatrix;

            Gizmos.color = new Color(1f, 0.3f, 0.25f, 0.9f);
            Gizmos.DrawWireCube(headCenterLocal, headSizeLocal);

            Gizmos.color = new Color(0.35f, 0.75f, 1f, 0.9f);
            Gizmos.DrawWireCube(bodyCenterLocal, bodySizeLocal);

            Gizmos.matrix = Matrix4x4.identity;
        }
    }
}
