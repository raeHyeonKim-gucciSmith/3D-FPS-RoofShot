using UnityEngine;

namespace RooftopContract
{
    /// <summary>
    /// One place an attacker can stand and not be shot, plus the step to the side that lets them
    /// shoot back. The map generator puts these behind the rooftop plant — chillers, cooling
    /// towers, condenser banks — and groups them into stages by distance from the player.
    ///
    /// A slot is exclusive. That is not politeness between agents, it is the difficulty dial:
    /// stage 3 has two slots, so no matter how many attackers are alive, only two of them can
    /// ever be within eight metres of the player. The rest are stuck further back waiting for a
    /// slot to open, which is what makes the fight arrive in waves instead of as a mob.
    /// </summary>
    public class CoverPoint : MonoBehaviour
    {
        [Tooltip("1 = 문 앞 진입선, 숫자가 클수록 플레이어에 가깝다.")]
        public int stage = 1;

        [Tooltip("선 채로 완전히 가려지는 엄폐인가. false면 허리 높이라 상체가 보인다.")]
        public bool hardCover = true;

        // Set by the map generator from the width of the object this slot hides behind. The
        // default is only a floor for hand-placed points: a step shorter than half the cover's
        // width does not clear it, and an attacker who "leans out" while still behind their own
        // casing is the reason shots at them used to feel like they went nowhere.
        [Tooltip("사격하려고 옆으로 나오는 거리 (m). 엄폐물 반폭 + 어깨 여유. 생성기가 자동 설정한다.")]
        public float peekOffset = 1.15f;

        public AssaultGuard Occupant { get; private set; }

        public bool IsFree => Occupant == null;

        public bool TryClaim(AssaultGuard guard)
        {
            if (guard == null) return false;
            if (Occupant != null && Occupant != guard) return false;
            Occupant = guard;
            return true;
        }

        public void Release(AssaultGuard guard)
        {
            if (Occupant == guard) Occupant = null;
        }

        /// <summary>Drops a claim whose owner died without releasing it.</summary>
        public void DropDeadOccupant()
        {
            if (Occupant == null) return;
            if (Occupant == null || !Occupant.isActiveAndEnabled || Occupant.IsDead) Occupant = null;
        }

        /// <summary>
        /// Which side of this cover to lean out from. Both sides are tested against the actual
        /// target, so a slot works from whichever direction the player happens to be peeking —
        /// no authored "correct side" to get wrong when the geometry moves.
        /// </summary>
        public Vector3 PeekPoint(Vector3 target, GameObject ignore)
        {
            Vector3 right = transform.right * peekOffset;
            Vector3 a = transform.position + right;
            Vector3 b = transform.position - right;

            bool aClear = Clear(a, target, ignore);
            bool bClear = Clear(b, target, ignore);

            if (aClear && !bClear) return a;
            if (bClear && !aClear) return b;
            // Both work, or neither does: take the side that faces the target, so a guard leans
            // towards the fight rather than away from it.
            return Vector3.Dot(target - a, target - a) <= Vector3.Dot(target - b, target - b) ? a : b;
        }

        static bool Clear(Vector3 from, Vector3 target, GameObject ignore)
        {
            Vector3 eye = from + Vector3.up * 1.35f;
            Vector3 dir = target - eye;
            float d = dir.magnitude;
            if (d < 1.2f) return true;

            // Stop short of the target. The aim point is the player's camera, which sits inside
            // the player's own capsule — a full-length ray hits the player and reports the shot
            // as blocked, so every peek side would look equally bad and the lean-out would pick
            // its direction at random.
            return !ImpactResolver.Raycast(eye, dir / d, d - 0.8f, ignore, out _);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = stage switch
            {
                1 => new Color(0.4f, 0.8f, 1f, 0.9f),
                2 => new Color(1f, 0.85f, 0.3f, 0.9f),
                _ => new Color(1f, 0.4f, 0.35f, 0.9f),
            };
            Gizmos.DrawWireSphere(transform.position, 0.45f);
            Gizmos.DrawLine(transform.position, transform.position + transform.right * peekOffset);
            Gizmos.DrawLine(transform.position, transform.position - transform.right * peekOffset);
        }
    }
}
