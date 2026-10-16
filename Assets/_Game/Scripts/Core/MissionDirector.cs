using System;
using UnityEngine;

namespace RooftopContract
{
    public enum MissionPhase { Prep, Engaged, Defense, Result }
    public enum MissionOutcome { None, Complete, Partial, Kia }

    /// <summary>
    /// Owns the mission state machine and every timer the rest of the game reads from.
    /// Prep -> (first shot) -> Engaged -> (+assaultDelay) -> Defense -> (+survivalDuration) -> Result
    /// Every duration is per-contract and set by the generator from MissionSet; the values on
    /// these fields are only what a hand-placed director would start with.
    /// </summary>
    public class MissionDirector : MonoBehaviour
    {
        public static MissionDirector Instance { get; private set; }

        [Header("Campaign")]
        [Tooltip("0-based. 이 씬이 몇 번째 계약인가.")]
        public int missionIndex;
        [Tooltip("전체 계약 수. HUD 표기와 다음 미션 전환에 쓰인다.")]
        public int missionCount = 3;
        public string codename = "CONTRACT";
        [TextArea(2, 3)] public string brief = "";

        // There is no "next mission" any more. Contracts are picked off a board in whatever
        // order the player likes, so the only thing that follows a mission is the office.

        [Header("Tempo")]
        [Tooltip("0이면 무제한. 그 외에는 이 초가 지나면 표적이 현장을 뜬다 (PREP 중에만 의미 있음).")]
        public float strikeWindow = 0f;
        [Tooltip("첫 사격 이후 돌격 경호대가 들이닥치기까지의 시간")]
        public float assaultDelay = 20f;
        [Tooltip("DEFENSE 페이즈에서 생존해야 하는 시간")]
        public float survivalDuration = 45f;
        [Tooltip("생존 시간이 끝난 뒤 헬기가 들어와 회수하기까지의 시간. 이 동안에도 남은 특공대는 계속 싸운다.")]
        public float extractionDuration = 7f;

        [Header("Runtime (read only)")]
        [SerializeField] MissionPhase phase = MissionPhase.Prep;
        [SerializeField] MissionOutcome outcome = MissionOutcome.None;
        [SerializeField] bool targetEliminated;
        [SerializeField] bool targetEscaped;
        [SerializeField] float phaseClock;

        public MissionPhase Phase => phase;
        public MissionOutcome Outcome => outcome;
        public bool TargetEliminated => targetEliminated;
        /// <summary>The target left the venue alive — boarded the bird, made the doorway.</summary>
        public bool TargetEscaped => targetEscaped;

        /// <summary>
        /// This mission puts a clock on the shot. Only true for contracts whose target is
        /// actively leaving, which is why it is a per-mission field and not a global rule —
        /// a timer on a man standing still on a terrace would be arbitrary.
        /// </summary>
        public bool HasStrikeWindow => strikeWindow > 0f;
        /// <summary>Seconds left to take the shot. Only meaningful during Prep.</summary>
        public float StrikeCountdown => Mathf.Max(0f, strikeWindow - phaseClock);

        /// <summary>Seconds until the assault arrives. Only meaningful during Engaged.</summary>
        public float AssaultCountdown => Mathf.Max(0f, assaultDelay - phaseClock);
        /// <summary>Seconds left to survive. Only meaningful during Defense.</summary>
        public float SurvivalCountdown => Mathf.Max(0f, survivalDuration - phaseClock);

        /// <summary>
        /// The survival clock has run out and the helicopter is inbound. Still MissionPhase.Defense
        /// — the fight does not stop because the timer did, and keeping the phase means nothing
        /// that already switches on Defense/Result has to learn about a fourth state.
        /// </summary>
        public bool IsExtracting => extracting;
        /// <summary>Seconds until the helicopter is overhead and the mission closes.</summary>
        public float ExtractionCountdown => Mathf.Max(0f, extractionDuration - extractionClock);

        public event Action<MissionPhase> PhaseChanged;
        /// <summary>Fired once, on the very first sniper shot. Carries the shot origin so guards know where to face.</summary>
        public event Action<Vector3> FirstShotFired;
        /// <summary>Fired when the first sniper round physically reaches a collider.</summary>
        public event Action<Vector3> FirstSniperImpact;
        public event Action<MissionOutcome> MissionEnded;
        /// <summary>Fired once when the survival clock expires and the exfil bird starts its run.</summary>
        public event Action ExtractionStarted;
        /// <summary>Fired once when the target leaves the venue alive. The VIP listens and removes itself.</summary>
        public event Action TargetGotAway;

        bool firstShotDone;
        bool firstImpactDone;
        bool extracting;
        float extractionClock;

        void Awake()
        {
            Instance = this;
            Time.timeScale = 1f;
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        void Update()
        {
            if (phase == MissionPhase.Result) return;

            phaseClock += Time.deltaTime;

            // The strike window is a Prep-phase rule only. Once the player has fired, the target
            // running for the exit is no longer a clock — it is just the evacuation, and the
            // VIP reaching its goal reports the escape itself.
            if (phase == MissionPhase.Prep && HasStrikeWindow && phaseClock >= strikeWindow)
                ReportTargetEscaped();

            if (phase == MissionPhase.Engaged && phaseClock >= assaultDelay)
                SetPhase(MissionPhase.Defense);
            else if (phase == MissionPhase.Defense && phaseClock >= survivalDuration)
                TickExtraction();
        }

        /// <summary>
        /// Surviving the clock does not end the mission — it calls the helicopter. The player
        /// holds the same spot while it flies in, and the assault that is already on the roof
        /// keeps shooting, so the last seconds are the loudest ones rather than a cut to a
        /// results screen mid-firefight.
        /// </summary>
        void TickExtraction()
        {
            if (!extracting)
            {
                extracting = true;
                ExtractionStarted?.Invoke();
            }

            extractionClock += Time.deltaTime;
            if (extractionClock >= extractionDuration)
                End(targetEliminated ? MissionOutcome.Complete : MissionOutcome.Partial);
        }

        /// <summary>Called by the sniper rig on every shot. Only the first one advances the phase.</summary>
        public void ReportSniperShot(Vector3 shooterPosition)
        {
            if (firstShotDone) return;
            firstShotDone = true;
            FirstShotFired?.Invoke(shooterPosition);
            SetPhase(MissionPhase.Engaged);
        }

        public void ReportSniperImpact(Vector3 shooterPosition)
        {
            if (firstImpactDone) return;
            firstImpactDone = true;
            FirstSniperImpact?.Invoke(shooterPosition);
        }

        public void ReportTargetEliminated()
        {
            if (targetEliminated) return;
            targetEliminated = true;
            // The contract is fulfilled, but the exfil clock still has to run out.
        }

        /// <summary>
        /// The target got out alive — through the doorway, or onto the helicopter.
        ///
        /// If this happens before the player ever fires, nobody knows there was a sniper and
        /// there is nothing to defend against: the job is simply blown, and the mission closes
        /// on the spot. Once a round has gone off the roof is coming for the player regardless,
        /// so the fight runs its full course and the contract just resolves as a partial.
        /// </summary>
        public void ReportTargetEscaped()
        {
            if (targetEscaped || targetEliminated) return;
            targetEscaped = true;
            TargetGotAway?.Invoke();
            if (phase == MissionPhase.Prep) End(MissionOutcome.Partial);
        }

        public void ReportPlayerDead() => End(MissionOutcome.Kia);

        void SetPhase(MissionPhase next)
        {
            if (phase == next) return;
            phase = next;
            phaseClock = 0f;
            PhaseChanged?.Invoke(phase);
        }

        void End(MissionOutcome result)
        {
            if (phase == MissionPhase.Result) return;
            outcome = result;
            SetPhase(MissionPhase.Result);
            // Recorded here rather than on the way out of the results screen, so quitting on
            // the results screen still counts the contract. The board marks the target, not the
            // job — a headshot still crosses the card off even if the roof got the player back.
            CampaignState.ReportMissionEnded(missionIndex, result, targetEliminated);
            MissionEnded?.Invoke(result);
        }
    }
}
