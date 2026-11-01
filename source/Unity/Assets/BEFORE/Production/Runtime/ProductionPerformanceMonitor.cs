using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;

namespace Before.Production
{
    /// <summary>
    /// Debug/evidence-only local frame and memory sampler. It never changes
    /// gameplay, command intake, time scale, presentation, or Sidecar state.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProductionPerformanceMonitor : MonoBehaviour
    {
        public const float DefaultWarmupSeconds = 30f;
        public const float AverageFpsTarget = 60f;
        public const float OnePercentLowTarget = 50f;
        public const float MemoryGrowthLimitMb = 50f;

        [SerializeField] private float warmupSeconds = DefaultWarmupSeconds;

        private readonly List<float> _frameSeconds = new List<float>(40000);
        private float _elapsed;
        private long _baselineMemory;
        private long _latestMemory;

        public float WarmupSeconds { get => warmupSeconds; set => warmupSeconds = Mathf.Max(0f, value); }
        public int SampleCount => _frameSeconds.Count;
        public float AverageFps => CalculateAverageFps(_frameSeconds);
        public float OnePercentLowFps => CalculateOnePercentLowFps(_frameSeconds);
        public float MemoryGrowthMb => BytesToMb(Math.Max(0L, _latestMemory - _baselineMemory));
        public bool WarmupComplete => _elapsed >= warmupSeconds;
        public bool MeetsMeasuredTargets => WarmupComplete && SampleCount > 0 &&
            AverageFps >= AverageFpsTarget && OnePercentLowFps >= OnePercentLowTarget &&
            MemoryGrowthMb < MemoryGrowthLimitMb;

        private void OnEnable() => ResetMeasurement();

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (!(dt > 0f) || float.IsNaN(dt) || float.IsInfinity(dt)) return;
            _elapsed += dt;
            if (_elapsed < warmupSeconds) return;
            if (_baselineMemory == 0L) _baselineMemory = Profiler.GetTotalAllocatedMemoryLong();
            _latestMemory = Profiler.GetTotalAllocatedMemoryLong();
            _frameSeconds.Add(dt);
        }

        public void ResetMeasurement()
        {
            _elapsed = 0f;
            _baselineMemory = 0L;
            _latestMemory = 0L;
            _frameSeconds.Clear();
        }

        public static float CalculateAverageFps(IReadOnlyList<float> frameSeconds)
        {
            if (frameSeconds == null || frameSeconds.Count == 0) return 0f;
            double total = 0d;
            int valid = 0;
            for (int i = 0; i < frameSeconds.Count; i++)
            {
                float dt = frameSeconds[i];
                if (!(dt > 0f) || float.IsNaN(dt) || float.IsInfinity(dt)) continue;
                total += dt;
                valid++;
            }
            return valid == 0 || total <= 0d ? 0f : (float)(valid / total);
        }

        public static float CalculateOnePercentLowFps(IReadOnlyList<float> frameSeconds)
        {
            if (frameSeconds == null || frameSeconds.Count == 0) return 0f;
            var valid = new List<float>(frameSeconds.Count);
            for (int i = 0; i < frameSeconds.Count; i++)
            {
                float dt = frameSeconds[i];
                if (dt > 0f && !float.IsNaN(dt) && !float.IsInfinity(dt)) valid.Add(dt);
            }
            if (valid.Count == 0) return 0f;
            valid.Sort((a, b) => b.CompareTo(a));
            int slowCount = Mathf.Max(1, Mathf.CeilToInt(valid.Count * 0.01f));
            double total = 0d;
            for (int i = 0; i < slowCount; i++) total += valid[i];
            return total <= 0d ? 0f : (float)(slowCount / total);
        }

        public static float BytesToMb(long bytes) => bytes / (1024f * 1024f);
    }
}
