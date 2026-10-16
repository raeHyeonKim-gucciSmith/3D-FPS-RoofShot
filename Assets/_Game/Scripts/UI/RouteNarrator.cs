using UnityEngine;

namespace RooftopContract
{
    /// <summary>
    /// Reads out the target's walk, one line per waypoint.
    ///
    /// The lines are authored by the venue that built the route, not here and not in MissionSet:
    /// the thing that knows there is a bus shelter at the third waypoint is the thing that put a
    /// bus shelter there. Anything else would have the commentary drift away from the map the
    /// first time someone moved a prop.
    ///
    /// Contracts whose target does not walk get no narrator at all — there is nothing to say
    /// about a man standing at a meeting, and a HUD that talks when nothing is happening trains
    /// the player to stop reading it.
    /// </summary>
    public class RouteNarrator : MonoBehaviour
    {
        public VipTarget target;
        [Tooltip("VIP 웨이포인트와 인덱스가 1:1로 대응하는 대사. 빈 칸은 조용히 넘어간다.")]
        public string[] cues = new string[0];
        public float cueDuration = 4.5f;

        void Start()
        {
            if (target == null) target = GetComponent<VipTarget>();
            if (target != null) target.WaypointReached += OnWaypointReached;
        }

        void OnDestroy()
        {
            if (target != null) target.WaypointReached -= OnWaypointReached;
        }

        void OnWaypointReached(int index)
        {
            if (cues == null || index < 0 || index >= cues.Length) return;
            if (string.IsNullOrEmpty(cues[index])) return;

            var feed = SubtitleFeed.Instance;
            if (feed != null) feed.Show(cues[index], cueDuration);
        }
    }
}
