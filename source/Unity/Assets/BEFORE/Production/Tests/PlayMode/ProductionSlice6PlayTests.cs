using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Before.Production.Tests
{
    public class ProductionSlice6PlayTests
    {
        private GameObject _root;
        private NulProductionController _controller;
        private ProductionObstacleJudge _judge;
        private ProductionGameLoop _loop;

        private static string JumpCommand()
            => "{\"v\":1,\"kind\":\"cmd\",\"session\":\"slice6\",\"sstart\":6000," +
               "\"cmdId\":1,\"seq\":1,\"tUs\":0,\"type\":\"JUMP\",\"phase\":\"fire\"}";

        private static void SetPrivate(object target, string field, object value)
        {
            FieldInfo f = target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(f, field);
            f.SetValue(target, value);
        }

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("slice6-rig");
            _root.SetActive(false);
            _root.AddComponent<Rigidbody2D>().gravityScale = 0f;
            var stand = new GameObject("stand").AddComponent<BoxCollider2D>();
            stand.transform.SetParent(_root.transform, false);
            var crouch = new GameObject("crouch").AddComponent<BoxCollider2D>();
            crouch.transform.SetParent(_root.transform, false);
            crouch.enabled = false;

            _controller = _root.AddComponent<NulProductionController>();
            _controller.udpPort = 8895;
            _controller.autoRun = false;
            SetPrivate(_controller, "standingCollider", stand);
            SetPrivate(_controller, "crouchCollider", crouch);
            _root.AddComponent<ProductionCourseLoop>();
            _judge = _root.AddComponent<ProductionObstacleJudge>();
            _loop = _root.AddComponent<ProductionGameLoop>();
            _root.SetActive(true);
            _loop.ResetLoop();
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.Destroy(_root);
        }

        [UnityTest]
        public IEnumerator RecoveryStopsMovementButStillConsumesLiveCommands()
        {
            yield return null;
            _root.transform.position = new Vector3(ProductionCourseLoop.FirstObstacleX, 0f, 0f);
            _judge.EvaluateNowAt(0f);
            Assert.IsTrue(_loop.InRecovery);
            Assert.IsTrue(_controller.MovementSuspended);
            Assert.IsFalse(_controller.Nul.Paused);

            _controller.InjectDatagram(JumpCommand());
            yield return null;
            Assert.AreEqual(1, _controller.Nul.JumpCount,
                "a recovery freeze must not suppress an accepted command");
            Assert.IsTrue(_controller.Airborne);
        }

        [UnityTest]
        public IEnumerator PreparedStopFailsClosedWallAndPassesOpenWall()
        {
            yield return null;
            float wallX = ProductionCourseLoop.FirstObstacleX +
                          2f * ProductionCourseLoop.ObstacleSpacing;

            _controller.Nul.StopActive = true;
            _root.transform.position = new Vector3(wallX - 30f, 0f, 0f);
            _judge.EvaluateNowAt(0f);
            _controller.Nul.StopActive = false;
            _root.transform.position = new Vector3(wallX, 0f, 0f);
            _judge.EvaluateNowAt(0f);
            Assert.IsFalse(_judge.LatestResult.Success, "closed energy wall must fail");

            _loop.ResetLoop();
            _root.transform.position = new Vector3(wallX - 30f, 0f, 0f);
            _controller.Nul.StopActive = true;
            _judge.EvaluateNowAt(3f);
            _controller.Nul.StopActive = false;
            _root.transform.position = new Vector3(wallX, 0f, 0f);
            _judge.EvaluateNowAt(3f);
            Assert.IsTrue(_judge.LatestResult.Success, "open energy wall must pass after Stop");
        }
    }
}
