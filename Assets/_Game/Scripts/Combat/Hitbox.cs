using UnityEngine;

namespace RooftopContract
{
    /// <summary>
    /// A collider that forwards hits to a Damageable with a multiplier.
    /// Head volumes use a high multiplier so a clean headshot kills outright — which is what
    /// keeps the shielded target killable once the bodyguards close in.
    /// </summary>
    public class Hitbox : MonoBehaviour
    {
        public Damageable owner;
        public float damageMultiplier = 1f;
        public bool isHead;

        public void Take(float baseDamage, Vector3 point, Vector3 direction, GameObject source)
        {
            if (owner == null) return;
            owner.ApplyDamage(new DamageInfo
            {
                amount = baseDamage * damageMultiplier,
                point = point,
                direction = direction,
                headshot = isHead,
                source = source
            });
        }
    }
}
