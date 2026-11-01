using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Before.Production.Tests
{
    public class ProductionSlice24PlayTests
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
        public IEnumerator AllThreeTeachingsContinueIntoForkThroughOneFrozenCommandPath()
        {
            BuildRig(out NulProductionController controller,
                out ProductionCourseLoop course, out ProductionGameLoop loop,
                out ProductionTeachingFlow teaching, out ProductionForkFlow fork);
            yield return null;

            yield return CompleteTeaching(controller, teaching, 1,
                ProductionObstacleKind.Jump, "JUMP");
            yield return CompleteTeaching(controller, teaching, 4,
                ProductionObstacleKind.Crouch, "CROUCH");
            yield return CompleteTeaching(controller, teaching, 13,
                ProductionObstacleKind.Stop, "STOP_ON");

            Assert.IsTrue(teaching.AllTeachingsCompleted);
            Assert.AreEqual(3, teaching.CompletedTeachingCount);
            Assert.IsFalse(teaching.IsActive);
            Assert.AreEqual(ProductionGameLoop.StartingLives, loop.Lives);

            ProductionCourseLoop.ObstacleSlot direction = DirectionSlot(course);
            controller.RecoverAt(new Vector2(direction.WorldX, 0f));
            fork.TryEnterFork();
            Assert.IsTrue(fork.IsActive);
            Assert.IsTrue(controller.Nul.DirectionMode);

            controller.InjectDatagram(Cmd(++_commandId, "RIGHT"));
            Assert.AreEqual(ProductionRoute.Right, fork.ChosenRoute);
            Assert.AreEqual(ProductionRoute.Right, loop.SelectedRoute);
            yield return new WaitForSeconds(ProductionForkFlow.ExpandSeconds +
                                            ProductionForkFlow.ConfirmSeconds +
                                            ProductionForkFlow.CollapseSeconds + 0.15f);

            Assert.IsFalse(fork.IsActive);
            Assert.AreEqual(ProductionRoute.Right, course.SelectedRoute);
            Assert.IsFalse(controller.StorySuspended);
            Assert.IsFalse(controller.MovementSuspended);
            Assert.IsFalse(controller.Nul.DirectionMode);
            Assert.AreEqual(ProductionGameLoop.StartingLives, loop.Lives);

            loop.ResetLoop();
            yield return null;
            Assert.AreEqual(0, teaching.CompletedTeachingCount);
            Assert.AreEqual(0, teaching.ProtectedProofCount);
            Assert.AreEqual(ProductionRoute.None, loop.SelectedRoute);
            Assert.AreEqual(ProductionRoute.None, course.SelectedRoute);
            Assert.IsFalse(fork.IsActive);
        }

        private IEnumerator CompleteTeaching(
            NulProductionController controller, ProductionTeachingFlow teaching,
            int beatId, ProductionObstacleKind kind, string commandType)
        {
            teaching.BeginTeaching(beatId, kind, 100f);
            Assert.IsTrue(teaching.IsActive);
            Assert.AreEqual(kind, teaching.TargetKind);
            for (int sample = 0; sample < ProductionTeachingFlow.SamplesPerTeaching; sample++)
                controller.InjectDatagram(Cmd(++_commandId, commandType));
            Assert.AreEqual(ProductionTeachingStage.Confirming, teaching.Stage);
            yield return new WaitForSeconds(ProductionTeachingFlow.ConfirmationSeconds + 0.1f);
            Assert.IsFalse(teaching.IsActive);
        }

        private void BuildRig(out NulProductionController controller,
                              out ProductionCourseLoop course, out ProductionGameLoop loop,
                              out ProductionTeachingFlow teaching, out ProductionForkFlow fork)
        {
            _root = new GameObject("slice24-story-fork-rig");
            _root.SetActive(false);
            _root.AddComponent<Rigidbody2D>().gravityScale = 0f;
            var stand = new GameObject("stand").AddComponent<BoxCollider2D>();
            stand.transform.SetParent(_root.transform, false);
            var crouch = new GameObject("crouch").AddComponent<BoxCollider2D>();
            crouch.transform.SetParent(_root.transform, false);
            crouch.enabled = false;

            controller = _root.AddComponent<NulProductionController>();
            controller.udpPort = 8899;
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
            teaching = _root.AddComponent<ProductionTeachingFlow>();
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

        private static string Cmd(int id, string type)
            => "{\"v\":1,\"kind\":\"cmd\",\"session\":\"story-fork\"," +
               "\"sstart\":20000,\"cmdId\":" + id + ",\"seq\":" + id +
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
