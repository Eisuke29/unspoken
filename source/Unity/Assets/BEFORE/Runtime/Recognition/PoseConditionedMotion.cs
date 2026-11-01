using System.Collections.Generic;
using UnityEngine;

namespace Before.Recognition
{
    // D-092 Pose-conditioned Motion Recognition.
    //
    // Jump/Stop are compound gestures: a free hand-shape (Pose) held while performing a free single
    // stroke (Motion). Neither alone fires. Two pipelines are kept strictly separate:
    //
    //   Pose   -- wrist-origin / palm-width-scale / shape-rotation-normalized 21 landmarks + joint
    //             angles + fingertip distances/extension. Screen movement NEVER enters pose features.
    //             Weights shape 0.35 / angles 0.45 / fingertip+extension 0.20. Stable (heavy) smoothing.
    //   Motion -- mirrored screen-space palm center, displacement / direction / speed, orientation
    //             preserved (up != down; NO pose rotation normalization). Light low-latency smoothing so
    //             fast strokes are not blunted.
    //
    // Feature extraction / normalization reuse the D-091 GestureV2Builder base (retained), but the single
    // -DTW live classification of D-091 is superseded here by the 7-state Pose->Motion->Trigger machine.
    //
    // Everything in this file is pure and deterministic so the compound firing grammar
    // (pose-only no fire / motion-only no fire / compound fires / wrong pose no fire / wrong motion no fire
    //  / prefix fire without final stillness / insufficient frames reject / latch / re-fire lockout) is
    // unit-testable without a camera. The class label is never derived from story context.

    public enum PcmState
    {
        Neutral,        // no stable pose
        PoseCandidate,  // a pose class is emerging but not yet stable enough
        PoseArmed,      // pose stable & matched -- shows a half-ring, fires nothing on its own
        MotionTracking, // pose armed, motion started, evaluating the stroke prefix
        Triggered,      // compound fired this frame
        Rejected,       // motion attempt rejected (see LastReject); waiting to settle
        Rearming        // fired/rejected; pose must release before another command
    }

    public enum PcmReject
    {
        None,
        PoseNoMatch,
        PoseAmbiguous,
        LowQuality,
        MotionTimeout,
        WrongDirection,
        PrefixNoMatch,
        InsufficientFrames,
        TrackingLost,
        NeedsRearm
    }

    /// <summary>One live/teaching frame: 21 landmarks (pose) + mirrored screen palm center (motion) + quality.</summary>
    public struct PcmFrame
    {
        public double Time;
        public Vector2[] Lm;        // 21 landmarks, camera-normalized (pose pipeline)
        public Vector2 PalmScreen;  // mirrored screen-space palm center in [0,1] panel coords (motion pipeline)
        public float Quality;
        public bool Valid;          // false when tracking is lost this frame

        public PcmFrame(double time, Vector2[] lm, Vector2 palmScreen, float quality, bool valid = true)
        {
            Time = time; Lm = lm; PalmScreen = palmScreen; Quality = quality; Valid = valid;
        }
    }

    /// <summary>Single-frame pose descriptor from the pose pipeline (no screen motion).</summary>
    public sealed class PoseDescriptor
    {
        public Vector2[] Shape;  // 21 canonical points
        public float[] Angles;   // 5 finger bend angles (/pi)
        public float[] Tips;     // 9 fingertip distance / extension terms

        public static PoseDescriptor Extract(Vector2[] lm)
        {
            var pv = GestureV2Builder.PoseVector(lm);
            var angles = new float[5];
            var tips = new float[9];
            for (int i = 0; i < 5; i++) angles[i] = pv[i];
            for (int i = 0; i < 9; i++) tips[i] = pv[5 + i];
            return new PoseDescriptor
            {
                Shape = GestureV2Builder.CanonicalShape(lm, 0f),
                Angles = angles,
                Tips = tips
            };
        }

        /// <summary>Weighted pose distance: shape 0.35 / angles 0.45 / fingertip+extension 0.20.</summary>
        public static float Distance(PoseDescriptor a, PoseDescriptor b)
        {
            float shape = 0f;
            for (int i = 0; i < 21; i++) shape += Vector2.Distance(a.Shape[i], b.Shape[i]);
            shape /= 21f;

            float ang = 0f;
            for (int i = 0; i < 5; i++) ang += Mathf.Abs(a.Angles[i] - b.Angles[i]);
            ang /= 5f;

            float tip = 0f;
            for (int i = 0; i < 9; i++) tip += Mathf.Abs(a.Tips[i] - b.Tips[i]);
            tip /= 9f;

            return 0.35f * shape + 0.45f * ang + 0.20f * tip;
        }
    }

    /// <summary>Learned pose prototype for one class (mean descriptor + intra-sample scale).</summary>
    public sealed class PoseModel
    {
        public readonly List<PoseDescriptor> Samples = new List<PoseDescriptor>();
        public PoseDescriptor Prototype;
        public float NormRef = 0.15f;     // scale used to normalize distances (learned)
        public bool Learned;

        public void AddSample(PoseDescriptor d)
        {
            Samples.Add(d);
            Recompute();
            Learned = Samples.Count > 0;
        }

        public void Recompute()
        {
            if (Samples.Count == 0) return;
            // medoid-style prototype: the sample with least summed distance to the others
            int best = 0; float bestSum = float.MaxValue;
            for (int i = 0; i < Samples.Count; i++)
            {
                float sum = 0f;
                for (int j = 0; j < Samples.Count; j++) if (i != j) sum += PoseDescriptor.Distance(Samples[i], Samples[j]);
                if (sum < bestSum) { bestSum = sum; best = i; }
            }
            Prototype = Samples[best];
            float mean = 0f; int cnt = 0;
            for (int j = 0; j < Samples.Count; j++) if (j != best) { mean += PoseDescriptor.Distance(Prototype, Samples[j]); cnt++; }
            mean = cnt > 0 ? mean / cnt : 0f;
            NormRef = Mathf.Clamp(mean * 2.0f, 0.06f, 0.60f);
        }

        /// <summary>Distance to prototype normalized by the learned intra-class scale.</summary>
        public float NormalizedDistance(PoseDescriptor d)
        {
            if (!Learned) return float.MaxValue;
            return PoseDescriptor.Distance(Prototype, d) / NormRef;
        }
    }

    /// <summary>Learned single-stroke motion template for one class (screen-space, orientation preserved).</summary>
    public sealed class MotionModel
    {
        public const int K = 16;                 // resample resolution
        public readonly List<Vector2[]> Samples = new List<Vector2[]>(); // each K normalized-to-start paths
        public Vector2[] Template;               // [K] medoid path (relative to start)
        public Vector2 Direction;                // dominant unit direction (end - start)
        public float Radius = 0.20f;             // learned prefix acceptance radius (in path-space units)
        public bool Learned;

        /// <summary>Resample a raw screen path to K points, expressed relative to its first point.</summary>
        public static Vector2[] Resample(IReadOnlyList<Vector2> raw)
        {
            var pts = new List<Vector2>(raw);
            if (pts.Count == 0) return null;
            if (pts.Count == 1) { var one = new Vector2[K]; for (int i = 0; i < K; i++) one[i] = Vector2.zero; return one; }
            // cumulative arclength
            var cum = new float[pts.Count];
            for (int i = 1; i < pts.Count; i++) cum[i] = cum[i - 1] + Vector2.Distance(pts[i - 1], pts[i]);
            float total = cum[pts.Count - 1];
            var outp = new Vector2[K];
            Vector2 start = pts[0];
            if (total < 1e-5f) { for (int i = 0; i < K; i++) outp[i] = Vector2.zero; return outp; }
            for (int k = 0; k < K; k++)
            {
                float target = total * k / (K - 1);
                int seg = 1;
                while (seg < pts.Count - 1 && cum[seg] < target) seg++;
                float segLen = Mathf.Max(1e-6f, cum[seg] - cum[seg - 1]);
                float f = Mathf.Clamp01((target - cum[seg - 1]) / segLen);
                outp[k] = Vector2.Lerp(pts[seg - 1], pts[seg], f) - start;
            }
            return outp;
        }

        public static float PathDistance(Vector2[] a, Vector2[] b)
        {
            float d = 0f;
            for (int i = 0; i < K; i++) d += Vector2.Distance(a[i], b[i]);
            return d / K;
        }

        /// <summary>Scale a start-relative path to unit extent so a short prefix matches the template's initial
        /// shape regardless of how far along the stroke it is (direction is preserved).</summary>
        public static Vector2[] NormalizeScale(Vector2[] path)
        {
            float m = 1e-4f;
            for (int i = 0; i < K; i++) m = Mathf.Max(m, path[i].magnitude);
            var o = new Vector2[K];
            for (int i = 0; i < K; i++) o[i] = path[i] / m;
            return o;
        }

        public void AddSample(Vector2[] resampled)
        {
            Samples.Add(resampled);
            Recompute();
            Learned = Samples.Count > 0;
        }

        public void Recompute()
        {
            if (Samples.Count == 0) return;
            int best = 0; float bestSum = float.MaxValue;
            for (int i = 0; i < Samples.Count; i++)
            {
                float sum = 0f;
                for (int j = 0; j < Samples.Count; j++) if (i != j) sum += PathDistance(Samples[i], Samples[j]);
                if (sum < bestSum) { bestSum = sum; best = i; }
            }
            Template = Samples[best];
            Direction = (Template[K - 1] - Template[0]);
            Direction = Direction.sqrMagnitude > 1e-8f ? Direction.normalized : Vector2.up;
            // radius measured in the same scale-normalized space used at fire time
            var tN = NormalizeScale(Template);
            float mean = 0f; int cnt = 0;
            for (int j = 0; j < Samples.Count; j++) if (j != best) { mean += PathDistance(tN, NormalizeScale(Samples[j])); cnt++; }
            mean = cnt > 0 ? mean / cnt : 0.05f;
            Radius = Mathf.Clamp(mean * 1.8f, 0.18f, 0.60f);
        }

        /// <summary>Prefix template = first <paramref name="fraction"/> of the taught stroke, resampled to K and
        /// scale-normalized so a short live prefix can be compared to it directly.</summary>
        public Vector2[] PrefixTemplate(float fraction)
        {
            int keep = Mathf.Clamp(Mathf.CeilToInt(K * fraction), 2, K);
            var sub = new List<Vector2>(keep);
            for (int i = 0; i < keep; i++) sub.Add(Template[i]);
            return NormalizeScale(Resample(sub));
        }
    }

    /// <summary>Tunable thresholds (defaults follow the D-092 spec).</summary>
    public sealed class PcmConfig
    {
        public float Diagonal = 1.41421356f;   // panel diagonal in the PalmScreen coordinate space
        public float QualityMin = 0.65f;
        public int PoseHistory = 7;
        public int PoseStableNeeded = 5;        // 5 of last 7 same class
        public float PoseArmMaxNorm = 1.35f;    // normalized pose distance ceiling to arm
        public float PoseMargin = 0.15f;        // required 2nd-best margin when BOTH classes learned
        public float MotionStartWait = 0.90f;   // seconds allowed after arming to start motion
        public float MotionStartSpeed = 0.12f;  // panel-diagonal / s, 2 consecutive frames
        public float MotionStartDisp = 0.04f;   // panel-diagonal displacement from armed point
        public float PoseLatch = 0.65f;         // pose may drop this long during motion
        public float TrackLostRelease = 0.15f;  // continuous invalid tracking that releases the latch
        public float PrefixFraction = 0.55f;    // keep template prefix up to 55% of the taught stroke
        public int MotionMinFrames = 4;
        public float FireMinDisp = 0.08f;       // * diagonal (also >= 0.75 * palm width)
        public float FirePalmWidthFactor = 0.75f;
        public float FireDirCos = 0.70f;
        public float MotionMinElapsed = 0.15f;
        public float MotionMaxElapsed = 0.90f;
        public float InsufficientElapsed = 0.25f;
        public float RearmRelease = 0.35f;      // pose must be released this long before the same command re-fires
    }

    /// <summary>Live per-frame result from the recognizer.</summary>
    public struct PcmResult
    {
        public PcmState State;
        public PcmReject Reject;
        public SymbolicClass ArmedClass;  // valid in PoseArmed/MotionTracking
        public SymbolicClass FiredClass;  // valid only when State == Triggered
        public bool Fired;
        public float PoseNorm;            // best normalized pose distance
        public float PoseSecondNorm;      // second-best normalized pose distance
        public float PoseMargin;
        public int PoseStableCount;       // frames of the best class in the history window
        public float MotionDisp;
        public float MotionDirCos;
        public float MotionElapsed;
        public int MotionFrames;
        public float PrefixDist;
    }
}
