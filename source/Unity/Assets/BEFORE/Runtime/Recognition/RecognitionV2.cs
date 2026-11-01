using System.Collections.Generic;
using Before.Config;
using UnityEngine;

namespace Before.Recognition
{
    /// <summary>
    /// Recognition V2 (D-091). Learns arbitrary player-invented dynamic gestures from the full 21-landmark
    /// stream, not palm-center-only. Everything here is pure and deterministic so the invariances
    /// (speed 0.75-1.30x, translation, scale, rotation +/-10 deg, small jitter, stillness trim, boundary
    /// shift) are unit-testable without a camera. The live label is never derived from story context.
    /// </summary>
    public struct LandmarkFrame
    {
        public double Time;
        public Vector2[] Lm;      // 21 mirrored-normalized landmarks (camera space [0,1])
        public float Handedness;  // +1 right, -1 left, 0 unknown

        public LandmarkFrame(double time, Vector2[] lm, float handedness = 0f)
        {
            Time = time; Lm = lm; Handedness = handedness;
        }
    }

    /// <summary>Multichannel, time-normalized feature for one dynamic gesture.</summary>
    public sealed class GestureFeatureV2
    {
        public Vector2[] PalmPath;     // [N] sequence-global palm trajectory (orientation preserved: up != down)
        public Vector2[] Tangents;     // [N] unit tangent of PalmPath
        public Vector2[][] Shape;      // [N][21] per-frame canonical landmarks (rotation/scale/mirror normalized)
        public float[][] Pose;         // [N][P] joint angles + fingertip relations
        public float Duration;         // seconds
        public float DurationNormalized;
    }

    /// <summary>Per-frame normalization and sequence building.</summary>
    public static class GestureV2Builder
    {
        // finger tip indices and MCP spans for pose features
        private static readonly int[] Tips = { 4, 8, 12, 16, 20 };
        private static readonly int[][] Fingers =
        {
            new[]{1,2,3,4}, new[]{5,6,7,8}, new[]{9,10,11,12}, new[]{13,14,15,16}, new[]{17,18,19,20}
        };

        public static float PalmWidth(Vector2[] lm)
        {
            float w = Vector2.Distance(lm[5], lm[17]);
            return Mathf.Max(w, 1e-3f);
        }

        public static Vector2 PalmCenter(Vector2[] lm)
        {
            return (lm[0] + lm[5] + lm[9] + lm[13] + lm[17]) / 5f;
        }

        /// <summary>Canonical hand shape: wrist origin, palm-width scale, rotate wrist->middle-MCP to +Y, mirror left hands.</summary>
        public static Vector2[] CanonicalShape(Vector2[] lm, float handedness)
        {
            Vector2 wrist = lm[0];
            float scale = PalmWidth(lm);
            Vector2 axis = (lm[9] - wrist);           // wrist -> middle MCP
            float ang = Mathf.Atan2(axis.y, axis.x);
            // rotate so axis points to +Y (90 deg)
            float rot = Mathf.PI * 0.5f - ang;
            float c = Mathf.Cos(rot), s = Mathf.Sin(rot);
            bool mirror = handedness < 0f;
            var outp = new Vector2[21];
            for (int i = 0; i < 21; i++)
            {
                Vector2 p = (lm[i] - wrist) / scale;
                Vector2 r = new Vector2(p.x * c - p.y * s, p.x * s + p.y * c);
                if (mirror) r.x = -r.x;
                outp[i] = r;
            }
            return outp;
        }

        /// <summary>Pose vector: 5 finger bend angles (/pi) + 4 adjacent-tip distances + 5 tip-to-wrist distances (/palmWidth).</summary>
        public static float[] PoseVector(Vector2[] lm)
        {
            float scale = PalmWidth(lm);
            var v = new float[14];
            for (int f = 0; f < 5; f++)
            {
                var idx = Fingers[f];
                Vector2 a = lm[idx[1]] - lm[idx[0]];
                Vector2 b = lm[idx[3]] - lm[idx[1]];
                float cos = Vector2.Dot(a.normalized, b.normalized);
                v[f] = Mathf.Acos(Mathf.Clamp(cos, -1f, 1f)) / Mathf.PI;
            }
            v[5] = Vector2.Distance(lm[8], lm[12]) / scale;
            v[6] = Vector2.Distance(lm[12], lm[16]) / scale;
            v[7] = Vector2.Distance(lm[16], lm[20]) / scale;
            v[8] = Vector2.Distance(lm[4], lm[8]) / scale;
            for (int t = 0; t < 5; t++) v[9 + t] = Vector2.Distance(lm[Tips[t]], lm[0]) / scale;
            return v;
        }

        /// <summary>Trim leading/trailing stillness (palm speed below cfg.v2TrimSpeed), keeping pre/post-roll.</summary>
        public static List<LandmarkFrame> Trim(IReadOnlyList<LandmarkFrame> frames, BeforeRecognitionConfig cfg, int startShift = 0, int endShift = 0)
        {
            int n = frames.Count;
            if (n < 2) return new List<LandmarkFrame>(frames);
            var speed = new float[n];
            for (int i = 1; i < n; i++)
            {
                float dt = Mathf.Max(1e-4f, (float)(frames[i].Time - frames[i - 1].Time));
                speed[i] = Vector2.Distance(PalmCenter(frames[i].Lm), PalmCenter(frames[i - 1].Lm)) / dt;
            }
            int start = 0, end = n - 1;
            while (start < n - 1 && speed[start + 1] < cfg.v2TrimSpeed) start++;
            while (end > 0 && speed[end] < cfg.v2TrimSpeed) end--;
            if (end <= start) { start = 0; end = n - 1; }
            start = Mathf.Clamp(start - cfg.v2PreRoll + startShift, 0, n - 1);
            end = Mathf.Clamp(end + cfg.v2PostRoll + endShift, 0, n - 1);
            if (end <= start) { start = 0; end = n - 1; }
            var outp = new List<LandmarkFrame>(end - start + 1);
            for (int i = start; i <= end; i++) outp.Add(frames[i]);
            return outp;
        }

        public static GestureFeatureV2 Build(IReadOnlyList<LandmarkFrame> frames, BeforeRecognitionConfig cfg)
        {
            if (frames == null || frames.Count < 2) return null;
            int N = cfg.v2Frames;
            double t0 = frames[0].Time;
            double t1 = frames[frames.Count - 1].Time;
            float duration = (float)(t1 - t0);
            if (duration <= 1e-5f) return null;

            // resample the raw landmark set to N frames by time
            var resampled = new Vector2[N][];
            var hand = new float[N];
            for (int k = 0; k < N; k++)
            {
                double target = t0 + duration * k / (N - 1);
                int seg = 0;
                while (seg < frames.Count - 2 && frames[seg + 1].Time < target) seg++;
                double ta = frames[seg].Time, tb = frames[seg + 1].Time;
                float u = tb > ta + 1e-6 ? (float)((target - ta) / (tb - ta)) : 0f;
                var lm = new Vector2[21];
                for (int i = 0; i < 21; i++) lm[i] = Vector2.Lerp(frames[seg].Lm[i], frames[seg + 1].Lm[i], u);
                resampled[k] = lm;
                hand[k] = frames[seg].Handedness;
            }

            var palm = new Vector2[N];
            var shape = new Vector2[N][];
            var pose = new float[N][];
            for (int k = 0; k < N; k++)
            {
                palm[k] = PalmCenter(resampled[k]);
                shape[k] = CanonicalShape(resampled[k], hand[k]);
                pose[k] = PoseVector(resampled[k]);
            }

            // sequence-global palm path: translate first to origin, scale by bbox diagonal (orientation preserved)
            Vector2 origin = palm[0];
            Vector2 mn = Vector2.positiveInfinity, mx = Vector2.negativeInfinity;
            for (int k = 0; k < N; k++) { palm[k] -= origin; mn = Vector2.Min(mn, palm[k]); mx = Vector2.Max(mx, palm[k]); }
            float diag = Mathf.Max((mx - mn).magnitude, cfg.minScaleDiagonal);
            for (int k = 0; k < N; k++) palm[k] /= diag;

            var tan = new Vector2[N];
            for (int k = 0; k < N; k++)
            {
                int a = Mathf.Max(0, k - 1), b = Mathf.Min(N - 1, k + 1);
                Vector2 d = palm[b] - palm[a];
                tan[k] = d.sqrMagnitude > 1e-12f ? d.normalized : (k > 0 ? tan[k - 1] : Vector2.up);
            }

            return new GestureFeatureV2
            {
                PalmPath = palm, Tangents = tan, Shape = shape, Pose = pose,
                Duration = duration,
                DurationNormalized = Mathf.Clamp01((duration - 0.6f) / 0.8f)
            };
        }
    }

    /// <summary>Multichannel weighted DTW with a Sakoe-Chiba band that permits ~0.75-1.30 time warp.</summary>
    public static class MultiChannelDtw
    {
        public static float Distance(GestureFeatureV2 a, GestureFeatureV2 b, BeforeRecognitionConfig cfg)
        {
            int n = a.PalmPath.Length, m = b.PalmPath.Length;
            int band = cfg.v2DtwBand;
            const float INF = float.MaxValue / 4f;
            var acc = new float[n, m];
            for (int i = 0; i < n; i++) for (int j = 0; j < m; j++) acc[i, j] = INF;

            for (int i = 0; i < n; i++)
            {
                int jMin = Mathf.Max(0, i - band), jMax = Mathf.Min(m - 1, i + band);
                for (int j = jMin; j <= jMax; j++)
                {
                    float cost = FrameCost(a, b, i, j, cfg);
                    if (i == 0 && j == 0) { acc[i, j] = cost; continue; }
                    float best = INF;
                    if (i > 0 && acc[i - 1, j] < best) best = acc[i - 1, j];
                    if (j > 0 && acc[i, j - 1] < best) best = acc[i, j - 1];
                    if (i > 0 && j > 0 && acc[i - 1, j - 1] < best) best = acc[i - 1, j - 1];
                    acc[i, j] = cost + best;
                }
            }

            // path length by backtracking
            int len = 1, pi = n - 1, pj = m - 1;
            while (pi > 0 || pj > 0)
            {
                float diag = (pi > 0 && pj > 0) ? acc[pi - 1, pj - 1] : INF;
                float up = pi > 0 ? acc[pi - 1, pj] : INF;
                float left = pj > 0 ? acc[pi, pj - 1] : INF;
                if (diag <= up && diag <= left) { pi--; pj--; }
                else if (up <= left) pi--; else pj--;
                len++;
            }
            float aligned = acc[n - 1, m - 1] / len;
            float dur = cfg.v2WeightDuration * Mathf.Abs(a.DurationNormalized - b.DurationNormalized);
            return aligned + dur;
        }

        private static float FrameCost(GestureFeatureV2 a, GestureFeatureV2 b, int i, int j, BeforeRecognitionConfig cfg)
        {
            float palm = Vector2.Distance(a.PalmPath[i], b.PalmPath[j]);
            float tan = (1f - Vector2.Dot(a.Tangents[i], b.Tangents[j])) * 0.5f;
            float shape = 0f;
            for (int k = 0; k < 21; k++) shape += Vector2.Distance(a.Shape[i][k], b.Shape[j][k]);
            shape /= 21f;
            float pose = 0f;
            var pa = a.Pose[i]; var pb = b.Pose[j];
            for (int k = 0; k < pa.Length; k++) { float d = pa[k] - pb[k]; pose += d * d; }
            pose = Mathf.Sqrt(pose / pa.Length);
            return cfg.v2WeightPalmMotion * palm
                 + cfg.v2WeightHandShape * shape
                 + cfg.v2WeightPose * pose
                 + cfg.v2WeightTangent * tan;
        }
    }

    public enum TeachV2Status { NeedMore, DroppedOutlierNeedOne, Confirmed, Failed }

    public readonly struct TeachV2Progress
    {
        public readonly TeachV2Status Status;
        public readonly int Collected;
        public readonly int Target;
        public TeachV2Progress(TeachV2Status s, int collected, int target) { Status = s; Collected = collected; Target = target; }
    }

    /// <summary>One confirmed class: real samples (<=8), medoid, radius; supports classify and pruning adaptation.</summary>
    public sealed class GestureClassV2
    {
        private readonly BeforeRecognitionConfig cfg;
        public readonly List<GestureFeatureV2> Samples = new List<GestureFeatureV2>();
        public int MedoidIndex { get; private set; }
        public float Radius { get; private set; }

        public GestureClassV2(BeforeRecognitionConfig config, IEnumerable<GestureFeatureV2> samples)
        {
            cfg = config;
            Samples.AddRange(samples);
            Recompute();
        }

        public GestureFeatureV2 Medoid => Samples[MedoidIndex];

        public float Distance(GestureFeatureV2 input)
        {
            float min = float.MaxValue;
            foreach (var s in Samples) min = Mathf.Min(min, MultiChannelDtw.Distance(input, s, cfg));
            float med = MultiChannelDtw.Distance(input, Medoid, cfg);
            return 0.5f * min + 0.5f * med;
        }

        public float NormalizedDistance(GestureFeatureV2 input) => Distance(input) / Radius;

        /// <summary>Add a successful sample; prune the sample farthest from the medoid when over the cap.</summary>
        public void AddAndPrune(GestureFeatureV2 sample)
        {
            Samples.Add(sample);
            Recompute();
            while (Samples.Count > cfg.v2MaxRealSamples)
            {
                int worst = -1; float worstD = -1f;
                for (int i = 0; i < Samples.Count; i++)
                {
                    if (i == MedoidIndex) continue;
                    float d = MultiChannelDtw.Distance(Samples[i], Medoid, cfg);
                    if (d > worstD) { worstD = d; worst = i; }
                }
                Samples.RemoveAt(worst);
                Recompute();
            }
        }

        private void Recompute()
        {
            int n = Samples.Count;
            MedoidIndex = 0;
            if (n == 1) { Radius = Mathf.Clamp(cfg.v2CohesionMax, cfg.v2RadiusMin, cfg.v2RadiusMax); return; }
            float bestSum = float.MaxValue;
            float meanPair = 0f; int pairs = 0;
            for (int i = 0; i < n; i++)
            {
                float sum = 0f;
                for (int j = 0; j < n; j++)
                {
                    if (i == j) continue;
                    float d = MultiChannelDtw.Distance(Samples[i], Samples[j], cfg);
                    sum += d;
                    if (j > i) { meanPair += d; pairs++; }
                }
                if (sum < bestSum) { bestSum = sum; MedoidIndex = i; }
            }
            float cohesion = pairs > 0 ? meanPair / pairs : cfg.v2CohesionMax;
            Radius = Mathf.Clamp(cfg.v2RadiusFactor * cohesion, cfg.v2RadiusMin, cfg.v2RadiusMax);
        }
    }

    public enum LiveV2State { Accepted, Ambiguous, NoMatch }

    public readonly struct LiveV2Result
    {
        public readonly LiveV2State State;
        public readonly SymbolicClass Chosen;
        public readonly float N1, N2;
        public LiveV2Result(LiveV2State s, SymbolicClass c, float n1, float n2) { State = s; Chosen = c; N1 = n1; N2 = n2; }
    }

    /// <summary>
    /// Two-class dynamic-gesture recognizer with adaptive 3+1 teaching, cross-class separation, and
    /// gated continual adaptation. Holds a per-class teaching buffer until the class is confirmed.
    /// </summary>
    public sealed class GestureRecognizerV2
    {
        private readonly BeforeRecognitionConfig cfg;
        private readonly List<GestureFeatureV2>[] buffers = { new List<GestureFeatureV2>(), new List<GestureFeatureV2>() };
        private readonly GestureClassV2[] classes = new GestureClassV2[2];

        public GestureRecognizerV2(BeforeRecognitionConfig config) { cfg = config; }

        public bool IsConfirmed(SymbolicClass c) => classes[(int)c] != null;
        public GestureClassV2 GetClass(SymbolicClass c) => classes[(int)c];
        public int Collected(SymbolicClass c) => buffers[(int)c].Count;

        /// <summary>Adaptive 3+1 teaching. Never rejects a divergent sample on the spot; clusters after >=3.</summary>
        public TeachV2Progress SubmitTeaching(SymbolicClass target, GestureFeatureV2 feature)
        {
            if (feature == null) return new TeachV2Progress(TeachV2Status.NeedMore, buffers[(int)target].Count, cfg.v2MinTeachSamples);
            var buf = buffers[(int)target];
            buf.Add(feature);
            if (buf.Count < cfg.v2MinTeachSamples)
                return new TeachV2Progress(TeachV2Status.NeedMore, buf.Count, cfg.v2MinTeachSamples);

            // compute per-sample mean distance to others
            int n = buf.Count;
            var mean = new float[n];
            float cohesion = 0f; int pairs = 0;
            for (int i = 0; i < n; i++)
            {
                float s = 0f;
                for (int j = 0; j < n; j++)
                {
                    if (i == j) continue;
                    float d = MultiChannelDtw.Distance(buf[i], buf[j], cfg);
                    s += d; if (j > i) { cohesion += d; pairs++; }
                }
                mean[i] = s / (n - 1);
            }
            cohesion = pairs > 0 ? cohesion / pairs : 0f;

            // detect a single strong outlier BEFORE accepting the set: a lone divergent sample must be
            // dropped even if the overall mean cohesion happens to sit under the threshold.
            int worst = 0; for (int i = 1; i < n; i++) if (mean[i] > mean[worst]) worst = i;
            float othersMedian = MedianExcluding(mean, worst);
            bool singleOutlier = mean[worst] > cfg.v2OutlierFactor * othersMedian;
            if (singleOutlier && n <= cfg.v2MaxTeachSamples)
            {
                buf.RemoveAt(worst);
                return new TeachV2Progress(TeachV2Status.DroppedOutlierNeedOne, buf.Count, buf.Count + 1);
            }

            if (cohesion <= cfg.v2CohesionMax)
            {
                Confirm(target, new List<GestureFeatureV2>(buf));
                return new TeachV2Progress(TeachV2Status.Confirmed, n, n);
            }

            if (n < cfg.v2MaxTeachSamples)
                return new TeachV2Progress(TeachV2Status.NeedMore, n, n + 1);

            // reached cap without cohesion: confirm using the 3 mutually-closest samples
            Confirm(target, MostCohesiveThree(buf));
            return new TeachV2Progress(TeachV2Status.Confirmed, 3, 3);
        }

        private void Confirm(SymbolicClass target, List<GestureFeatureV2> samples)
        {
            classes[(int)target] = new GestureClassV2(cfg, samples);
            buffers[(int)target].Clear();
        }

        /// <summary>After both classes are confirmed, are they distinct enough?</summary>
        public bool SeparationOk()
        {
            if (classes[0] == null || classes[1] == null) return false;
            float d = MultiChannelDtw.Distance(classes[0].Medoid, classes[1].Medoid, cfg);
            return d >= cfg.v2ClassSeparationFactor * Mathf.Max(classes[0].Radius, classes[1].Radius);
        }

        public LiveV2Result Classify(GestureFeatureV2 input)
        {
            float nJump = classes[0] != null ? classes[0].NormalizedDistance(input) : float.MaxValue;
            float nStop = classes[1] != null ? classes[1].NormalizedDistance(input) : float.MaxValue;
            SymbolicClass best; float n1, n2;
            if (nJump <= nStop) { best = SymbolicClass.Jump; n1 = nJump; n2 = nStop; }
            else { best = SymbolicClass.Stop; n1 = nStop; n2 = nJump; }
            if (n1 > cfg.v2NoMatchN1) return new LiveV2Result(LiveV2State.NoMatch, best, n1, n2);
            if (n1 <= cfg.v2HighConfN1 && (n2 - n1) >= cfg.v2HighConfMargin)
                return new LiveV2Result(LiveV2State.Accepted, best, n1, n2);
            return new LiveV2Result(LiveV2State.Ambiguous, best, n1, n2);
        }

        /// <summary>Continual adaptation. Caller must pass a high-confidence, context-successful sample.</summary>
        public bool AdaptOnSuccess(SymbolicClass target, GestureFeatureV2 feature, float quality, float margin)
        {
            var cls = classes[(int)target];
            if (cls == null || feature == null) return false;
            if (quality < cfg.v2AdaptQualityMin) return false;
            if (margin < cfg.v2AdaptMinMargin) return false;
            cls.AddAndPrune(feature);
            return true;
        }

        public void ResetAll()
        {
            classes[0] = classes[1] = null;
            buffers[0].Clear(); buffers[1].Clear();
        }

        private static float MedianExcluding(float[] values, int excludeIndex)
        {
            var list = new List<float>(values.Length);
            for (int i = 0; i < values.Length; i++) if (i != excludeIndex) list.Add(values[i]);
            list.Sort();
            int c = list.Count;
            if (c == 0) return 0f;
            return c % 2 == 1 ? list[c / 2] : 0.5f * (list[c / 2 - 1] + list[c / 2]);
        }

        private List<GestureFeatureV2> MostCohesiveThree(List<GestureFeatureV2> buf)
        {
            int n = buf.Count;
            int bi = 0, bj = 1, bk = 2; float best = float.MaxValue;
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                    for (int k = j + 1; k < n; k++)
                    {
                        float d = MultiChannelDtw.Distance(buf[i], buf[j], cfg)
                                + MultiChannelDtw.Distance(buf[i], buf[k], cfg)
                                + MultiChannelDtw.Distance(buf[j], buf[k], cfg);
                        if (d < best) { best = d; bi = i; bj = j; bk = k; }
                    }
            return new List<GestureFeatureV2> { buf[bi], buf[bj], buf[bk] };
        }
    }
}
