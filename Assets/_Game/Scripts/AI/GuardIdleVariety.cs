using UnityEngine;

namespace RooftopContract
{
    /// <summary>
    /// Breaks up the standing pose so a guard detail does not read as four copies of one loop.
    ///
    /// The detail's default loop is the weapon-holding scan — a guard doing their job, and the
    /// only stance a rifle sits in convincingly (the plain idle is attention, arms at the sides).
    /// Exactly one of them is also allowed to check their shoe, which is a guard *not* doing
    /// their job. Giving that to a single person makes it read as a character trait; giving it to
    /// everyone would make the whole detail look asleep, and this is the roof the player is about
    /// to shoot at.
    ///
    /// IdleLook is still honoured if a controller exposes it, which happens only when there is no
    /// scan clip to use as the base loop. Normally the base loop *is* the scan.
    ///
    /// All of it stops the moment the first round lands. After that nobody is loitering.
    /// </summary>
    [RequireComponent(typeof(ProtectorGuard))]
    public class GuardIdleVariety : MonoBehaviour
    {
        [Tooltip("이 경호원만 가끔 발을 확인한다. 한 명에게만 켠다.")]
        public bool allowSlackIdle;

        [Tooltip("변형 동작 사이의 간격 (초)")]
        public Vector2 interval = new Vector2(7f, 16f);

        [Tooltip("농땡이 동작이 나올 확률 (allowSlackIdle 인 경호원에 한해)")]
        [Range(0f, 1f)] public float slackChance = 0.35f;

        ProtectorGuard guard;
        Animator animator;
        UnityEngine.AI.NavMeshAgent agent;
        float timer;
        bool hasLook, hasSlack;

        void Awake()
        {
            guard = GetComponent<ProtectorGuard>();
            animator = GetComponentInChildren<Animator>();
            agent = GetComponent<UnityEngine.AI.NavMeshAgent>();
            timer = Random.Range(interval.x, interval.y);

            if (animator != null)
            {
                foreach (var p in animator.parameters)
                {
                    if (p.type != AnimatorControllerParameterType.Trigger) continue;
                    if (p.name == "IdleLook") hasLook = true;
                    if (p.name == "IdleSlack") hasSlack = true;
                }
            }
        }

        void Update()
        {
            if (animator == null || guard == null) return;
            if (guard.Alerted || guard.Health.IsDead) return;

            // Only while actually standing still — a variation played mid-stride looks broken.
            bool moving = agent != null && agent.velocity.sqrMagnitude > 0.05f;
            if (moving) return;

            timer -= Time.deltaTime;
            if (timer > 0f) return;
            timer = Random.Range(interval.x, interval.y);

            bool slack = allowSlackIdle && hasSlack && Random.value < slackChance;
            if (slack) animator.SetTrigger("IdleSlack");
            else if (hasLook) animator.SetTrigger("IdleLook");
        }
    }
}
