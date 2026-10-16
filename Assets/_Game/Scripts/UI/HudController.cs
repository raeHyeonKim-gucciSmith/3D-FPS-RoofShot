using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace RooftopContract
{
    /// <summary>
    /// The whole HUD, drawn with IMGUI. That is a deliberate choice for this build:
    /// zero prefab wiring, zero canvas setup, and it survives any scene rebuild.
    /// Swap it for uGUI later without touching a single gameplay script.
    /// </summary>
    public class HudController : MonoBehaviour
    {
        public SniperRig sniper;
        public PistolRig pistol;
        public PlayerRoot player;
        [Tooltip("걷는 표적의 남은 시간을 읽기 위한 참조. 정지 표적 계약에서는 비어 있어도 된다.")]
        public VipTarget target;

        [Header("Style")]
        public Color accent = new Color(1f, 0.78f, 0.25f);
        public Color danger = new Color(0.95f, 0.28f, 0.24f);
        public Color ink = new Color(0.92f, 0.94f, 0.95f);

        float hitMarkerTime = -1f;
        bool lastHitWasHead;
        float damageFlash;
        Texture2D px;
        GUIStyle label, big, huge, center, control, assaultWarning, pauseButton;
        bool pauseMenuOpen;

        void Awake()
        {
            px = new Texture2D(1, 1);
            px.SetPixel(0, 0, Color.white);
            px.Apply();
            HitFeedback.OnPlayerHitConfirmed += OnHitConfirmed;
        }

        void Start()
        {
            if (player != null && player.health != null)
                player.health.Damaged += _ => damageFlash = 1f;
        }

        void OnDestroy() => HitFeedback.OnPlayerHitConfirmed -= OnHitConfirmed;

        void OnDisable()
        {
            // Never leave the editor or the next scene frozen if this object is disabled
            // while the pause menu is open.
            Time.timeScale = 1f;
        }

        void OnHitConfirmed(bool head)
        {
            hitMarkerTime = Time.time;
            lastHitWasHead = head;
        }

        void Update()
        {
            if (damageFlash > 0f) damageFlash -= Time.deltaTime * 1.6f;

            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.escapeKey.wasPressedThisFrame)
            {
                var activeDirector = MissionDirector.Instance;
                if (activeDirector != null && activeDirector.Phase != MissionPhase.Result)
                    SetPauseMenu(!pauseMenuOpen);
            }

            var director = MissionDirector.Instance;
            if (director == null || director.Phase != MissionPhase.Result) return;

            if (kb.rKey.wasPressedThisFrame)
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);

            // Every ending goes back to the office, including a dead one. The campaign loop is
            // office → shot → hold → exfil → office, and the office is where the player finds
            // out what the contract cost them and what the next one is. Which contract that
            // turns out to be is CampaignState's call, not this screen's — a K.I.A. leaves the
            // same job on the desk, anything else moves on.
            if (kb.spaceKey.wasPressedThisFrame)
                SceneManager.LoadScene(MissionSet.OfficeSceneName);
        }

        void EnsureStyles()
        {
            if (label != null) return;
            label = UIFontProvider.Style(GUI.skin.label); label.fontSize = 16; label.fontStyle = FontStyle.Bold;
            label.clipping = TextClipping.Overflow; label.wordWrap = false;
            big = UIFontProvider.Style(GUI.skin.label); big.fontSize = 26; big.fontStyle = FontStyle.Bold;
            big.clipping = TextClipping.Overflow; big.wordWrap = false;
            huge = UIFontProvider.Style(GUI.skin.label); huge.fontSize = 54; huge.fontStyle = FontStyle.Bold;
            huge.clipping = TextClipping.Overflow; huge.wordWrap = false;
            huge.alignment = TextAnchor.MiddleCenter;
            center = UIFontProvider.Style(GUI.skin.label); center.fontSize = 20;
            center.clipping = TextClipping.Overflow; center.wordWrap = false;
            center.alignment = TextAnchor.MiddleCenter;
            control = new GUIStyle(center); control.fontStyle = FontStyle.Bold;
            assaultWarning = new GUIStyle(center); assaultWarning.fontSize = 22;
            assaultWarning.fontStyle = FontStyle.Bold;
            pauseButton = UIFontProvider.Style(GUI.skin.button);
            pauseButton.fontSize = 18;
            pauseButton.fontStyle = FontStyle.Bold;
            pauseButton.alignment = TextAnchor.MiddleCenter;
            pauseButton.clipping = TextClipping.Overflow;
            pauseButton.wordWrap = false;
        }

        void OnGUI()
        {
            EnsureStyles();
            var director = MissionDirector.Instance;
            float w = Screen.width, h = Screen.height;

            if (damageFlash > 0f)
            {
                GUI.color = new Color(danger.r, danger.g, danger.b, damageFlash * 0.35f);
                GUI.DrawTexture(new Rect(0, 0, w, h), px);
                GUI.color = Color.white;
            }

            if (director == null) return;

            switch (director.Phase)
            {
                case MissionPhase.Prep:
                case MissionPhase.Engaged:
                    DrawSniperHud(director, w, h);
                    break;
                case MissionPhase.Defense:
                    DrawDefenseHud(director, w, h);
                    break;
                case MissionPhase.Result:
                    DrawResult(director, w, h);
                    return;
            }

            DrawHitMarker(w, h);

            if (pauseMenuOpen)
                DrawPauseMenu(w, h);
        }

        void SetPauseMenu(bool open)
        {
            pauseMenuOpen = open;
            Time.timeScale = open ? 0f : 1f;
            Cursor.lockState = open ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = open;
        }

        void DrawPauseMenu(float w, float h)
        {
            GUI.color = new Color(0f, 0f, 0f, 0.82f);
            GUI.DrawTexture(new Rect(0f, 0f, w, h), px);

            GUI.color = ink;
            GUI.Label(new Rect(0f, h * 0.25f, w, 70f), "MISSION PAUSED", huge);
            GUI.color = new Color(1f, 1f, 1f, 0.62f);
            GUI.Label(new Rect(0f, h * 0.35f, w, 30f), "진행 중인 미션을 계속하거나 포기할 수 있습니다.", center);

            float buttonWidth = Mathf.Min(360f, w - 48f);
            float x = (w - buttonWidth) * 0.5f;
            var resumeRect = new Rect(x, h * 0.46f, buttonWidth, 54f);
            var abandonRect = new Rect(x, h * 0.46f + 70f, buttonWidth, 54f);

            GUI.color = Color.white;
            if (GUI.Button(resumeRect, "게임으로 돌아가기  (ESC)", pauseButton))
                SetPauseMenu(false);

            GUI.color = new Color(1f, 0.62f, 0.58f);
            if (GUI.Button(abandonRect, "미션 포기  ·  사무실로 복귀", pauseButton))
            {
                // Do not call MissionDirector.End: abandoning must not create a result,
                // debrief, or target-eliminated campaign flag.
                SetPauseMenu(false);
                SceneManager.LoadScene(MissionSet.OfficeSceneName);
            }
            GUI.color = Color.white;
        }

        void DrawSniperHud(MissionDirector d, float w, float h)
        {
            if (sniper == null) return;

            // The rifle has no hip-fire reticle. Its aiming mark only exists through the scope;
            // the pistol keeps its separate iron cross during the defense phase.
            if (sniper.IsScoped)
            {
                ScopeOverlay.Draw(px, w, h, sniper.Magnification);
                GUI.color = new Color(0.70f, 0.88f, 1f, 0.90f);
                GUI.Label(new Rect(12f, h * 0.65f, w - 24f, 40f),
                    "HOLD SHIFT TO HOLD BREATH", control);
            }

            // Bottom-left: ammo + magnification.
            GUI.color = ink;
            GUI.Label(new Rect(34, h - 101, 400, 44), $"ROUNDS  {sniper.AmmoLeft} / {sniper.magazine}", big);
            GUI.color = accent;
            GUI.Label(new Rect(34, h - 64, Mathf.Min(460f, w - 68f), 36),
                $"{sniper.Magnification:0.#}x · RMB TO CHANGE ZOOM", label);

            // Bolt cycle bar — tells the player when the next shot is available.
            if (sniper.BoltProgress < 1f)
            {
                Bar(new Rect(34, h - 30, 220, 5), sniper.BoltProgress, new Color(1, 1, 1, 0.18f), accent);
            }

            // Bottom-right: range readout, the honest way to deal with bullet drop.
            float range = sniper.RangeToCrosshair();
            GUI.color = ink;
            string rangeText = range > 1f ? $"{range:0} m" : "-- m";
            GUI.Label(new Rect(w - 220, h - 101, 200, 44), rangeText, big);
            GUI.color = new Color(1, 1, 1, 0.55f);
            GUI.Label(new Rect(w - 260, h - 64, 240, 36), "TARGET RANGE", label);

            // Centre warning: after the first shot this is the next decision the player is
            // waiting for, so keep it in the sight picture rather than at the screen edge.
            if (d.Phase == MissionPhase.Engaged)
            {
                GUI.color = danger;
                string t = $"ASSAULT TEAM ARRIVES IN {Mathf.CeilToInt(d.AssaultCountdown)}s";
                GUI.Label(new Rect(12f, h * 0.34f - 4f, w - 24f, 42f), t, assaultWarning);
                GUI.color = new Color(1, 1, 1, 0.7f);
                GUI.Label(new Rect(12f, 14f, w - 24f, 40f),
                    d.TargetEliminated ? "TARGET DOWN — PREPARE FOR ASSAULT" : "TARGET IN COVER — HEADSHOTS ONLY", center);
            }
            else
            {
                GUI.color = accent;
                GUI.Label(new Rect(0, 22, w, 30),
                    $"CONTRACT {d.missionIndex + 1} / {d.missionCount}  ·  {d.codename}", center);
                GUI.color = new Color(1, 1, 1, 0.6f);
                if (!string.IsNullOrEmpty(d.brief))
                    GUI.Label(new Rect(0, 48, w, 26), d.brief, center);
                // Only contracts whose target is actively leaving carry a clock, and there are
                // two ways to have one: a mission-authored strike window, or a target physically
                // walking off the map. The smaller wins, because either can end the job and the
                // player should be told about whichever comes first.
                //
                // This is a window, not a wait. It is deliberately not "target arrives in 3s" —
                // he is shootable the entire way in, and a number counting down to a moment
                // would tell the player to hold their shot until the worst possible instant.
                // While he is still walking in from off to one side there is nothing to shoot at
                // and nothing to decide, so the number counts down to his arrival. Once he is in
                // play it flips to what is left of the window, which is the number that should
                // make the player hurry.
                if (target != null && target.IsApproaching)
                {
                    GUI.color = accent;
                    GUI.Label(new Rect(0, h - 150, w, 40),
                        $"TARGET ENTERS RANGE IN {Mathf.CeilToInt(target.SecondsUntilInPlay)}s", center);
                }
                else if (d.HasStrikeWindow)
                {
                    // Only a mission-authored strike window puts a clock here. A target simply
                    // walking to a door is NOT one: he is shootable the whole way, and a running
                    // countdown over that turns a stretch of hunting into a stretch of watching
                    // a number. The walk announces itself through the subtitles instead.
                    float left = d.StrikeCountdown;
                    GUI.color = left <= 15f ? danger : accent;
                    GUI.Label(new Rect(0, h - 150, w, 40),
                        $"TARGET LEAVES IN  {Mathf.CeilToInt(left)}s", center);
                }
            }
            GUI.color = Color.white;
        }

        void DrawDefenseHud(MissionDirector d, float w, float h)
        {
            DrawIronCross(w, h, pistol != null ? pistol.AimBloom : 0f);

            if (d.IsExtracting)
            {
                // The clock is done; what matters now is that the bird is on its way. Pulsed so
                // it reads as a state change in peripheral vision during a firefight.
                float pulse = 0.65f + 0.35f * Mathf.Sin(Time.time * 6f);
                GUI.color = new Color(accent.r, accent.g, accent.b, pulse);
                var extractionTitle = new GUIStyle(huge)
                {
                    fontSize = Mathf.Min(huge.fontSize, Mathf.Max(28, Mathf.FloorToInt(w / 18f)))
                };
                GUI.Label(new Rect(12f, 18f, w - 24f, 72f), "EXTRACTION IN PROGRESS", extractionTitle);
                GUI.color = new Color(1, 1, 1, 0.65f);
                GUI.Label(new Rect(12f, 84f, w - 24f, 38f), "HELICOPTER INBOUND · HOLD POSITION", center);
            }
            else
            {
                GUI.color = accent;
                GUI.Label(new Rect(12f, 18f, w - 24f, 72f), $"{Mathf.CeilToInt(d.SurvivalCountdown)}", huge);
                GUI.color = new Color(1, 1, 1, 0.65f);
                GUI.Label(new Rect(12f, 84f, w - 24f, 38f), "SECONDS UNTIL EXTRACTION", center);
            }

            if (pistol != null)
            {
                GUI.color = pistol.AmmoInMag == 0 ? danger : ink;
                GUI.Label(new Rect(34, h - 101, 400, 44),
                    pistol.IsReloading ? "RELOADING" : $"{pistol.AmmoInMag} / {pistol.magazineSize}", big);
                if (pistol.IsReloading) Bar(new Rect(34, h - 56, 200, 5), pistol.ReloadProgress, new Color(1, 1, 1, 0.18f), accent);
                else
                {
                    GUI.color = new Color(1, 1, 1, 0.55f);
                    GUI.Label(new Rect(34, h - 68, Mathf.Min(620f, w - 68f), 38),
                        "R RELOAD · A/D MOVE · HOLD RMB TO TAKE COVER", label);
                }
            }

            if (player != null && player.health != null)
            {
                float n = player.health.Normalized;
                GUI.color = ink;
                GUI.Label(new Rect(w - 240, h - 101, 200, 44), $"{Mathf.CeilToInt(player.health.Health)}", big);
                Bar(new Rect(w - 240, h - 56, 200, 6), n, new Color(1, 1, 1, 0.18f),
                    Color.Lerp(danger, new Color(0.4f, 0.85f, 0.5f), n));
            }

            if (player != null && !player.IsPeeking)
            {
                GUI.color = new Color(0.55f, 0.85f, 1f, 0.75f);
                GUI.Label(new Rect(12f, h * 0.68f - 5f, w - 24f, 40f), "RELEASE RMB TO STAND", control);
            }
            else if (player != null)
            {
                GUI.color = new Color(0.55f, 0.85f, 1f, 0.75f);
                GUI.Label(new Rect(12f, h * 0.68f - 5f, w - 24f, 40f), "HOLD RMB TO TAKE COVER", control);
            }
            GUI.color = Color.white;
        }

        void DrawResult(MissionDirector d, float w, float h)
        {
            GUI.color = new Color(0, 0, 0, 0.72f);
            GUI.DrawTexture(new Rect(0, 0, w, h), px);

            // "All clear" is about the board, not about this being the highest-numbered
            // contract — the player picks jobs in any order, so finishing mission 2 last is
            // just as much the end of the campaign as finishing mission 3 last.
            bool boardClear = CampaignState.CampaignComplete(d.missionCount);
            string title, sub;
            switch (d.Outcome)
            {
                case MissionOutcome.Complete:
                    title = boardClear ? "ALL CONTRACTS CLEARED" : "CONTRACT COMPLETE";
                    sub = boardClear ? "All targets eliminated."
                                     : "Target eliminated. Extraction successful.";
                    GUI.color = accent; break;
                case MissionOutcome.Partial:
                    title = "CONTRACT FAILED";
                    sub = d.TargetEscaped
                        ? "Target escaped."
                        : "Extraction successful. Target survived.";
                    GUI.color = ink; break;
                default:
                    title = "K.I.A."; sub = "You were killed before extraction."; GUI.color = danger; break;
            }

            GUI.Label(new Rect(0, h * 0.36f, w, 70), title, huge);
            GUI.color = new Color(1, 1, 1, 0.7f);
            GUI.Label(new Rect(24, h * 0.46f, w - 48f, 40), sub, center);

            GUI.color = new Color(1, 1, 1, 0.45f);
            GUI.Label(new Rect(0, h * 0.53f, w, 26),
                $"CONTRACT {d.missionIndex + 1} / {d.missionCount}  ·  {d.codename}", center);

            GUI.color = accent;
            GUI.Label(new Rect(12, h * 0.62f, w - 24f, 32), "PRESS SPACE TO RETURN TO OFFICE", control);
            GUI.color = new Color(1, 1, 1, 0.45f);
            GUI.Label(new Rect(12, h * 0.67f, w - 24f, 30), "PRESS R TO REPLAY CONTRACT", control);
            GUI.color = Color.white;
        }

        void DrawIronCross(float w, float h, float bloom = 0f)
        {
            float cx = w * 0.5f, cy = h * 0.5f;
            float gap = 7f + Mathf.Clamp01(bloom) * 22f;
            float len = 11f, t = 2f;
            GUI.color = new Color(1, 1, 1, 0.85f);
            GUI.DrawTexture(new Rect(cx - gap - len, cy - t * 0.5f, len, t), px);
            GUI.DrawTexture(new Rect(cx + gap, cy - t * 0.5f, len, t), px);
            GUI.DrawTexture(new Rect(cx - t * 0.5f, cy - gap - len, t, len), px);
            GUI.DrawTexture(new Rect(cx - t * 0.5f, cy + gap, t, len), px);
            GUI.color = Color.white;
        }

        void DrawHitMarker(float w, float h)
        {
            if (hitMarkerTime < 0f) return;
            float age = Time.time - hitMarkerTime;
            if (age > 0.35f) return;

            float a = 1f - age / 0.35f;
            GUI.color = lastHitWasHead ? new Color(1f, 0.35f, 0.3f, a) : new Color(1f, 1f, 1f, a);
            float cx = w * 0.5f, cy = h * 0.5f;
            float d = 9f + (1f - a) * 6f, len = 8f, t = 2f;
            DrawRotatedTick(cx - d, cy - d, len, t, 45f);
            DrawRotatedTick(cx + d, cy - d, len, t, -45f);
            DrawRotatedTick(cx - d, cy + d, len, t, -45f);
            DrawRotatedTick(cx + d, cy + d, len, t, 45f);
            GUI.color = Color.white;
        }

        void DrawRotatedTick(float x, float y, float len, float t, float angle)
        {
            Matrix4x4 m = GUI.matrix;
            GUIUtility.RotateAroundPivot(angle, new Vector2(x, y));
            GUI.DrawTexture(new Rect(x - len * 0.5f, y - t * 0.5f, len, t), px);
            GUI.matrix = m;
        }

        void Bar(Rect r, float fill, Color back, Color front)
        {
            GUI.color = back;
            GUI.DrawTexture(r, px);
            GUI.color = front;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width * Mathf.Clamp01(fill), r.height), px);
            GUI.color = Color.white;
        }
    }
}
