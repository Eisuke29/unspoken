using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Before.Production.Tests
{
    public class ProductionSlice31PlayTests
    {
        private GameObject _root;
        private ProductionCourseDefinition _definition;
        private int _commandId;

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.Destroy(_root);
            if (_definition != null) Object.Destroy(_definition);
        }

        [UnityTest]
        public IEnumerator BothRouteProfilesResolveAllFiftyTwoDemandsAndReachCore()
        {
            BuildRig(out NulProductionController controller,
                out ProductionCourseLoop course, out ProductionObstacleJudge judge,
                out ProductionGameLoop loop, out ProductionForkFlow fork,
                out ProductionBranchFlow branch, out ProductionCoreFlow core);
            yield return null;

            yield return RunFullRoute(ProductionRoute.Left, "LEFT",
                "d165-left", 31000, controller, course, judge, loop,
                fork, branch, core);
            yield return RunFullRoute(ProductionRoute.Right, "RIGHT",
                "d165-right", 32000, controller, course, judge, loop,
                fork, branch, core);
        }

        private IEnumerator RunFullRoute(ProductionRoute expected, string direction,
            string session, int sessionStart, NulProductionController controller,
            ProductionCourseLoop course, ProductionObstacleJudge judge,
            ProductionGameLoop loop, ProductionForkFlow fork,
            ProductionBranchFlow branch, ProductionCoreFlow core)
        {
            loop.ResetLoop();
            yield return null;
            _commandId = 0;

            int preForkDemands = 0;
            foreach (ProductionCourseLoop.ObstacleSlot slot in course.Slots)
            {
                if (slot.BeatId >= ProductionForkFlow.ForkBeatId) continue;
                ResolveSymbolic(slot, session, sessionStart, controller, judge);
                preForkDemands++;
            }
            Assert.AreEqual(39, preForkDemands);
            Assert.AreEqual(preForkDemands, judge.SuccessCount);
            Assert.AreEqual(0, judge.FailureCount);

            ProductionCourseLoop.ObstacleSlot forkSlot = DirectionSlot(course);
            controller.RecoverAt(new Vector2(forkSlot.WorldX, 0f));
            fork.TryEnterFork();
            Assert.AreEqual(ProductionForkStage.Expanding, fork.Stage);
            controller.InjectDatagram(Cmd(session, sessionStart,
                ++_commandId, direction));
            yield return new WaitForSeconds(ProductionForkFlow.ExpandSeconds +
                                            ProductionForkFlow.ConfirmSeconds +
                                            ProductionForkFlow.CollapseSeconds + 0.15f);

            Assert.AreEqual(expected, loop.SelectedRoute);
            Assert.AreEqual(expected, branch.ActiveRoute);
            Assert.AreEqual(preForkDemands + 1, judge.SuccessCount);
            Assert.AreEqual(0, judge.FailureCount);

            int branchDemands = 0;
            foreach (ProductionCourseLoop.ObstacleSlot slot in course.Slots)
            {
                if (slot.BeatId < ProductionBranchFlow.FirstBranchBeatId ||
                    slot.BeatId > ProductionBranchFlow.LastBranchBeatId) continue;
                ResolveSymbolic(slot, session, sessionStart, controller, judge);
                branchDemands++;
            }
            Assert.AreEqual(ProductionBranchFlow.BranchDemandCount, branchDemands);
            Assert.AreEqual(course.Slots.Length, judge.SuccessCount);
            Assert.AreEqual(0, judge.FailureCount);
            Assert.AreEqual(ProductionGameLoop.StartingLives, loop.Lives);

            controller.SetStorySuspended(true);
            controller.SetMovementSuspended(true);
            controller.RecoverAt(new Vector2(branch.CoreApproachWorldX, 0f));
            yield return null;
            yield return new WaitForSeconds(ProductionCoreFlow.UnfoldSeconds +
                                            ProductionCoreFlow.TransferSeconds +
                                            ProductionCoreFlow.DisconnectSeconds + 0.2f);

            Assert.IsTrue(branch.BranchCompleted);
            Assert.AreEqual(ProductionCoreStage.IndependentHold, core.Stage);
            Assert.AreEqual(expected, loop.SelectedRoute);
            Assert.AreEqual(ProductionGameLoop.StartingLives, loop.Lives);
            Assert.Greater(controller.Bus.Published, course.Slots.Length,
                "Stop ON/OFF pairs must publish more commands than demand count");

            loop.ResetLoop();
            yield return null;
            Assert.AreEqual(0, judge.SuccessCount);
            Assert.AreEqual(0, judge.FailureCount);
            Assert.AreEqual(ProductionRoute.None, loop.SelectedRoute);
            Assert.AreEqual(ProductionRoute.None, branch.ActiveRoute);
            Assert.AreEqual(ProductionCoreStage.None, core.Stage);
            Assert.AreEqual(ProductionGameLoop.StartingLives, loop.Lives);
            Assert.AreEqual(0f, controller.transform.position.x, 0.01f);
            Assert.IsFalse(controller.StorySuspended);
            Assert.IsFalse(controller.MovementSuspended);
        }

        private void ResolveSymbolic(ProductionCourseLoop.ObstacleSlot slot,
            string session, int sessionStart, NulProductionController controller,
            ProductionObstacleJudge judge)
        {
            controller.RecoverAt(new Vector2(slot.WorldX - 30f, 0f));
            switch (slot.Kind)
            {
                case ProductionObstacleKind.Jump:
                    controller.InjectDatagram(Cmd(session, sessionStart,
                        ++_commandId, "JUMP"));
                    controller.transform.position = new Vector3(slot.WorldX, 8f, 0f);
                    judge.EvaluateNowAt(3f);
                    break;
                case ProductionObstacleKind.Crouch:
                    controller.InjectDatagram(Cmd(session, sessionStart,
                        ++_commandId, "CROUCH"));
                    controller.transform.position = new Vector3(slot.WorldX, 0f, 0f);
                    judge.EvaluateNowAt(3f);
                    break;
                case ProductionObstacleKind.Stop:
                    controller.InjectDatagram(Cmd(session, sessionStart,
                        ++_commandId, "STOP_ON"));
                    judge.EvaluateNowAt(3f);
                    controller.InjectDatagram(Cmd(session, sessionStart,
                        ++_commandId, "STOP_OFF"));
                    controller.transform.position = new Vector3(slot.WorldX, 0f, 0f);
                    judge.EvaluateNowAt(3f);
                    break;
                default:
                    Assert.Fail("unexpected non-symbolic demand in branch traversal");
                    break;
            }

            Assert.AreEqual(slot.StableId, judge.LatestResult.StableId,
                "wrong result at beat " + slot.BeatId + " demand " + slot.DemandIndex);
            Assert.IsTrue(judge.LatestResult.Success,
                "failed beat " + slot.BeatId + " demand " + slot.DemandIndex);
        }

        private void BuildRig(out NulProductionController controller,
            out ProductionCourseLoop course, out ProductionObstacleJudge judge,
            out ProductionGameLoop loop, out ProductionForkFlow fork,
            out ProductionBranchFlow branch, out ProductionCoreFlow core)
        {
            _root = new GameObject("slice31-full-route-rig");
            _root.SetActive(false);
            _root.AddComponent<Rigidbody2D>().gravityScale = 0f;
            var stand = new GameObject("stand").AddComponent<BoxCollider2D>();
            stand.transform.SetParent(_root.transform, false);
            var crouch = new GameObject("crouch").AddComponent<BoxCollider2D>();
            crouch.transform.SetParent(_root.transform, false);
            crouch.enabled = false;

            controller = _root.AddComponent<NulProductionController>();
            controller.udpPort = 8921;
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
            branch = _root.AddComponent<ProductionBranchFlow>();
            core = _root.AddComponent<ProductionCoreFlow>();
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

        private static string Cmd(string session, int sessionStart, int id, string type)
            => "{\"v\":1,\"kind\":\"cmd\",\"session\":\"" + session +
               "\",\"sstart\":" + sessionStart +
               ",\"cmdId\":" + id + ",\"seq\":" + id +
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
