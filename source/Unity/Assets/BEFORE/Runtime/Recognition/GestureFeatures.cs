using System;
using System.Collections.Generic;
using Before.Config;
using UnityEngine;

namespace Before.Recognition
{
    /// <summary>One filtered palm sample in mirrored panel coordinates [0,1], timestamped on the realtime clock.</summary>
    public struct PalmSample
    {
        public double Time;
        public Vector2 Position;
        // Recognition V2 payload (D-091). Landmarks are a per-frame clone of the mirrored 21 landmarks so a
        // later inference cannot corrupt buffered history. Null for legacy/palm-only callers.
        public Vector2[] Landmarks;
        public float Handedness;
        public float Quality;
    }

    /// <summary>Normalized 32-point feature sequence per recognition-spec.md.</summary>
    public sealed class GestureFeature
    {
        public readonly Vector2[] Points;      // translated + uniformly scaled, rotation/mirror preserved
        public readonly Vector2[] Tangents;    // per-point unit tangent
        public readonly float[] Speeds;        // normalized relative speed profile
        public readonly float Duration;        // raw seconds
        public readonly float DurationNormalized; // mapped into [0,1] over [0.6,1.4]

        public GestureFeature(Vector2[] points, Vector2[] tangents, float[] speeds, float duration)
        {
            Points = points;
            Tangents = tangents;
            Speeds = speeds;
            Duration = duration;
            DurationNormalized = Mathf.Clamp01((duration - 0.6f) / 0.8f);
        }
    }

    /// <summary>Arc-length resampling and normalization. Pure and deterministic.</summary>
    public static class TrajectoryNormalizer
    {
        public static GestureFeature Build(IReadOnlyList<PalmSample> samples, BeforeRecognitionConfig cfg)
        {
            if (samples == null || samples.Count < 2) return null;
            int n = cfg.resampleCount;
            float duration = (float)(samples[samples.Count - 1].Time - samples[0].Time);

            // cumulative arc length
            float total = 0f;
            var cumulative = new float[samples.Count];
            for (int i = 1; i < samples.Count; i++)
            {
                total += Vector2.Distance(samples[i].Position, samples[i - 1].Position);
                cumulative[i] = total;
            }

            var points = new Vector2[n];
            var times = new float[n];
            if (total <= 1e-6f)
            {
                for (int i = 0; i < n; i++) { points[i] = samples[0].Position; times[i] = duration * i / (n - 1); }
            }
            else
            {
                int seg = 0;
                for (int i = 0; i < n; i++)
                {
                    float target = total * i / (n - 1);
                    while (seg < samples.Count - 2 && cumulative[seg + 1] < target) seg++;
                    float segLen = cumulative[seg + 1] - cumulative[seg];
                    float t = segLen > 1e-8f ? (target - cumulative[seg]) / segLen : 0f;
                    points[i] = Vector2.Lerp(samples[seg].Position, samples[seg + 1].Position, t);
                    times[i] = Mathf.Lerp((float)(samples[seg].Time - samples[0].Time), (float)(samples[seg + 1].Time - samples[0].Time), t);
                }
            }

            // translate so first point is origin
            Vector2 origin = points[0];
            Vector2 min = Vector2.positiveInfinity, max = Vector2.negativeInfinity;
            for (int i = 0; i < n; i++)
            {
                points[i] -= origin;
                min = Vector2.Min(min, points[i]);
                max = Vector2.Max(max, points[i]);
            }

            // uniform scale by max(bbox diagonal, minScale); preserve aspect, rotation, mirror, travel direction
            float diagonal = Mathf.Max((max - min).magnitude, cfg.minScaleDiagonal);
            for (int i = 0; i < n; i++) points[i] /= diagonal;

            // per-point unit tangent and relative speed
            var tangents = new Vector2[n];
            var speeds = new float[n];
            float maxSpeed = 1e-6f;
            for (int i = 0; i < n; i++)
            {
                int a = Mathf.Max(0, i - 1);
                int b = Mathf.Min(n - 1, i + 1);
                Vector2 d = points[b] - points[a];
                tangents[i] = d.sqrMagnitude > 1e-12f ? d.normalized : (i > 0 ? tangents[i - 1] : Vector2.right);
                float dt = Mathf.Max(1e-4f, times[b] - times[a]);
                speeds[i] = d.magnitude / dt;
                if (speeds[i] > maxSpeed) maxSpeed = speeds[i];
            }
            for (int i = 0; i < n; i++) speeds[i] /= maxSpeed;

            return new GestureFeature(points, tangents, speeds, duration);
        }
    }

    /// <summary>Weighted DTW with Sakoe–Chiba band per recognition-spec.md. Pure and deterministic.</summary>
    public static class WeightedDtw
    {
        public static float Distance(GestureFeature a, GestureFeature b, BeforeRecognitionConfig cfg)
        {
            AlignedDistance(a, b, cfg, out float distance, null);
            return distance;
        }

        /// <summary>Computes distance; if alignmentOut is non-null, fills index pairs (i in a, j in b) of the optimal path.</summary>
        public static void AlignedDistance(GestureFeature a, GestureFeature b, BeforeRecognitionConfig cfg, out float distance, List<(int ai, int bi)> alignmentOut)
        {
            int n = a.Points.Length;
            int m = b.Points.Length;
            int band = cfg.dtwBand;
            const float INF = float.MaxValue / 4f;
            var cost = new float[n, m];
            var acc = new float[n, m];
            for (int i = 0; i < n; i++)
                for (int j = 0; j < m; j++)
                    acc[i, j] = INF;

            for (int i = 0; i < n; i++)
            {
                int jMin = Mathf.Max(0, i - band);
                int jMax = Mathf.Min(m - 1, i + band);
                for (int j = jMin; j <= jMax; j++)
                {
                    float pos = Vector2.Distance(a.Points[i], b.Points[j]);
                    float tan = (1f - Vector2.Dot(a.Tangents[i], b.Tangents[j])) * 0.5f;
                    float spd = Mathf.Abs(a.Speeds[i] - b.Speeds[j]);
                    cost[i, j] = cfg.weightPosition * pos + cfg.weightTangent * tan + cfg.weightSpeed * spd;
                }
            }

            acc[0, 0] = cost[0, 0];
            for (int i = 0; i < n; i++)
            {
                int jMin = Mathf.Max(0, i - band);
                int jMax = Mathf.Min(m - 1, i + band);
                for (int j = jMin; j <= jMax; j++)
                {
                    if (i == 0 && j == 0) continue;
                    float best = INF;
                    if (i > 0 && acc[i - 1, j] < best) best = acc[i - 1, j];
                    if (j > 0 && acc[i, j - 1] < best) best = acc[i, j - 1];
                    if (i > 0 && j > 0 && acc[i - 1, j - 1] < best) best = acc[i - 1, j - 1];
                    acc[i, j] = cost[i, j] + best;
                }
            }

            // path length by backtracking
            int pathLength = 1;
            int pi = n - 1, pj = m - 1;
            alignmentOut?.Add((pi, pj));
            while (pi > 0 || pj > 0)
            {
                float diag = (pi > 0 && pj > 0) ? acc[pi - 1, pj - 1] : INF;
                float up = pi > 0 ? acc[pi - 1, pj] : INF;
                float left = pj > 0 ? acc[pi, pj - 1] : INF;
                if (diag <= up && diag <= left) { pi--; pj--; }
                else if (up <= left) { pi--; }
                else { pj--; }
                pathLength++;
                alignmentOut?.Add((pi, pj));
            }
            alignmentOut?.Reverse();

            float aligned = acc[n - 1, m - 1] / pathLength;
            float durationTerm = cfg.weightDuration * Mathf.Abs(a.DurationNormalized - b.DurationNormalized);
            distance = aligned + durationTerm;
        }

        /// <summary>DTW-aligned pointwise average of two features (used for prototype seeding/adaptation).</summary>
        public static GestureFeature AlignedAverage(GestureFeature a, GestureFeature b, BeforeRecognitionConfig cfg, float weightB)
        {
            var path = new List<(int ai, int bi)>(a.Points.Length * 2);
            AlignedDistance(a, b, cfg, out _, path);
            int n = a.Points.Length;
            var points = new Vector2[n];
            var speeds = new float[n];
            var counts = new int[n];
            foreach (var (ai, bi) in path)
            {
                points[ai] += Vector2.Lerp(a.Points[ai], b.Points[bi], weightB);
                speeds[ai] += Mathf.Lerp(a.Speeds[ai], b.Speeds[bi], weightB);
                counts[ai]++;
            }
            var tangents = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                if (counts[i] > 0) { points[i] /= counts[i]; speeds[i] /= counts[i]; }
                else { points[i] = a.Points[i]; speeds[i] = a.Speeds[i]; }
            }
            for (int i = 0; i < n; i++)
            {
                int p = Mathf.Max(0, i - 1);
                int q = Mathf.Min(n - 1, i + 1);
                Vector2 d = points[q] - points[p];
                tangents[i] = d.sqrMagnitude > 1e-12f ? d.normalized : Vector2.right;
            }
            float duration = Mathf.Lerp(a.Duration, b.Duration, weightB);
            return new GestureFeature(points, tangents, speeds, duration);
        }
    }
}
