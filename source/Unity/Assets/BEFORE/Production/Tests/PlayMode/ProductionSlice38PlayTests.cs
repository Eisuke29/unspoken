using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Before.Production.Tests
{
    public class ProductionSlice38PlayTests
    {
        private GameObject _root;
        private ProductionCourseDefinition _definition;

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.Destroy(_root);
            if (_definition != null) Object.Destroy(_definition);
        }

        [UnityTest]
        public IEnumerator NewSessionReactsDuringRecoveryThenRemainsConsumedAfterRecovery()
        {
            BuildRig(out NulProductionController controller,
                out ProductionCourseLoop course, out ProductionObstacleJudge judge,
                out ProductionGameLoop loop);
            yield return null;

            controller.InjectDatagram(Cmd("d173-old", 40000, 7, "STOP_OFF"));
            ProductionCourseLoop.ObstacleSlot slot = course.Slots[0];
            controller.RecoverAt(new Vector2(slot.WorldX, 0f));
            judge.EvaluateNowAt(controller.GameplayTime);
            Assert.IsTrue(loop.InRecovery);
            Assert.AreEqual(2, loop.Lives);
            Assert.IsTrue(controller.MovementSuspended);

            controller.InjectDatagram(Cmd("d173-new", 40001, 1, "STOP_ON"));
            controller.InjectDatagram(Cmd("d173-new", 40001, 1, "STOP_ON"));
            yield return null;
            Assert.AreEqual(2, controller.Bus.Published);
            Assert.IsTrue(controller.Nul.StopActive);
            Assert.AreEqual(NulProductionController.NulAnimState.Stop, controller.State);
            Assert.AreEqual("d173-new", controller.Router.Session.ActiveSession);

            controller.InjectDatagram(Cmd("d173-old", 40000, 8, "STOP_OFF"));
            Assert.IsTrue(controller.Nul.StopActive,
                "late old-session STOP_OFF cannot cancel the new-session reaction");
            Assert.AreEqual(1, controller.Router.Session.DiscardedStale);

            yield return new WaitForSeconds(ProductionGameLoop.RecoveryFreezeSeconds + 0.15f);
            Assert.IsFalse(loop.InRecovery);
            Assert.IsFalse(controller.MovementSuspended);
            Assert.IsFalse(controller.Nul.StopActive,
                "checkpoint recovery clears transient reaction state");

            controller.InjectDatagram(Cmd("d173-new", 40001, 1, "STOP_ON"));
            Assert.AreEqual(2, controller.Bus.Published,
                "the recovery clear must not replay or un-dedup the prior command");
            Assert.IsFalse(controller.Nul.StopActive);

            controller.InjectDatagram(Cmd("d173-new", 40001, 2, "STOP_ON"));
            Assert.AreEqual(3, controller.Bus.Published);
            Assert.IsTrue(controller.Nul.StopActive,
                "the next unique command reacts during recovery grace");
        }

        private void BuildRig(out NulProductionController controller,
            out ProductionCourseLoop course, out ProductionObstacleJudge judge,
            out ProductionGameLoop loop)
        {
            _root = new GameObject("slice38-recovery-session-rig");
            _root.SetActive(false);
            _root.AddComponent<Rigidbody2D>().gravityScale = 0f;
            var stand = new GameObject("stand").AddComponent<BoxCollider2D>();
            stand.transform.SetParent(_root.transform, false);
            var crouch = new GameObject("crouch").AddComponent<BoxCollider2D>();
            crouch.transform.SetParent(_root.transform, false);
            crouch.enabled = false;
            controller = _root.AddComponent<NulProductionController>();
            controller.udpPort = 8928;
            controller.autoRun = false;
            controller.autoLaunchSidecar = false;
            SetPrivate(controller, "standingCollider", stand);
            SetPrivate(controller, "crouchCollider", crouch);
            course = _root.AddComponent<ProductionCourseLoop>();
            _definition = ScriptableObject.CreateInstance<ProductionCourseDefinition>();
            _definition.LoadProductionDefaults();
            course.Definition = _definition;
            judge = _root.AddComponent<ProductionObstacleJudge>();
            loop = _root.AddComponent<ProductionGameLoop>();
            _root.SetActive(true);
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
