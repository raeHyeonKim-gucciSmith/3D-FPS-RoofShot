using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace RooftopContract.EditorTools
{
    /// <summary>
    /// Helpers for bringing your own MagicaVoxel characters in, and for retiring the sample pack
    /// once they are working.
    /// </summary>
    public static class RC_CharacterTools
    {
        // ------------------------------------------------------------------ hitbox preview

        /// <summary>
        /// Drops the selected character into the scene with an ActorSetup attached so its
        /// measured head/body boxes draw as gizmos. This is the fastest way to check whether
        /// headHeightFraction suits a given model's proportions — chibi heads need a much
        /// larger fraction than realistic ones.
        /// </summary>
        [MenuItem("Rooftop Contract/도구/캐릭터 — 히트박스 미리보기", priority = 23)]
        public static void PreviewHitboxes()
        {
            var prefab = Selection.activeObject as GameObject;
            if (prefab == null)
            {
                EditorUtility.DisplayDialog("Select a character",
                    "Project 창에서 캐릭터 프리팹이나 모델(.fbx/.obj)을 하나 선택한 뒤 다시 실행하세요.", "OK");
                return;
            }

            var casting = RC_SceneBuilder.LoadOrCreateCasting();

            var root = new GameObject($"HITBOX_PREVIEW_{prefab.name}");
            var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
            if (model == null) model = Object.Instantiate(prefab, root.transform);
            model.name = "Visual";
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.Euler(0f, casting.modelYawOffset, 0f);

            NormalizeForPreview(model, root.transform, casting);

            root.AddComponent<Damageable>();
            var setup = root.AddComponent<ActorSetup>();
            setup.headHeightFraction = casting.headHeightFraction;
            setup.headPadding = casting.headPadding;
            setup.bodyPadding = casting.bodyPadding;
            setup.maxBodyWidthFraction = casting.maxBodyWidthFraction;
            setup.headDamageMultiplier = casting.headDamageMultiplier;
            setup.ResolveAnchors();
            setup.Measure();

            Selection.activeGameObject = root;
            SceneView.lastActiveSceneView?.FrameSelected();

            Vector3 h = setup.HeadBoxSize, b2 = setup.BodyBoxSize;
            float headShare = setup.ActorHeight > 0f ? h.y / setup.ActorHeight : 0f;
            float bodyWidthShare = setup.ActorHeight > 0f ? b2.x / setup.ActorHeight : 0f;

            Debug.Log(
                $"[Rooftop Contract] '{prefab.name}'\n" +
                $"  키          : {setup.ActorHeight:0.00} m  (목표 {casting.targetHeight:0.00})\n" +
                $"  머리 앵커   : {(setup.HeadAnchor != null ? setup.HeadAnchor.name : "없음 — 메시 상단 기준")}\n" +
                $"  무기 앵커   : {(setup.RightHand != null ? setup.RightHand.name : "없음 — 가슴 높이 대체")}\n" +
                $"  머리 박스   : {h.x:0.00} x {h.y:0.00} x {h.z:0.00} m   (키의 {headShare:P0})\n" +
                $"  몸통 박스   : {b2.x:0.00} x {b2.y:0.00} x {b2.z:0.00} m (폭이 키의 {bodyWidthShare:P0})\n" +
                $"  판정        : {Verdict(headShare, bodyWidthShare)}");
        }

        /// <summary>
        /// Turns the two measurements into plain advice. A body box much wider than a third of
        /// the character's height means the model is standing in a T-pose and the arms are being
        /// counted as torso — the single most common thing to get wrong here.
        /// </summary>
        static string Verdict(float headShare, float bodyWidthShare)
        {
            var notes = new List<string>();

            if (headShare > 0.32f) notes.Add("머리 박스가 큽니다 — headHeightFraction 을 낮춰 보세요");
            else if (headShare < 0.12f) notes.Add("머리 박스가 작습니다 — headHeightFraction 을 올려 보세요");

            if (bodyWidthShare > 0.45f)
                notes.Add("몸통 폭이 넓습니다 — T포즈로 팔이 몸통에 포함된 상태일 수 있습니다. " +
                          "maxBodyWidthFraction 을 0.35 근처로 낮추세요");

            return notes.Count == 0 ? "양호 — 그대로 쓰셔도 됩니다" : string.Join(" / ", notes);
        }

        static void NormalizeForPreview(GameObject model, Transform root, CastingConfig casting)
        {
            if (casting.normalizeHeight)
            {
                ActorSetup.NormalizeToHeight(model.transform, casting.targetHeight,
                                             casting.groundPivot, root.position);
            }
            else if (casting.groundPivot && ActorSetup.TryMeasureWorldBounds(model, out Bounds g))
            {
                model.transform.position += new Vector3(
                    root.position.x - g.center.x, root.position.y - g.min.y, root.position.z - g.center.z);
            }
        }

        static Bounds WorldBounds(GameObject go)
        {
            if (ActorSetup.TryMeasureWorldBounds(go, out Bounds b)) return b;
            return new Bounds(go.transform.position, Vector3.zero);
        }

        // ------------------------------------------------------------------ marker helper

        /// <summary>
        /// Adds the two anchor markers to the selected scene object. Positioning an empty by hand
        /// on the head beats every heuristic, and it is the only option that works on an unrigged
        /// mesh whose head is not simply "the top of the silhouette".
        /// </summary>
        [MenuItem("Rooftop Contract/도구/캐릭터 — 히트박스 마커 생성", priority = 24)]
        public static void AddMarkers()
        {
            var go = Selection.activeGameObject;
            if (go == null || EditorUtility.IsPersistent(go))
            {
                EditorUtility.DisplayDialog("Select a scene object",
                    "씬에 있는 캐릭터 오브젝트를 선택한 뒤 실행하세요. (프리팹 에셋이 아니라 Hierarchy 쪽)", "OK");
                return;
            }

            var setup = go.GetComponent<ActorSetup>();
            string headName = setup != null ? setup.headMarkerName : "HITBOX_HEAD";
            string handName = setup != null ? setup.handMarkerName : "ATTACH_WEAPON";

            Bounds b = WorldBounds(go);
            var head = EnsureChild(go.transform, headName, new Vector3(b.center.x, b.max.y - b.size.y * 0.12f, b.center.z));
            var hand = EnsureChild(go.transform, handName, new Vector3(b.max.x, b.center.y, b.center.z));

            Selection.activeGameObject = head.gameObject;
            Debug.Log($"[Rooftop Contract] '{go.name}' 에 마커 2개를 추가했습니다. " +
                      $"'{headName}' 를 머리 중앙으로, '{handName}' 를 오른손 위치로 옮긴 뒤 프리팹으로 저장하세요.");
        }

        static Transform EnsureChild(Transform parent, string name, Vector3 worldPos)
        {
            foreach (Transform t in parent) if (t.name == name) return t;
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Add hitbox marker");
            go.transform.SetParent(parent, true);
            go.transform.position = worldPos;
            return go.transform;
        }
    }
}
