using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Before.Production.Tests
{
    public class ProductionSlice36PlayTests
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
        public IEnumerator NewSessionDirectionCommitsForkAndStaleOldDirectionCannotReverseIt()
        {
            BuildRig(out NulProductionController controller,
                out ProductionCourseLoop course, out ProductionGameLoop loop,
                out ProductionForkFlow fork, out ProductionBranchFlow branch);
            yield return null;

            controller.InjectDatagram(Cmd("d171-old", 38000, 7, "STOP_OFF"));
            Assert.AreEqual(1, controller.Bus.Published);
            Assert.AreEqual("d171-old", controller.Router.Session.ActiveSession);

            ProductionCourseLoop.ObstacleSlot direction = DirectionSlot(course);
            controller.RecoverAt(new Vector2(direction.WorldX, 0f));
            fork.TryEnterFork();
            yield return null;
            Assert.IsTrue(controller.Nul.DirectionMode);

            controller.InjectDatagram(Cmd("d171-new", 38001, 1, "RIGHT"));
            controller.InjectDatagram(Cmd("d171-new", 38001, 1, "RIGHT"));
            yield return null;
            Assert.AreEqual(2, controller.Bus.Published);
            Assert.AreEqual("d171-new", controller.Router.Session.ActiveSession);
            Assert.AreEqual(ProductionRoute.Right, loop.SelectedRoute);
            Assert.AreEqual(ProductionRoute.Right, branch.ActiveRoute);
            Assert.IsTrue(controller.Nul.DirectionMode,
                "exclusive ownership remains through the specified map collapse");

            controller.InjectDatagram(Cmd("d171-old", 38000, 8, "LEFT"));
            yield return null;
            Assert.AreEqual(2, controller.Bus.Published);
            Assert.AreEqual(ProductionRoute.Right, loop.SelectedRoute);
            Assert.AreEqual(ProductionRoute.Right, branch.ActiveRoute);
            Assert.AreEqual(1, controller.Router.Session.DiscardedStale);

            yield return new WaitForSeconds(ProductionForkFlow.ExpandSeconds +
                                            ProductionForkFlow.ConfirmSeconds +
                                            ProductionForkFlow.CollapseSeconds + 0.2f);
            Assert.AreEqual(ProductionForkStage.None, fork.Stage);
            Assert.IsFalse(controller.Nul.DirectionMode);
        }

        private void BuildRig(out NulProductionController controller,
            out ProductionCourseLoop course, out ProductionGameLoop loop,
            out ProductionForkFlow fork, out ProductionBranchFlow branch)
        {
            _root = new GameObject("slice36-fork-session-rig");
            _root.SetActive(false);
            _root.AddComponent<Rigidbody2D>().gravityScale = 0f;
            var stand = new GameObject("stand").AddComponent<BoxCollider2D>();
            stand.transform.SetParent(_root.transform, false);
            var crouch = new GameObject("crouch").AddComponent<BoxCollider2D>();
            crouch.transform.SetParent(_root.transform, false);
            crouch.enabled = false;
            controller = _root.AddComponent<NulProductionController>();
            controller.udpPort = 8926;
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
