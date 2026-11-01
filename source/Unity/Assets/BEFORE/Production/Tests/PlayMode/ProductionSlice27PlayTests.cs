using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Before.Production.Tests
{
    public class ProductionSlice27PlayTests
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
        public IEnumerator LeftForkAppliesWideLowCityProfileAndResetRestoresDefaults()
        {
            yield return VerifyRouteAndReset(ProductionRoute.Left, "LEFT", 8911);
        }

        [UnityTest]
        public IEnumerator RightForkAppliesTightIndustrialProfileAndPresentsSignal()
        {
            yield return VerifyRouteAndReset(ProductionRoute.Right, "RIGHT", 8912);
        }

        private IEnumerator VerifyRouteAndReset(ProductionRoute expected, string command, int port)
        {
            BuildRig(port, out NulProductionController controller,
                out ProductionCourseLoop course, out ProductionGameLoop loop,
                out ProductionForkFlow fork, out ProductionBranchFlow branch,
                out ProductionBranchPresenter presenter);
            yield return null;

            ProductionCourseLoop.ObstacleSlot[] defaults =
                (ProductionCourseLoop.ObstacleSlot[])course.Slots.Clone();
            ProductionCourseLoop.ObstacleSlot direction = DirectionSlot(course);
            controller.RecoverAt(new Vector2(direction.WorldX, 0f));
            fork.TryEnterFork();
            yield return null;
            controller.InjectDatagram(Cmd("branch-" + port, 1, command));
            yield return null;

            Assert.AreEqual(expected, loop.SelectedRoute);
            Assert.AreEqual(expected, branch.ActiveRoute);
            Assert.IsTrue(branch.BranchStarted);
            Assert.IsFalse(branch.BranchCompleted);
            Assert.Greater(branch.CoreApproachWorldX, branch.BranchStartWorldX);
            presenter.RefreshNow();
            Assert.AreEqual(ProductionBranchPresenter.TemporaryRendererCount,
                presenter.VisualRendererCount);
            Assert.IsTrue(presenter.Visible);

            int branchIndex = 0;
            for (int i = 0; i < course.Slots.Length; i++)
            {
                Assert.AreEqual(defaults[i].StableId, course.Slots[i].StableId);
                if (course.Slots[i].BeatId < ProductionBranchFlow.FirstBranchBeatId ||
                    course.Slots[i].BeatId > ProductionBranchFlow.LastBranchBeatId) continue;
                Assert.AreEqual(ProductionBranchFlow.KindAt(expected, branchIndex),
                    course.Slots[i].Kind);
                branchIndex++;
            }
            Assert.AreEqual(ProductionBranchFlow.BranchDemandCount, branchIndex);

            controller.RecoverAt(new Vector2(branch.CoreApproachWorldX, 0f));
            yield return null;
            Assert.IsTrue(branch.BranchCompleted);
            Assert.AreEqual(1f, branch.BranchProgress01, 0.001f);

            loop.ResetLoop();
            yield return null;
            Assert.AreEqual(ProductionRoute.None, branch.ActiveRoute);
            Assert.IsFalse(branch.BranchStarted);
            Assert.IsFalse(branch.BranchCompleted);
            Assert.AreEqual(0f, branch.BranchProgress01, 0.001f);
            for (int i = 0; i < course.Slots.Length; i++)
            {
                Assert.AreEqual(defaults[i].StableId, course.Slots[i].StableId);
                Assert.AreEqual(defaults[i].Kind, course.Slots[i].Kind);
                Assert.AreEqual(defaults[i].WorldX, course.Slots[i].WorldX, 0.001f);
            }
        }

        private void BuildRig(int port, out NulProductionController controller,
            out ProductionCourseLoop course, out ProductionGameLoop loop,
            out ProductionForkFlow fork, out ProductionBranchFlow branch,
            out ProductionBranchPresenter presenter)
        {
            _root = new GameObject("slice27-branch-rig");
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
            presenter = _root.AddComponent<ProductionBranchPresenter>();
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
               "\",\"sstart\":20000,\"cmdId\":" + id + ",\"seq\":" + id +
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
