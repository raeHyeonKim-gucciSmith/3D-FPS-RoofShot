using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RooftopContract.EditorTools
{
    /// <summary>
    /// Re-serializes the existing mission scenes against the current assembly, instead of
    /// regenerating them.
    ///
    /// **Why this exists.** The scenes on disk were written by an older compiled assembly, so
    /// fields that the AI scripts have since gained were never serialized into them:
    /// ProtectionDetail's four escort fields, ProtectorGuard's two, AssaultDirector.hiddenRushDelay
    /// and VipTarget.inPlayWaypoint. A missing field loads as its C# default, which is why the
    /// escort formation is silently off (escortWhileCalm defaults to false).
    ///
    /// Every one of those defaults is already the value the generator would write — the generator
    /// only ever assigns escortWhileCalm, and it takes it straight from the target's route flag.
    /// So the whole repair is: open each scene, set that one flag from data already in the scene,
    /// and save. Saving is what writes the other fields in.
    ///
    /// This touches nothing else. It does not run the map builder, does not load or write
    /// MissionSet.asset or CastingConfig.asset, and does not create or delete a single
    /// GameObject — so hand-tuned values that live anywhere in these scenes survive it.
    /// </summary>
    public static class RC_ApplyMechanics
    {
        [MenuItem("Rooftop Contract/도구/씬 — 메커니즘 필드만 채우기 (재생성 없음)", priority = 4)]
        public static void Apply()
        {
            var scenePaths = new List<string>();
            for (int i = 0; i < 32; i++)
            {
                var path = MissionSet.ScenePathFor(i);
                if (!File.Exists(path)) break;
                scenePaths.Add(path);
            }

            if (scenePaths.Count == 0)
            {
                EditorUtility.DisplayDialog("미션 씬 없음",
                    "Assets/_Game/Scenes/Mission1.unity 를 찾지 못했습니다.", "OK");
                return;
            }

            if (!EditorUtility.DisplayDialog("메커니즘만 적용",
                $"미션 씬 {scenePaths.Count}개를 열어서 다시 저장합니다.\n\n" +
                "• 경호원 호위 대형 (ProtectionDetail / ProtectorGuard)\n" +
                "• 돌격대 압박 타이밍 (AssaultDirector.hiddenRushDelay)\n" +
                "• 표적 접근 판정 (VipTarget.inPlayWaypoint)\n\n" +
                "씬을 재생성하지 않습니다. 오브젝트를 만들거나 지우지 않습니다.\n" +
                "MissionSet / CastingConfig 는 읽지도 않습니다.\n\n" +
                "바뀌는 값은 ProtectionDetail.escortWhileCalm 하나이고,\n" +
                "그 값은 씬 안의 VipTarget.oneWayRoute 에서 그대로 가져옵니다.",
                "적용", "취소")) return;

            // Anything unsaved in the editor right now would be lost by OpenScene. Let the user
            // decide before a single scene is touched.
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            string returnTo = EditorSceneManager.GetActiveScene().path;
            var report = new StringBuilder();
            int changed = 0;

            try
            {
                for (int i = 0; i < scenePaths.Count; i++)
                {
                    EditorUtility.DisplayProgressBar("메커니즘 적용",
                        Path.GetFileName(scenePaths[i]), (float)i / scenePaths.Count);

                    var scene = EditorSceneManager.OpenScene(scenePaths[i], OpenSceneMode.Single);
                    report.AppendLine($"── {Path.GetFileNameWithoutExtension(scenePaths[i])}");

                    var details = Object.FindObjectsByType<ProtectionDetail>(
                        FindObjectsInactive.Include, FindObjectsSortMode.None);

                    if (details.Length == 0)
                        report.AppendLine("   ! ProtectionDetail 없음");

                    foreach (var detail in details)
                    {
                        // The generator writes escortWhileCalm = map.targetOneWayRoute, and the
                        // target's own oneWayRoute came from that same flag — so reading it back
                        // off the VIP reproduces the generator's answer without the map builder.
                        bool walkingTarget = detail.vip != null && detail.vip.oneWayRoute;
                        if (detail.escortWhileCalm != walkingTarget)
                        {
                            detail.escortWhileCalm = walkingTarget;
                            EditorUtility.SetDirty(detail);
                            changed++;
                        }
                        report.AppendLine($"   escortWhileCalm = {detail.escortWhileCalm}" +
                                          $"  (호위 {detail.guards.Count}명, 반경 {detail.escortRadius}" +
                                          $" / 측면 {detail.escortLateralScale})");
                    }

                    foreach (var guard in Object.FindObjectsByType<ProtectorGuard>(
                                 FindObjectsInactive.Include, FindObjectsSortMode.None))
                    {
                        report.AppendLine($"   ProtectorGuard.escortSpeed = {guard.escortSpeed}" +
                                          $" / repath {guard.escortRepathDistance}");
                        break;
                    }

                    foreach (var director in Object.FindObjectsByType<AssaultDirector>(
                                 FindObjectsInactive.Include, FindObjectsSortMode.None))
                        report.AppendLine($"   AssaultDirector.hiddenRushDelay = {director.hiddenRushDelay}" +
                                          $"  (maxAlive {director.maxAlive}, 간격 {director.spawnInterval})");

                    foreach (var vip in Object.FindObjectsByType<VipTarget>(
                                 FindObjectsInactive.Include, FindObjectsSortMode.None))
                        report.AppendLine($"   VipTarget.inPlayWaypoint = {vip.inPlayWaypoint}" +
                                          $"  (oneWayRoute {vip.oneWayRoute})");

                    // Mark dirty even when the flag already matched: saving is what serialises the
                    // fields the old assembly never wrote, and that is the point of the pass.
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                if (!string.IsNullOrEmpty(returnTo) && File.Exists(returnTo))
                    EditorSceneManager.OpenScene(returnTo);
            }

            Debug.Log($"[Rooftop Contract] 메커니즘 적용 완료 — 씬 {scenePaths.Count}개, " +
                      $"escortWhileCalm 변경 {changed}건\n{report}");

            EditorUtility.DisplayDialog("적용 완료",
                $"씬 {scenePaths.Count}개를 다시 저장했습니다.\n" +
                $"escortWhileCalm 변경: {changed}건\n\n{report}\n" +
                "자세한 내용은 Console 에도 남았습니다.", "OK");
        }
    }
}
