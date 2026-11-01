using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Before.Production.Tests
{
    public class ProductionSlice35PlayTests
    {
        private GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.Destroy(_root);
        }

        [UnityTest]
        public IEnumerator PauseConsumesFirstCommandOfNewSessionWithoutReplay()
        {
            NulProductionController controller = BuildRig();
            yield return null;

            controller.InjectDatagram(Cmd("d170-old", 37000, 7, "STOP_OFF"));
            Assert.AreEqual(1, controller.Bus.Published);
            Assert.AreEqual("d170-old", controller.Router.Session.ActiveSession);

            controller.Nul.Paused = true;
            controller.InjectDatagram(Cmd("d170-new", 37001, 1, "JUMP"));
            controller.InjectDatagram(Cmd("d170-new", 37001, 1, "JUMP"));
            Assert.AreEqual(2, controller.Bus.Published,
                "the first new-session command crosses the Bus exactly once");
            Assert.AreEqual("d170-new", controller.Router.Session.ActiveSession);
            Assert.AreEqual(1, controller.Router.Dedup.Count);
            Assert.AreEqual(0, controller.Nul.JumpCount);
            Assert.IsFalse(controller.Airborne);

            controller.Nul.Paused = false;
            yield return null;
            controller.InjectDatagram(Cmd("d170-new", 37001, 1, "JUMP"));
            Assert.AreEqual(2, controller.Bus.Published,
                "the paused command remains consumed after resume");
            Assert.AreEqual(0, controller.Nul.JumpCount);
            Assert.IsFalse(controller.Airborne);

            controller.InjectDatagram(Cmd("d170-new", 37001, 2, "JUMP"));
            Assert.AreEqual(3, controller.Bus.Published);
            Assert.AreEqual(1, controller.Nul.JumpCount);
            Assert.IsTrue(controller.Airborne,
                "the next unique command reacts immediately after resume");

            controller.InjectDatagram(Cmd("d170-old", 37000, 8, "JUMP"));
            Assert.AreEqual(3, controller.Bus.Published);
            Assert.AreEqual(1, controller.Nul.JumpCount);
            Assert.AreEqual(1, controller.Router.Session.DiscardedStale);
        }

        private NulProductionController BuildRig()
        {
            _root = new GameObject("slice35-pause-session-rig");
            _root.SetActive(false);
            _root.AddComponent<Rigidbody2D>().gravityScale = 0f;
            var stand = new GameObject("stand").AddComponent<BoxCollider2D>();
            stand.transform.SetParent(_root.transform, false);
            var crouch = new GameObject("crouch").AddComponent<BoxCollider2D>();
            crouch.transform.SetParent(_root.transform, false);
            crouch.enabled = false;
            var controller = _root.AddComponent<NulProductionController>();
            controller.udpPort = 8925;
            controller.autoRun = false;
            controller.autoLaunchSidecar = false;
            SetPrivate(controller, "standingCollider", stand);
            SetPrivate(controller, "crouchCollider", crouch);
            _root.SetActive(true);
            return controller;
        }

        private static string Cmd(string session, int start, int id, string type)
            => "{\"v\":1,\"kind\":\"cmd\",\"session\":\"" + session +
               "\",\"sstart\":" + start + ",\"cmdId\":" + id +
               ",\"seq\":" + id + ",\"tUs\":0,\"type\":\"" + type +
               "\",\"phase\":\"fire\"}";

        private static void SetPrivate(object target, string field, object value)
        {
            FieldInfo info = target.GetType().GetField(field,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(info, field);
            info.SetValue(target, value);
        }
    }
}
