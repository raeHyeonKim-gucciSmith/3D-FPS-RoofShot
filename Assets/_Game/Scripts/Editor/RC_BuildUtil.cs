using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace RooftopContract.EditorTools
{
    /// <summary>Shared editor helpers for constructing the level from prefabs and primitives.</summary>
    public static class RC_BuildUtil
    {
        // ---- asset loading -------------------------------------------------

        public static GameObject LoadPrefab(string path)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go == null) Debug.LogWarning($"[Rooftop Contract] Missing asset: {path}");
            return go;
        }

        public static List<GameObject> LoadPrefabsInFolder(string folder, string prefix = null)
        {
            var list = new List<GameObject>();
            if (!AssetDatabase.IsValidFolder(folder)) return list;
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (prefix != null && !System.IO.Path.GetFileName(path).StartsWith(prefix)) continue;
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go != null) list.Add(go);
            }
            list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return list;
        }

        // ---- bounds --------------------------------------------------------

        static readonly Dictionary<GameObject, Bounds> boundsCache = new Dictionary<GameObject, Bounds>();

        /// <summary>
        /// World-space bounds of a prefab at identity transform. The building pack has no
        /// documented dimensions, so everything is measured rather than assumed.
        /// </summary>
        public static Bounds MeasurePrefab(GameObject prefab)
        {
            if (prefab == null) return new Bounds(Vector3.zero, Vector3.one);
            if (boundsCache.TryGetValue(prefab, out var cached)) return cached;

            var temp = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            temp.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            temp.transform.localScale = Vector3.one;

            var renderers = temp.GetComponentsInChildren<Renderer>();
            Bounds b;
            if (renderers.Length == 0) b = new Bounds(Vector3.zero, Vector3.one);
            else
            {
                b = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            }
            Object.DestroyImmediate(temp);

            boundsCache[prefab] = b;
            return b;
        }

        public static void ClearBoundsCache() => boundsCache.Clear();

        // ---- primitives ----------------------------------------------------

        public static GameObject Box(Transform parent, string name, Vector3 center, Vector3 size,
                                     Material mat, float yaw = 0f, bool collider = true)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = center;
            go.transform.localScale = size;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            var r = go.GetComponent<MeshRenderer>();
            if (mat != null) r.sharedMaterial = mat;
            return go;
        }

        public static GameObject Cylinder(Transform parent, string name, Vector3 center, float radius,
                                          float height, Material mat, bool collider = true)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = center;
            go.transform.localScale = new Vector3(radius * 2f, height * 0.5f, radius * 2f);
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            var r = go.GetComponent<MeshRenderer>();
            if (mat != null) r.sharedMaterial = mat;
            return go;
        }

        public static GameObject Empty(Transform parent, string name, Vector3 pos = default, Quaternion rot = default)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, rot == default ? Quaternion.identity : rot);
            return go;
        }

        /// <summary>Instantiates a prefab, scaled so its rendered height matches targetHeight, foot on groundY.</summary>
        public static GameObject PlaceScaledToHeight(GameObject prefab, Transform parent, Vector3 groundPos,
                                                     float targetHeight, float yaw)
        {
            if (prefab == null) return null;
            Bounds b = MeasurePrefab(prefab);
            float srcHeight = Mathf.Max(0.01f, b.size.y);
            float scale = targetHeight / srcHeight;

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.transform.localScale = Vector3.one * scale;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            // Lift so the measured bottom sits exactly on groundPos.y.
            float bottomOffset = (b.min.y) * scale;
            go.transform.position = new Vector3(groundPos.x, groundPos.y - bottomOffset, groundPos.z);
            return go;
        }

        public static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform t in go.transform) SetLayerRecursive(t.gameObject, layer);
        }

        public static void MarkStaticRecursive(GameObject go)
        {
            GameObjectUtility.SetStaticEditorFlags(go,
                StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic |
                StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
            foreach (Transform t in go.transform) MarkStaticRecursive(t.gameObject);
        }

        // ---- layers --------------------------------------------------------

        public static void EnsureLayer(string name)
        {
            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tagManager.FindProperty("layers");

            for (int i = 0; i < layers.arraySize; i++)
                if (layers.GetArrayElementAtIndex(i).stringValue == name) return;

            for (int i = 8; i < layers.arraySize; i++)
            {
                var el = layers.GetArrayElementAtIndex(i);
                if (string.IsNullOrEmpty(el.stringValue))
                {
                    el.stringValue = name;
                    tagManager.ApplyModifiedProperties();
                    return;
                }
            }
            Debug.LogWarning($"[Rooftop Contract] No free layer slot for '{name}'.");
        }
    }
}
