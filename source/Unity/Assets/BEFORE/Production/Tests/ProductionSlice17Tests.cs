using NUnit.Framework;

namespace Before.Production.Tests
{
    public class ProductionSlice17Tests
    {
        [Test]
        public void FrameSummaryUsesDurationWeightedAverageAndWorstOnePercent()
        {
            var frames = new float[100];
            for (int i = 0; i < frames.Length; i++) frames[i] = 1f / 60f;
            frames[99] = 0.1f;

            float average = ProductionPerformanceMonitor.CalculateAverageFps(frames);
            float low = ProductionPerformanceMonitor.CalculateOnePercentLowFps(frames);
            Assert.AreEqual(57.142857f, average, 0.001f);
            Assert.AreEqual(10f, low, 0.001f);
        }

        [Test]
        public void InvalidFrameDurationsAreIgnoredAndBytesUseBinaryMegabytes()
        {
            float[] frames = { 1f / 50f, 0f, -1f, float.NaN, float.PositiveInfinity };
            Assert.AreEqual(50f, ProductionPerformanceMonitor.CalculateAverageFps(frames), 0.001f);
            Assert.AreEqual(50f, ProductionPerformanceMonitor.CalculateOnePercentLowFps(frames), 0.001f);
            Assert.AreEqual(50f, ProductionPerformanceMonitor.BytesToMb(50L * 1024L * 1024L));
        }
    }
}
