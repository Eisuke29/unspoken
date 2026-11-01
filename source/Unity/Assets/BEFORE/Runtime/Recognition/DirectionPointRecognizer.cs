using System.Collections.Generic;
using UnityEngine;

namespace Before.Recognition
{
    // D-094 Direction by index-finger POINTING (supersedes the left/right swipe).
    //
    // The player points the index finger to screen-left or -right and holds 0.45 s to commit. Judged from the
    // index MCP(5)->tip(8) vector, a pointing posture (index extended, other fingers curled), lateral
    // dominance (|dx| > |dy|), and 5-of-7 frame stability. After commit no further input is accepted until the
    // hand returns to Neutral (Reset). Exclusive input ownership during the direction beat is enforced by the
    // coordinator (Jump/Stop recognizers are stopped); this class only judges the pointing itself.

    public enum PointDir { None = 0, Left = -1, Right = 1 }

    public sealed class DirectionPointRecognizer
    {
        public sealed class Cfg
        {
            public float QualityMin = 0.65f;
            public int History = 7;
            public int StableNeeded = 5;
            public float HoldToCommit = 0.45f;
            public float ExtensionRatio = 1.25f; // index extension vs mean of the other three fingers
            public float LateralRatio = 1.15f;   // |dx| must exceed |dy| * this
        }

        public readonly Cfg C = new Cfg();
        private readonly List<PointDir> history = new List<PointDir>(8);
        private double heldSince = -1;
        private PointDir stable = PointDir.None;
        private bool committed;
        private PointDir committedDir = PointDir.None;

        public PointDir Stable => stable;
        public bool Committed => committed;
        public PointDir CommittedDir => committedDir;
        /// <summary>Route preview while holding (violet), even before commit.</summary>
        public PointDir Preview => committed ? committedDir : stable;
        public float HoldProgress { get; private set; }

        public void Reset()
        {
            history.Clear(); heldSince = -1; stable = PointDir.None; committed = false; committedDir = PointDir.None; HoldProgress = 0f;
        }

        /// <summary>Classify one frame's pointing direction (None if not a clear lateral point).</summary>
        public static PointDir Classify(Vector2[] lm, Cfg c)
        {
            if (lm == null || lm.Length < 21) return PointDir.None;
            float pw = GestureV2Builder.PalmWidth(lm);
            float idx = Vector2.Distance(lm[8], lm[0]) / pw;
            float mid = Vector2.Distance(lm[12], lm[0]) / pw;
            float ring = Vector2.Distance(lm[16], lm[0]) / pw;
            float pinky = Vector2.Distance(lm[20], lm[0]) / pw;
            float othersMean = (mid + ring + pinky) / 3f;
            bool pointing = idx > othersMean * c.ExtensionRatio;
            Vector2 v = lm[8] - lm[5]; // index MCP -> tip
            bool lateral = Mathf.Abs(v.x) > Mathf.Abs(v.y) * c.LateralRatio;
            if (!pointing || !lateral) return PointDir.None;
            return v.x < 0f ? PointDir.Left : PointDir.Right; // mirrored screen coords: smaller x = screen-left
        }

        /// <summary>Advance one frame; returns true on the frame a direction commits.</summary>
        public bool Step(PcmFrame f)
        {
            PointDir dir = (f.Valid && f.Quality >= C.QualityMin) ? Classify(f.Lm, C) : PointDir.None;
            history.Add(dir);
            while (history.Count > C.History) history.RemoveAt(0);

            int left = 0, right = 0;
            for (int i = 0; i < history.Count; i++)
            {
                if (history[i] == PointDir.Left) left++;
                else if (history[i] == PointDir.Right) right++;
            }
            PointDir newStable = left >= C.StableNeeded ? PointDir.Left : (right >= C.StableNeeded ? PointDir.Right : PointDir.None);

            if (committed) { stable = newStable; return false; } // locked until Reset

            if (newStable != PointDir.None)
            {
                if (heldSince < 0 || newStable != stable) heldSince = f.Time;
                stable = newStable;
                HoldProgress = Mathf.Clamp01((float)((f.Time - heldSince) / C.HoldToCommit));
                if (f.Time - heldSince >= C.HoldToCommit)
                {
                    committed = true; committedDir = stable; HoldProgress = 1f;
                    return true;
                }
            }
            else { stable = PointDir.None; heldSince = -1; HoldProgress = 0f; }
            return false;
        }
    }
}
