using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Before.Production.Tests
{
    public class ProductionSlice41PlayTests
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
        public IEnumerator EndingRestartClearsStoryAppliedStopAndCrouchWithoutReplay()
        {
            BuildRig(out NulProductionController controller,
                out ProductionGameLoop loop, out ProductionBranchFlow branch,
                out ProductionCoreFlow core, out ProductionEndingFlow ending);
            yield return null;

            Assert.IsTrue(branch.ApplyRoute(ProductionRoute.Left));
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
            Assert.IsTrue(controller.StorySuspended);

            controller.InjectDatagram(Cmd(1, "STOP_ON"));
            controller.InjectDatagram(Cmd(2, "CROUCH"));
            controller.InjectDatagram(Cmd(1, "STOP_ON"));
            controller.InjectDatagram(Cmd(2, "CROUCH"));
            yield return null;
            Assert.AreEqual(2, controller.Bus.Published);
            Assert.IsTrue(controller.Nul.StopActive);
            Assert.IsTrue(controller.Nul.Crouching);
            Assert.IsFalse(controller.CrouchColliderActive,
                "story suspension consumes state but suppresses collider reaction");

            ending.RestartFromEnding();
            yield return null;
            Assert.AreEqual(ProductionEndingStage.None, ending.Stage);
            Assert.AreEqual(ProductionGameLoop.StartingLives, loop.Lives);
            Assert.IsFalse(controller.StorySuspended);
            Assert.IsFalse(controller.Nul.StopActive);
            Assert.IsFalse(controller.Nul.Crouching);
            Assert.IsFalse(controller.CrouchColliderActive);

            controller.InjectDatagram(Cmd(1, "STOP_ON"));
            controller.InjectDatagram(Cmd(2, "CROUCH"));
            Assert.AreEqual(2, controller.Bus.Published,
                "ending restart must not replay either consumed persistent command");
            Assert.IsFalse(controller.Nul.StopActive);
            Assert.IsFalse(controller.Nul.Crouching);
            Assert.IsFalse(controller.CrouchColliderActive);

            controller.InjectDatagram(Cmd(3, "CROUCH"));
            yield return null;
            Assert.AreEqual(3, controller.Bus.Published);
            Assert.IsTrue(controller.Nul.Crouching);
            Assert.IsTrue(controller.CrouchColliderActive,
                "the next unique CROUCH uses the normal collider path");

            controller.InjectDatagram(Cmd(4, "STOP_ON"));
            yield return null;
            Assert.AreEqual(4, controller.Bus.Published);
            Assert.IsTrue(controller.Nul.StopActive,
                "the next unique STOP uses the normal persistent latch");
        }

        private void BuildRig(out NulProductionController controller,
            out ProductionGameLoop loop, out ProductionBranchFlow branch,
            out ProductionCoreFlow core, out ProductionEndingFlow ending)
        {
            _root = new GameObject("slice41-ending-transient-rig");
            _root.SetActive(false);
            _root.AddComponent<Rigidbody2D>().gravityScale = 0f;
            var stand = new GameObject("stand").AddComponent<BoxCollider2D>();
            stand.transform.SetParent(_root.transform, false);
            var crouch = new GameObject("crouch").AddComponent<BoxCollider2D>();
            crouch.transform.SetParent(_root.transform, false);
            crouch.enabled = false;
            controller = _root.AddComponent<NulProductionController>();
            controller.udpPort = 8931;
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
            ending = _root.AddComponent<ProductionEndingFlow>();
            _root.SetActive(true);
        }

        private static string Cmd(int id, string type)
            => "{\"v\":1,\"kind\":\"cmd\",\"session\":\"d176-terminal\"" +
               ",\"sstart\":45000,\"cmdId\":" + id + ",\"seq\":" + id +
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
