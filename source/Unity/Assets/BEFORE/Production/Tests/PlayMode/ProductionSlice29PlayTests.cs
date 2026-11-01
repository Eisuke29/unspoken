using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Before.Production.Tests
{
    public class ProductionSlice29PlayTests
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
        public IEnumerator CoreRunsFarewellIntoIndefiniteHoldAndExistingRestartResetsAll()
        {
            BuildRig(8915, out NulProductionController controller,
                out ProductionGameLoop loop, out ProductionBranchFlow branch,
                out ProductionEndingFlow ending, out ProductionEndingPresenter presenter);
            yield return ReachFarewell(controller, branch);
            Assert.AreEqual(ProductionEndingStage.TestSteps, ending.Stage);

            float choreography = ProductionEndingFlow.TestStepsSeconds +
                ProductionEndingFlow.LookBackSeconds +
                ProductionEndingFlow.MemoryTracesSeconds +
                ProductionEndingFlow.BowSeconds +
                ProductionEndingFlow.DepartSeconds +
                ProductionEndingFlow.WorldFadeSeconds +
                ProductionEndingFlow.TraceFadeSeconds +
                ProductionEndingFlow.CompleteMessageSeconds;
            yield return new WaitForSeconds(choreography + 0.25f);

            Assert.AreEqual(ProductionEndingStage.EndingHold, ending.Stage);
            Assert.IsTrue(ending.GameplayEnded);
            Assert.IsTrue(controller.StorySuspended);
            Assert.IsTrue(controller.MovementSuspended);
            float lockedX = controller.transform.position.x;
            controller.InjectDatagram(Cmd("ending-a", 1, "JUMP"));
            yield return new WaitForSeconds(0.15f);
            Assert.AreEqual(ProductionEndingStage.EndingHold, ending.Stage,
                "ending must hold indefinitely without automatic reset or exit");
            Assert.AreEqual(lockedX, controller.transform.position.x, 0.01f);
            Assert.IsFalse(controller.Airborne);
            presenter.RefreshNow();
            Assert.IsTrue(presenter.Visible);
            Assert.AreEqual(1f, presenter.WorldFadeAlpha, 0.001f);

            ending.RestartFromEnding();
            yield return null;
            Assert.AreEqual(ProductionEndingStage.None, ending.Stage);
            Assert.IsFalse(ending.GameplayEnded);
            Assert.AreEqual(ProductionGameLoop.StartingLives, loop.Lives);
            Assert.AreEqual(ProductionRoute.None, loop.SelectedRoute);
            Assert.IsFalse(controller.StorySuspended);
            Assert.IsFalse(controller.MovementSuspended);
            Assert.AreEqual(0f, controller.transform.position.x, 0.01f);
        }

        [UnityTest]
        public IEnumerator PauseFreezesFarewellAndResetClearsItWithoutLeavingPlay()
        {
            BuildRig(8916, out NulProductionController controller,
                out ProductionGameLoop loop, out ProductionBranchFlow branch,
                out ProductionEndingFlow ending, out _);
            yield return ReachFarewell(controller, branch);
            controller.Nul.Paused = true;
            float elapsed = ending.StageElapsed;
            yield return new WaitForSeconds(0.15f);
            Assert.AreEqual(ProductionEndingStage.TestSteps, ending.Stage);
            Assert.AreEqual(elapsed, ending.StageElapsed, 0.03f);

            controller.Nul.Paused = false;
            yield return new WaitForSeconds(0.15f);
            Assert.Greater(ending.StageElapsed, elapsed);
            loop.ResetLoop();
            yield return null;
            Assert.AreEqual(ProductionEndingStage.None, ending.Stage);
            Assert.IsFalse(controller.Nul.Paused);
            Assert.IsFalse(controller.StorySuspended);
        }

        private IEnumerator ReachFarewell(NulProductionController controller,
            ProductionBranchFlow branch)
        {
            Assert.IsTrue(branch.ApplyRoute(ProductionRoute.Left));
            controller.RecoverAt(new Vector2(branch.CoreApproachWorldX, 0f));
            yield return null;
            yield return new WaitForSeconds(ProductionCoreFlow.UnfoldSeconds +
                                            ProductionCoreFlow.TransferSeconds +
                                            ProductionCoreFlow.DisconnectSeconds + 0.15f);
        }

        private void BuildRig(int port, out NulProductionController controller,
            out ProductionGameLoop loop, out ProductionBranchFlow branch,
            out ProductionEndingFlow ending, out ProductionEndingPresenter presenter)
        {
            _root = new GameObject("slice29-ending-rig");
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
            _root.AddComponent<ProductionCoreFlow>();
            ending = _root.AddComponent<ProductionEndingFlow>();
            presenter = _root.AddComponent<ProductionEndingPresenter>();
            _root.SetActive(true);
        }

        private static string Cmd(string session, int id, string type)
            => "{\"v\":1,\"kind\":\"cmd\",\"session\":\"" + session +
               "\",\"sstart\":24000,\"cmdId\":" + id + ",\"seq\":" + id +
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
