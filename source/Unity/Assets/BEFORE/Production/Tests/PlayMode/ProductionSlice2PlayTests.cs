using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Before.Production.Tests
{
    public class ProductionSlice2PlayTests
    {
        private GameObject _root;
        private NulProductionController _controller;
        private ProductionCourseLoop _course;
        private ProductionObstacleJudge _judge;

        private static string Cmd(int id, string type)
            => "{\"v\":1,\"kind\":\"cmd\",\"session\":\"slice2\",\"sstart\":2000," +
               "\"cmdId\":" + id + ",\"seq\":1,\"tUs\":0,\"type\":\"" + type +
               "\",\"phase\":\"fire\"}";

        private static void SetPrivate(object target, string field, object value)
        {
            FieldInfo f = target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(f, field);
            f.SetValue(target, value);
        }

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("slice2-rig");
            _root.SetActive(false);
            var rb = _root.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0f;

            var standGo = new GameObject("stand");
            standGo.transform.SetParent(_root.transform, false);
            var stand = standGo.AddComponent<BoxCollider2D>();
            stand.size = new Vector2(16f, 28f);
            stand.offset = new Vector2(0f, 14f);

            var crouchGo = new GameObject("crouch");
            crouchGo.transform.SetParent(_root.transform, false);
            var crouch = crouchGo.AddComponent<BoxCollider2D>();
            crouch.size = new Vector2(16f, 15.4f);
            crouch.offset = new Vector2(0f, 7.7f);
            crouch.enabled = false;

            _controller = _root.AddComponent<NulProductionController>();
            _controller.udpPort = 8891;
            _controller.autoRun = false;
            SetPrivate(_controller, "standingCollider", stand);
            SetPrivate(_controller, "crouchCollider", crouch);

            _course = _root.AddComponent<ProductionCourseLoop>();
            _course.Runner = _root.transform;
            _judge = _root.AddComponent<ProductionObstacleJudge>();
            _judge.Controller = _controller;
            _judge.Course = _course;
            _root.SetActive(true);
            _course.ResetCourse(0f);
            _judge.ResetJudgement();
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.Destroy(_root);
        }

        [UnityTest]
        public IEnumerator Jump_And_Crouch_ResolveFromLiveCommandState()
        {
            yield return null;
            _controller.InjectDatagram(Cmd(1, "JUMP"));
            _root.transform.position = new Vector3(ProductionCourseLoop.FirstObstacleX, 8f, 0f);
            _judge.EvaluateNow();
            Assert.IsTrue(_judge.LatestResult.Success);
            Assert.AreEqual(ProductionObstacleKind.Jump, _judge.LatestResult.Kind);

            _controller.InjectDatagram(Cmd(2, "CROUCH"));
            _root.transform.position = new Vector3(
                ProductionCourseLoop.FirstObstacleX + ProductionCourseLoop.ObstacleSpacing, 0f, 0f);
            _judge.EvaluateNow();
            Assert.IsTrue(_judge.LatestResult.Success);
            Assert.AreEqual(ProductionObstacleKind.Crouch, _judge.LatestResult.Kind);
        }

        [UnityTest]
        public IEnumerator Stop_RequiresAStopInsidePreparationWindow()
        {
            yield return null;
            float x = ProductionCourseLoop.FirstObstacleX + 2f * ProductionCourseLoop.ObstacleSpacing;
            _root.transform.position = new Vector3(x - 30f, 0f, 0f);
            _controller.InjectDatagram(Cmd(1, "STOP_ON"));
            _judge.EvaluateNowAt(3.1f);
            _controller.InjectDatagram(Cmd(2, "STOP_OFF"));
            _root.transform.position = new Vector3(x, 0f, 0f);
            _judge.EvaluateNowAt(3.1f);

            Assert.IsTrue(_judge.LatestResult.Success);
            Assert.AreEqual(ProductionObstacleKind.Stop, _judge.LatestResult.Kind);
        }

        [UnityTest]
        public IEnumerator DirectionWindow_IsExclusiveAndCommitsRight()
        {
            yield return null;
            float x = ProductionCourseLoop.FirstObstacleX + 3f * ProductionCourseLoop.ObstacleSpacing;
            _root.transform.position = new Vector3(x - 30f, 0f, 0f);
            _judge.EvaluateNow();
            Assert.IsTrue(_controller.Nul.DirectionMode);

            _controller.InjectDatagram(Cmd(1, "JUMP"));
            Assert.AreEqual(0, _controller.Nul.JumpCount, "direction mode stays exclusive");
            _controller.InjectDatagram(Cmd(2, "RIGHT"));
            _judge.EvaluateNow();

            Assert.IsTrue(_judge.LatestResult.Success);
            Assert.AreEqual(ProductionObstacleKind.Direction, _judge.LatestResult.Kind);
            Assert.AreEqual(1, _judge.LatestResult.Direction);
            Assert.IsFalse(_controller.Nul.DirectionMode);
        }

        [UnityTest]
        public IEnumerator MissingReaction_ProducesOneFailure()
        {
            yield return null;
            _root.transform.position = new Vector3(ProductionCourseLoop.FirstObstacleX, 0f, 0f);
            _judge.EvaluateNow();
            _judge.EvaluateNow();
            Assert.AreEqual(1, _judge.FailureCount);
            Assert.IsFalse(_judge.LatestResult.Success);
        }
    }
}
