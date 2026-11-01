using System;
using Before.Capture;
using Before.Diagnostics;
using NUnit.Framework;
using UnityEngine;

namespace Before.Tests
{
    public sealed class HandSmokeEditModeTests
    {
        [Test]
        public void PercentileBuffer_ReportsMedianAndP95()
        {
            var buffer = new PercentileBuffer(600);
            for (int i = 1; i <= 100; i++) buffer.Add(i);
            Assert.AreEqual(100, buffer.Count);
            Assert.AreEqual(50f, buffer.Percentile(0.5f), 1.5f);
            Assert.AreEqual(95f, buffer.Percentile(0.95f), 1.5f);
        }

        [Test]
        public void PercentileBuffer_WrapsAtCapacityKeepingNewestSamples()
        {
            var buffer = new PercentileBuffer(10);
            for (int i = 0; i < 50; i++) buffer.Add(1000f);
            for (int i = 0; i < 10; i++) buffer.Add(5f);
            Assert.AreEqual(10, buffer.Count);
            Assert.AreEqual(5f, buffer.Percentile(0.5f), 0.001f);
            Assert.AreEqual(5f, buffer.Percentile(0.95f), 0.001f);
        }

        [Test]
        public void PercentileBuffer_EmptyReturnsZero()
        {
            var buffer = new PercentileBuffer(10);
            Assert.AreEqual(0f, buffer.Percentile(0.5f));
        }

        [Test]
        public void SingleFlightGate_AllowsOnlyOneInFlight()
        {
            var gate = new SingleFlightGate();
            Assert.IsTrue(gate.TryBegin());
            Assert.IsFalse(gate.TryBegin(), "Second concurrent begin must be rejected.");
            Assert.AreEqual(1, gate.InFlight);
            gate.End();
            Assert.AreEqual(0, gate.InFlight);
            Assert.IsTrue(gate.TryBegin());
            gate.End();
            Assert.AreEqual(1, gate.MaxObserved, "Max observed in-flight must stay 1 under correct use.");
        }

        [Test]
        public void SingleFlightGate_EndWithoutBeginDoesNotGoNegative()
        {
            var gate = new SingleFlightGate();
            gate.End();
            Assert.AreEqual(0, gate.InFlight);
        }

        [Test]
        public void RateMonitor_MedianHzOverWindow()
        {
            var monitor = new RateMonitor();
            // 15 events per second for 60 seconds.
            for (double t = 0; t < 60.0; t += 1.0 / 15.0) monitor.AddEvent(t);
            double median = monitor.MedianHz(60.0, 60.0);
            Assert.GreaterOrEqual(median, 14.0, "Median rate over 60 s should be at least 14 Hz for a 15 Hz stream.");
        }

        [Test]
        public void RateMonitor_SilenceYieldsZeroMedian()
        {
            var monitor = new RateMonitor();
            for (double t = 0; t < 5.0; t += 0.1) monitor.AddEvent(t);
            double median = monitor.MedianHz(200.0, 60.0);
            Assert.AreEqual(0.0, median, 0.001);
        }

        [Test]
        public void HandObservation_NoHandHasPresenceFalseAndSafeLandmarks()
        {
            var observation = HandObservation.NoHand(1.0, 0.2f, 12f);
            Assert.IsFalse(observation.HandPresent);
            Assert.AreEqual(Vector3.zero, observation.GetLandmark(0));
            Assert.AreEqual(Vector3.zero, observation.GetLandmark(20));
        }

        [Test]
        public void HandObservation_CopiesLandmarksImmutably()
        {
            var source = new Vector3[21];
            source[3] = new Vector3(0.25f, 0.5f, 0.1f);
            var observation = new HandObservation(2.0, true, 0.9f, 20f, source);
            source[3] = Vector3.one * 99f;
            Assert.AreEqual(new Vector3(0.25f, 0.5f, 0.1f), observation.GetLandmark(3));
        }

        [Test]
        public void BlazeHandTracker_DisposeIsIdempotentAndUninitializedDetectIsCanceled()
        {
            var tracker = new BlazeHandTracker();
            Assert.IsFalse(tracker.IsInitialized);
            tracker.Dispose();
            tracker.Dispose(); // second dispose must be a no-op
            Assert.IsFalse(tracker.IsInitialized);
        }

        [Test]
        public void BlazeHandTracker_InitializeRejectsMissingInputs()
        {
            var tracker = new BlazeHandTracker();
            Assert.Throws<ArgumentException>(() => tracker.Initialize(null, null, null));
            tracker.Dispose();
        }
    }
}
