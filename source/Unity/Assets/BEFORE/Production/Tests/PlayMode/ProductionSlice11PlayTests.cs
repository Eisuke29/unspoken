using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Before.Production.Tests
{
    public class ProductionSlice11PlayTests
    {
        private GameObject _root;
        private NulProductionController _controller;
        private ProductionCourseLoop _course;
        private ProductionObstacleJudge _judge;
        private ProductionGameLoop _loop;
        private ProductionHud _hud;

        private static void SetPrivate(object target, string field, object value)
        {
            FieldInfo f = target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(f, field);
            f.SetValue(target, value);
        }

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("slice11-hud-rig");
            _root.SetActive(false);
            _root.AddComponent<Rigidbody2D>().gravityScale = 0f;
            var stand = new GameObject("stand").AddComponent<BoxCollider2D>();
            stand.transform.SetParent(_root.transform, false);
            var crouch = new GameObject("crouch").AddComponent<BoxCollider2D>();
            crouch.transform.SetParent(_root.transform, false);
            crouch.enabled = false;

            _controller = _root.AddComponent<NulProductionController>();
            _controller.udpPort = 8894;
            _controller.autoRun = false;
            SetPrivate(_controller, "standingCollider", stand);
            SetPrivate(_controller, "crouchCollider", crouch);
            _course = _root.AddComponent<ProductionCourseLoop>();
            _judge = _root.AddComponent<ProductionObstacleJudge>();
            _loop = _root.AddComponent<ProductionGameLoop>();
            _hud = _root.AddComponent<ProductionHud>();
            _hud.GameLoop = _loop;
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
        public IEnumerator HudMirrorsThreeLivesGameOverAndRestartWithoutOwningState()
        {
            yield return null;
            Assert.AreEqual(3, _hud.DisplayedLives);
            Assert.IsFalse(_hud.ShowGameOver);

            MissJump();
            Assert.AreEqual(2, _hud.DisplayedLives);
            yield return RecoverAndExpireGrace();
            MissJump();
            Assert.AreEqual(1, _hud.DisplayedLives);
            yield return RecoverAndExpireGrace();
            MissJump();

            Assert.AreEqual(0, _hud.DisplayedLives);
            Assert.IsTrue(_hud.ShowGameOver);
            _loop.RestartGame();
            Assert.AreEqual(3, _hud.DisplayedLives);
            Assert.IsFalse(_hud.ShowGameOver);
        }
    }
}
