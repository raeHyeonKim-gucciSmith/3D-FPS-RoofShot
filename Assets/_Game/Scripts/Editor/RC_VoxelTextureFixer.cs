using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace RooftopContract.EditorTools
{
    /// <summary>
    /// MagicaVoxel exports its colours as a tiny palette strip (typically 256x1) where every
    /// pixel is one exact colour and neighbouring pixels are unrelated. Unity's defaults are
    /// wrong for that in three ways at once:
    ///
    ///   mipmaps  — the smallest mip of a 256x1 strip is a single pixel: the average of the
    ///              whole palette. At 250m the character renders as one flat brown blob, which
    ///              in this game is precisely the distance that matters.
    ///   bilinear — samples between palette entries, so every voxel colour bleeds into the
    ///              colours stored next to it in the strip. Unrelated colours, visibly wrong.
    ///   DXT      — block compression on a 1px-tall strip shifts hues outright.
    ///
    /// This sets Point / no mips / uncompressed / clamp on every palette-sized texture it finds.
    /// </summary>
    public static class RC_VoxelTextureFixer
    {
        [MenuItem("Rooftop Contract/도구/에셋 — 팔레트 텍스처 수정", priority = 41)]
        public static void FixAll()
        {
            var folders = new List<string>();
            // The helicopter pack is palette-textured the same way the voxel characters are
            // ("PaletteTextureDiffuse"), so it walks into the identical mipmap trap: averaged
            // down to one colour at any distance. It is on this list for that reason, not
            // because it is a character.
            foreach (var f in new[] { "Assets/VoxelCharacter", "Assets/_Game/Characters",
                                      "Assets/Low Poly Helicopters Pack Free" })
                if (AssetDatabase.IsValidFolder(f)) folders.Add(f);

            if (folders.Count == 0)
            {
                EditorUtility.DisplayDialog("No character folder",
                    "Assets/VoxelCharacter 또는 Assets/_Game/Characters 를 찾지 못했습니다.", "OK");
                return;
            }

            var guids = AssetDatabase.FindAssets("t:Texture2D", folders.ToArray());
            int fixedCount = 0, skipped = 0;

            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;

                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                // Only touch palette-like textures. A hand-painted 1024x1024 skin should keep
                // its mips and filtering.
                bool paletteLike = tex != null && (tex.height <= 8 || tex.width <= 8 || (tex.width * tex.height) <= 4096);
                if (!paletteLike) { skipped++; continue; }

                if (Apply(importer)) { fixedCount++; importer.SaveAndReimport(); }
            }

            // Quiet when there was nothing to do. This runs on every build now, and a log line
            // saying "fixed 0" every time trains you to stop reading the console.
            if (fixedCount > 0)
                Debug.Log($"[Rooftop Contract] 팔레트 텍스처 {fixedCount}개 수정, {skipped}개는 일반 텍스처로 판단해 건너뜀. " +
                          "(Point / mipmap off / 압축 없음 / Clamp)");
        }

        static bool Apply(TextureImporter importer)
        {
            bool changed = false;

            if (importer.mipmapEnabled) { importer.mipmapEnabled = false; changed = true; }
            if (importer.filterMode != FilterMode.Point) { importer.filterMode = FilterMode.Point; changed = true; }
            if (importer.anisoLevel != 0) { importer.anisoLevel = 0; changed = true; }
            if (importer.wrapMode != TextureWrapMode.Clamp) { importer.wrapMode = TextureWrapMode.Clamp; changed = true; }
            if (importer.npotScale != TextureImporterNPOTScale.None) { importer.npotScale = TextureImporterNPOTScale.None; changed = true; }
            if (importer.textureCompression != TextureImporterCompression.Uncompressed)
            {
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                changed = true;
            }

            // Platform overrides win over the default settings, so clear compression there too.
            foreach (var platform in new[] { "Standalone", "Android", "iPhone", "WebGL" })
            {
                var settings = importer.GetPlatformTextureSettings(platform);
                if (!settings.overridden) continue;
                if (settings.textureCompression != TextureImporterCompression.Uncompressed)
                {
                    settings.textureCompression = TextureImporterCompression.Uncompressed;
                    importer.SetPlatformTextureSettings(settings);
                    changed = true;
                }
            }

            return changed;
        }
    }
}
