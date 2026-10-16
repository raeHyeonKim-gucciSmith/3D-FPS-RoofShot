using UnityEngine;

namespace RooftopContract
{
    /// <summary>
    /// Seats a weapon in an actor's hand without anyone hand-tuning Euler angles.
    ///
    /// Two things make a fixed offset unreliable here. A hand bone's local axes differ between
    /// rigs — the values that fit the old sample rig point a Mixamo hand somewhere else entirely.
    /// And the pose at edit time is the T-pose, while the pose that matters is whatever the idle
    /// animation puts the hand in, so anything measured before the Animator runs is measured
    /// against the wrong arm position.
    ///
    /// So this waits for the Animator to settle, then solves the placement from three things it
    /// reads off the models themselves:
    ///
    ///   • which way the barrel points — from a child named "*Barrel*" if the weapon pack labels
    ///     its parts (this one does), otherwise from the mesh's own proportions;
    ///   • where the hand belongs on the weapon — the "*Trigger*"/"*Grip*" part, not the centre
    ///     of the bounding box. A rifle held by its middle reads as a briefcase;
    ///   • which way to aim it — down the line from the trigger hand to the support hand, so a
    ///     two-handed idle decides where the muzzle goes instead of an authored guess.
    ///
    /// Once the pose settles it freezes and disables itself — from then on the weapon is just a
    /// child of the hand and follows the animation normally.
    /// </summary>
    public class WeaponGrip : MonoBehaviour
    {
        [Tooltip("이 무기를 든 액터의 루트. 양손을 못 찾았을 때 총구가 향할 방향의 기준.")]
        public Transform actorRoot;
        [Tooltip("손 기준 미세 조정 (조준 방향 기준). z=앞, y=위, x=오른쪽. " +
                 "기본값의 y는 총몸을 손목보다 살짝 올려 주먹이 손잡이를 감싼 것처럼 보이게 한다.")]
        public Vector3 gripOffset = new Vector3(0f, 0.03f, 0f);
        [Tooltip("정렬 후 추가 회전. 총을 살짝 기울이고 싶을 때만.")]
        public Vector3 extraEuler = Vector3.zero;
        [Tooltip("총구 위치를 받을 트랜스폼. 총열 끝으로 자동 배치된다.")]
        public Transform muzzle;

        [Tooltip("총의 실제 길이 (m). 0 이면 크기를 건드리지 않는다. 기관단총 0.6, 권총 0.24 정도.")]
        public float targetLength = 0.6f;

        [Tooltip("그립 부품을 못 찾았을 때 손이 잡을 지점. 0=개머리판, 1=총구.")]
        [Range(0f, 1f)] public float gripFraction = 0.3f;

        [Tooltip("애니메이터가 자세를 잡을 때까지 다시 푸는 프레임 수.")]
        public int settleFrames = 8;

        [Header("Manual — 손으로 맞춘 값이 있으면 계산을 건너뛴다")]
        [Tooltip("켜면 아래 두 값을 그대로 적용한다. 자세와 무관하므로 프레임을 기다릴 필요도 없다.")]
        public bool manual;
        [Tooltip("손뼈 기준 위치 (m). 캐릭터 정규화 스케일로 나눠서 적용한다.")]
        public Vector3 handOffset;
        [Tooltip("손뼈 기준 회전.")]
        public Vector3 handEuler;

        int framesSolved;
        Transform supportHand;
        bool supportSearched;

        // The weapon's own geometry never changes, so it is measured once and reused across the
        // settle frames — reading sharedMesh.vertices allocates a fresh array every call.
        bool measured;
        Bounds localBounds;
        Vector3 localAxis;
        Vector3 localUp;

        void Awake()
        {
            // A hand-authored grip is expressed in the hand's own space, so it does not care what
            // the arm is doing. Nothing to wait for.
            if (manual) { Solve(); enabled = false; }
        }

        void LateUpdate()
        {
            // Re-solved for a few frames rather than once: the first frames of a scene are an
            // Animator transition, and freezing the weapon against a half-blended arm is exactly
            // how it ends up floating next to the hand.
            Solve();
            if (++framesSolved >= Mathf.Max(1, settleFrames)) enabled = false;
        }

        /// <summary>Public so the editor generator can solve immediately on a posed model.</summary>
        public void Solve()
        {
            if (actorRoot == null) actorRoot = transform.root;
            if (!TryMeasure(out Bounds local, out Vector3 axis, out Vector3 up)) return;

            // Normalise the weapon's real-world length.
            //
            // The weapon is parented to a hand bone that carries the character's own height
            // normalisation — roughly 1.5x here — so a rifle authored at 1:1 arrives half again
            // too big and reads as a cannon. Scaling to a stated length makes the weapon
            // independent of both the character's scale and the weapon pack's units.
            float localLength = Vector3.Scale(local.size, Abs(axis)).magnitude;
            if (targetLength > 0.0001f && localLength > 0.0001f)
            {
                float worldLength = localLength * AverageScale(transform.lossyScale);
                if (worldLength > 0.0001f)
                    transform.localScale *= targetLength / worldLength;
            }

            float s = AverageScale(transform.lossyScale);
            Transform hand = transform.parent != null ? transform.parent : actorRoot;

            if (manual)
            {
                // Stored in metres so one set of numbers fits every character: each model carries
                // its own height-normalisation scale, so the same local offset would mean a
                // different distance on each of them.
                float handScale = AverageScale(hand.lossyScale);
                transform.localRotation = Quaternion.Euler(handEuler);
                transform.localPosition = handScale > 0.0001f ? handOffset / handScale : handOffset;
                if (muzzle != null)
                {
                    Vector3 tip = local.center + axis * Vector3.Scale(local.extents, Abs(axis)).magnitude;
                    muzzle.position = transform.TransformPoint(tip);
                }
                return;
            }

            // Aim down the actor's facing, unless the idle is holding the weapon in two hands —
            // then the hands themselves are a better answer than any authored angle.
            Vector3 aim = AimDirection(hand);
            Quaternion face = Quaternion.LookRotation(aim, actorRoot.up);

            // Take the weapon's own (barrel, up) frame onto (forward, up), then onto the aim.
            // A bare FromToRotation would leave the roll free, and for a model whose barrel runs
            // down its local -Z that is a 180° turn about an axis Unity picks for us — half the
            // time the sights end up underneath.
            Quaternion align = Quaternion.Inverse(Quaternion.LookRotation(axis, up));
            transform.rotation = face * Quaternion.Euler(extraEuler) * align;

            // Put the *grip* on the hand, not the weapon's centre.
            //
            // The world offset of a local-space point is just rotation * point. An earlier
            // version also multiplied by the inverse of the barrel-alignment rotation, applying
            // the correction twice and parking the rifle through the character's chest.
            Vector3 gripLocal = GripLocal(local, axis, localLength);
            transform.position = hand.position
                               + face * gripOffset
                               - transform.rotation * (gripLocal * s);

            if (muzzle != null)
            {
                Vector3 tipLocal = local.center + axis * Vector3.Scale(local.extents, Abs(axis)).magnitude;
                muzzle.position = transform.position + transform.rotation * (tipLocal * s);
            }
        }

        /// <summary>
        /// The weapon's length in metres along its own long axis, at its current scale. The grip
        /// workbench reads it back so whatever size you settle on by eye becomes the stored one.
        /// </summary>
        public float MeasureWorldLength()
        {
            if (!TryMeasure(out Bounds b, out Vector3 a, out _)) return 0f;
            return Vector3.Scale(b.size, Abs(a)).magnitude * AverageScale(transform.lossyScale);
        }

        // ------------------------------------------------------------------ aim

        /// <summary>
        /// Trigger hand to support hand. A rifle-hold idle puts the left hand on the fore-end, so
        /// that line is the barrel line, whatever the rig does with its wrist axes. Rejected when
        /// the hands are together, too far apart to be on the same weapon, or nowhere near the
        /// front (arms at rest, a one-handed pistol pose) — then the line means nothing.
        /// </summary>
        Vector3 AimDirection(Transform hand)
        {
            Vector3 fallback = actorRoot.forward;
            Transform support = FindSupportHand();
            if (support == null || support == hand) return fallback;

            Vector3 v = support.position - hand.position;
            float d = v.magnitude;
            if (d < 0.12f) return fallback;
            if (targetLength > 0.0001f && d > targetLength * 1.15f) return fallback;

            // Flatten to the actor's horizontal plane. The support hand usually sits higher than
            // the trigger hand, and taking that line raw pitches the whole weapon up like a
            // flagpole — with the stock swinging down through the chest. A guard at rest holds
            // the weapon level; only the yaw of the hands is worth reading.
            Vector3 dir = Vector3.ProjectOnPlane(v, actorRoot.up);
            if (dir.sqrMagnitude < 0.0001f) return fallback;
            dir.Normalize();

            if (Vector3.Dot(dir, fallback) < 0.35f) return fallback;
            return dir;
        }

        Transform FindSupportHand()
        {
            if (supportSearched) return supportHand;
            supportSearched = true;

            var animator = actorRoot != null ? actorRoot.GetComponentInChildren<Animator>() : null;
            if (animator != null && animator.avatar != null && animator.isHuman)
                supportHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);

            if (supportHand == null && actorRoot != null)
            {
                // Same rule as ActorSetup's bone search: shortest match wins, because a Mixamo rig
                // carries both "LeftHand" and "LeftHandIndex1" and bone order is not guaranteed.
                foreach (var t in actorRoot.GetComponentsInChildren<Transform>(true))
                {
                    string n = t.name.ToLowerInvariant();
                    if (!n.Contains("lefthand") && !n.Contains("hand_l") && !n.Contains("l_hand")) continue;
                    if (n.Contains("end")) continue;
                    if (supportHand == null || t.name.Length < supportHand.name.Length) supportHand = t;
                }
            }
            return supportHand;
        }

        // ------------------------------------------------------------------ weapon anatomy

        /// <summary>
        /// Where the trigger hand sits on the weapon, in the weapon's own space. The named part is
        /// worth looking for: holding an SMG by the centre of its bounding box puts the hand a
        /// magazine's width in front of the trigger, and the whole gun then hangs too far back.
        /// </summary>
        Vector3 GripLocal(Bounds local, Vector3 axis, float localLength)
        {
            Transform grip = FindPart("trigger") ?? FindPart("grip");
            if (grip != null)
            {
                Vector3 p = transform.InverseTransformPoint(grip.position);
                // Keep the part's position along the barrel, but stay on the weapon's own centre
                // line across it — a trigger pivot can sit off to one side of the receiver.
                float along = Vector3.Dot(p - local.center, axis);
                return local.center + axis * along;
            }
            return local.center + axis * ((gripFraction - 0.5f) * localLength);
        }

        /// <summary>
        /// Measures the weapon and returns the long axis signed towards the muzzle, plus the
        /// weapon's own up — the two together are the frame the aim rotation is built from.
        /// </summary>
        bool TryMeasure(out Bounds bounds, out Vector3 axis, out Vector3 up)
        {
            if (measured)
            {
                bounds = localBounds;
                axis = localAxis;
                up = localUp;
                return true;
            }

            bounds = default;
            axis = Vector3.forward;
            up = Vector3.up;

            var points = new System.Collections.Generic.List<Vector3>();
            Matrix4x4 toLocal = transform.worldToLocalMatrix;
            bool first = true;

            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;

                Matrix4x4 m = toLocal * r.transform.localToWorldMatrix;
                foreach (var v in mf.sharedMesh.vertices)
                {
                    Vector3 p = m.MultiplyPoint3x4(v);
                    points.Add(p);
                    if (first) { bounds = new Bounds(p, Vector3.zero); first = false; }
                    else bounds.Encapsulate(p);
                }
            }
            if (first) return false;

            Vector3 unsigned = LongestAxis(bounds.size);
            axis = unsigned * MuzzleSign(bounds, unsigned, points);
            up = UpAxis(bounds, unsigned);

            localBounds = bounds;
            localAxis = axis;
            localUp = up;
            measured = true;
            return true;
        }

        /// <summary>
        /// Which way is up on the weapon, as one of its own axes.
        ///
        /// Of the two axes across the barrel, a gun is always taller than it is wide — magazine,
        /// stock and sights all stack vertically — so the larger one is the vertical. Its sign
        /// comes from the trigger sitting below the barrel, which is true of every firearm ever
        /// made and, more to the point, of every prefab in this pack.
        /// </summary>
        Vector3 UpAxis(Bounds bounds, Vector3 unsigned)
        {
            Vector3 a, b;
            if (unsigned == Vector3.right) { a = Vector3.up; b = Vector3.forward; }
            else if (unsigned == Vector3.up) { a = Vector3.forward; b = Vector3.right; }
            else { a = Vector3.up; b = Vector3.right; }

            Vector3 candidate = Vector3.Scale(bounds.size, a).magnitude >= Vector3.Scale(bounds.size, b).magnitude ? a : b;

            Transform grip = FindPart("trigger") ?? FindPart("grip");
            Transform barrel = FindPart("barrel");
            if (grip != null && barrel != null)
            {
                Vector3 d = transform.InverseTransformPoint(grip.position)
                          - transform.InverseTransformPoint(barrel.position);
                float along = Vector3.Dot(d, candidate);
                if (Mathf.Abs(along) > 0.005f) return candidate * -Mathf.Sign(along);
            }
            return candidate;
        }

        /// <summary>
        /// Which end is the front. Prefers a part named "*Barrel*" — this weapon pack labels every
        /// piece — and falls back to proportions: a barrel is a tube and a stock is a block, so
        /// the slimmer half is the front. Without this the barrel-alignment rotation is a coin
        /// flip and half the cast points its muzzle at its own stomach.
        /// </summary>
        float MuzzleSign(Bounds bounds, Vector3 unsigned, System.Collections.Generic.List<Vector3> points)
        {
            Transform barrel = FindPart("barrel");
            if (barrel != null)
            {
                float along = Vector3.Dot(transform.InverseTransformPoint(barrel.position) - bounds.center, unsigned);
                if (Mathf.Abs(along) > 0.02f) return Mathf.Sign(along);
            }

            float frontSum = 0f, backSum = 0f;
            int frontCount = 0, backCount = 0;
            foreach (var p in points)
            {
                Vector3 d = p - bounds.center;
                float along = Vector3.Dot(d, unsigned);
                float radial = (d - unsigned * along).magnitude;
                if (along >= 0f) { frontSum += radial; frontCount++; }
                else { backSum += radial; backCount++; }
            }
            if (frontCount == 0 || backCount == 0) return 1f;

            float frontAvg = frontSum / frontCount;
            float backAvg = backSum / backCount;
            // Only act on a clear difference; a symmetric shape carries no information either way.
            if (frontAvg > backAvg * 1.15f) return -1f;
            return 1f;
        }

        Transform FindPart(string needle)
        {
            Transform best = null;
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                if (t == transform) continue;
                if (!t.name.ToLowerInvariant().Contains(needle)) continue;
                // Shortest name wins: "SMG_P_Barrel" beats "SMG_P_BarrelGuard_Rail".
                if (best == null || t.name.Length < best.name.Length) best = t;
            }
            return best;
        }

        static Vector3 LongestAxis(Vector3 size)
        {
            if (size.x >= size.y && size.x >= size.z) return Vector3.right;
            if (size.z >= size.y) return Vector3.forward;
            return Vector3.up;
        }

        static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        static float AverageScale(Vector3 lossy) => (Mathf.Abs(lossy.x) + Mathf.Abs(lossy.y) + Mathf.Abs(lossy.z)) / 3f;
    }
}
