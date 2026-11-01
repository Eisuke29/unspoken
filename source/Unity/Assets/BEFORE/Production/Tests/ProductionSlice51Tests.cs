using NUnit.Framework;
using UnityEngine;

namespace Before.Production.Tests
{
    public class ProductionSlice51Tests
    {
        private GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
        }

        [Test]
        public void SidecarStartInfoCarriesSeparateLoopbackControlPort()
        {
            var info = SidecarProcessManager.CreateStartInfo(
                @"C:\UnityProjects\BEFORE", 8765, 8766);
            StringAssert.Contains("--udp 8765", info.Arguments);
            StringAssert.Contains("--control 8766", info.Arguments);
            StringAssert.DoesNotContain("http://", info.Arguments);
        }

        [Test]
        public void ControlStatusRejectsRgbAndExposesAtomicSampleEvent()
        {
            _root = new GameObject("slice51-control");
            var client = _root.AddComponent<SidecarTeachingControlClient>();
            client.TransportEnabled = false;

            Assert.IsTrue(client.ApplyStatusJson(
                "{\"active\":true,\"kind\":\"jump\",\"required\":3," +
                "\"accepted\":1,\"phase\":\"accepted\",\"reason\":\"\"," +
                "\"event_id\":1,\"finalized\":false,\"rgb_frames_sent\":0}"));
            Assert.IsTrue(client.TryConsumeEvent(out SidecarTeachingEvent evt));
            Assert.IsTrue(evt.WasAccepted);
            Assert.AreEqual(1, evt.Accepted);
            Assert.IsFalse(evt.Finalized);

            Assert.IsFalse(client.ApplyStatusJson(
                "{\"event_id\":2,\"rgb_frames_sent\":1}"));
            Assert.AreEqual(0, client.RgbFramesSent);
        }

        [Test]
        public void ProductionHudUsesOnlyLargeStageAndOneLineJapaneseRejections()
        {
            Assert.AreEqual("離すのが早すぎます",
                ProductionTeachingPresenter.JapaneseRejectionFor("early_release"));
            Assert.AreEqual("手を見失いました。手をフレーム内へ",
                ProductionTeachingPresenter.JapaneseRejectionFor("tracking_lost"));
            Assert.AreEqual("形が不安定です",
                ProductionTeachingPresenter.JapaneseRejectionFor("unstable"));
            Assert.AreEqual("前のサンプルと一致しません",
                ProductionTeachingPresenter.JapaneseRejectionFor("inconsistent"));
            StringAssert.DoesNotContain("\n",
                ProductionTeachingPresenter.JapaneseRejectionFor("out_of_frame"));
        }

        [Test]
        public void SemanticControlClientNeverQueuesSyntheticKeys()
        {
            _root = new GameObject("slice51-actions");
            var client = _root.AddComponent<SidecarTeachingControlClient>();
            client.TransportEnabled = false;
            client.BeginTeaching(ProductionObstacleKind.Crouch, false);
            Assert.AreEqual("begin", client.LastQueuedAction);
            client.PressSpace();
            Assert.AreEqual("press", client.LastQueuedAction);
            client.ReleaseSpace();
            Assert.AreEqual("release", client.LastQueuedAction);
            client.SetDirectionMode(true);
            Assert.AreEqual("direction", client.LastQueuedAction);
        }
    }
}
