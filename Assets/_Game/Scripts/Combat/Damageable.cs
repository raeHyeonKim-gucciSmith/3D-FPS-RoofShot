using System;
using UnityEngine;

namespace RooftopContract
{
    public struct DamageInfo
    {
        public float amount;
        public Vector3 point;
        public Vector3 direction;
        public bool headshot;
        public GameObject source;
    }

    /// <summary>Health pool shared by the player, the VIP and every guard.</summary>
    public class Damageable : MonoBehaviour
    {
        public float maxHealth = 100f;
        [SerializeField] float health;

        public float Health => health;
        public float Normalized => maxHealth <= 0f ? 0f : health / maxHealth;
        public bool IsDead { get; private set; }

        public event Action<DamageInfo> Damaged;
        public event Action<DamageInfo> Died;

        void Awake() => health = maxHealth;

        public void ApplyDamage(DamageInfo info)
        {
            if (IsDead) return;
            health -= info.amount;
            Damaged?.Invoke(info);
            if (health <= 0f)
            {
                IsDead = true;
                Died?.Invoke(info);
            }
        }

        public void Heal(float amount)
        {
            if (IsDead) return;
            health = Mathf.Min(maxHealth, health + amount);
        }
    }

}
