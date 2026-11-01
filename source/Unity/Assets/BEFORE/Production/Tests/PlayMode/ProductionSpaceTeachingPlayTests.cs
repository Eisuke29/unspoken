using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Before.Production.Tests
{
    public class ProductionSpaceTeachingPlayTests
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
        public IEnumerator SpaceIsActiveOnlyDuringTeachingAndSidecarEventsOwnAcceptedCount()
        {
            BuildRig(8911, includeCourse: false, out NulProductionController controller,
                out _, out ProductionTeachingFlow flow, out _, out ProductionTeachingInput input,
                out SidecarTeachingControlClient control);
            yield return null;

            int idleRequests = control.RequestCount;
            input.TickInput(true, true);
            Assert.AreEqual(idleRequests, control.RequestCount,
                "SPACE must be inert during ordinary running");

            flow.BeginTeaching(1, ProductionObstacleKind.Jump, 100f);
            input.SyncContext();
            Assert.AreEqual("begin", control.LastQueuedAction);
            input.TickInput(true, false);
            Assert.AreEqual("press", control.LastQueuedAction);
            input.TickInput(false, true);
            Assert.AreEqual("release", control.LastQueuedAction);

            control.ApplyStatusJson(Status(1, 1, "accepted", "", false));
            yield return null;
            Assert.AreEqual(1, flow.AcceptedSamples);
            Assert.AreEqual(ProductionTeachingStage.Collecting, flow.Stage);

            control.ApplyStatusJson(Status(2, 1, "rejected", "early_release", false));
            yield return null;
            Assert.AreEqual(1, flow.AcceptedSamples,
                "a rejected attempt must not change accepted count");
            Assert.AreEqual("early_release", flow.LastRejectionReason);

            control.ApplyStatusJson(Status(3, 3, "accepted", "", true));
            yield return null;
            Assert.AreEqual(3, flow.AcceptedSamples);
            Assert.AreEqual(ProductionTeachingStage.Confirming, flow.Stage);

            flow.ResetFlow();
            int afterReset = control.RequestCount;
            input.TickInput(true, true);
            Assert.AreEqual(afterReset, control.RequestCount);
            Assert.IsFalse(controller.StorySuspended);
        }

        [UnityTest]
        public IEnumerator ForkAutomaticallyEntersAndLeavesSidecarDirectionMode()
        {
            BuildRig(8912, includeCourse: true, out NulProductionController controller,
                out ProductionCourseLoop course, out _, out ProductionForkFlow fork, out _,
                out SidecarTeachingControlClient control);
            yield return null;

            ProductionCourseLoop.ObstacleSlot slot = DirectionSlot(course);
            controller.RecoverAt(new Vector2(slot.WorldX, 0f));
            fork.TryEnterFork();
            Assert.AreEqual("direction", control.LastQueuedAction);
            Assert.IsTrue(control.LastDirectionEnabled);
            Assert.IsTrue(controller.Nul.DirectionMode);

            fork.ResetFlow();
            Assert.AreEqual("direction", control.LastQueuedAction);
            Assert.IsFalse(control.LastDirectionEnabled);
            Assert.IsFalse(controller.Nul.DirectionMode);
        }

        [UnityTest]
        public IEnumerator RepairUsesOneMarkerAndAckReturnsSameSignToFreshTeaching()
        {
            BuildRig(8921, includeCourse: false, out NulProductionController controller,
                out _, out ProductionTeachingFlow flow, out _, out ProductionTeachingInput input,
                out SidecarTeachingControlClient control);
            var presenter = _root.AddComponent<ProductionTeachingPresenter>();
            presenter.Flow = flow;
            yield return null;
            control.TransportEnabled = false;

            int idleRequests = control.RequestCount;
            input.TickInput(false, false, true);
            Assert.AreEqual(idleRequests, control.RequestCount,
                "R must not become a command reset outside Teaching/Repair");

            flow.BeginTeaching(13, ProductionObstacleKind.Stop, 200f);
            SetAutoProperty(flow, "Stage", ProductionTeachingStage.RepairCollecting);
            input.SyncContext();
            Assert.IsTrue(flow.IsRepair);
            Assert.AreEqual(1, presenter.ProgressMarkerCount);
            Assert.AreEqual("止まる合図　0/1", presenter.CurrentTargetProgress);

            controller.Nul.DirectionMode = true;
            int requestCount = control.RequestCount;
            input.TickInput(false, false, true);
            Assert.Greater(control.RequestCount, requestCount);
            Assert.AreEqual("reset_command", control.LastQueuedAction);
            Assert.AreEqual("stop", control.LastResetTarget);
            Assert.IsTrue(flow.ResetPending);
            Assert.IsTrue(controller.StorySuspended);
            Assert.IsTrue(controller.Nul.DirectionMode);

            int requestId = control.LastResetRequestId;
            Assert.IsTrue(control.ApplyStatusJson(
                "{\"active\":true,\"kind\":\"stop\",\"repair\":false," +
                "\"required\":3,\"accepted\":0,\"phase\":\"ready\"," +
                "\"reason\":\"\",\"event_id\":0,\"finalized\":false," +
                "\"direction_mode\":true,\"ack_action\":\"reset_command\"," +
                "\"ack_target\":\"stop\",\"ack_id\":" + requestId + "," +
                "\"rgb_frames_sent\":0}"));
            yield return null;

            Assert.IsFalse(flow.ResetPending);
            Assert.AreEqual(ProductionTeachingStage.Collecting, flow.Stage);
            Assert.IsFalse(flow.IsRepair);
            Assert.AreEqual(3, flow.RequiredSamples);
            Assert.AreEqual(0, flow.AcceptedSamples);
            Assert.AreEqual(3, presenter.ProgressMarkerCount);
            Assert.AreEqual("止まる合図　0/3", presenter.CurrentTargetProgress);
            Assert.AreEqual("止まる合図をリセットしました", presenter.CurrentResetNotice);
            Assert.IsTrue(controller.StorySuspended);
            Assert.IsTrue(controller.MovementSuspended);
            Assert.IsTrue(controller.Nul.DirectionMode);
        }

        private void BuildRig(int port, bool includeCourse,
            out NulProductionController controller, out ProductionCourseLoop course,
            out ProductionTeachingFlow flow, out ProductionForkFlow fork,
            out ProductionTeachingInput input, out SidecarTeachingControlClient control)
        {
            _root = new GameObject("production-space-teaching-rig");
            _root.SetActive(false);
            _root.AddComponent<Rigidbody2D>().gravityScale = 0f;
            var stand = new GameObject("stand").AddComponent<BoxCollider2D>();
            stand.transform.SetParent(_root.transform, false);
            var crouch = new GameObject("crouch").AddComponent<BoxCollider2D>();
            crouch.transform.SetParent(_root.transform, false);
            crouch.enabled = false;

            controller = _root.AddComponent<NulProductionController>();
            controller.udpPort = port;
            controller.autoRun = false;
            controller.autoLaunchSidecar = false;
            SetPrivate(controller, "standingCollider", stand);
            SetPrivate(controller, "crouchCollider", crouch);
            course = includeCourse ? _root.AddComponent<ProductionCourseLoop>() : null;
            if (course != null)
            {
                _definition = ScriptableObject.CreateInstance<ProductionCourseDefinition>();
                _definition.LoadProductionDefaults();
                course.Definition = _definition;
                _root.AddComponent<ProductionObstacleJudge>();
                _root.AddComponent<ProductionGameLoop>();
            }
            flow = includeCourse ? null : _root.AddComponent<ProductionTeachingFlow>();
            fork = includeCourse ? _root.AddComponent<ProductionForkFlow>() : null;
            _root.SetActive(true);
            control = _root.GetComponent<SidecarTeachingControlClient>();
            control.TransportEnabled = false;
            input = _root.GetComponent<ProductionTeachingInput>();
        }

        private static ProductionCourseLoop.ObstacleSlot DirectionSlot(ProductionCourseLoop course)
        {
            foreach (ProductionCourseLoop.ObstacleSlot slot in course.Slots)
                if (slot.BeatId == ProductionForkFlow.ForkBeatId) return slot;
            Assert.Fail("Direction fork slot missing");
            return default;
        }

        private static string Status(int eventId, int accepted, string phase,
                                     string reason, bool finalized)
            => "{\"active\":true,\"kind\":\"jump\",\"required\":3," +
               "\"accepted\":" + accepted + ",\"phase\":\"" + phase +
               "\",\"reason\":\"" + reason + "\",\"event_id\":" + eventId +
               ",\"finalized\":" + finalized.ToString().ToLowerInvariant() +
               ",\"rgb_frames_sent\":0}";

        private static void SetPrivate(object target, string field, object value)
        {
            FieldInfo info = target.GetType().GetField(field,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(info, field);
            info.SetValue(target, value);
        }

        private static void SetAutoProperty(object target, string property, object value)
            => SetPrivate(target, "<" + property + ">k__BackingField", value);
    }
}
