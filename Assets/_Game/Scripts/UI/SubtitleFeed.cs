using System.Collections.Generic;
using UnityEngine;

namespace RooftopContract
{
    /// <summary>
    /// One line of radio traffic at the bottom of the screen, fading in and out.
    ///
    /// It exists because two of the three contracts open with the target walking twenty-odd
    /// metres before anything happens, and through an 8x scope at 300m a player who does not
    /// already know what they are looking at sees a still frame. The dead time was not too long,
    /// it was unnarrated.
    ///
    /// IMGUI and a static instance, matching HudController: no canvas, no prefab wiring, and it
    /// survives a scene rebuild.
    /// </summary>
    public class SubtitleFeed : MonoBehaviour
    {
        public static SubtitleFeed Instance { get; private set; }

        [Header("Timing")]
        public float fadeTime = 0.4f;
        public float defaultDuration = 4.5f;
        [Tooltip("이전 줄이 사라지기 전에 새 줄이 오면 큐에 쌓는다. 겹쳐 쓰면 둘 다 못 읽는다.")]
        public int maxQueued = 3;

        [Header("Style")]
        public Color tint = new Color(0.86f, 0.90f, 0.96f);
        [Tooltip("화면 세로 위치 비율. 0.76 이면 탄약 표시 위, 조준선 아래.")]
        [Range(0.5f, 0.95f)] public float screenY = 0.76f;

        readonly Queue<(string text, float duration)> pending = new();
        string current;
        float clock;
        float duration;
        Texture2D px;
        GUIStyle style;

        void Awake()
        {
            Instance = this;
            px = new Texture2D(1, 1);
            px.SetPixel(0, 0, Color.white);
            px.Apply();
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        public void Show(string text, float seconds = -1f)
        {
            if (string.IsNullOrEmpty(text)) return;
            float d = seconds > 0f ? seconds : defaultDuration;

            if (current == null) { Begin(text, d); return; }
            if (pending.Count < maxQueued) pending.Enqueue((text, d));
        }

        public void Clear()
        {
            pending.Clear();
            current = null;
        }

        void Begin(string text, float d)
        {
            current = text;
            duration = d;
            clock = 0f;
        }

        void Update()
        {
            if (current == null)
            {
                if (pending.Count > 0) { var next = pending.Dequeue(); Begin(next.text, next.duration); }
                return;
            }

            clock += Time.deltaTime;
            if (clock >= duration) current = null;
        }

        /// <summary>Fade in over fadeTime, hold, fade out over fadeTime.</summary>
        float Alpha()
        {
            if (current == null) return 0f;
            if (clock < fadeTime) return Mathf.Clamp01(clock / fadeTime);
            float remaining = duration - clock;
            if (remaining < fadeTime) return Mathf.Clamp01(remaining / fadeTime);
            return 1f;
        }

        void OnGUI()
        {
            if (current == null) return;

            if (style == null)
            {
                style = UIFontProvider.Style(GUI.skin.label);
                style.fontSize = 19;
                style.alignment = TextAnchor.MiddleCenter;
                style.wordWrap = true;
            }

            float a = Alpha();
            float w = Screen.width, h = Screen.height;
            float boxW = Mathf.Min(880f, w - 40f);
            float textW = boxW - 28f;
            float boxH = Mathf.Max(40f, style.CalcHeight(new GUIContent(current), textW) + 10f);
            var r = new Rect((w - boxW) * 0.5f, h * screenY, boxW, boxH);

            // A dark plate behind the text. At 8x the scope view is bright sky and pale concrete,
            // and light type on it is unreadable without something to sit on.
            GUI.color = new Color(0f, 0f, 0f, 0.5f * a);
            GUI.DrawTexture(r, px);
            GUI.color = new Color(tint.r, tint.g, tint.b, 0.25f * a);
            GUI.DrawTexture(new Rect(r.x, r.y, 2f, r.height), px);

            GUI.color = new Color(tint.r, tint.g, tint.b, a);
            GUI.Label(new Rect(r.x + 14f, r.y + 5f, textW, r.height - 10f), current, style);
            GUI.color = Color.white;
        }
    }
}
