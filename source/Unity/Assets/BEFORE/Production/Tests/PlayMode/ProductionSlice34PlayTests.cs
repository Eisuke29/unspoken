using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Before.Production.Tests
{
    public class ProductionSlice34PlayTests
    {
        private const string OldSession = "d169-old-sidecar";
        private const string NewSession = "d169-new-sidecar";
        private const int OldStart = 36000;
        private const int NewStart = 36001;

        private GameObject _root;
        private ProductionCourseDefinition _definition;

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.Destroy(_root);
            if (_definition != null) Object.Destroy(_definition);
        }

        [UnityTest]
        public IEnumerator NewSidecarSessionWithResetCommandIdWorksAfterEndingRestart()
        {
            BuildRig(out NulProductionController controller,
                out ProductionGameLoop loop, out ProductionBranchFlow branch,
                out ProductionCoreFlow core, out ProductionEndingFlow ending);
            yield return null;

            Assert.IsTrue(branch.ApplyRoute(ProductionRoute.Right));
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
            int published = controller.Bus.Published;
            int jumps = controller.Nul.JumpCount;

            controller.InjectDatagram(Cmd(OldSession, OldStart, 1, "JUMP"));
            controller.InjectDatagram(Cmd(OldSession, OldStart, 1, "JUMP"));
            Assert.AreEqual(published + 1, controller.Bus.Published);
            Assert.AreEqual(jumps + 1, controller.Nul.JumpCount);
            Assert.IsFalse(controller.Airborne,
                "terminal suspension consumes but does not physically react");

            ending.RestartFromEnding();
            yield return null;
            Assert.AreEqual(ProductionEndingStage.None, ending.Stage);
            Assert.AreEqual(ProductionRoute.None, loop.SelectedRoute);
            Assert.AreEqual(OldSession, controller.Router.Session.ActiveSession);

            controller.InjectDatagram(Cmd(NewSession, NewStart, 1, "JUMP"));
            Assert.AreEqual(published + 2, controller.Bus.Published,
                "a newer Sidecar session may restart command IDs at one");
            Assert.AreEqual(jumps + 2, controller.Nul.JumpCount);
            Assert.AreEqual(NewSession, controller.Router.Session.ActiveSession);
            Assert.AreEqual(1, controller.Router.Dedup.Count,
                "session rollover clears only the old session's dedup history");
            Assert.IsTrue(controller.Airborne,
                "the first command of the new session reacts immediately");

            controller.InjectDatagram(Cmd(OldSession, OldStart, 2, "JUMP"));
            Assert.AreEqual(published + 2, controller.Bus.Published,
                "the superseded Sidecar session must remain quarantined");
            Assert.AreEqual(jumps + 2, controller.Nul.JumpCount);
            Assert.AreEqual(1, controller.Router.Session.DiscardedStale);
        }

        private void BuildRig(out NulProductionController controller,
            out ProductionGameLoop loop, out ProductionBranchFlow branch,
            out ProductionCoreFlow core, out ProductionEndingFlow ending)
        {
            _root = new GameObject("slice34-session-rollover-rig");
            _root.SetActive(false);
            _root.AddComponent<Rigidbody2D>().gravityScale = 0f;
            var stand = new GameObject("stand").AddComponent<BoxCollider2D>();
            stand.transform.SetParent(_root.transform, false);
            var crouch = new GameObject("crouch").AddComponent<BoxCollider2D>();
            crouch.transform.SetParent(_root.transform, false);
            crouch.enabled = false;
            controller = _root.AddComponent<NulProductionController>();
            controller.udpPort = 8924;
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
