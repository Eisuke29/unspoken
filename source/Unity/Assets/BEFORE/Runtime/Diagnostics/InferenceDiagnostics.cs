using System;
using System.Collections.Generic;

namespace Before.Diagnostics
{
    /// <summary>Fixed-capacity rolling sample buffer with allocation-free percentile queries (scratch reused).</summary>
    public sealed class PercentileBuffer
    {
        private readonly float[] samples;
        private readonly float[] scratch;
        private int count;
        private int next;

        public PercentileBuffer(int capacity)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            samples = new float[capacity];
            scratch = new float[capacity];
        }

        public int Count => count;

        public void Add(float value)
        {
            samples[next] = value;
            next = (next + 1) % samples.Length;
            if (count < samples.Length) count++;
        }

        /// <summary>percentile in [0,1]; returns 0 when empty.</summary>
        public float Percentile(float percentile)
        {
            if (count == 0) return 0f;
            Array.Copy(samples, scratch, count);
            Array.Sort(scratch, 0, count);
            int index = (int)Math.Round(percentile * (count - 1));
            if (index < 0) index = 0;
            if (index >= count) index = count - 1;
            return scratch[index];
        }

        public void Clear()
        {
            count = 0;
            next = 0;
        }
    }

    /// <summary>Guards that at most one inference request is in flight and records the observed maximum.</summary>
    public sealed class SingleFlightGate
    {
        public int InFlight { get; private set; }
        public int MaxObserved { get; private set; }

        public bool TryBegin()
        {
            if (InFlight >= 1) return false;
            InFlight++;
            if (InFlight > MaxObserved) MaxObserved = InFlight;
            return true;
        }

        public void End()
        {
            if (InFlight > 0) InFlight--;
        }

        public void Reset()
        {
            InFlight = 0;
            MaxObserved = 0;
        }
    }

    /// <summary>Tracks event timestamps to report instantaneous rate and the median per-second rate over a window.</summary>
    public sealed class RateMonitor
    {
        private readonly Queue<double> events = new Queue<double>(256);
        private readonly List<int> buckets = new List<int>(128);
        private const double RetainSeconds = 130.0;
        private double firstEvent = double.NaN;

        public void AddEvent(double time)
        {
            if (double.IsNaN(firstEvent)) firstEvent = time;
            events.Enqueue(time);
            Trim(time);
        }

        public int CountInLastSecond(double now)
        {
            Trim(now);
            int n = 0;
            foreach (double t in events)
            {
                if (now - t <= 1.0) n++;
            }
            return n;
        }

        /// <summary>
        /// Median of whole-second event counts. Only counts seconds that have actually elapsed since the
        /// first event, so a short run (e.g. 3 s) is not diluted by ~57 empty buckets to a false 0 Hz.
        /// </summary>
        public double MedianHz(double now, double windowSeconds)
        {
            Trim(now);
            if (double.IsNaN(firstEvent)) return 0.0;
            double elapsed = now - firstEvent;
            // number of fully-elapsed seconds we can measure, capped to the rolling window
            int windowCount = (int)System.Math.Min(windowSeconds, System.Math.Floor(elapsed));
            if (windowCount < 1)
            {
                // sub-second span: fall back to the instantaneous last-second count so tracked hands never read 0
                return CountInLastSecond(now);
            }
            buckets.Clear();
            for (int i = 0; i < windowCount; i++) buckets.Add(0);
            // count only fully-elapsed seconds (age in [1, windowCount+1)); the current partial second
            // (age < 1) is excluded so it cannot drag the median down.
            foreach (double t in events)
            {
                double age = now - t;
                if (age < 1.0) continue;
                int bucket = (int)age - 1;
                if (bucket >= 0 && bucket < windowCount) buckets[bucket]++;
            }
            buckets.Sort();
            int mid = buckets.Count / 2;
            if (buckets.Count % 2 == 1) return buckets[mid];
            return (buckets[mid - 1] + buckets[mid]) * 0.5;
        }

        public void Clear()
        {
            events.Clear();
            firstEvent = double.NaN;
        }

        private void Trim(double now)
        {
            while (events.Count > 0 && now - events.Peek() > RetainSeconds)
            {
                events.Dequeue();
            }
        }
    }
}
