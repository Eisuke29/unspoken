using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Before.Production.Tests
{
    public class ProductionSlice46PlayTests
    {
        private GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.Destroy(_root);
        }

        [UnityTest]
        public IEnumerator StorySuspensionDoesNotTrapAnExistingImpulse()
        {
            MakeRig(18947, out NulProductionController controller,
                out ProductionGameLoop _,
                out ProductionCameraImpulsePresenter impulse);
            yield return null;

            impulse.TriggerImpulse(
                ProductionCameraImpulsePresenter.ImpulseKind.Failure);
            controller.SetStorySuspended(true);
            yield return new WaitForSecondsRealtime(0.20f);

            Assert.AreEqual(ProductionCameraImpulsePresenter.ImpulseKind.None,
                impulse.ActiveImpulse);
            Assert.AreEqual(Vector2.zero, impulse.CurrentLogicalOffset);
        }

        [UnityTest]
        public IEnumerator TerminalPauseLetsFailureImpulseFinish()
        {
            MakeRig(18948, out NulProductionController controller,
                out ProductionGameLoop gameLoop,
                out ProductionCameraImpulsePresenter impulse);
            yield return null;

            SetAutoProperty(gameLoop, "IsGameOver", true);
            controller.Nul.Paused = true;
            impulse.TriggerImpulse(
                ProductionCameraImpulsePresenter.ImpulseKind.Failure);
            yield return new WaitForSecondsRealtime(0.20f);

            Assert.AreEqual(ProductionCameraImpulsePresenter.ImpulseKind.None,
                impulse.ActiveImpulse);
            Assert.AreEqual(Vector2.zero, impulse.CurrentLogicalOffset);
        }

        private void MakeRig(int port, out NulProductionController controller,
            out ProductionGameLoop gameLoop,
            out ProductionCameraImpulsePresenter impulse)
        {
            _root = new GameObject("slice46-runner");
            _root.SetActive(false);
            _root.AddComponent<Rigidbody2D>();
            controller = _root.AddComponent<NulProductionController>();
            controller.udpPort = port;
            controller.autoLaunchSidecar = false;
            controller.autoRun = false;
            gameLoop = _root.AddComponent<ProductionGameLoop>();

            var cameraObject = new GameObject("slice46-camera");
            cameraObject.transform.SetParent(_root.transform, false);
            cameraObject.AddComponent<Camera>().orthographic = true;
            impulse = cameraObject.AddComponent<ProductionCameraImpulsePresenter>();
            impulse.Controller = controller;
            impulse.GameLoop = gameLoop;
            _root.SetActive(true);
        }

        private static void SetAutoProperty(object target, string property,
                                            object value)
        {
            FieldInfo field = target.GetType().GetField("<" + property +
                ">k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field);
            field.SetValue(target, value);
        }
    }
}
