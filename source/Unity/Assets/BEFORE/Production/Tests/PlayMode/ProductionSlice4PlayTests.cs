using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Before.Production.Tests
{
    public class ProductionSlice4PlayTests
    {
        private GameObject _root;
        private NulProductionController _controller;
        private ProductionCourseLoop _course;
        private ProductionObstacleJudge _judge;
        private ProductionGameLoop _loop;

        private static void SetPrivate(object target, string field, object value)
        {
            FieldInfo f = target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(f, field);
            f.SetValue(target, value);
        }

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("slice4-rig");
            _root.SetActive(false);
            _root.AddComponent<Rigidbody2D>().gravityScale = 0f;

            var stand = new GameObject("stand").AddComponent<BoxCollider2D>();
            stand.transform.SetParent(_root.transform, false);
            var crouch = new GameObject("crouch").AddComponent<BoxCollider2D>();
            crouch.transform.SetParent(_root.transform, false);
            crouch.enabled = false;

            _controller = _root.AddComponent<NulProductionController>();
            _controller.udpPort = 8893;
            _controller.autoRun = false;
            SetPrivate(_controller, "standingCollider", stand);
            SetPrivate(_controller, "crouchCollider", crouch);

            _course = _root.AddComponent<ProductionCourseLoop>();
            _course.Runner = _root.transform;
            _judge = _root.AddComponent<ProductionObstacleJudge>();
            _judge.Controller = _controller;
            _judge.Course = _course;
            _loop = _root.AddComponent<ProductionGameLoop>();
            _loop.Controller = _controller;
            _loop.Judge = _judge;
            _loop.Course = _course;

            _root.SetActive(true);
            _loop.ResetLoop();
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.Destroy(_root);
        }

        private void MissJump()
        {
            _root.transform.position = new Vector3(ProductionCourseLoop.FirstObstacleX, 0f, 0f);
            _judge.EvaluateNow();
        }

        private IEnumerator RecoverAndExpireGrace()
        {
            _loop.CompleteRecovery();
            yield return new WaitForSeconds(ProductionGameLoop.RecoveryGraceSeconds + 0.05f);
        }

        [UnityTest]
        public IEnumerator ThirdFailureEntersGameOver_AndRestartResetsGameplay()
        {
            yield return null;
            MissJump();
            yield return RecoverAndExpireGrace();
            MissJump();
            yield return RecoverAndExpireGrace();
            MissJump();

            Assert.AreEqual(0, _loop.Lives);
            Assert.IsTrue(_loop.IsGameOver);
            Assert.IsTrue(_controller.Nul.Paused);
            Assert.IsFalse(_loop.InRecovery);

            _loop.RestartGame();
            yield return new WaitForFixedUpdate();

            Assert.AreEqual(ProductionGameLoop.StartingLives, _loop.Lives);
            Assert.IsFalse(_loop.IsGameOver);
            Assert.IsFalse(_controller.Nul.Paused);
            Assert.AreEqual(0f, _root.transform.position.x, 0.01f);
            Assert.AreEqual(ProductionCourseLoop.FirstObstacleX, _course.Slots[0].WorldX, 0.01f);
            Assert.AreEqual(0, _judge.SuccessCount);
            Assert.AreEqual(0, _judge.FailureCount);
        }
    }
}
