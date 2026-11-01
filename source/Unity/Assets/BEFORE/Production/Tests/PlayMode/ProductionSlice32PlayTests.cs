using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Before.Production.Tests
{
    public class ProductionSlice32PlayTests
    {
        private const string Session = "d166-continuous-story";
        private const int SessionStart = 34000;

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
        public IEnumerator TeachingRepairFullRightCourseEndingAndRestartShareOneSession()
        {
            BuildRig(out NulProductionController controller,
                out ProductionCourseLoop course, out ProductionObstacleJudge judge,
                out ProductionGameLoop loop, out ProductionTeachingFlow teaching,
                out ProductionForkFlow fork, out ProductionBranchFlow branch,
                out ProductionCoreFlow core, out ProductionEndingFlow ending);
            yield return null;
            // Teaching checkpoints are intentionally fast-forwarded below. Keep
            // automatic LateUpdate judgement off until the explicit proof so
            // skipped travel between teaching anchors is not counted as a miss.
            judge.enabled = false;
            course.enabled = false;

            yield return CompleteTeaching(controller, teaching, course,
                1, ProductionObstacleKind.Jump, "JUMP");
            yield return CompleteTeaching(controller, teaching, course,
                4, ProductionObstacleKind.Crouch, "CROUCH");
            yield return CompleteTeaching(controller, teaching, course,
                13, ProductionObstacleKind.Stop, "STOP_ON");
            Assert.IsTrue(teaching.AllTeachingsCompleted);
            Assert.AreEqual(3, teaching.CompletedTeachingCount);

            ProductionCourseLoop.ObstacleSlot firstProof = SlotAtBeat(course, 1);
            controller.RecoverAt(new Vector2(firstProof.WorldX, 0f));
            course.enabled = true;
            judge.enabled = true;
            judge.EvaluateNowAt(3f);
            Assert.AreEqual(1, judge.FailureCount);
            Assert.AreEqual(ProductionGameLoop.StartingLives, loop.Lives);
            Assert.AreEqual(ProductionTeachingStage.RepairCollecting, teaching.Stage);
            Assert.AreEqual(1, teaching.ProtectedProofCount);

            controller.InjectDatagram(Cmd(++_commandId, "JUMP"));
            Assert.AreEqual(ProductionTeachingStage.RepairConfirming, teaching.Stage);
            yield return new WaitForSeconds(ProductionTeachingFlow.ConfirmationSeconds + 0.1f);
            Assert.IsFalse(teaching.IsActive);

            int preForkDemands = 0;
            foreach (ProductionCourseLoop.ObstacleSlot slot in course.Slots)
            {
                if (slot.BeatId >= ProductionForkFlow.ForkBeatId) continue;
                ResolveSymbolic(slot, controller, judge);
                preForkDemands++;
            }
            Assert.AreEqual(39, preForkDemands);
            Assert.AreEqual(39, judge.SuccessCount);
            Assert.AreEqual(1, judge.FailureCount);
            Assert.AreEqual(ProductionGameLoop.StartingLives, loop.Lives);

            ProductionCourseLoop.ObstacleSlot forkSlot = SlotAtBeat(course,
                ProductionForkFlow.ForkBeatId);
            controller.RecoverAt(new Vector2(forkSlot.WorldX, 0f));
            fork.TryEnterFork();
            controller.InjectDatagram(Cmd(++_commandId, "RIGHT"));
            yield return new WaitForSeconds(ProductionForkFlow.ExpandSeconds +
                                            ProductionForkFlow.ConfirmSeconds +
                                            ProductionForkFlow.CollapseSeconds + 0.15f);
            Assert.AreEqual(ProductionRoute.Right, loop.SelectedRoute);
            Assert.AreEqual(ProductionRoute.Right, branch.ActiveRoute);
            Assert.AreEqual(40, judge.SuccessCount);

            int branchDemands = 0;
            foreach (ProductionCourseLoop.ObstacleSlot slot in course.Slots)
            {
                if (slot.BeatId < ProductionBranchFlow.FirstBranchBeatId ||
                    slot.BeatId > ProductionBranchFlow.LastBranchBeatId) continue;
                ResolveSymbolic(slot, controller, judge);
                branchDemands++;
            }
            Assert.AreEqual(ProductionBranchFlow.BranchDemandCount, branchDemands);
            Assert.AreEqual(course.Slots.Length, judge.SuccessCount);
            Assert.AreEqual(1, judge.FailureCount,
                "the protected proof remains the only failed judgement");
            Assert.AreEqual(ProductionGameLoop.StartingLives, loop.Lives);

            controller.SetStorySuspended(true);
            controller.SetMovementSuspended(true);
            controller.RecoverAt(new Vector2(branch.CoreApproachWorldX, 0f));
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
            Assert.AreEqual(ProductionRoute.Right, loop.SelectedRoute);
            Assert.AreEqual(ProductionGameLoop.StartingLives, loop.Lives);

            ending.RestartFromEnding();
            yield return null;
            Assert.AreEqual(0, teaching.CompletedTeachingCount);
            Assert.AreEqual(0, teaching.ProtectedProofCount);
            Assert.AreEqual(0, judge.SuccessCount);
            Assert.AreEqual(0, judge.FailureCount);
            Assert.AreEqual(ProductionRoute.None, loop.SelectedRoute);
            Assert.AreEqual(ProductionRoute.None, branch.ActiveRoute);
            Assert.AreEqual(ProductionCoreStage.None, core.Stage);
            Assert.AreEqual(ProductionEndingStage.None, ending.Stage);
            Assert.AreEqual(ProductionGameLoop.StartingLives, loop.Lives);
            Assert.AreEqual(0f, controller.transform.position.x, 0.01f);
            Assert.IsFalse(controller.StorySuspended);
            Assert.IsFalse(controller.MovementSuspended);
        }

        private IEnumerator CompleteTeaching(NulProductionController controller,
            ProductionTeachingFlow teaching, ProductionCourseLoop course,
            int beatId, ProductionObstacleKind kind, string command)
        {
            ProductionCourseLoop.ObstacleSlot slot = SlotAtBeat(course, beatId);
            teaching.BeginTeaching(beatId, kind, slot.WorldX);
            for (int i = 0; i < ProductionTeachingFlow.SamplesPerTeaching; i++)
                controller.InjectDatagram(Cmd(++_commandId, command));
            Assert.AreEqual(ProductionTeachingStage.Confirming, teaching.Stage);
            yield return new WaitForSeconds(ProductionTeachingFlow.ConfirmationSeconds + 0.1f);
            Assert.IsFalse(teaching.IsActive);
        }

        private void ResolveSymbolic(ProductionCourseLoop.ObstacleSlot slot,
            NulProductionController controller, ProductionObstacleJudge judge)
        {
            controller.RecoverAt(new Vector2(slot.WorldX - 30f, 0f));
            switch (slot.Kind)
            {
                case ProductionObstacleKind.Jump:
                    controller.InjectDatagram(Cmd(++_commandId, "JUMP"));
                    controller.transform.position = new Vector3(slot.WorldX, 8f, 0f);
                    judge.EvaluateNowAt(3f);
                    break;
                case ProductionObstacleKind.Crouch:
                    controller.InjectDatagram(Cmd(++_commandId, "CROUCH"));
                    controller.transform.position = new Vector3(slot.WorldX, 0f, 0f);
                    judge.EvaluateNowAt(3f);
                    break;
                case ProductionObstacleKind.Stop:
                    controller.InjectDatagram(Cmd(++_commandId, "STOP_ON"));
                    judge.EvaluateNowAt(3f);
                    controller.InjectDatagram(Cmd(++_commandId, "STOP_OFF"));
                    controller.transform.position = new Vector3(slot.WorldX, 0f, 0f);
                    judge.EvaluateNowAt(3f);
                    break;
                default:
                    Assert.Fail("unexpected non-symbolic demand");
                    break;
            }

            Assert.AreEqual(slot.StableId, judge.LatestResult.StableId,
                "wrong result at beat " + slot.BeatId + " demand " + slot.DemandIndex);
            Assert.IsTrue(judge.LatestResult.Success,
                "failed beat " + slot.BeatId + " demand " + slot.DemandIndex);
        }

        private void BuildRig(out NulProductionController controller,
            out ProductionCourseLoop course, out ProductionObstacleJudge judge,
            out ProductionGameLoop loop, out ProductionTeachingFlow teaching,
            out ProductionForkFlow fork, out ProductionBranchFlow branch,
            out ProductionCoreFlow core, out ProductionEndingFlow ending)
        {
            _root = new GameObject("slice32-continuous-story-rig");
            _root.SetActive(false);
            _root.AddComponent<Rigidbody2D>().gravityScale = 0f;
            var stand = new GameObject("stand").AddComponent<BoxCollider2D>();
            stand.transform.SetParent(_root.transform, false);
            var crouch = new GameObject("crouch").AddComponent<BoxCollider2D>();
            crouch.transform.SetParent(_root.transform, false);
            crouch.enabled = false;

            controller = _root.AddComponent<NulProductionController>();
            controller.udpPort = 8922;
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
            teaching = _root.AddComponent<ProductionTeachingFlow>();
            fork = _root.AddComponent<ProductionForkFlow>();
            branch = _root.AddComponent<ProductionBranchFlow>();
            core = _root.AddComponent<ProductionCoreFlow>();
            ending = _root.AddComponent<ProductionEndingFlow>();
            _root.SetActive(true);
        }

        private static ProductionCourseLoop.ObstacleSlot SlotAtBeat(
            ProductionCourseLoop course, int beatId)
        {
            foreach (ProductionCourseLoop.ObstacleSlot slot in course.Slots)
                if (slot.BeatId == beatId) return slot;
            Assert.Fail("missing beat " + beatId);
            return default;
        }

        private static string Cmd(int id, string type)
            => "{\"v\":1,\"kind\":\"cmd\",\"session\":\"" + Session +
               "\",\"sstart\":" + SessionStart +
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
