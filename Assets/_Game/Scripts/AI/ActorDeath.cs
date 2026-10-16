using UnityEngine;
using UnityEngine.AI;

namespace RooftopContract
{
    /// <summary>
    /// The character pack has no death animation, so death is procedural:
    /// the actor tips over about its feet, settles, then despawns.
    /// </summary>
    [RequireComponent(typeof(Damageable))]
    public class ActorDeath : MonoBehaviour
    {
        public float fallDuration = 0.55f;
        [Tooltip("사망 후 시체가 제거되기까지의 시간. 웹 빌드의 오브젝트 수를 낮게 유지한다.")]
        public float despawnDelay = 5f;

        bool falling;
        float t;
        Quaternion startRot, endRot;
        Vector3 startPos, endPos;

        void Awake()
        {
            // Existing generated mission scenes serialized the old 14 second value. Clamp it
            // here as well as changing the default so those scenes receive the Web-friendly
            // cleanup time without requiring a destructive full scene rebuild.
            despawnDelay = Mathf.Min(despawnDelay, 5f);
            GetComponent<Damageable>().Died += OnDied;
        }

        void OnDied(DamageInfo info)
        {
            var agent = GetComponent<NavMeshAgent>();
            if (agent != null && agent.enabled) { agent.isStopped = true; agent.enabled = false; }

            foreach (var b in GetComponents<MonoBehaviour>())
                if (b != this && b is not Damageable) b.enabled = false;

            var setup = GetComponent<ActorSetup>();
            if (setup != null) setup.DisableHitboxes();

            var mark = GetComponentInChildren<AlertMark>(true);
            if (mark != null) mark.gameObject.SetActive(false);

            Destroy(gameObject, despawnDelay);

            // A real death animation if the character has one. Head and body shots use different
            // clips — in a sniper game that difference is the clearest feedback the player gets
            // about the hit they just made.
            if (TryPlayDeathClip(info)) return;

            // Fall away from the shot.
            Vector3 push = info.direction;
            push.y = 0f;
            if (push.sqrMagnitude < 0.001f) push = -transform.forward;
            push.Normalize();
            Vector3 axis = Vector3.Cross(Vector3.up, push);

            startRot = transform.rotation;
            endRot = Quaternion.AngleAxis(88f, axis) * startRot;
            startPos = transform.position;
            float h = setup != null ? setup.ActorHeight : 1.8f;
            endPos = startPos + push * (h * 0.35f);

            falling = true;
        }

        /// <summary>
        /// Fires the matching death trigger if the actor's controller has one. Returns false when
        /// there is no clip, in which case the caller falls back to tipping the body over — the
        /// game must still read correctly for a character that shipped without death animations.
        /// </summary>
        bool TryPlayDeathClip(DamageInfo info)
        {
            var animator = GetComponentInChildren<Animator>();
            if (animator == null || animator.runtimeAnimatorController == null) return false;

            string wanted = info.headshot ? "DeathHead" : "DeathBody";
            string fallback = info.headshot ? "DeathBody" : "DeathHead";

            string trigger = HasTrigger(animator, wanted) ? wanted
                           : HasTrigger(animator, fallback) ? fallback
                           : null;
            if (trigger == null) return false;

            animator.applyRootMotion = false;
            animator.SetTrigger(trigger);
            return true;
        }

        static bool HasTrigger(Animator animator, string name)
        {
            foreach (var p in animator.parameters)
                if (p.type == AnimatorControllerParameterType.Trigger && p.name == name) return true;
            return false;
        }

        void Update()
        {
            if (!falling) return;
            t += Time.deltaTime / Mathf.Max(0.05f, fallDuration);
            float e = 1f - (1f - Mathf.Clamp01(t)) * (1f - Mathf.Clamp01(t)); // ease-out
            transform.SetPositionAndRotation(Vector3.Lerp(startPos, endPos, e), Quaternion.Slerp(startRot, endRot, e));
            if (t >= 1f) falling = false;
        }
    }
}
