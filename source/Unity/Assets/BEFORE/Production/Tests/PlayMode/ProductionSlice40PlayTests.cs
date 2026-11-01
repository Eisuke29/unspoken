using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Before.Production.Tests
{
    public class ProductionSlice40PlayTests
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
        public IEnumerator CoreNewSessionRemainsConsumedThroughEndingAndRestart()
        {
            BuildRig(out NulProductionController controller,
                out ProductionGameLoop loop, out ProductionBranchFlow branch,
                out ProductionCoreFlow core, out ProductionEndingFlow ending);
            yield return null;

            controller.InjectDatagram(Cmd("d175-old", 43000, 7, "STOP_OFF"));
            Assert.AreEqual(1, controller.Bus.Published);
            Assert.IsTrue(branch.ApplyRoute(ProductionRoute.Right));
            controller.RecoverAt(new Vector2(branch.CoreApproachWorldX, 0f));
            yield return null;
            Assert.AreEqual(ProductionCoreStage.Unfolding, core.Stage);
            Assert.IsTrue(controller.StorySuspended);

            int jumps = controller.Nul.JumpCount;
            controller.InjectDatagram(Cmd("d175-new", 43001, 1, "JUMP"));
            controller.InjectDatagram(Cmd("d175-new", 43001, 1, "JUMP"));
            Assert.AreEqual(2, controller.Bus.Published);
            Assert.AreEqual(jumps + 1, controller.Nul.JumpCount,
                "new-session input is consumed exactly once during CORE");
            Assert.AreEqual("d175-new", controller.Router.Session.ActiveSession);
            Assert.IsFalse(controller.Airborne,
                "CORE story suspension suppresses physical reaction");

            controller.InjectDatagram(Cmd("d175-old", 43000, 8, "JUMP"));
            Assert.AreEqual(2, controller.Bus.Published);
            Assert.AreEqual(1, controller.Router.Session.DiscardedStale);

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
            Assert.IsFalse(controller.Airborne);

            ending.RestartFromEnding();
            yield return null;
            Assert.AreEqual(ProductionEndingStage.None, ending.Stage);
            Assert.IsFalse(controller.StorySuspended);
            Assert.AreEqual("d175-new", controller.Router.Session.ActiveSession);

            controller.InjectDatagram(Cmd("d175-new", 43001, 1, "JUMP"));
            Assert.AreEqual(2, controller.Bus.Published,
                "the CORE-consumed command must not replay after ending restart");
            Assert.IsFalse(controller.Airborne);

            controller.InjectDatagram(Cmd("d175-new", 43001, 2, "JUMP"));
            Assert.AreEqual(3, controller.Bus.Published);
            Assert.AreEqual(jumps + 2, controller.Nul.JumpCount);
            Assert.IsTrue(controller.Airborne,
                "the next unique command reacts immediately after restart");
            Assert.AreEqual(ProductionGameLoop.StartingLives, loop.Lives);
        }

        private void BuildRig(out NulProductionController controller,
            out ProductionGameLoop loop, out ProductionBranchFlow branch,
            out ProductionCoreFlow core, out ProductionEndingFlow ending)
        {
            _root = new GameObject("slice40-core-session-rig");
            _root.SetActive(false);
            _root.AddComponent<Rigidbody2D>().gravityScale = 0f;
            var stand = new GameObject("stand").AddComponent<BoxCollider2D>();
            stand.transform.SetParent(_root.transform, false);
            var crouch = new GameObject("crouch").AddComponent<BoxCollider2D>();
            crouch.transform.SetParent(_root.transform, false);
            crouch.enabled = false;
            controller = _root.AddComponent<NulProductionController>();
            controller.udpPort = 8930;
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
