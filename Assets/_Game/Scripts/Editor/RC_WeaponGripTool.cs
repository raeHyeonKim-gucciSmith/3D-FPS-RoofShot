using UnityEditor;
using UnityEngine;

namespace RooftopContract.EditorTools
{
    /// <summary>
    /// Place the weapon in the hand by eye, once, and keep the result.
    ///
    /// Every automatic rule tried here — align to the actor's facing, align to the line between
    /// the hands, seat the trigger on the wrist — gets the weapon *near* the hand and still leaves
    /// it cutting through a torso, because a 0.6 m weapon on a chibi character with a short body
    /// has no geometrically correct answer. A human eye settles it in about thirty seconds.
    ///
    /// The catch is that the scenes are regenerated on every build, so dragging the weapon in a
    /// mission scene is worthless — the next build throws it away. So this workbench builds a
    /// disposable preview *posed in the stance the game will actually use*, lets you move the
    /// weapon with the normal gizmo, and then writes the result back into CastingConfig as a
    /// hand-bone-relative transform. That value is what every guard in every rebuilt scene gets.
    ///
    /// Stored in metres, not in the hand's local units: each character carries its own
    /// height-normalisation scale, so the same local offset would mean a different distance on
    /// each of them. Rotation transfers as-is — every character here comes off the same Mixamo
    /// rig, so their hand bones share an axis convention.
    /// </summary>
    public static class RC_WeaponGripTool
    {
        const string WorkbenchName = "RC_WeaponGripWorkbench";
        const string WeaponName = "Weapon";
        const string ProfileMarkerPrefix = "__RC_GRIP_PROFILE_";
        // Keep the preview far above every generated rooftop. At world zero the character stood
        // inside the current scene's grey floor, which hid the feet, weapon and lower body.
        static readonly Vector3 WorkbenchPosition = new Vector3(0f, 1000f, 0f);
        // Every model has independent geometry; never reuse another weapon's saved grip/IK.
        enum GripProfile { ProtectorSmg, AssaultPistol, ProtectorPistol, AssaultPrimary }

        [MenuItem("Rooftop Contract/도구/무기 — 그립 워크벤치 (SMG_P)", priority = 60)]
        public static void OpenWorkbench()
        {
            OpenWorkbench(GripProfile.ProtectorSmg);
        }

        [MenuItem("Rooftop Contract/도구/무기 — Pistol_H 워크벤치", priority = 63)]
        public static void OpenPistolWorkbench()
        {
            OpenWorkbench(GripProfile.AssaultPistol);
        }

        [MenuItem("Rooftop Contract/도구/무기 — Pistol_I 워크벤치", priority = 64)]
        public static void OpenProtectorPistolWorkbench()
        {
            OpenWorkbench(GripProfile.ProtectorPistol);
        }

        [MenuItem("Rooftop Contract/도구/무기 — AR_U 워크벤치", priority = 65)]
        public static void OpenAssaultPrimaryWorkbench()
        {
            OpenWorkbench(GripProfile.AssaultPrimary);
        }

        static void OpenWorkbench(GripProfile profile)
        {
            var casting = RC_SceneBuilder.LoadOrCreateCasting();
            bool pistol = profile == GripProfile.AssaultPistol || profile == GripProfile.ProtectorPistol;
            bool assault = profile == GripProfile.AssaultPistol || profile == GripProfile.AssaultPrimary;
            var prefab = PickCharacter(casting, assault);
            if (prefab == null)
            {
                EditorUtility.DisplayDialog("No character",
                    "경호원 프리팹을 찾지 못했습니다.\n\nCastingConfig 의 protectorPrefabs 를 채우거나 " +
                    "(메뉴 0b), 캐릭터 프리팹을 하나 선택한 뒤 다시 실행해 주세요.", "OK");
                return;
            }

            var old = GameObject.Find(WorkbenchName);
            if (old != null) Object.DestroyImmediate(old);

            var root = new GameObject(WorkbenchName);
            root.transform.position = WorkbenchPosition;

            // Built the same way the scene generator builds an actor, because a weapon fitted
            // against a differently-scaled preview is a weapon fitted against nothing.
            var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
            model.name = "Visual";
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.Euler(0f, casting.modelYawOffset, 0f);
            model.transform.localScale = Vector3.one;
            if (casting.normalizeHeight)
                ActorSetup.NormalizeToHeight(model.transform, casting.targetHeight,
                                             casting.groundPivot, root.transform.position);

            var clip = pistol
                     ? RC_SceneBuilder.GuardClipFor(prefab, "pistolidle")
                     : RC_SceneBuilder.ArmedIdleClipFor(prefab);
            var animator = model.GetComponentInChildren<Animator>();
            if (clip != null && animator != null) PoseWith(animator.gameObject, clip);

            // Resolve the hand through ActorSetup so the workbench hangs the weapon off exactly
            // the transform the generator will, marker override and all — then drop the
            // component, whose gizmos would otherwise build hitbox children in here.
            var setup = root.AddComponent<ActorSetup>();
            setup.ResolveAnchors();
            Transform hand = setup.RightHand;
            Object.DestroyImmediate(setup);

            if (hand == null)
            {
                Object.DestroyImmediate(root);
                EditorUtility.DisplayDialog("No hand bone",
                    $"'{prefab.name}' 에서 오른손 뼈를 찾지 못했습니다.\n\n" +
                    "Rig ▸ Optimize Game Objects 가 켜져 있으면 본이 계층에서 사라집니다. " +
                    "메뉴 '7. Fix Character Rigs' 를 먼저 실행해 주세요.", "OK");
                return;
            }

            string weaponPath = profile switch
            {
                GripProfile.AssaultPistol => $"{RC_SceneBuilder.PistolWeaponFolder}/Pistol_H.prefab",
                GripProfile.ProtectorPistol => $"{RC_SceneBuilder.PistolWeaponFolder}/Pistol_I.prefab",
                GripProfile.AssaultPrimary => $"{RC_SceneBuilder.WeaponFolder}/AR_U_PreSet.prefab",
                _ => $"{RC_SceneBuilder.WeaponFolder}/SMG_P_PreSet.prefab"
            };
            var weaponPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(weaponPath);
            if (weaponPrefab == null)
            {
                Object.DestroyImmediate(root);
                EditorUtility.DisplayDialog("No weapon",
                    $"무기 프리팹을 찾지 못했습니다: {weaponPath}", "OK");
                return;
            }

            var weapon = (GameObject)PrefabUtility.InstantiatePrefab(weaponPrefab, hand);
            weapon.name = WeaponName;
            var profileMarker = new GameObject(ProfileMarkerPrefix + profile);
            profileMarker.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSaveInBuild;
            profileMarker.transform.SetParent(weapon.transform, false);
            foreach (var c in weapon.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);

            // Start from whatever the config already holds — the stored manual placement if there
            // is one, the automatic solve if there is not. Either way it is a starting point to
            // nudge, not a result to accept.
            var grip = weapon.AddComponent<WeaponGrip>();
            grip.actorRoot = root.transform;
            grip.gripOffset = casting.weaponGripOffset;
            grip.extraEuler = casting.weaponExtraEuler;
            ApplyStoredProfile(casting, profile, grip);
            grip.Solve();
            Object.DestroyImmediate(grip);

            Selection.activeGameObject = weapon;
            var view = SceneView.lastActiveSceneView;
            if (view != null) { view.FrameSelected(); view.Repaint(); }

            poseIndex = 0;

            Debug.Log($"[Rooftop Contract] {ProfileLabel(profile)} 그립 워크벤치 — {prefab.name} / {ClipLabel(clip)}\n" +
                      "  1. 씬 뷰에서 'Weapon' 을 이동·회전(·스케일)해서 손에 맞춥니다. 로컬 좌표(Local) 권장.\n" +
                      "  2. 메뉴 '8c. Cycle Workbench Pose' 로 걷기·달리기 등 다른 자세도 확인합니다.\n" +
                      "  3. 메뉴 '8b. Save Weapon Grip' 으로 CastingConfig 에 저장합니다.\n" +
                      "  4. 메뉴 '3. Build All Missions' 로 전 경호원에 반영합니다.\n" +
                      $"  '{WorkbenchName}' 오브젝트는 지워도 되는 임시물입니다.");
        }

        [MenuItem("Rooftop Contract/도구/무기 — 포즈 순환", priority = 62)]
        public static void CyclePose()
        {
            var root = GameObject.Find(WorkbenchName);
            var model = root != null ? FindChildNamed(root.transform, "Visual") : null;
            if (model == null)
            {
                EditorUtility.DisplayDialog("No workbench",
                    $"'{WorkbenchName}' 을 찾지 못했습니다.\n\n메뉴 '8. Weapon Grip Workbench' 를 먼저 실행해 주세요.", "OK");
                return;
            }

            var prefab = SourcePrefabOf(model.gameObject);
            Transform weapon = FindChildNamed(root.transform, WeaponName);
            GripProfile profile = ProfileOf(weapon);
            var poses = BuildPoseList(prefab, profile);
            if (poses.Count == 0)
            {
                Debug.LogWarning("[Rooftop Contract] 순환할 클립을 찾지 못했습니다.");
                return;
            }

            poseIndex = (poseIndex + 1) % poses.Count;
            var pose = poses[poseIndex];

            var animator = model.GetComponentInChildren<Animator>();
            if (animator == null) return;

            // The weapon is a child of the hand bone, so re-posing moves it along with the arm —
            // that is the whole point of the check. Its local grip is never touched.
            PoseWith(animator.gameObject, pose.clip, pose.time * pose.clip.length);

            var view = SceneView.lastActiveSceneView;
            if (view != null) view.Repaint();

            Debug.Log($"[Rooftop Contract] {ProfileLabel(profile)} / " +
                      $"{(IsPistolProfile(profile) ? "권총 자세" : "라이플 자세")} / " +
                      $"자세 {poseIndex + 1}/{poses.Count} — " +
                      $"{pose.clip.name} @ {pose.time * 100f:0}%  ({pose.label})\n" +
                      "  총이 몸을 뚫는 자세가 있으면, 전부 봐줄 만한 타협점으로 맞춘 뒤 '8b' 로 저장하세요.");
        }

        [MenuItem("Rooftop Contract/도구/무기 — 그립 저장", priority = 61)]
        public static void SaveGrip()
        {
            var root = GameObject.Find(WorkbenchName);
            Transform weapon = root != null ? FindChildNamed(root.transform, WeaponName) : null;
            if (weapon == null || weapon.parent == null)
            {
                EditorUtility.DisplayDialog("No workbench",
                    $"'{WorkbenchName}' 을 찾지 못했습니다.\n\n메뉴 '8. Weapon Grip Workbench' 를 먼저 실행해 주세요.", "OK");
                return;
            }

            var casting = RC_SceneBuilder.LoadOrCreateCasting();
            Transform hand = weapon.parent;
            GripProfile profile = ProfileOf(weapon);
            bool pistol = profile == GripProfile.AssaultPistol || profile == GripProfile.ProtectorPistol;

            // localPosition is in the hand's units, and the hand carries the character's height
            // normalisation. Multiply back out so the stored number is metres.
            float handScale = AverageScale(hand.lossyScale);
            Vector3 handOffset = weapon.localPosition * handScale;
            Vector3 handEuler = Normalize(weapon.localRotation.eulerAngles);
            if (profile == GripProfile.AssaultPistol)
            {
                casting.pistolHandOffset = handOffset;
                casting.pistolHandEuler = handEuler;
                casting.pistolGripManual = true;
            }
            else if (profile == GripProfile.ProtectorPistol)
            {
                casting.protectorPistolHandOffset = handOffset;
                casting.protectorPistolHandEuler = handEuler;
                casting.protectorPistolGripManual = true;
            }
            else if (profile == GripProfile.AssaultPrimary)
            {
                casting.assaultPrimaryHandOffset = handOffset;
                casting.assaultPrimaryHandEuler = handEuler;
                casting.assaultPrimaryGripManual = true;
            }
            else
            {
                casting.weaponHandOffset = handOffset;
                casting.weaponHandEuler = handEuler;
                casting.weaponGripManual = true;
            }

            // Whatever size it was scaled to by eye is also an answer worth keeping.
            var probe = weapon.gameObject.AddComponent<WeaponGrip>();
            probe.targetLength = 0f;
            float length = probe.MeasureWorldLength();
            Object.DestroyImmediate(probe);
            if (length > 0.01f)
            {
                if (profile == GripProfile.AssaultPistol) casting.pistolLength = length;
                else if (profile == GripProfile.ProtectorPistol) casting.protectorPistolLength = length;
                else if (profile == GripProfile.AssaultPrimary) casting.assaultPrimaryLength = length;
                else casting.smgLength = length;
            }

            // Pose cycling is only a visual stress test. The pistol support hand is always
            // captured from the agreed canonical grip (Pistol Idle), regardless of which pose
            // happened to be visible when Save was pressed.
            if (pistol)
            {
                var model = FindChildNamed(root.transform, "Visual");
                var prefab = model != null ? SourcePrefabOf(model.gameObject) : null;
                var baseClip = RC_SceneBuilder.GuardClipFor(prefab, "pistolidle");
                var animator = model != null ? model.GetComponentInChildren<Animator>() : null;
                if (baseClip != null && animator != null) PoseWith(animator.gameObject, baseClip);
            }

            string ikNote = CaptureSupportHand(root, weapon, casting, profile);

            EditorUtility.SetDirty(casting);
            AssetDatabase.SaveAssets();
            Selection.activeObject = casting;
            EditorGUIUtility.PingObject(casting);

            Debug.Log($"[Rooftop Contract] {ProfileLabel(profile)} 그립 저장 — 위치 {handOffset:F3} m, " +
                      $"회전 {handEuler:F1}, 길이 {length:F2} m\n" +
                      $"  왼손 IK — {ikNote}\n" +
                      "  해당 무기 전용 수동 그립이 켜졌습니다. 다른 무기의 값은 변경하지 않았습니다.\n" +
                      "  '3. Build All Missions' 를 돌리면 전 경호원·돌격대에 같은 값이 적용됩니다.");
        }

        // ------------------------------------------------------------------ support hand

        /// <summary>
        /// Records where the left hand sits relative to the weapon, right now, in the rifle-hold
        /// pose you just fitted the weapon to.
        ///
        /// This is the whole reason the IK target does not have to be guessed. The clip's author
        /// already decided where a support hand goes on a rifle; you decided where the rifle goes
        /// in the right hand; the relationship between the two falls out of those two decisions
        /// and needs no third one. Every clip that was not authored around this weapon then gets
        /// pulled back to it.
        ///
        /// Refused when the left hand is nowhere near the weapon, because then it is not a grip —
        /// it is just where an arm happened to be, and pinning other clips to it would look worse
        /// than leaving the hand alone.
        /// </summary>
        static string CaptureSupportHand(GameObject root, Transform weapon, CastingConfig casting, GripProfile profile)
        {
            var animator = root.GetComponentInChildren<Animator>();
            Transform left = animator != null && animator.avatar != null && animator.avatar.isHuman
                           ? animator.GetBoneTransform(HumanBodyBones.LeftHand)
                           : null;

            if (left == null)
            {
                SetSupportCapture(casting, profile, false, Vector3.zero, Vector3.zero);
                return "왼손 뼈를 못 찾아 캡처 안 함 (총열 덮개 부품으로 대체)";
            }

            float gap = DistanceToWeapon(weapon, left.position);
            // A rifle support hand touches the handguard, but a two-hand pistol grip wraps the
            // left hand around the right hand and can sit farther from the pistol mesh itself.
            // Keep the rifle guard strict while allowing the authored Pistol Idle hand overlap.
            bool pistolProfile = IsPistolProfile(profile);
            float captureLimit = pistolProfile ? 0.28f : 0.15f;
            if (gap > captureLimit)
            {
                SetSupportCapture(casting, profile, false, Vector3.zero, Vector3.zero);
                return $"왼손이 총에서 {gap * 100f:0} cm 떨어져 있어 캡처 안 함 — 파지로 볼 수 없습니다. " +
                       $"총을 양손에 맞춰 놓고 다시 저장하면 잡힙니다 (허용 {captureLimit * 100f:0} cm)";
            }

            float ws = AverageScale(weapon.lossyScale);
            Vector3 offset = weapon.InverseTransformPoint(left.position) * ws;
            Vector3 euler = Normalize((Quaternion.Inverse(weapon.rotation) * left.rotation).eulerAngles);
            SetSupportCapture(casting, profile, true, offset, euler);
            return $"캡처됨 — 총 기준 {offset:F3} m (왼손이 총에서 {gap * 100f:0.0} cm)";
        }

        static void SetSupportCapture(CastingConfig casting, GripProfile profile, bool captured,
                                      Vector3 offset, Vector3 euler)
        {
            if (profile == GripProfile.AssaultPistol)
            {
                casting.pistolSupportCaptured = captured;
                casting.pistolSupportOffset = offset;
                casting.pistolSupportEuler = euler;
            }
            else if (profile == GripProfile.ProtectorPistol)
            {
                casting.protectorPistolSupportCaptured = captured;
                casting.protectorPistolSupportOffset = offset;
                casting.protectorPistolSupportEuler = euler;
            }
            else if (profile == GripProfile.AssaultPrimary)
            {
                casting.assaultPrimarySupportCaptured = captured;
                casting.assaultPrimarySupportOffset = offset;
                casting.assaultPrimarySupportEuler = euler;
            }
            else
            {
                casting.weaponSupportCaptured = captured;
                casting.weaponSupportOffset = offset;
                casting.weaponSupportEuler = euler;
            }
        }

        /// <summary>Distance from a point to the weapon's actual surface volume, not its origin.</summary>
        static float DistanceToWeapon(Transform weapon, Vector3 point)
        {
            Bounds world = default;
            bool first = true;
            foreach (var r in weapon.GetComponentsInChildren<Renderer>(true))
            {
                if (first) { world = r.bounds; first = false; }
                else world.Encapsulate(r.bounds);
            }
            if (first) return Vector3.Distance(weapon.position, point);
            return Mathf.Sqrt(world.SqrDistance(point));
        }

        // ------------------------------------------------------------------ pose cycling

        struct Pose
        {
            public AnimationClip clip;
            public float time;      // normalised
            public string label;
        }

        static int poseIndex;

        /// <summary>
        /// Every stance a guard carries the weapon in, in the order they matter: the scan is what
        /// the player looks at through the scope for most of a mission, walking is second, and
        /// the rest are brief. Two samples per clip — a loop's extremes are what clip through a
        /// torso, and the midpoint is where an arm swing peaks.
        /// </summary>
        static System.Collections.Generic.List<Pose> BuildPoseList(GameObject prefab, GripProfile profile)
        {
            var list = new System.Collections.Generic.List<Pose>();
            if (prefab == null) return list;

            if (IsPistolProfile(profile))
            {
                AddPoses(list, RC_SceneBuilder.GuardClipFor(prefab, "pistolidle"), "권총 서기 — 그립 기준");
                AddPoses(list, RC_SceneBuilder.GuardClipFor(prefab, "pistolrun"), "권총 이동");
                AddPoses(list, RC_SceneBuilder.GuardClipFor(prefab, "pistolkneelingidle"), "엄폐 무릎 대기");
                AddPoses(list, RC_SceneBuilder.GuardClipFor(prefab, "pistolshooting"), "무릎 사격");
                AddPoses(list, RC_SceneBuilder.GuardClipFor(prefab, "pistolstandtokneel"), "서기 → 무릎");
                AddPoses(list, RC_SceneBuilder.GuardClipFor(prefab, "pistolkneeltostand"), "무릎 → 서기");
                return list;
            }

            AddPoses(list, RC_SceneBuilder.GuardClipFor(prefab, "lookaround"), "경계 (기본 대기)");
            AddPoses(list, RC_SceneBuilder.GuardClipFor(prefab, "walk"), "걷기");
            AddPoses(list, RC_SceneBuilder.GuardClipFor(prefab, "run"), "달리기 — VIP 밀착");
            AddPoses(list, RC_SceneBuilder.GuardClipFor(prefab, "checkshoe"), "농땡이 (한 명만)");
            AddPoses(list, RC_SceneBuilder.GuardClipFor(prefab, "idle"), "차렷 (폴백, 지금은 미사용)");
            return list;
        }

        static bool IsPistolWeapon(Transform weapon)
        {
            return IsPistolProfile(ProfileOf(weapon));
        }

        static bool IsPistolProfile(GripProfile profile) =>
            profile == GripProfile.AssaultPistol || profile == GripProfile.ProtectorPistol;

        static GripProfile ProfileOf(Transform weapon)
        {
            if (weapon == null) return GripProfile.ProtectorSmg;

            // The marker is authoritative. Prefab correspondence can be lost after a preview
            // object is renamed or after an editor domain reload, which previously made AR_U
            // occasionally fall into the pistol pose list.
            foreach (var child in weapon.GetComponentsInChildren<Transform>(true))
            {
                if (!child.name.StartsWith(ProfileMarkerPrefix)) continue;
                string value = child.name.Substring(ProfileMarkerPrefix.Length);
                if (System.Enum.TryParse(value, out GripProfile marked)) return marked;
            }

            var source = PrefabUtility.GetCorrespondingObjectFromSource(weapon.gameObject);
            string sourceName = source != null ? source.name.ToLowerInvariant() : string.Empty;
            if (sourceName.Contains("pistol_h")) return GripProfile.AssaultPistol;
            if (sourceName.Contains("pistol_i")) return GripProfile.ProtectorPistol;
            if (sourceName.Contains("ar_u")) return GripProfile.AssaultPrimary;
            return GripProfile.ProtectorSmg;
        }

        static string ProfileLabel(GripProfile profile) => profile switch
        {
            GripProfile.AssaultPistol => "특공대 Pistol_H",
            GripProfile.ProtectorPistol => "경호원 Pistol_I",
            GripProfile.AssaultPrimary => "특공대 AR_U",
            _ => "경호원 SMG_P"
        };

        static void ApplyStoredProfile(CastingConfig casting, GripProfile profile, WeaponGrip grip)
        {
            switch (profile)
            {
                case GripProfile.AssaultPistol:
                    grip.targetLength = casting.pistolLength;
                    grip.manual = casting.pistolGripManual;
                    grip.handOffset = casting.pistolHandOffset;
                    grip.handEuler = casting.pistolHandEuler;
                    break;
                case GripProfile.ProtectorPistol:
                    grip.targetLength = casting.protectorPistolLength;
                    grip.manual = casting.protectorPistolGripManual;
                    grip.handOffset = casting.protectorPistolHandOffset;
                    grip.handEuler = casting.protectorPistolHandEuler;
                    break;
                case GripProfile.AssaultPrimary:
                    grip.targetLength = casting.assaultPrimaryLength;
                    grip.manual = casting.assaultPrimaryGripManual;
                    grip.handOffset = casting.assaultPrimaryHandOffset;
                    grip.handEuler = casting.assaultPrimaryHandEuler;
                    break;
                default:
                    grip.targetLength = casting.smgLength;
                    grip.manual = casting.weaponGripManual;
                    grip.handOffset = casting.weaponHandOffset;
                    grip.handEuler = casting.weaponHandEuler;
                    break;
            }
        }

        static void AddPoses(System.Collections.Generic.List<Pose> list, AnimationClip clip, string label)
        {
            if (clip == null) return;
            foreach (var t in new[] { 0f, 0.5f })
                list.Add(new Pose { clip = clip, time = t, label = label });
        }

        static GameObject SourcePrefabOf(GameObject instance)
        {
            var source = PrefabUtility.GetCorrespondingObjectFromSource(instance);
            if (source != null) return source;
            return CastingConfig.Pick(RC_SceneBuilder.LoadOrCreateCasting().protectorPrefabs, 0);
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>
        /// Poses the model and keeps the pose.
        ///
        /// AnimationMode is the only sampler that reliably retargets a Humanoid clip in the
        /// editor, but leaving it on locks the scene into preview mode and turning it off reverts
        /// everything it touched. So: sample, copy the resulting bone transforms out, stop, and
        /// write them back as ordinary transform values.
        /// </summary>
        static void PoseWith(GameObject animatorGo, AnimationClip clip, float time = 0f)
        {
            var bones = animatorGo.GetComponentsInChildren<Transform>(true);
            var pos = new Vector3[bones.Length];
            var rot = new Quaternion[bones.Length];
            var scale = new Vector3[bones.Length];

            bool startedHere = !AnimationMode.InAnimationMode();
            if (startedHere) AnimationMode.StartAnimationMode();

            AnimationMode.BeginSampling();
            AnimationMode.SampleAnimationClip(animatorGo, clip, time);
            AnimationMode.EndSampling();

            for (int i = 0; i < bones.Length; i++)
            {
                pos[i] = bones[i].localPosition;
                rot[i] = bones[i].localRotation;
                scale[i] = bones[i].localScale;
            }

            if (startedHere) AnimationMode.StopAnimationMode();

            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] == null) continue;
                bones[i].localPosition = pos[i];
                bones[i].localRotation = rot[i];
                bones[i].localScale = scale[i];
            }
        }

        static GameObject PickCharacter(CastingConfig casting, bool assault)
        {
            // A selected character prefab wins: fitting the weapon against the specific model you
            // are looking at is the whole point.
            var selected = Selection.activeObject as GameObject;
            if (selected != null)
            {
                string path = AssetDatabase.GetAssetPath(selected);
                if (!string.IsNullOrEmpty(path) && path.StartsWith("Assets/VoxelCharacter"))
                    return selected;
            }
            return CastingConfig.Pick(assault ? casting.assaultPrefabs : casting.protectorPrefabs, 0);
        }

        static Transform FindChildNamed(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        static string ClipLabel(AnimationClip c) => c != null ? c.name : "클립 없음 (T포즈)";

        static float AverageScale(Vector3 lossy) =>
            (Mathf.Abs(lossy.x) + Mathf.Abs(lossy.y) + Mathf.Abs(lossy.z)) / 3f;

        /// <summary>Euler angles into -180..180 so the saved numbers read as small corrections.</summary>
        static Vector3 Normalize(Vector3 euler) => new Vector3(Wrap(euler.x), Wrap(euler.y), Wrap(euler.z));

        static float Wrap(float a)
        {
            a %= 360f;
            if (a > 180f) a -= 360f;
            if (a < -180f) a += 360f;
            return a;
        }
    }
}
