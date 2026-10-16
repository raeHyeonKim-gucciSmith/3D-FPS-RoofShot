using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace RooftopContract.EditorTools
{
    /// <summary>
    /// Forces every character/animation FBX to import as Humanoid.
    ///
    /// Unity defaults a rigged FBX to Generic, and a Generic clip does not retarget: it will not
    /// play on another character, and it misbehaves even on its own Humanoid avatar. Since the
    /// whole animation plan here is "one clip, six characters", a single file left on Generic
    /// silently breaks that clip everywhere.
    /// </summary>
    public static class RC_RigFixer
    {
        const string CharRoot = "Assets/VoxelCharacter";

        [MenuItem("Rooftop Contract/도구/캐릭터 — 리그 Humanoid 변환", priority = 21)]
        public static void FixAll()
        {
            if (!AssetDatabase.IsValidFolder(CharRoot))
            {
                EditorUtility.DisplayDialog("No character folder", $"{CharRoot} 를 찾지 못했습니다.", "OK");
                return;
            }

            var fixedFiles = new List<string>();
            var alreadyOk = new List<string>();
            var failed = new List<string>();

            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { CharRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null) continue;

                string file = System.IO.Path.GetFileName(path);
                bool changed = false;

                if (importer.animationType != ModelImporterAnimationType.Human)
                {
                    importer.animationType = ModelImporterAnimationType.Human;
                    importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                    changed = true;
                }

                // Exposed bone transforms are what the head hitbox and the weapon attach point
                // resolve against. Optimising them away makes both silently fall back.
                if (importer.optimizeGameObjects)
                {
                    importer.optimizeGameObjects = false;
                    changed = true;
                }

                // Loop settings by intent. Locomotion has to loop or the guard takes one step and
                // freezes; a death clip must NOT loop or the body dies over and over forever.
                string lower = System.IO.Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                bool isDeath = lower.Contains("death");
                bool isLoco = lower.Contains("idle") || lower.Contains("walk") || lower.Contains("run");

                if (isDeath || isLoco)
                {
                    var clips = importer.clipAnimations;
                    if (clips == null || clips.Length == 0) clips = importer.defaultClipAnimations;

                    bool wantLoop = isLoco && !isDeath;
                    bool loopChanged = false;
                    for (int i = 0; i < clips.Length; i++)
                    {
                        if (clips[i].loopTime == wantLoop) continue;
                        clips[i].loopTime = wantLoop;
                        loopChanged = true;
                    }
                    if (loopChanged) { importer.clipAnimations = clips; changed = true; }
                }

                if (!changed) { alreadyOk.Add(file); continue; }

                importer.SaveAndReimport();

                // Verify rather than assume: Mixamo auto-rigs map cleanly, but a model whose
                // proportions confuse the avatar mapper will silently come back without one.
                var reloaded = AssetImporter.GetAtPath(path) as ModelImporter;
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var animator = go != null ? go.GetComponent<Animator>() : null;

                if (reloaded != null && reloaded.animationType == ModelImporterAnimationType.Human
                    && animator != null && animator.avatar != null && animator.avatar.isValid)
                    fixedFiles.Add(file);
                else
                    failed.Add(file);
            }

            var report = new List<string>();
            if (fixedFiles.Count > 0) report.Add($"  Humanoid 로 변경: {string.Join(", ", fixedFiles)}");
            if (alreadyOk.Count > 0) report.Add($"  이미 정상: {alreadyOk.Count}개");
            if (failed.Count > 0) report.Add($"  ⚠ 아바타 생성 실패: {string.Join(", ", failed)}");

            if (failed.Count > 0)
            {
                Debug.LogError("[Rooftop Contract] 리그 정리\n" + string.Join("\n", report) +
                               "\n실패한 파일은 Inspector ▸ Rig ▸ Configure 에서 본 매핑을 확인해야 합니다.");
            }
            else
            {
                Debug.Log("[Rooftop Contract] 리그 정리\n" + string.Join("\n", report));
            }
        }
    }
}
