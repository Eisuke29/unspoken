using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Before.Production.Tests
{
    public class ProductionSlice3PlayTests
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
            _root = new GameObject("slice3-rig");
            _root.SetActive(false);
            _root.AddComponent<Rigidbody2D>().gravityScale = 0f;

            var standGo = new GameObject("stand");
            standGo.transform.SetParent(_root.transform, false);
            var stand = standGo.AddComponent<BoxCollider2D>();
            var crouchGo = new GameObject("crouch");
            crouchGo.transform.SetParent(_root.transform, false);
            var crouch = crouchGo.AddComponent<BoxCollider2D>();
            crouch.enabled = false;

            _controller = _root.AddComponent<NulProductionController>();
            _controller.udpPort = 8892;
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

            _root.SetActive(true);
            _course.ResetCourse(0f);
            _judge.ResetJudgement();
            _loop.ResetLoop();
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.Destroy(_root);
        }

        private void MissFirstJump()
        {
            _root.transform.position = new Vector3(ProductionCourseLoop.FirstObstacleX, 0f, 0f);
            _judge.EvaluateNow();
        }

        [UnityTest]
        public IEnumerator Failure_ConsumesOneLifeAndPausesForRecovery()
        {
            yield return null;
            MissFirstJump();
            Assert.AreEqual(2, _loop.Lives);
            Assert.AreEqual(1, _loop.RecoveryCount);
            Assert.IsTrue(_loop.InRecovery);
            Assert.IsTrue(_controller.MovementSuspended);
            Assert.IsFalse(_controller.Nul.Paused, "recovery must not gate always-on commands");
            Assert.AreEqual(ProductionGameLoop.CheckpointFor(ProductionCourseLoop.FirstObstacleX),
                            _loop.CurrentCheckpointX, 0.001f);
        }

        [UnityTest]
        public IEnumerator Recovery_RepositionsAndStartsGrace()
        {
            yield return null;
            MissFirstJump();
            _loop.CompleteRecovery();
            yield return new WaitForFixedUpdate();

            Assert.IsFalse(_loop.InRecovery);
            Assert.IsTrue(_loop.Invulnerable);
            Assert.IsFalse(_controller.Nul.Paused);
            Assert.IsFalse(_controller.MovementSuspended);
            Assert.AreEqual(ProductionGameLoop.CheckpointFor(ProductionCourseLoop.FirstObstacleX),
                            _root.transform.position.x, 0.01f);
            Assert.IsTrue(_controller.StandingColliderActive);
            Assert.IsFalse(_controller.Airborne);
        }

        [UnityTest]
        public IEnumerator Grace_IgnoresAnImmediateSecondMiss()
        {
            yield return null;
            MissFirstJump();
            _loop.CompleteRecovery();
            int lives = _loop.Lives;

            float crouchX = ProductionCourseLoop.FirstObstacleX + ProductionCourseLoop.ObstacleSpacing;
            _root.transform.position = new Vector3(crouchX, 0f, 0f);
            _judge.EvaluateNow();
            Assert.AreEqual(lives, _loop.Lives);
        }
    }
}
