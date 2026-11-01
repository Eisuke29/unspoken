using System.Collections.Generic;
using UnityEngine;

namespace Before.Recognition
{
    // D-095 Stop as a user-defined STATIC hand-sign (separate from the D-092 dynamic compound Jump).
    //
    // Teaching: 3 samples, each holding the SAME hand-shape while Space is held for >= 0.8 s. A Stop shape
    // too close to the Jump start-pose does not confirm (the story asks for a different shape).
    //
    // Live: the sign becomes "active" (NUL stops) after 5-of-7 frames match AND a 0.45 s hold. NUL keeps
    // stopping while the shape is held. It releases only after a valid hand is NOT the shape for >= 0.35 s;
    // a transient mismatch < 0.35 s is ignored; hand loss / low quality / tracking loss are NOT a release.
    // (Autonomous "wait until the gate is safe even after release" is a planner concern, not this recognizer.)

    public enum StaticState { Neutral, Candidate, Held, Releasing }

    public sealed class StaticSignRecognizer
    {
        public sealed class Cfg
        {
            public float QualityMin = 0.65f;
            public int History = 7;
            public int StableNeeded = 5;      // 5 of last 7 frames match
            public float MatchNormMax = 1.35f; // normalized pose distance ceiling to count as a match
            public float HoldToStart = 0.45f;  // seconds of holding before Stop activates
            public float ReleaseGrace = 0.35f; // seconds NOT matching (with a valid hand) before release
            public float TeachMinHold = 0.80f;  // each teaching sample must hold >= 0.8 s
            public float SeparationMinNorm = 0.6f; // Stop must differ from Jump start-pose by at least this
        }

        public readonly Cfg C = new Cfg();
        public readonly PoseModel Model = new PoseModel();

        private readonly List<(bool match, bool valid)> history = new List<(bool, bool)>(8);
        private StaticState state = StaticState.Neutral;
        private double heldSince = -1;      // when the current match run began
        private double mismatchSince = -1;  // when a valid non-matching run began
        public StaticState State => state;
        public bool Active => state == StaticState.Held || state == StaticState.Releasing;
        public bool IsConfirmed => Model.Samples.Count >= 3;

        public enum TeachResult { AcceptedNeedMore, Confirmed, TooShort, LowQuality, TooSimilarToJump }

        /// <summary>Add one held-shape teaching sample. <paramref name="jumpPose"/> (may be null) is the Jump
        /// start-pose prototype; a Stop shape too close to it is rejected.</summary>
        public TeachResult SubmitHold(IReadOnlyList<PcmFrame> frames, PoseDescriptor jumpPose)
        {
            if (frames == null || frames.Count < 5) return TeachResult.TooShort;
            double total = frames[frames.Count - 1].Time - frames[0].Time;
            if (total < C.TeachMinHold) return TeachResult.TooShort;
            for (int i = 0; i < frames.Count; i++)
                if (!frames[i].Valid || frames[i].Lm == null || frames[i].Quality < C.QualityMin) return TeachResult.LowQuality;

            PoseDescriptor pose = MedoidPose(frames);
            // reject a Stop shape that is too close to the taught Jump start-pose
            if (jumpPose != null)
            {
                float sep = PoseDescriptor.Distance(pose, jumpPose);
                if (sep < C.SeparationMinNorm * 0.1f) return TeachResult.TooSimilarToJump; // raw-distance guard
            }
            Model.AddSample(pose);
            return IsConfirmed ? TeachResult.Confirmed : TeachResult.AcceptedNeedMore;
        }

        public void ResetLive()
        {
            history.Clear(); state = StaticState.Neutral; heldSince = -1; mismatchSince = -1;
        }

        public void ResetAll() { Model.Samples.Clear(); Model.Learned = false; ResetLive(); }

        /// <summary>Advance one live frame. Returns the current Active (NUL should be stopped) flag.</summary>
        public bool Step(PcmFrame f)
        {
            bool validHand = f.Valid && f.Lm != null && f.Quality >= C.QualityMin;
            bool match = false;
            if (validHand && Model.Learned)
            {
                float norm = Model.NormalizedDistance(PoseDescriptor.Extract(f.Lm));
                match = norm <= C.MatchNormMax;
            }
            history.Add((match, validHand));
            while (history.Count > C.History) history.RemoveAt(0);
            int matchCount = 0; for (int i = 0; i < history.Count; i++) if (history[i].match) matchCount++;
            bool stableMatch = matchCount >= C.StableNeeded;

            switch (state)
            {
                case StaticState.Neutral:
                case StaticState.Candidate:
                    if (stableMatch)
                    {
                        if (heldSince < 0) heldSince = f.Time;
                        state = (f.Time - heldSince >= C.HoldToStart) ? StaticState.Held : StaticState.Candidate;
                    }
                    else { heldSince = -1; state = StaticState.Neutral; }
                    mismatchSince = -1;
                    break;

                case StaticState.Held:
                case StaticState.Releasing:
                    // hand loss / low quality / tracking loss do NOT release: only a VALID non-matching hand does
                    if (match) { mismatchSince = -1; state = StaticState.Held; }
                    else if (validHand)
                    {
                        if (mismatchSince < 0) mismatchSince = f.Time;
                        state = StaticState.Releasing;
                        if (f.Time - mismatchSince >= C.ReleaseGrace)
                        {
                            state = StaticState.Neutral; heldSince = -1; mismatchSince = -1;
                        }
                    }
                    // invalid hand while held: stay Held, freeze the release timer
                    else { mismatchSince = -1; if (state == StaticState.Releasing) state = StaticState.Held; }
                    break;
            }
            return Active;
        }

        private static PoseDescriptor MedoidPose(IReadOnlyList<PcmFrame> frames)
        {
            var descs = new List<PoseDescriptor>(frames.Count);
            for (int i = 0; i < frames.Count; i++) descs.Add(PoseDescriptor.Extract(frames[i].Lm));
            int best = 0; float bestSum = float.MaxValue;
            for (int i = 0; i < descs.Count; i++)
            {
                float sum = 0f;
                for (int j = 0; j < descs.Count; j++) if (i != j) sum += PoseDescriptor.Distance(descs[i], descs[j]);
                if (sum < bestSum) { bestSum = sum; best = i; }
            }
            return descs[best];
        }
    }
}
