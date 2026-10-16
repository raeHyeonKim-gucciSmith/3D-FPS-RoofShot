using UnityEngine;

namespace RooftopContract
{
    /// <summary>
    /// Bundled Korean-capable font. Web builds cannot fall back to fonts installed on the
    /// player's operating system, so every IMGUI screen uses this project resource explicitly.
    /// </summary>
    public static class UIFontProvider
    {
        static Font korean;
        public static Font Korean => korean != null ? korean : korean = Resources.Load<Font>("NotoSansKR");

        public static GUIStyle Style(GUIStyle source)
        {
            var style = new GUIStyle(source);
            if (Korean != null) style.font = Korean;
            return style;
        }
    }
}
