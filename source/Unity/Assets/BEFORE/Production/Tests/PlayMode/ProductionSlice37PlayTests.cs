using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Before.Production.Tests
{
    public class ProductionSlice37PlayTests
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
        public IEnumerator TeachingContinuesAcrossNewSessionAndIgnoresLateOldSample()
        {
            BuildRig(out NulProductionController controller,
                out ProductionCourseLoop course, out ProductionTeachingFlow teaching);
            yield return null;

            float hazardX = course.Slots[0].WorldX;
            teaching.BeginTeaching(1, ProductionObstacleKind.Jump, hazardX);
            Assert.AreEqual(ProductionTeachingStage.Collecting, teaching.Stage);

            controller.InjectDatagram(Cmd("d172-old", 39000, 7, "JUMP"));
            Assert.AreEqual(1, teaching.AcceptedSamples);

            controller.InjectDatagram(Cmd("d172-new", 39001, 1, "JUMP"));
            controller.InjectDatagram(Cmd("d172-new", 39001, 1, "JUMP"));
            Assert.AreEqual(2, teaching.AcceptedSamples);
            Assert.AreEqual("d172-new", controller.Router.Session.ActiveSession);

            controller.InjectDatagram(Cmd("d172-old", 39000, 8, "JUMP"));
            Assert.AreEqual(2, teaching.AcceptedSamples);
            Assert.AreEqual(1, controller.Router.Session.DiscardedStale);

            controller.InjectDatagram(Cmd("d172-new", 39001, 2, "JUMP"));
            Assert.AreEqual(3, teaching.AcceptedSamples);
            Assert.AreEqual(ProductionTeachingStage.Confirming, teaching.Stage);
            Assert.IsFalse(controller.Airborne);
            yield return new WaitForSeconds(ProductionTeachingFlow.ConfirmationSeconds + 0.1f);

            Assert.IsFalse(teaching.IsActive);
            Assert.AreEqual(1, teaching.CompletedTeachingCount);
            Assert.IsFalse(controller.StorySuspended);
            Assert.IsFalse(controller.MovementSuspended);
            Assert.AreEqual(3, controller.Bus.Published);
        }

        private void BuildRig(out NulProductionController controller,
            out ProductionCourseLoop course, out ProductionTeachingFlow teaching)
        {
            _root = new GameObject("slice37-teaching-session-rig");
            _root.SetActive(false);
            _root.AddComponent<Rigidbody2D>().gravityScale = 0f;
            var stand = new GameObject("stand").AddComponent<BoxCollider2D>();
            stand.transform.SetParent(_root.transform, false);
            var crouch = new GameObject("crouch").AddComponent<BoxCollider2D>();
            crouch.transform.SetParent(_root.transform, false);
            crouch.enabled = false;
            controller = _root.AddComponent<NulProductionController>();
            controller.udpPort = 8927;
            controller.autoRun = false;
            controller.autoLaunchSidecar = false;
            SetPrivate(controller, "standingCollider", stand);
            SetPrivate(controller, "crouchCollider", crouch);
            course = _root.AddComponent<ProductionCourseLoop>();
            _definition = ScriptableObject.CreateInstance<ProductionCourseDefinition>();
            _definition.LoadProductionDefaults();
            course.Definition = _definition;
            _root.AddComponent<ProductionObstacleJudge>();
            _root.AddComponent<ProductionGameLoop>();
            teaching = _root.AddComponent<ProductionTeachingFlow>();
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
