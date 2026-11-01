using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Before.Production.Tests
{
    public class ProductionSlice22PlayTests
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
        public IEnumerator ForkStopsWorld_SuppressesOtherCommands_CommitsDirectionAndResumes()
        {
            BuildRig(8896, out NulProductionController controller,
                out ProductionCourseLoop course, out ProductionObstacleJudge judge,
                out ProductionGameLoop loop, out ProductionForkFlow fork);
            yield return null;

            ProductionCourseLoop.ObstacleSlot slot = DirectionSlot(course);
            controller.RecoverAt(new Vector2(slot.WorldX, 0f));
            fork.TryEnterFork();
            yield return null;

            Assert.AreEqual(ProductionForkStage.Expanding, fork.Stage);
            Assert.IsTrue(controller.StorySuspended);
            Assert.IsTrue(controller.MovementSuspended);
            Assert.IsTrue(controller.Nul.DirectionMode);
            Assert.AreEqual(slot.WorldX, _root.transform.position.x, 0.01f);

            float frozenAt = controller.GameplayTime;
            int jumpCount = controller.Nul.JumpCount;
            controller.InjectDatagram(Cmd("fork-a", 1, "JUMP"));
            yield return new WaitForSeconds(0.12f);
            Assert.AreEqual(frozenAt, controller.GameplayTime, 0.03f);
            Assert.AreEqual(jumpCount, controller.Nul.JumpCount,
                "DirectionMode suppresses Jump without replay");
            Assert.AreEqual(ProductionRoute.None, loop.SelectedRoute);

            controller.InjectDatagram(Cmd("fork-a", 2, "LEFT"));
            Assert.AreEqual(ProductionRoute.Left, fork.ChosenRoute);
            Assert.AreEqual(ProductionForkStage.Confirmed, fork.Stage);
            Assert.AreEqual(-1, controller.PresentationFacing);
            Assert.AreEqual(ProductionRoute.Left, loop.SelectedRoute);
            Assert.AreEqual(ProductionRoute.Left, course.SelectedRoute);
            Assert.AreEqual(1, judge.SuccessCount);
            Assert.AreEqual(ProductionGameLoop.StartingLives, loop.Lives);

            yield return new WaitForSeconds(ProductionForkFlow.ExpandSeconds +
                                            ProductionForkFlow.ConfirmSeconds +
                                            ProductionForkFlow.CollapseSeconds + 0.15f);
            Assert.IsFalse(fork.IsActive);
            Assert.IsFalse(controller.StorySuspended);
            Assert.IsFalse(controller.MovementSuspended);
            Assert.IsFalse(controller.Nul.DirectionMode);
            Assert.AreEqual(1, controller.PresentationFacing);
            Assert.AreEqual(ProductionRoute.Left, loop.SelectedRoute);
            Assert.Greater(controller.GameplayTime, frozenAt);
        }

        [UnityTest]
        public IEnumerator ForkPauseMakesNoProgressAndLoopResetClearsPendingAndRouteState()
        {
            BuildRig(8897, out NulProductionController controller,
                out ProductionCourseLoop course, out _, out ProductionGameLoop loop,
                out ProductionForkFlow fork);
            var presenter = _root.AddComponent<ProductionForkPresenter>();
            presenter.Flow = fork;
            yield return null;

            ProductionCourseLoop.ObstacleSlot slot = DirectionSlot(course);
            controller.RecoverAt(new Vector2(slot.WorldX, 0f));
            fork.TryEnterFork();
            yield return null;
            Assert.IsTrue(fork.IsActive);

            controller.Nul.Paused = true;
            yield return null;
            Assert.IsFalse(presenter.Visible);
            float elapsed = fork.StageElapsed;
            controller.InjectDatagram(Cmd("fork-b", 1, "RIGHT"));
            yield return new WaitForSeconds(0.12f);
            Assert.AreEqual(elapsed, fork.StageElapsed, 0.03f);
            Assert.AreEqual(ProductionRoute.None, loop.SelectedRoute);

            controller.Nul.Paused = false;
            controller.InjectDatagram(Cmd("fork-b", 2, "RIGHT"));
            Assert.AreEqual(ProductionRoute.Right, loop.SelectedRoute);
            loop.ResetLoop();
            yield return null;

            Assert.IsFalse(fork.IsActive);
            Assert.AreEqual(ProductionRoute.None, fork.ChosenRoute);
            Assert.AreEqual(ProductionRoute.None, loop.SelectedRoute);
            Assert.AreEqual(ProductionRoute.None, course.SelectedRoute);
            Assert.IsFalse(controller.StorySuspended);
            Assert.IsFalse(controller.MovementSuspended);
            Assert.IsFalse(controller.Nul.DirectionMode);
            Assert.AreEqual(0f, _root.transform.position.x, 0.01f);
        }

        private void BuildRig(int port, out NulProductionController controller,
                              out ProductionCourseLoop course, out ProductionObstacleJudge judge,
                              out ProductionGameLoop loop, out ProductionForkFlow fork)
        {
            _root = new GameObject("slice22-fork-rig");
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
            course = _root.AddComponent<ProductionCourseLoop>();
            _definition = ScriptableObject.CreateInstance<ProductionCourseDefinition>();
            _definition.LoadProductionDefaults();
            course.Definition = _definition;
            judge = _root.AddComponent<ProductionObstacleJudge>();
            loop = _root.AddComponent<ProductionGameLoop>();
            fork = _root.AddComponent<ProductionForkFlow>();
            _root.SetActive(true);
        }

        private static ProductionCourseLoop.ObstacleSlot DirectionSlot(
            ProductionCourseLoop course)
        {
            foreach (ProductionCourseLoop.ObstacleSlot slot in course.Slots)
                if (slot.BeatId == ProductionForkFlow.ForkBeatId) return slot;
            Assert.Fail("Direction fork slot missing");
            return default;
        }

        private static string Cmd(string session, int id, string type)
            => "{\"v\":1,\"kind\":\"cmd\",\"session\":\"" + session +
               "\",\"sstart\":19000,\"cmdId\":" + id + ",\"seq\":" + id +
               ",\"tUs\":0,\"type\":\"" + type + "\",\"phase\":\"fire\"}";

        private static void SetPrivate(object target, string field, object value)
        {
            FieldInfo info = target.GetType().GetField(field,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(info, field);
            info.SetValue(target, value);
        }
    }
}
