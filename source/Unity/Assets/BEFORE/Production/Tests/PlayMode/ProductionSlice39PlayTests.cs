using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Before.Production.Tests
{
    public class ProductionSlice39PlayTests
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
        public IEnumerator GameOverConsumesNewSessionCommandWithoutReplayAfterRestart()
        {
            BuildRig(out NulProductionController controller,
                out ProductionCourseLoop course, out ProductionObstacleJudge judge,
                out ProductionGameLoop loop);
            yield return null;

            controller.InjectDatagram(Cmd("d174-old", 41000, 7, "STOP_OFF"));
            Assert.AreEqual(1, controller.Bus.Published);

            for (int failure = 0; failure < ProductionGameLoop.StartingLives; failure++)
            {
                controller.RecoverAt(new Vector2(course.Slots[0].WorldX, 0f));
                judge.EvaluateNowAt(controller.GameplayTime);
                if (failure < ProductionGameLoop.StartingLives - 1)
                {
                    loop.CompleteRecovery();
                    yield return new WaitForSeconds(
                        ProductionGameLoop.RecoveryGraceSeconds + 0.05f);
                }
            }

            Assert.AreEqual(0, loop.Lives);
            Assert.IsTrue(loop.IsGameOver);
            Assert.IsTrue(controller.Nul.Paused);
            Assert.IsTrue(controller.MovementSuspended);

            controller.InjectDatagram(Cmd("d174-new", 41001, 1, "STOP_ON"));
            controller.InjectDatagram(Cmd("d174-new", 41001, 1, "STOP_ON"));
            Assert.AreEqual(2, controller.Bus.Published,
                "the first new-session command crosses the Bus exactly once in GAME OVER");
            Assert.AreEqual("d174-new", controller.Router.Session.ActiveSession);
            Assert.AreEqual(1, controller.Router.Dedup.Count);
            Assert.IsFalse(controller.Nul.StopActive,
                "GAME OVER consumes input without gameplay reaction");

            controller.InjectDatagram(Cmd("d174-old", 41000, 8, "STOP_OFF"));
            Assert.AreEqual(2, controller.Bus.Published);
            Assert.AreEqual(1, controller.Router.Session.DiscardedStale);

            loop.RestartGame();
            yield return null;
            Assert.AreEqual(ProductionGameLoop.StartingLives, loop.Lives);
            Assert.IsFalse(loop.IsGameOver);
            Assert.IsFalse(controller.Nul.Paused);
            Assert.IsFalse(controller.MovementSuspended);

            controller.InjectDatagram(Cmd("d174-new", 41001, 1, "STOP_ON"));
            Assert.AreEqual(2, controller.Bus.Published,
                "the command consumed in GAME OVER must not replay after restart");
            Assert.IsFalse(controller.Nul.StopActive);

            controller.InjectDatagram(Cmd("d174-new", 41001, 2, "STOP_ON"));
            yield return null;
            Assert.AreEqual(3, controller.Bus.Published);
            Assert.IsTrue(controller.Nul.StopActive,
                "the next unique command reacts immediately after restart");
        }

        private void BuildRig(out NulProductionController controller,
            out ProductionCourseLoop course, out ProductionObstacleJudge judge,
            out ProductionGameLoop loop)
        {
            _root = new GameObject("slice39-gameover-session-rig");
            _root.SetActive(false);
            _root.AddComponent<Rigidbody2D>().gravityScale = 0f;
            var stand = new GameObject("stand").AddComponent<BoxCollider2D>();
            stand.transform.SetParent(_root.transform, false);
            var crouch = new GameObject("crouch").AddComponent<BoxCollider2D>();
            crouch.transform.SetParent(_root.transform, false);
            crouch.enabled = false;
            controller = _root.AddComponent<NulProductionController>();
            controller.udpPort = 8929;
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
