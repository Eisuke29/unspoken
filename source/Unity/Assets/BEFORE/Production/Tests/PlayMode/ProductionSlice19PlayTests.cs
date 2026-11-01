using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace Before.Production.Tests
{
    public class ProductionSlice19PlayTests : InputTestFixture
    {
        private GameObject _root;
        private Keyboard _keyboard;

        private static void SetPrivate(object target, string field, object value)
        {
            FieldInfo info = target.GetType().GetField(field,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(info, field);
            info.SetValue(target, value);
        }

        [TearDown]
        public override void TearDown()
        {
            if (_root != null) Object.Destroy(_root);
            base.TearDown();
        }

        [UnityTest]
        public IEnumerator PKeyUpdatesNormalPausePresentationWithoutChangingHudOwnership()
        {
            _root = new GameObject("slice19-pause-hud");
            _root.SetActive(false);
            _root.AddComponent<Rigidbody2D>().gravityScale = 0f;
            var stand = new GameObject("stand").AddComponent<BoxCollider2D>();
            stand.transform.SetParent(_root.transform, false);
            var crouch = new GameObject("crouch").AddComponent<BoxCollider2D>();
            crouch.transform.SetParent(_root.transform, false);
            crouch.enabled = false;

            var controller = _root.AddComponent<NulProductionController>();
            controller.udpPort = 8894;
            controller.autoRun = false;
            SetPrivate(controller, "standingCollider", stand);
            SetPrivate(controller, "crouchCollider", crouch);
            var loop = _root.AddComponent<ProductionGameLoop>();
            loop.Controller = controller;
            var hud = _root.AddComponent<ProductionHud>();
            hud.GameLoop = loop;
            _root.SetActive(true);
            _keyboard = InputSystem.AddDevice<Keyboard>();
            yield return null;

            Assert.IsFalse(hud.ShowPaused);
            Press(_keyboard.pKey, queueEventOnly: true);
            yield return null;
            Assert.IsTrue(hud.ShowPaused);

            Release(_keyboard.pKey, queueEventOnly: true);
            yield return null;
            Press(_keyboard.pKey, queueEventOnly: true);
            yield return null;
            Assert.IsFalse(hud.ShowPaused);
        }
    }
}
