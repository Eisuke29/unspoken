using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Before.Production.Tests
{
    public class ProductionSlice30PlayTests
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
        public IEnumerator LeftAndRightEachReachEndingHoldAndRestartInOnePlaySession()
        {
            BuildRig(8917, out NulProductionController controller,
                out ProductionCourseLoop course, out ProductionGameLoop loop,
                out ProductionForkFlow fork, out ProductionBranchFlow branch,
                out ProductionCoreFlow core, out ProductionEndingFlow ending);
            yield return null;

            float leftCore = 0f;
            float rightCore = 0f;
            yield return RunRoute(ProductionRoute.Left, "LEFT", "d164-left", 25000,
                controller, course, loop, fork, branch, core, ending,
                value => leftCore = value);
            yield return RunRoute(ProductionRoute.Right, "RIGHT", "d164-right", 26000,
                controller, course, loop, fork, branch, core, ending,
                value => rightCore = value);

            Assert.AreNotEqual(leftCore, rightCore,
                "route-specific progression must converge at different approach distances");
            Assert.Greater(leftCore, rightCore,
                "wide low-city route is intentionally longer than tight industrial route");
        }

        private IEnumerator RunRoute(ProductionRoute expected, string command,
            string session, int sessionStart, NulProductionController controller,
            ProductionCourseLoop course, ProductionGameLoop loop,
            ProductionForkFlow fork, ProductionBranchFlow branch,
            ProductionCoreFlow core, ProductionEndingFlow ending,
            System.Action<float> captureCore)
        {
            ProductionCourseLoop.ObstacleSlot direction = DirectionSlot(course);
            controller.RecoverAt(new Vector2(direction.WorldX, 0f));
            fork.TryEnterFork();
            yield return null;
            controller.InjectDatagram(Cmd(session, sessionStart, command));
            yield return new WaitForSeconds(ProductionForkFlow.ExpandSeconds +
                                            ProductionForkFlow.ConfirmSeconds +
                                            ProductionForkFlow.CollapseSeconds + 0.15f);

            Assert.AreEqual(expected, loop.SelectedRoute);
            Assert.AreEqual(expected, branch.ActiveRoute);
            float coreX = branch.CoreApproachWorldX;
            captureCore(coreX);
            // This integration test skips the physical branch traversal. Suspend
            // judgement for the teleport so skipped hazards are not counted as misses.
            controller.SetStorySuspended(true);
            controller.SetMovementSuspended(true);
            controller.RecoverAt(new Vector2(coreX, 0f));
            yield return null;

            float endingSeconds = ProductionEndingFlow.TestStepsSeconds +
                ProductionEndingFlow.LookBackSeconds +
                ProductionEndingFlow.MemoryTracesSeconds +
                ProductionEndingFlow.BowSeconds +
                ProductionEndingFlow.DepartSeconds +
                ProductionEndingFlow.WorldFadeSeconds +
                ProductionEndingFlow.TraceFadeSeconds +
                ProductionEndingFlow.CompleteMessageSeconds;
            yield return new WaitForSeconds(ProductionCoreFlow.UnfoldSeconds +
                                            ProductionCoreFlow.TransferSeconds +
                                            ProductionCoreFlow.DisconnectSeconds +
                                            endingSeconds + 0.35f);

            Assert.AreEqual(ProductionCoreStage.IndependentHold, core.Stage);
            Assert.AreEqual(ProductionEndingStage.EndingHold, ending.Stage);
            Assert.IsTrue(ending.GameplayEnded);
            Assert.AreEqual(expected, loop.SelectedRoute);
            Assert.AreEqual(ProductionGameLoop.StartingLives, loop.Lives);

            ending.RestartFromEnding();
            yield return null;
            Assert.AreEqual(ProductionRoute.None, loop.SelectedRoute);
            Assert.AreEqual(ProductionRoute.None, branch.ActiveRoute);
            Assert.AreEqual(ProductionCoreStage.None, core.Stage);
            Assert.AreEqual(ProductionEndingStage.None, ending.Stage);
            Assert.AreEqual(0f, controller.transform.position.x, 0.01f);
            Assert.IsFalse(controller.StorySuspended);
            Assert.IsFalse(controller.MovementSuspended);
        }

        private void BuildRig(int port, out NulProductionController controller,
            out ProductionCourseLoop course, out ProductionGameLoop loop,
            out ProductionForkFlow fork, out ProductionBranchFlow branch,
            out ProductionCoreFlow core, out ProductionEndingFlow ending)
        {
            _root = new GameObject("slice30-dual-route-rig");
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
            _root.AddComponent<ProductionObstacleJudge>();
            loop = _root.AddComponent<ProductionGameLoop>();
            fork = _root.AddComponent<ProductionForkFlow>();
            branch = _root.AddComponent<ProductionBranchFlow>();
            core = _root.AddComponent<ProductionCoreFlow>();
            ending = _root.AddComponent<ProductionEndingFlow>();
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

        private static string Cmd(string session, int sessionStart, string type)
            => "{\"v\":1,\"kind\":\"cmd\",\"session\":\"" + session +
               "\",\"sstart\":" + sessionStart +
               ",\"cmdId\":1,\"seq\":1,\"tUs\":0,\"type\":\"" + type +
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
