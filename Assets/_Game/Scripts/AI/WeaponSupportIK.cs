using UnityEngine;

namespace RooftopContract
{
    /// <summary>
    /// Pins the support hand to the weapon's fore-end.
    ///
    /// The weapon is parented to the right hand, so the right hand is always correct and the left
    /// one is always a lie: it holds whatever imaginary rifle the clip was authored around. In the
    /// rifle-hold idle that happens to line up. In walk, run and the shoe check — none of which
    /// were authored holding this weapon — the left hand swings free while the fore-end goes
    /// somewhere else entirely, and at 8x that reads immediately.
    ///
    /// Humanoid IK fixes it in one goal. What it needs is a target, and guessing where a hand
    /// belongs on an arbitrary weapon is the same guessing game that failed for the right hand.
    /// So the target is not guessed: the grip workbench captures where the left hand sits
    /// relative to the weapon in the pose you fitted it to, and that captured relationship is
    /// what every other clip is pulled back to. Without a capture it falls back to the weapon's
    /// own handguard part, position only — a fair guess, and one that stays off if the weapon has
    /// no such part (a pistol, held one-handed, wants no support hand at all).
    ///
    /// Must live on the Animator's GameObject: OnAnimatorIK is only delivered there, and only
    /// when the controller's layer has its IK pass enabled (the generator turns that on).
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class WeaponSupportIK : MonoBehaviour
    {
        [Tooltip("이 액터가 든 무기. 비면 IK는 꺼진 채로 있는다.")]
        public Transform weapon;

        [Tooltip("총 기준 왼손 위치 (m). 워크벤치가 캡처한 값.")]
        public Vector3 supportOffset;
        [Tooltip("총 기준 왼손 회전. 워크벤치가 캡처한 값.")]
        public Vector3 supportEuler;
        [Tooltip("실제 파지 자세에서 캡처된 값인가. 아니면 총열 덮개 부품으로 대체하고 회전은 포기한다.")]
        public bool captured;

        [Range(0f, 1f)] public float positionWeight = 1f;
        [Range(0f, 1f)] public float rotationWeight = 0.7f;

        [Tooltip("가중치가 오르내리는 시간 (초). 스폰과 사망에서 손이 순간이동하지 않게.")]
        public float blendTime = 0.2f;

        [Tooltip("총열 덮개 부품 이름 후보. 손잡이(grip)는 오른손 자리라 일부러 뺐다.")]
        public string[] handguardHints = { "barrelguard", "handguard", "foregrip", "guard" };

        Animator animator;
        Damageable health;
        Transform handguard;
        bool searched;
        float weight;

        void Awake()
        {
            animator = GetComponent<Animator>();
            health = GetComponentInParent<Damageable>();
        }

        void OnAnimatorIK(int layerIndex)
        {
            if (animator == null || !animator.isHuman) return;

            bool want = weapon != null
                     && (health == null || !health.IsDead)
                     && TryGrip(out Vector3 pos, out Quaternion rot);

            // Eased rather than switched. A hand that snaps onto the weapon the frame a guard
            // spawns, or snaps off the frame they die, is exactly the pop this is meant to avoid.
            weight = blendTime > 0.001f
                   ? Mathf.MoveTowards(weight, want ? 1f : 0f, Time.deltaTime / blendTime)
                   : (want ? 1f : 0f);

            if (weight <= 0.001f)
            {
                animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, 0f);
                animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, 0f);
                return;
            }

            // Recomputed after the blend so a dying actor still has a target to blend away from.
            if (!TryGrip(out Vector3 target, out Quaternion targetRot)) return;

            animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, weight * positionWeight);
            animator.SetIKPosition(AvatarIKGoal.LeftHand, target);

            // Rotation only from a real capture. A handguard part gives a position and says
            // nothing about which way a palm faces, and a wrong wrist reads worse than a free one.
            float rw = captured ? weight * rotationWeight : 0f;
            animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, rw);
            if (rw > 0f) animator.SetIKRotation(AvatarIKGoal.LeftHand, targetRot);
        }

        bool TryGrip(out Vector3 position, out Quaternion rotation)
        {
            position = default;
            rotation = default;
            if (weapon == null) return false;

            if (captured)
            {
                // Stored in metres, like every other placement here, so it survives a character
                // whose normalisation scale differs.
                float s = AverageScale(weapon.lossyScale);
                Vector3 local = s > 0.0001f ? supportOffset / s : supportOffset;
                position = weapon.TransformPoint(local);
                rotation = weapon.rotation * Quaternion.Euler(supportEuler);
                return true;
            }

            var anchor = Handguard();
            if (anchor == null) return false;
            position = anchor.position;
            rotation = weapon.rotation;
            return true;
        }

        Transform Handguard()
        {
            if (searched) return handguard;
            searched = true;

            foreach (var hint in handguardHints)
            {
                foreach (var t in weapon.GetComponentsInChildren<Transform>(true))
                {
                    if (t == weapon) continue;
                    if (!t.name.ToLowerInvariant().Contains(hint)) continue;
                    // Shortest name wins, as everywhere else: "SMG_P_BarrelGuard" over
                    // "SMG_P_BarrelGuard_Rail".
                    if (handguard == null || t.name.Length < handguard.name.Length) handguard = t;
                }
                if (handguard != null) break;
            }
            return handguard;
        }

        static float AverageScale(Vector3 lossy) =>
            (Mathf.Abs(lossy.x) + Mathf.Abs(lossy.y) + Mathf.Abs(lossy.z)) / 3f;
    }
}
