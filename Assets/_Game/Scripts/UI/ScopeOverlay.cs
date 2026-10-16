using UnityEngine;

namespace RooftopContract
{
    /// <summary>
    /// Scope picture: black-out corners, a circular eyepiece and a clean duplex reticle with
    /// elevation holds below centre. Drawn from a single white pixel so it stays sharp at every
    /// browser resolution.
    /// </summary>
    public static class ScopeOverlay
    {
        static Texture2D centreDotTexture;

        public static void Draw(Texture2D px, float w, float h, float magnification)
        {
            float cx = w * 0.5f, cy = h * 0.5f;
            float radius = Mathf.Min(w, h) * 0.42f;

            // Vignette the area outside the eyepiece with concentric bands. Cheap, and it reads
            // as a real scope because the falloff is on the diagonal corners too.
            GUI.color = Color.black;
            float outer = Mathf.Max(w, h);
            int bands = 34;
            for (int i = 0; i < bands; i++)
            {
                float t = i / (float)bands;
                float r = Mathf.Lerp(radius, outer, t);
                float thickness = (outer - radius) / bands + 1.5f;
                Ring(px, cx, cy, r, thickness);
            }

            // Eyepiece rim.
            GUI.color = new Color(0f, 0f, 0f, 0.9f);
            Ring(px, cx, cy, radius, 6f);

            // Heavy outer posts stop short of centre, matching a practical duplex optic.
            GUI.color = new Color(0.015f, 0.015f, 0.015f, 0.98f);
            float heavy = Mathf.Max(3f, radius * 0.010f);
            float innerGap = radius * 0.145f;
            float outerInset = radius * 0.92f;
            GUI.DrawTexture(new Rect(cx - outerInset, cy - heavy * 0.5f,
                outerInset - innerGap, heavy), px);
            GUI.DrawTexture(new Rect(cx + innerGap, cy - heavy * 0.5f,
                outerInset - innerGap, heavy), px);
            GUI.DrawTexture(new Rect(cx - heavy * 0.5f, cy - outerInset,
                heavy, outerInset - innerGap), px);
            GUI.DrawTexture(new Rect(cx - heavy * 0.5f, cy + innerGap,
                heavy, outerInset - innerGap), px);

            // Fine centre cross. The tiny open square keeps the exact point of impact readable.
            float fine = Mathf.Max(1.2f, radius * 0.004f);
            float centreGap = Mathf.Max(4f, radius * 0.018f);
            GUI.DrawTexture(new Rect(cx - innerGap, cy - fine * 0.5f,
                innerGap - centreGap, fine), px);
            GUI.DrawTexture(new Rect(cx + centreGap, cy - fine * 0.5f,
                innerGap - centreGap, fine), px);
            GUI.DrawTexture(new Rect(cx - fine * 0.5f, cy - innerGap,
                fine, innerGap - centreGap), px);
            GUI.DrawTexture(new Rect(cx - fine * 0.5f, cy + centreGap,
                fine, innerGap - centreGap), px);

            // There is deliberately no bullet-drop ladder here any more. The marks that used to
            // sit below centre were spaced by screen radius, so a rung meant a different hold at
            // every magnification and no hold at all in metres — a scale that teaches the player
            // something untrue. The rifle is zeroed to the contract's range instead
            // (SniperRig.zeroRange), which puts the point of impact on the centre cross, and a
            // ladder measuring holds nobody needs would only invite them to aim low.

            // A real antialiased circle instead of stacked rectangles. Fractional IMGUI pixel
            // rounding made the old three-strip approximation visibly lopsided in WebGL.
            float dot = Mathf.Max(5f, radius * 0.016f);
            GUI.DrawTexture(new Rect(cx - dot * 0.5f, cy - dot * 0.5f, dot, dot),
                CentreDotTexture, ScaleMode.StretchToFill, true);

            GUI.color = Color.white;
        }

        static void Ring(Texture2D px, float cx, float cy, float r, float thickness)
        {
            const int segments = 72;
            for (int i = 0; i < segments; i++)
            {
                float a0 = i / (float)segments * Mathf.PI * 2f;
                float a1 = (i + 1) / (float)segments * Mathf.PI * 2f;
                Vector2 p0 = new Vector2(cx + Mathf.Cos(a0) * r, cy + Mathf.Sin(a0) * r);
                Vector2 p1 = new Vector2(cx + Mathf.Cos(a1) * r, cy + Mathf.Sin(a1) * r);
                Line(px, p0, p1, thickness);
            }
        }

        static void Line(Texture2D px, Vector2 a, Vector2 b, float thickness)
        {
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 0.01f) return;
            float angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;

            Matrix4x4 m = GUI.matrix;
            GUIUtility.RotateAroundPivot(angle, a);
            GUI.DrawTexture(new Rect(a.x, a.y - thickness * 0.5f, len, thickness), px);
            GUI.matrix = m;
        }

        static void Dot(Texture2D px, float x, float y, float size)
        {
            GUI.DrawTexture(new Rect(x - size * 0.5f, y - size * 0.5f, size, size), px);
        }

        static Texture2D CentreDotTexture
        {
            get
            {
                if (centreDotTexture != null) return centreDotTexture;

                const int resolution = 32;
                centreDotTexture = new Texture2D(resolution, resolution, TextureFormat.RGBA32,
                    false, true)
                {
                    name = "Scope Centre Dot",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.HideAndDontSave
                };

                var pixels = new Color[resolution * resolution];
                Vector2 centre = new Vector2(resolution * 0.5f, resolution * 0.5f);
                float radius = resolution * 0.43f;
                for (int y = 0; y < resolution; y++)
                for (int x = 0; x < resolution; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), centre);
                    float alpha = 1f - Mathf.SmoothStep(radius - 1.5f, radius + 1.5f, distance);
                    pixels[y * resolution + x] = new Color(1f, 1f, 1f, alpha);
                }

                centreDotTexture.SetPixels(pixels);
                centreDotTexture.Apply(false, true);
                return centreDotTexture;
            }
        }
    }
}
