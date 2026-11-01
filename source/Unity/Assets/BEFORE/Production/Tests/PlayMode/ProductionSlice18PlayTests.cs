using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Before.Production.Tests
{
    public class ProductionSlice18PlayTests
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
        public IEnumerator TeachingFreezesWorld_AcknowledgesThreeCommands_AndProtectsFirstProofOnce()
        {
            BuildRig(8870, out NulProductionController controller,
                out ProductionCourseLoop course, out ProductionObstacleJudge judge,
                out ProductionGameLoop loop, out ProductionTeachingFlow flow);
            yield return null;

            float hazardX = course.Slots[0].WorldX;
            _root.transform.position = new Vector3(hazardX - ProductionTeachingFlow.SafeRunUp, 0f, 0f);
            yield return null;
            Assert.IsTrue(flow.IsActive);
            Assert.AreEqual(ProductionObstacleKind.Jump, flow.TargetKind);
            Assert.IsTrue(controller.StorySuspended);
            Assert.IsTrue(controller.MovementSuspended);

            float frozenAt = controller.GameplayTime;
            yield return new WaitForSeconds(0.12f);
            Assert.AreEqual(frozenAt, controller.GameplayTime, 0.03f);

            controller.InjectDatagram(Cmd(1, "JUMP"));
            controller.InjectDatagram(Cmd(2, "JUMP"));
            controller.InjectDatagram(Cmd(3, "JUMP"));
            Assert.AreEqual(3, flow.AcceptedSamples);
            Assert.AreEqual(ProductionTeachingStage.Confirming, flow.Stage);
            Assert.IsFalse(controller.Airborne,
                "teaching-exclusive commands must not start gameplay physics");
            yield return new WaitForSeconds(ProductionTeachingFlow.ConfirmationSeconds + 0.1f);

            Assert.IsFalse(flow.IsActive);
            Assert.AreEqual(1, flow.CompletedTeachingCount);
            Assert.IsFalse(controller.StorySuspended);
            Assert.IsFalse(controller.MovementSuspended);

            controller.autoRun = false;
            _root.transform.position = new Vector3(hazardX, 0f, 0f);
            judge.EvaluateNowAt(controller.GameplayTime);
            Assert.AreEqual(1, judge.FailureCount);
            Assert.AreEqual(ProductionGameLoop.StartingLives, loop.Lives,
                "first proof failure must preserve all lives");
            Assert.IsTrue(flow.IsRepair);
            Assert.AreEqual(hazardX - ProductionTeachingFlow.SafeRunUp,
                _root.transform.position.x, 0.01f);

            controller.InjectDatagram(Cmd(4, "JUMP"));
            Assert.AreEqual(ProductionTeachingStage.RepairConfirming, flow.Stage);
            yield return new WaitForSeconds(ProductionTeachingFlow.ConfirmationSeconds + 0.1f);
            Assert.IsFalse(flow.IsActive);

            _root.transform.position = new Vector3(hazardX, 0f, 0f);
            judge.EvaluateNowAt(controller.GameplayTime);
            Assert.AreEqual(2, judge.FailureCount);
            Assert.AreEqual(2, loop.Lives,
                "the one protected retry must not mask a second failure");
            Assert.IsTrue(loop.InRecovery);
        }

        [UnityTest]
        public IEnumerator PauseConsumesWithoutTeachingProgress_AndLoopResetClearsStoryState()
        {
            BuildRig(8871, out NulProductionController controller,
                out ProductionCourseLoop course, out _, out ProductionGameLoop loop,
                out ProductionTeachingFlow flow);
            var presenter = _root.AddComponent<ProductionTeachingPresenter>();
            presenter.Flow = flow;
            yield return null;

            float hazardX = course.Slots[0].WorldX;
            _root.transform.position = new Vector3(hazardX - ProductionTeachingFlow.SafeRunUp, 0f, 0f);
            yield return null;
            Assert.IsTrue(flow.IsActive);

            controller.Nul.Paused = true;
            yield return null;
            Assert.IsFalse(presenter.Visible, "Pause presentation remains exclusive");
            float pausedAt = controller.GameplayTime;
            controller.InjectDatagram(Cmd(1, "JUMP"));
            yield return new WaitForSeconds(0.1f);
            Assert.AreEqual(0, flow.AcceptedSamples);
            Assert.AreEqual(pausedAt, controller.GameplayTime, 0.03f);
            Assert.AreEqual(0, controller.Nul.JumpCount,
                "paused command remains consumed with no replay or teaching acknowledgement");

            controller.Nul.Paused = false;
            controller.InjectDatagram(Cmd(2, "JUMP"));
            Assert.AreEqual(1, flow.AcceptedSamples);
            loop.ResetLoop();
            yield return null;

            Assert.IsFalse(flow.IsActive);
            Assert.AreEqual(0, flow.CompletedTeachingCount);
            Assert.AreEqual(0, flow.ProtectedProofCount);
            Assert.IsFalse(controller.StorySuspended);
            Assert.IsFalse(controller.MovementSuspended);
            Assert.AreEqual(ProductionGameLoop.StartingLives, loop.Lives);
            Assert.AreEqual(0f, _root.transform.position.x, 0.01f);
        }

        private void BuildRig(int port, out NulProductionController controller,
                              out ProductionCourseLoop course, out ProductionObstacleJudge judge,
                              out ProductionGameLoop loop, out ProductionTeachingFlow flow)
        {
            _root = new GameObject("slice18-teaching-rig");
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
            SetPrivate(controller, "standingCollider", stand);
            SetPrivate(controller, "crouchCollider", crouch);
            course = _root.AddComponent<ProductionCourseLoop>();
            _definition = ScriptableObject.CreateInstance<ProductionCourseDefinition>();
            _definition.LoadProductionDefaults();
            course.Definition = _definition;
            judge = _root.AddComponent<ProductionObstacleJudge>();
            loop = _root.AddComponent<ProductionGameLoop>();
            flow = _root.AddComponent<ProductionTeachingFlow>();
            _root.SetActive(true);
        }

        private static string Cmd(int id, string type)
            => "{\"v\":1,\"kind\":\"cmd\",\"session\":\"teach\",\"sstart\":18000," +
               "\"cmdId\":" + id + ",\"seq\":1,\"tUs\":0,\"type\":\"" + type +
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
