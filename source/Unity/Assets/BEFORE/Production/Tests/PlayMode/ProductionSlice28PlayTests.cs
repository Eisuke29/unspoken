using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Before.Production.Tests
{
    public class ProductionSlice28PlayTests
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
        public IEnumerator BranchCompletionStopsGameplayAndReachesIndependentPower()
        {
            BuildRig(8913, out NulProductionController controller,
                out ProductionGameLoop loop, out ProductionBranchFlow branch,
                out ProductionCoreFlow core, out ProductionCorePresenter presenter);
            yield return null;

            Assert.IsTrue(branch.ApplyRoute(ProductionRoute.Left));
            controller.RecoverAt(new Vector2(branch.CoreApproachWorldX, 0f));
            yield return null;
            Assert.AreEqual(ProductionCoreStage.Unfolding, core.Stage);
            Assert.IsTrue(controller.StorySuspended);
            Assert.IsTrue(controller.MovementSuspended);
            Assert.AreEqual(branch.CoreApproachWorldX, controller.transform.position.x, 0.01f);

            controller.InjectDatagram(Cmd("core-a", 1, "JUMP"));
            yield return new WaitForSeconds(0.1f);
            Assert.IsFalse(controller.Airborne,
                "CORE suspension consumes command input without gameplay reaction");

            yield return new WaitForSeconds(ProductionCoreFlow.UnfoldSeconds +
                                            ProductionCoreFlow.TransferSeconds +
                                            ProductionCoreFlow.DisconnectSeconds + 0.15f);
            Assert.AreEqual(ProductionCoreStage.IndependentHold, core.Stage);
            Assert.IsTrue(core.IndependentPower);
            Assert.IsTrue(controller.StorySuspended);
            presenter.RefreshNow();
            Assert.IsTrue(presenter.Visible);
            Assert.AreEqual(ProductionCorePresenter.TemporaryRendererCount,
                presenter.VisualRendererCount);

            loop.ResetLoop();
            yield return null;
            Assert.AreEqual(ProductionCoreStage.None, core.Stage);
            Assert.IsFalse(core.IndependentPower);
            Assert.IsFalse(controller.StorySuspended);
            Assert.IsFalse(controller.MovementSuspended);
        }

        [UnityTest]
        public IEnumerator PauseFreezesCoreStageAndDoesNotReplayConsumedDirection()
        {
            BuildRig(8914, out NulProductionController controller,
                out _, out ProductionBranchFlow branch, out ProductionCoreFlow core,
                out _);
            yield return null;
            Assert.IsTrue(branch.ApplyRoute(ProductionRoute.Right));
            controller.RecoverAt(new Vector2(branch.CoreApproachWorldX, 0f));
            yield return null;

            controller.Nul.Paused = true;
            float elapsed = core.StageElapsed;
            controller.InjectDatagram(Cmd("core-b", 1, "RIGHT"));
            yield return new WaitForSeconds(0.15f);
            Assert.AreEqual(ProductionCoreStage.Unfolding, core.Stage);
            Assert.AreEqual(elapsed, core.StageElapsed, 0.03f);
            Assert.AreEqual(0, controller.Nul.LastDirection);

            controller.Nul.Paused = false;
            yield return new WaitForSeconds(0.15f);
            Assert.Greater(core.StageElapsed, elapsed);
            Assert.AreEqual(0, controller.Nul.LastDirection,
                "paused direction was consumed and did not replay");
        }

        private void BuildRig(int port, out NulProductionController controller,
            out ProductionGameLoop loop, out ProductionBranchFlow branch,
            out ProductionCoreFlow core, out ProductionCorePresenter presenter)
        {
            _root = new GameObject("slice28-core-rig");
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
            var course = _root.AddComponent<ProductionCourseLoop>();
            _definition = ScriptableObject.CreateInstance<ProductionCourseDefinition>();
            _definition.LoadProductionDefaults();
            course.Definition = _definition;
            _root.AddComponent<ProductionObstacleJudge>();
            loop = _root.AddComponent<ProductionGameLoop>();
            branch = _root.AddComponent<ProductionBranchFlow>();
            core = _root.AddComponent<ProductionCoreFlow>();
            presenter = _root.AddComponent<ProductionCorePresenter>();
            _root.SetActive(true);
        }

        private static string Cmd(string session, int id, string type)
            => "{\"v\":1,\"kind\":\"cmd\",\"session\":\"" + session +
               "\",\"sstart\":23000,\"cmdId\":" + id + ",\"seq\":" + id +
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
