using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RooftopContract.EditorTools
{
    /// <summary>
    /// Fills CastingConfig from the character folder by name.
    ///
    /// The folder name decides the role (Vip*, Guard*, Assault*, Player), and within a folder the
    /// FBX that actually carries a mesh becomes the character — a Mixamo "With Skin" download has
    /// one, an animation-only "Without Skin" download does not. That distinction is the whole
    /// reason this can be automatic: it never mistakes a Walk clip for a character.
    /// </summary>
    public static class RC_AutoCast
    {
        const string CharRoot = "Assets/VoxelCharacter";

        [MenuItem("Rooftop Contract/도구/캐릭터 — 배역 자동 배정", priority = 22)]
        public static void AutoAssign()
        {
            if (!AssetDatabase.IsValidFolder(CharRoot))
            {
                EditorUtility.DisplayDialog("No character folder", $"{CharRoot} 를 찾지 못했습니다.", "OK");
                return;
            }

            var casting = RC_SceneBuilder.LoadOrCreateCasting();

            var vips = new List<GameObject>();
            var guards = new List<GameObject>();
            var assault = new List<GameObject>();
            var civilians = new List<GameObject>();
            var report = new List<string>();

            foreach (var folder in AssetDatabase.GetSubFolders(CharRoot).OrderBy(f => f))
            {
                string leaf = System.IO.Path.GetFileName(folder);
                var character = PickCharacterFbx(folder);
                if (character == null)
                {
                    report.append_skip(leaf, "메시를 가진 FBX 없음 (Without Skin만 있는 폴더?)");
                    continue;
                }

                string key = leaf.ToLowerInvariant();
                if (key.StartsWith("player"))
                {
                    report.append_skip(leaf, "1인칭이라 플레이어 모델은 사용하지 않음");
                    continue;
                }

                if (key.StartsWith("vip") || key.StartsWith("target")) { vips.Add(character); report.append_ok(leaf, "VIP", character.name); }
                else if (key.StartsWith("guard") || key.StartsWith("bodyguard")) { guards.Add(character); report.append_ok(leaf, "경호원", character.name); }
                else if (key.StartsWith("assault")) { assault.Add(character); report.append_ok(leaf, "돌격대", character.name); }
                else { civilians.Add(character); report.append_ok(leaf, "민간인", character.name); }
            }

            Undo.RecordObject(casting, "Auto-assign cast");
            if (vips.Count > 0) casting.vipPrefabs = vips.ToArray();
            if (guards.Count > 0) casting.protectorPrefabs = guards.ToArray();
            if (assault.Count > 0) casting.assaultPrefabs = assault.ToArray();
            casting.civilianPrefabs = civilians.ToArray();

            if (casting.walkClip == null) casting.walkClip = FindClip("walk");

            EditorUtility.SetDirty(casting);
            AssetDatabase.SaveAssets();

            Selection.activeObject = casting;
            EditorGUIUtility.PingObject(casting);

            string walk = casting.walkClip != null ? casting.walkClip.name : "없음 — 액터가 미끄러져 이동합니다";
            Debug.Log("[Rooftop Contract] 캐스팅 자동 배정\n" + string.Join("\n", report) +
                      $"\n  이동 클립  : {walk}" +
                      "\n결과가 틀렸으면 CastingConfig 에서 직접 고치면 됩니다.");
        }

        /// <summary>
        /// The character file is the one with a skinned mesh. Among several (Mixamo gives you a
        /// mesh on every "With Skin" download), prefer an idle or T-pose: the bind pose is what
        /// the hitboxes get measured against, so a neutral stance measures cleanest.
        /// </summary>
        static GameObject PickCharacterFbx(string folder)
        {
            var candidates = new List<GameObject>();
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null) continue;
                if (go.GetComponentInChildren<SkinnedMeshRenderer>(true) == null &&
                    go.GetComponentInChildren<MeshRenderer>(true) == null) continue;
                candidates.Add(go);
            }
            if (candidates.Count == 0) return null;

            return candidates
                .OrderBy(c => Rank(c.name))
                .ThenBy(c => c.name.Length)
                .First();
        }

        static int Rank(string name)
        {
            string n = name.ToLowerInvariant();
            if (n.Contains("tpose") || n.Contains("t-pose")) return 0;
            if (n.Contains("idle")) return 1;
            if (n.Contains("walk")) return 2;
            // Death and reaction poses measure badly — the character is on the floor.
            if (n.Contains("death") || n.Contains("terrified") || n.Contains("hit")) return 9;
            return 5;
        }

        static AnimationClip FindClip(string needle)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { CharRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (obj is not AnimationClip clip) continue;
                    if (clip.name.StartsWith("__preview__")) continue;
                    if (clip.name.ToLowerInvariant().Contains(needle)) return clip;
                }
            }
            return null;
        }
    }

    static class ReportExtensions
    {
        public static void append_ok(this List<string> list, string folder, string role, string file)
            => list.Add($"  {folder,-12} → {role,-6} : {file}");

        public static void append_skip(this List<string> list, string folder, string why)
            => list.Add($"  {folder,-12} → 건너뜀 : {why}");
    }
}
