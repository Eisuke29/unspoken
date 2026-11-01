using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Before.Production.Tests
{
    public class ProductionSlice23PlayTests
    {
        private GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.Destroy(_root);
        }

        [UnityTest]
        public IEnumerator ControllerCreatesOnlyUdpAndOptInSidecarServicesWithoutWebcam()
        {
            _root = new GameObject("slice23-runtime-boundary");
            _root.SetActive(false);
            _root.AddComponent<Rigidbody2D>().gravityScale = 0f;
            var stand = new GameObject("stand").AddComponent<BoxCollider2D>();
            stand.transform.SetParent(_root.transform, false);
            var crouch = new GameObject("crouch").AddComponent<BoxCollider2D>();
            crouch.transform.SetParent(_root.transform, false);
            crouch.enabled = false;

            var controller = _root.AddComponent<NulProductionController>();
            controller.udpPort = 8898;
            controller.autoRun = false;
            controller.autoLaunchSidecar = false;
            SetPrivate(controller, "standingCollider", stand);
            SetPrivate(controller, "crouchCollider", crouch);
            _root.SetActive(true);
            yield return null;

            Assert.IsTrue(controller.ReceiverListening);
            Assert.IsFalse(controller.SidecarLaunchAttempted);
            Assert.IsFalse(controller.SidecarRunning);
            Assert.AreEqual(0, Resources.FindObjectsOfTypeAll<WebCamTexture>().Length);
            Assert.AreEqual(1, _root.GetComponents<UdpCommandReceiver>().Length);
            Assert.AreEqual(1, _root.GetComponents<SidecarProcessManager>().Length);
        }

        private static void SetPrivate(object target, string field, object value)
        {
            FieldInfo info = target.GetType().GetField(field,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(info, field);
            info.SetValue(target, value);
        }
    }
}
