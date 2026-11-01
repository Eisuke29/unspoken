using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Before.Production.Tests
{
    public class ProductionSlice5PlayTests
    {
        [UnityTest]
        public IEnumerator ProductionReceiverListensContinuously_WhenSidecarLaunchIsOptedOut()
        {
            var go = new GameObject("slice5-receiver-rig");
            go.SetActive(false);
            go.AddComponent<Rigidbody2D>();
            var controller = go.AddComponent<NulProductionController>();
            controller.udpPort = 8894;
            controller.autoRun = false;
            controller.autoLaunchSidecar = false;
            go.SetActive(true);

            yield return null;
            yield return null;
            Assert.IsTrue(controller.ReceiverListening);
            Assert.IsFalse(controller.SidecarLaunchAttempted);
            Assert.AreEqual("idle", controller.SidecarStatus);

            go.SetActive(false);
            yield return null;
            Assert.IsFalse(controller.ReceiverListening);
            go.SetActive(true);
            yield return null;
            Assert.IsTrue(controller.ReceiverListening,
                "receiver must recover after a Play/domain lifecycle disable-enable");

            Object.Destroy(go);
            yield return null;
        }
    }
}
