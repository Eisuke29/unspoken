using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Before.Production.Tests
{
    public class ProductionSlice45PlayTests
    {
        private GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.Destroy(_root);
        }

        [UnityTest]
        public IEnumerator JumpAndLandingTransitionsTriggerTheirPresentationImpulses()
        {
            NulProductionController controller = MakeRig(18945,
                out ProductionCameraImpulsePresenter impulse);
            yield return null;

            controller.InjectDatagram(Cmd(1, "JUMP"));
            for (int i = 0; i < 4 && impulse.ActiveImpulse !=
                 ProductionCameraImpulsePresenter.ImpulseKind.Takeoff; i++)
                yield return null;
            Assert.AreEqual(ProductionCameraImpulsePresenter.ImpulseKind.Takeoff,
                impulse.ActiveImpulse);

            yield return new WaitForSeconds(0.85f);
            Assert.AreEqual(NulProductionController.NulAnimState.Landing,
                controller.State);
            Assert.AreEqual(ProductionCameraImpulsePresenter.ImpulseKind.Landing,
                impulse.ActiveImpulse);
        }

        [UnityTest]
        public IEnumerator PauseFreezesImpulseAndAccessibilityDisableClearsIt()
        {
            NulProductionController controller = MakeRig(18946,
                out ProductionCameraImpulsePresenter impulse);
            yield return null;

            impulse.TriggerImpulse(
                ProductionCameraImpulsePresenter.ImpulseKind.Failure);
            yield return null;
            controller.Nul.Paused = true;
            yield return null;
            Vector2 paused = impulse.CurrentLogicalOffset;
            yield return new WaitForSecondsRealtime(0.10f);
            Assert.AreEqual(paused.x, impulse.CurrentLogicalOffset.x, 0.0001f);
            Assert.AreEqual(paused.y, impulse.CurrentLogicalOffset.y, 0.0001f);

            controller.Nul.Paused = false;
            yield return new WaitForSecondsRealtime(0.20f);
            Assert.AreEqual(ProductionCameraImpulsePresenter.ImpulseKind.None,
                impulse.ActiveImpulse);

            impulse.TriggerImpulse(
                ProductionCameraImpulsePresenter.ImpulseKind.Failure);
            impulse.ShakeEnabled = false;
            Assert.AreEqual(ProductionCameraImpulsePresenter.ImpulseKind.None,
                impulse.ActiveImpulse);
            Assert.AreEqual(Vector2.zero, impulse.CurrentLogicalOffset);
        }

        private NulProductionController MakeRig(int port,
            out ProductionCameraImpulsePresenter impulse)
        {
            _root = new GameObject("slice45-runner");
            _root.SetActive(false);
            _root.AddComponent<Rigidbody2D>();
            var controller = _root.AddComponent<NulProductionController>();
            controller.udpPort = port;
            controller.autoLaunchSidecar = false;
            controller.autoRun = false;

            var cameraObject = new GameObject("slice45-camera");
            cameraObject.transform.SetParent(_root.transform, false);
            cameraObject.AddComponent<Camera>().orthographic = true;
            impulse = cameraObject.AddComponent<ProductionCameraImpulsePresenter>();
            impulse.Controller = controller;
            _root.SetActive(true);
            return controller;
        }

        private static string Cmd(int id, string type)
            => "{\"v\":1,\"kind\":\"cmd\",\"session\":\"slice45\"," +
               "\"sstart\":945,\"cmdId\":" + id +
               ",\"seq\":1,\"tUs\":0,\"type\":\"" + type +
               "\",\"phase\":\"fire\"}";
    }
}
