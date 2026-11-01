using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Before.Production.Tests
{
    public class ProductionSlice17PlayTests
    {
        [UnityTest]
        public IEnumerator MonitorWarmsUpSamplesUnscaledFramesAndResetsWithoutGameplayOwnership()
        {
            var go = new GameObject("slice17-performance");
            var monitor = go.AddComponent<ProductionPerformanceMonitor>();
            monitor.WarmupSeconds = 0f;
            monitor.ResetMeasurement();
            yield return null;
            yield return null;

            Assert.IsTrue(monitor.WarmupComplete);
            Assert.GreaterOrEqual(monitor.SampleCount, 1);
            Assert.Greater(monitor.AverageFps, 0f);
            Assert.Greater(monitor.OnePercentLowFps, 0f);

            Time.timeScale = 0f;
            int before = monitor.SampleCount;
            yield return null;
            Assert.Greater(monitor.SampleCount, before, "sampling uses unscaled frame time");
            Time.timeScale = 1f;

            monitor.ResetMeasurement();
            Assert.AreEqual(0, monitor.SampleCount);
            Object.Destroy(go);
        }
    }
}
