using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Before.Production.Tests
{
    public class ProductionSlice33PlayTests
    {
        private const string Session = "d167-terminal";
        private const int SessionStart = 35000;

        private GameObject _root;
        private ProductionCourseDefinition _definition;

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.Destroy(_root);
            if (_definition != null) Object.Destroy(_definition);
        }

        [UnityTest]
        public IEnumerator EndingConsumesOnceAndNextCommandWorksAfterSameSessionRestart()
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
            int published = controller.Bus.Published;
            int jumps = controller.Nul.JumpCount;

            controller.InjectDatagram(Cmd(1, "JUMP"));
            Assert.AreEqual(published + 1, controller.Bus.Published,
                "terminal command must still cross session/dedup and publish");
            Assert.AreEqual(jumps + 1, controller.Nul.JumpCount,
                "published terminal command is consumed by the frozen state path");
            Assert.IsFalse(controller.Airborne,
                "story suspension must suppress physical reaction");

            controller.InjectDatagram(Cmd(1, "JUMP"));
            Assert.AreEqual(published + 1, controller.Bus.Published,
                "duplicate command must remain deduplicated in ending hold");
            Assert.AreEqual(jumps + 1, controller.Nul.JumpCount);

            ending.RestartFromEnding();
            yield return null;
            Assert.AreEqual(ProductionEndingStage.None, ending.Stage);
            Assert.AreEqual(ProductionRoute.None, loop.SelectedRoute);
            Assert.IsFalse(controller.StorySuspended);
            Assert.IsFalse(controller.MovementSuspended);
            Assert.IsFalse(controller.Airborne);

            controller.InjectDatagram(Cmd(1, "JUMP"));
            Assert.AreEqual(published + 1, controller.Bus.Published,
                "restart must not erase dedup and replay an old command");
            Assert.IsFalse(controller.Airborne);

            controller.InjectDatagram(Cmd(2, "JUMP"));
            Assert.AreEqual(published + 2, controller.Bus.Published);
            Assert.AreEqual(jumps + 2, controller.Nul.JumpCount);
            Assert.IsTrue(controller.Airborne,
                "the next unique command in the same session works immediately");
            Assert.AreEqual(ProductionGameLoop.StartingLives, loop.Lives);
        }

        private void BuildRig(out NulProductionController controller,
            out ProductionGameLoop loop, out ProductionBranchFlow branch,
            out ProductionCoreFlow core, out ProductionEndingFlow ending)
        {
            _root = new GameObject("slice33-terminal-transport-rig");
            _root.SetActive(false);
            _root.AddComponent<Rigidbody2D>().gravityScale = 0f;
            var stand = new GameObject("stand").AddComponent<BoxCollider2D>();
            stand.transform.SetParent(_root.transform, false);
            var crouch = new GameObject("crouch").AddComponent<BoxCollider2D>();
            crouch.transform.SetParent(_root.transform, false);
            crouch.enabled = false;
            controller = _root.AddComponent<NulProductionController>();
            controller.udpPort = 8923;
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
