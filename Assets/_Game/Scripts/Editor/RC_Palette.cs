using UnityEditor;
using UnityEngine;

namespace RooftopContract.EditorTools
{
    /// <summary>
    /// Shared URP materials for the constructed geometry (rooftops, parapets, props).
    /// Created once as real assets so the built scene is not full of runtime-only materials.
    /// </summary>
    public static class RC_Palette
    {
        public const string Folder = "Assets/_Game/Materials";

        public static Material Concrete => Get("RC_Concrete", new Color(0.42f, 0.43f, 0.45f), 0.85f);
        public static Material ConcreteDark => Get("RC_ConcreteDark", new Color(0.24f, 0.25f, 0.27f), 0.9f);
        public static Material Asphalt => Get("RC_Asphalt", new Color(0.13f, 0.13f, 0.15f), 0.95f);
        public static Material Metal => Get("RC_Metal", new Color(0.55f, 0.57f, 0.60f), 0.35f, 0.8f);
        public static Material RustMetal => Get("RC_RustMetal", new Color(0.45f, 0.32f, 0.24f), 0.7f, 0.3f);
        public static Material Helipad => Get("RC_Helipad", new Color(0.86f, 0.72f, 0.18f), 0.9f);
        public static Material RoofTar => Get("RC_RoofTar", new Color(0.20f, 0.21f, 0.23f), 0.95f);
        public static Material Glass => Get("RC_Glass", new Color(0.32f, 0.48f, 0.60f), 0.15f, 0.6f);

        /// <summary>Condenser coil faces — the blue-green grilles that read as HVAC at a glance.</summary>
        public static Material Coil => Get("RC_Coil", new Color(0.24f, 0.40f, 0.47f), 0.30f, 0.5f);
        /// <summary>Painted plant casing. Lighter than concrete so the machinery separates from the deck.</summary>
        public static Material Plant => Get("RC_Plant", new Color(0.76f, 0.78f, 0.80f), 0.42f, 0.2f);
        /// <summary>A doorway with the lights off behind it.</summary>
        public static Material DoorDark => Get("RC_DoorDark", new Color(0.05f, 0.05f, 0.06f), 0.1f);
        /// <summary>Street tree canopies. Dark enough to read as a silhouette against the pavement.</summary>
        public static Material Foliage => Get("RC_Foliage", new Color(0.20f, 0.31f, 0.19f), 0.25f);
        /// <summary>Office interior: warm wood for desks and shelving in the briefing room.</summary>
        public static Material Wood => Get("RC_Wood", new Color(0.35f, 0.24f, 0.16f), 0.35f);
        /// <summary>Office interior: the pinboard the dossier is stapled to.</summary>
        public static Material Cork => Get("RC_Cork", new Color(0.52f, 0.38f, 0.22f), 0.15f);
        /// <summary>Office interior: painted wall, a shade warmer than the exterior concrete.</summary>
        public static Material Wall => Get("RC_Wall", new Color(0.38f, 0.36f, 0.33f), 0.2f);

        /// <summary>
        /// The KayKit props ship with materials embedded in their FBX files, which import as
        /// built-in Standard and cannot be rewritten in place. The whole kit shares one atlas,
        /// so a single URP material with that atlas replaces all of them at build time.
        /// </summary>
        public static Material CityBits
        {
            get
            {
                EnsureFolder();
                const string path = Folder + "/RC_CityBits.mat";
                var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (existing != null) return existing;

                var m = new Material(Lit) { name = "RC_CityBits" };
                Apply(m, Color.white, 0.25f, 0f);

                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(
                    "Assets/KayKit_City_Builder_Bits_1.0_FREE/KayKit_City_Builder_Bits_1.0_FREE/Assets/fbx (unity)/citybits_texture.png");
                if (tex != null && m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
                else Debug.LogWarning("[Rooftop Contract] citybits_texture.png not found — props will be untextured.");

                AssetDatabase.CreateAsset(m, path);
                return m;
            }
        }

        static Shader lit;
        static Shader Lit
        {
            get
            {
                if (lit == null)
                    lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                return lit;
            }
        }

        public static Material Get(string name, Color color, float smoothness, float metallic = 0f)
        {
            EnsureFolder();
            string path = $"{Folder}/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            var m = new Material(Lit) { name = name };
            Apply(m, color, smoothness, metallic);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static void Apply(Material m, Color color, float smoothness, float metallic)
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Color")) m.SetColor("_Color", color);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
        }

        static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/_Game")) AssetDatabase.CreateFolder("Assets", "_Game");
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/_Game", "Materials");
        }
    }
}
