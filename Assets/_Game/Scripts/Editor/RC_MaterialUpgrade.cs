using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace RooftopContract.EditorTools
{
    /// <summary>
    /// The building, weapon and city-bit packs all ship with built-in-pipeline Standard materials,
    /// which render magenta under URP. This converts them in place, preserving colour, texture,
    /// smoothness and metallic. Only the voxel character pack is already URP and is left alone.
    /// </summary>
    public static class RC_MaterialUpgrade
    {
        static readonly string[] TargetFolders =
        {
            "Assets/Polyeler",
            "Assets/KayKit_City_Builder_Bits_1.0_FREE",
            "Assets/Low Poly Pistol Weapon Pack 2",
            "Assets/Low Poly Weapon Pack 4_MW_1",
            // The helicopter pack ships a Built-in-shader material and a URP .unitypackage it
            // expects you to import by hand. Left alone, the exfil bird renders magenta.
            "Assets/Low Poly Helicopters Pack Free",
        };

        [MenuItem("Rooftop Contract/도구/에셋 — URP 머티리얼 변환", priority = 40)]
        public static void Run()
        {
            Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
            if (urpLit == null)
            {
                EditorUtility.DisplayDialog("URP not found",
                    "Universal Render Pipeline/Lit shader is missing. Is the project on URP?", "OK");
                return;
            }

            var guids = new List<string>();
            foreach (var folder in TargetFolders)
                if (AssetDatabase.IsValidFolder(folder))
                    guids.AddRange(AssetDatabase.FindAssets("t:Material", new[] { folder }));

            int converted = 0;
            try
            {
                AssetDatabase.StartAssetEditing();
                for (int i = 0; i < guids.Count; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (mat == null || mat.shader == null) continue;

                    string shaderName = mat.shader.name;
                    bool isBuiltIn = shaderName == "Standard"
                                  || shaderName == "Standard (Specular setup)"
                                  || shaderName.StartsWith("Legacy Shaders/")
                                  || shaderName == "Mobile/Diffuse";
                    if (!isBuiltIn) continue;

                    EditorUtility.DisplayProgressBar("Upgrading materials", path, i / (float)guids.Count);
                    Convert(mat, urpLit);
                    converted++;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                EditorUtility.ClearProgressBar();
            }

            // Only pay for the save and the reimport if something actually changed. This runs on
            // every build now (it is folded into BUILD EVERYTHING), and the steady state is
            // "nothing to convert" — which should cost a folder scan, not an AssetDatabase
            // refresh.
            if (converted > 0)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log($"[Rooftop Contract] Upgraded {converted} material(s) of {guids.Count} scanned to URP/Lit.");
            }
        }

        static void Convert(Material mat, Shader urpLit)
        {
            Color color = mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.white;
            Texture main = mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex") : null;
            Vector2 scale = mat.HasProperty("_MainTex") ? mat.GetTextureScale("_MainTex") : Vector2.one;
            Vector2 offset = mat.HasProperty("_MainTex") ? mat.GetTextureOffset("_MainTex") : Vector2.zero;
            float metallic = mat.HasProperty("_Metallic") ? mat.GetFloat("_Metallic") : 0f;
            float gloss = mat.HasProperty("_Glossiness") ? mat.GetFloat("_Glossiness") : 0.3f;
            Texture bump = mat.HasProperty("_BumpMap") ? mat.GetTexture("_BumpMap") : null;
            Color emission = mat.HasProperty("_EmissionColor") ? mat.GetColor("_EmissionColor") : Color.black;

            mat.shader = urpLit;

            mat.SetColor("_BaseColor", color);
            if (main != null)
            {
                mat.SetTexture("_BaseMap", main);
                mat.SetTextureScale("_BaseMap", scale);
                mat.SetTextureOffset("_BaseMap", offset);
            }
            mat.SetFloat("_Metallic", metallic);
            mat.SetFloat("_Smoothness", gloss);
            if (bump != null)
            {
                mat.SetTexture("_BumpMap", bump);
                mat.EnableKeyword("_NORMALMAP");
            }
            if (emission.maxColorComponent > 0.001f)
            {
                mat.SetColor("_EmissionColor", emission);
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            EditorUtility.SetDirty(mat);
        }
    }
}
