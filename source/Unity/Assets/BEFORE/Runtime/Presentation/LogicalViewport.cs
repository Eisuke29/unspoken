using UnityEngine;

namespace Before.Presentation
{
    /// <summary>
    /// Pure, testable geometry for the 320x180 logical frame and the D-088 landmark hand panel.
    /// No Unity runtime state; every method is deterministic so acceptance tests can verify the
    /// integer-scale Fit (Fix B), the exact camera-name match (Fix C), and the landmark mapping
    /// (mirroring, palm-center, safe border) without a camera or Play Mode.
    /// </summary>
    public static class LogicalViewport
    {
        public const int LogicalWidth = 320;
        public const int LogicalHeight = 180;

        /// <summary>Largest integer scale at which 320x180 still fits inside the screen (min 1).</summary>
        public static int IntegerScale(int screenWidth, int screenHeight)
        {
            float scale = Mathf.Min(screenWidth / (float)LogicalWidth, screenHeight / (float)LogicalHeight);
            return Mathf.Max(1, Mathf.FloorToInt(scale));
        }

        /// <summary>Centered, integer-scaled, void-black-letterboxed viewport rect for the logical frame.</summary>
        public static Rect Viewport(int screenWidth, int screenHeight)
        {
            int s = IntegerScale(screenWidth, screenHeight);
            float w = LogicalWidth * s, h = LogicalHeight * s;
            float x = Mathf.Floor((screenWidth - w) * 0.5f);
            float y = Mathf.Floor((screenHeight - h) * 0.5f);
            return new Rect(x, y, w, h);
        }

        /// <summary>True when the whole logical frame fits inside the screen at the chosen integer scale.</summary>
        public static bool FullyContained(int screenWidth, int screenHeight)
        {
            var v = Viewport(screenWidth, screenHeight);
            return v.x >= 0f && v.y >= 0f && v.xMax <= screenWidth + 0.001f && v.yMax <= screenHeight + 0.001f;
        }

        /// <summary>Exact device-name match; never a substring or fuzzy match (D-067 / Fix C).</summary>
        public static bool MatchesDevice(string deviceName, string preferred)
        {
            return deviceName == preferred;
        }

        /// <summary>Mirror a raw normalized landmark so the participant sees a mirror (D-088).</summary>
        public static Vector2 Mirror(Vector2 raw) => new Vector2(1f - raw.x, raw.y);

        /// <summary>Palm center = mean of landmarks 0,5,9,13,17 (D-088).</summary>
        public static Vector2 PalmCenter(Vector2[] landmarks)
        {
            Vector2 sum = Vector2.zero;
            int[] idx = { 0, 5, 9, 13, 17 };
            foreach (int i in idx) sum += landmarks[i];
            return sum / idx.Length;
        }

        /// <summary>
        /// Map a mirrored-normalized landmark [0,1] directly into panel pixel space, preserving the
        /// in-camera position (no per-frame bounding-box fit). Panel origin is top-left.
        /// </summary>
        public static Vector2 ToPanel(Vector2 mirroredNormalized, int panelWidth, int panelHeight)
        {
            return new Vector2(mirroredNormalized.x * panelWidth, (1f - mirroredNormalized.y) * panelHeight);
        }

        /// <summary>True when the (mirrored-normalized) point lies inside the safe border margin.</summary>
        public static bool NearSafeBorder(Vector2 mirroredNormalized, int panelWidth, int safeMargin)
        {
            float m = safeMargin / (float)panelWidth;
            return mirroredNormalized.x < m || mirroredNormalized.x > 1f - m
                || mirroredNormalized.y < m * 0.75f || mirroredNormalized.y > 1f - m * 0.75f;
        }
    }
}
