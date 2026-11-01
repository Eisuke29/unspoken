using System;
using System.Collections.Generic;
using Before.Config;
using UnityEngine;

namespace Before.Recognition
{
    /// <summary>One Euro filter (per axis) at inference rate.</summary>
    public sealed class OneEuroFilter2
    {
        private readonly float minCutoff;
        private readonly float beta;
        private readonly float dCutoff;
        private Vector2 previous;
        private Vector2 previousDerivative;
        private double previousTime;
        private bool hasPrevious;

        public OneEuroFilter2(float minCutoff, float beta, float dCutoff)
        {
            this.minCutoff = minCutoff;
            this.beta = beta;
            this.dCutoff = dCutoff;
        }

        public Vector2 Filter(Vector2 value, double time)
        {
            if (!hasPrevious)
            {
                hasPrevious = true;
                previous = value;
                previousDerivative = Vector2.zero;
                previousTime = time;
                return value;
            }

            float dt = Mathf.Max(1e-4f, (float)(time - previousTime));
            previousTime = time;

            Vector2 derivative = (value - previous) / dt;
            float aD = Alpha(dCutoff, dt);
            Vector2 smoothedDerivative = Vector2.Lerp(previousDerivative, derivative, aD);
            previousDerivative = smoothedDerivative;

            float cutoff = minCutoff + beta * smoothedDerivative.magnitude;
            float a = Alpha(cutoff, dt);
            Vector2 filtered = Vector2.Lerp(previous, value, a);
            previous = filtered;
            return filtered;
        }

        public void Reset()
        {
            hasPrevious = false;
        }

        private static float Alpha(float cutoff, float dt)
        {
            float tau = 1f / (2f * Mathf.PI * Mathf.Max(1e-4f, cutoff));
            return 1f / (1f + tau / dt);
        }
    }

    public enum SegmenterState
    {
        Disabled,
        Arming,
        Armed,
        Capturing,
        Closing
    }

    public enum SegmentEvent
    {
        None,
        SegmentClosed,
        SegmentRejectedShort,
        SegmentCancelled
    }

    /// <summary>
    /// Armed stillness → moving frames → ending stillness segmenter for live symbolic commands.
    /// Speeds are normalized to panel diagonal per second. Times use the realtime clock.
    /// </summary>
    public sealed class MotionSegmenter
    {
        private readonly BeforeRecognitionConfig cfg;
        private readonly List<PalmSample> capture = new List<PalmSample>(128);

        public SegmenterState State { get; private set; } = SegmenterState.Disabled;
        public IReadOnlyList<PalmSample> LastClosedSegment { get; private set; }
        public float LastSpeed { get; private set; }

        private double stillSince = -1;
        private int movingFrames;
        private int endStillFrames;
        private Vector2 lastPosition;
        private double lastTime = -1;
        private bool hasLast;

        public MotionSegmenter(BeforeRecognitionConfig config)
        {
            cfg = config;
        }

        public void SetEnabled(bool enabled)
        {
            if (enabled && State == SegmenterState.Disabled) { State = SegmenterState.Arming; ResetTracking(); }
            else if (!enabled) { State = SegmenterState.Disabled; ResetTracking(); }
        }

        public void CancelActive()
        {
            if (State == SegmenterState.Capturing || State == SegmenterState.Closing)
            {
                State = SegmenterState.Arming;
                ResetTracking();
            }
        }

        /// <summary>Feed one filtered valid sample. Returns a segment event.</summary>
        public SegmentEvent Feed(PalmSample sample)
        {
            if (State == SegmenterState.Disabled) return SegmentEvent.None;

            float speed = 0f;
            if (hasLast)
            {
                float dt = Mathf.Max(1e-4f, (float)(sample.Time - lastTime));
                speed = Vector2.Distance(sample.Position, lastPosition) / dt;
            }
            LastSpeed = speed;
            lastPosition = sample.Position;
            lastTime = sample.Time;
            hasLast = true;

            switch (State)
            {
                case SegmenterState.Arming:
                    if (speed < cfg.armSpeed)
                    {
                        if (stillSince < 0) stillSince = sample.Time;
                        if (sample.Time - stillSince >= cfg.armSeconds)
                        {
                            State = SegmenterState.Armed;
                            movingFrames = 0;
                        }
                    }
                    else
                    {
                        stillSince = -1;
                    }
                    return SegmentEvent.None;

                case SegmenterState.Armed:
                    if (speed > cfg.startSpeed)
                    {
                        movingFrames++;
                        if (movingFrames >= cfg.startFrames)
                        {
                            State = SegmenterState.Capturing;
                            capture.Clear();
                            capture.Add(sample);
                            endStillFrames = 0;
                        }
                    }
                    else
                    {
                        movingFrames = 0;
                    }
                    return SegmentEvent.None;

                case SegmenterState.Capturing:
                    capture.Add(sample);
                    float elapsed = (float)(sample.Time - capture[0].Time);
                    if (speed < cfg.armSpeed) endStillFrames++;
                    else endStillFrames = 0;

                    bool byStillness = endStillFrames >= cfg.endFrames;
                    bool byCap = elapsed >= cfg.maxSegmentSeconds;
                    if (byStillness || byCap)
                    {
                        State = SegmenterState.Arming;
                        ResetTracking();
                        if (elapsed < cfg.minSegmentSeconds)
                        {
                            return SegmentEvent.SegmentRejectedShort;
                        }
                        LastClosedSegment = new List<PalmSample>(capture);
                        return SegmentEvent.SegmentClosed;
                    }
                    return SegmentEvent.None;
            }
            return SegmentEvent.None;
        }

        private void ResetTracking()
        {
            stillSince = -1;
            movingFrames = 0;
            endStillFrames = 0;
        }
    }

    /// <summary>Signed broad horizontal swipe detection, enabled only while a fork map waits for direction.</summary>
    public static class DirectionSwipeDetector
    {
        /// <summary>Returns +1 (right), -1 (left), or 0 (not a direction swipe). Panel coords are mirrored.</summary>
        public static int Detect(IReadOnlyList<PalmSample> segment, BeforeRecognitionConfig cfg)
        {
            if (segment == null || segment.Count < 2) return 0;
            float duration = (float)(segment[segment.Count - 1].Time - segment[0].Time);
            if (duration < cfg.minSegmentSeconds || duration > cfg.maxSegmentSeconds + 1e-3f) return 0;

            float dx = segment[segment.Count - 1].Position.x - segment[0].Position.x;
            float dy = segment[segment.Count - 1].Position.y - segment[0].Position.y;
            if (Mathf.Abs(dx) < cfg.directionMinDx) return 0;
            if (Mathf.Abs(dx) < cfg.directionAxisRatio * Mathf.Abs(dy)) return 0;

            int finalSign = dx >= 0 ? 1 : -1;
            int agree = 0, totalDeltas = 0;
            for (int i = 1; i < segment.Count; i++)
            {
                float step = segment[i].Position.x - segment[i - 1].Position.x;
                if (Mathf.Abs(step) < 1e-5f) continue;
                totalDeltas++;
                if ((step >= 0 ? 1 : -1) == finalSign) agree++;
            }
            if (totalDeltas == 0 || (float)agree / totalDeltas < cfg.directionSignShare) return 0;
            return finalSign;
        }
    }
}
