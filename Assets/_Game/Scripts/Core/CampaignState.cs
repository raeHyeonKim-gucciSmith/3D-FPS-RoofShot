using UnityEngine;

namespace RooftopContract
{
    /// <summary>
    /// Which targets are down, and how the last job went.
    ///
    /// Contracts are not a queue. The player picks any of the three off the board, in any order,
    /// as many times as they like — so the campaign's state is not "how far did I get", it is a
    /// set of flags: is this target dead yet. A contract you walked away from is simply still on
    /// the board, which is the whole reason failing one does not need a retry prompt.
    ///
    /// This has to survive a scene load, and it is backed by PlayerPrefs rather than a
    /// DontDestroyOnLoad singleton for one practical reason: every scene in this project is
    /// regenerated from the editor menu and then entered directly with Play. A singleton that
    /// only exists if you started from the office would leave Mission2.unity unplayable on its
    /// own during development, which is exactly how this game gets tested. PlayerPrefs works no
    /// matter which scene you press Play in, and it also means quitting mid-campaign resumes.
    ///
    /// Nothing here is authoritative during a mission — the running MissionDirector owns that.
    /// This is only the handoff between scenes.
    /// </summary>
    public static class CampaignState
    {
        const string EliminatedPrefix = "rc.down.";
        const string CursorKey = "rc.cursor";
        const string OutcomeKey = "rc.lastOutcome";
        const string LastIndexKey = "rc.lastIndex";
        const string DebriefKey = "rc.hasDebrief";

        /// <summary>Is this contract's target confirmed dead.</summary>
        public static bool IsEliminated(int missionIndex) =>
            PlayerPrefs.GetInt(EliminatedPrefix + missionIndex, 0) != 0;

        /// <summary>Which card the board should open on. Follows the last job the player took.</summary>
        public static int Cursor
        {
            get => Mathf.Max(0, PlayerPrefs.GetInt(CursorKey, 0));
            set { PlayerPrefs.SetInt(CursorKey, Mathf.Max(0, value)); PlayerPrefs.Save(); }
        }

        public static MissionOutcome LastOutcome => (MissionOutcome)PlayerPrefs.GetInt(OutcomeKey, 0);
        public static int LastMissionIndex => PlayerPrefs.GetInt(LastIndexKey, 0);

        /// <summary>
        /// True when the office should open on a debrief of the mission just finished rather
        /// than straight onto the board. Cleared as soon as the player reads it.
        /// </summary>
        public static bool HasDebrief
        {
            get => PlayerPrefs.GetInt(DebriefKey, 0) != 0;
            private set { PlayerPrefs.SetInt(DebriefKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        /// <summary>
        /// Called when the player leaves a finished mission.
        ///
        /// The board marks the *target*, not the job: <paramref name="targetEliminated"/> is what
        /// crosses a card off, even if the player did not make it off the roof afterwards. Dying
        /// costs nothing here because every contract stays selectable regardless — there is no
        /// state a bad run can strand the player in.
        /// </summary>
        public static void ReportMissionEnded(int missionIndex, MissionOutcome outcome, bool targetEliminated)
        {
            if (targetEliminated) PlayerPrefs.SetInt(EliminatedPrefix + missionIndex, 1);
            PlayerPrefs.SetInt(OutcomeKey, (int)outcome);
            PlayerPrefs.SetInt(LastIndexKey, missionIndex);
            PlayerPrefs.SetInt(CursorKey, missionIndex);
            HasDebrief = true;   // saves
        }

        public static void ConsumeDebrief() => HasDebrief = false;

        public static int EliminatedCount(int missionCount)
        {
            int n = 0;
            for (int i = 0; i < missionCount; i++) if (IsEliminated(i)) n++;
            return n;
        }

        public static bool CampaignComplete(int missionCount) =>
            missionCount > 0 && EliminatedCount(missionCount) >= missionCount;

        public static void ResetCampaign(int missionCount)
        {
            for (int i = 0; i < Mathf.Max(missionCount, 16); i++)
                PlayerPrefs.DeleteKey(EliminatedPrefix + i);
            PlayerPrefs.SetInt(OutcomeKey, (int)MissionOutcome.None);
            PlayerPrefs.SetInt(CursorKey, 0);
            PlayerPrefs.SetInt(LastIndexKey, 0);
            HasDebrief = false;
        }
    }
}
