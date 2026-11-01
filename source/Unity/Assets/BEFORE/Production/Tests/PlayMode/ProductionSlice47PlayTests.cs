using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Before.Production.Tests
{
    public class ProductionSlice47PlayTests
    {
        private GameObject _root;
        private NulProductionController _controller;
        private ProductionCourseLoop _course;
        private ProductionObstacleJudge _judge;
        private ProductionGameLoop _loop;
        private ProductionHud _hud;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("slice47-final-hud");
            _root.SetActive(false);
            _root.AddComponent<Rigidbody2D>().gravityScale = 0f;
            var stand = new GameObject("stand").AddComponent<BoxCollider2D>();
            stand.transform.SetParent(_root.transform, false);
            var crouch = new GameObject("crouch").AddComponent<BoxCollider2D>();
            crouch.transform.SetParent(_root.transform, false);
            crouch.enabled = false;

            _controller = _root.AddComponent<NulProductionController>();
            _controller.udpPort = 8927;
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

        [UnityTest]
        public IEnumerator HeartLossGlitchesRightmostAndZeroWarningIsOneShot()
        {
            yield return null;
            Assert.AreEqual(-1, _hud.LostHeartIndex);
            MissJump();
            Assert.AreEqual(2, _hud.LostHeartIndex);
            Assert.AreEqual(2, _hud.DisplayedLives);

            yield return new WaitForSecondsRealtime(
                ProductionHud.HeartLossSeconds + 0.1f);
            Assert.AreEqual(-1, _hud.LostHeartIndex);
            yield return RecoverAndExpireGrace();
            MissJump();
            yield return RecoverAndExpireGrace();
            MissJump();

            Assert.AreEqual(0, _hud.DisplayedLives);
            Assert.IsTrue(_hud.ZeroWarningVisible);
            Assert.AreEqual(ProductionHud.ZeroWarning,
                "SYS// 復元信号は残っていません");

            yield return new WaitForSecondsRealtime(
                ProductionHud.ZeroWarningSeconds + 0.1f);
            Assert.IsFalse(_hud.ZeroWarningVisible);
            _loop.RestartGame();
            Assert.AreEqual(3, _hud.DisplayedLives);
            Assert.AreEqual(-1, _hud.LostHeartIndex);
            Assert.IsFalse(_hud.ZeroWarningVisible);
        }

        private void MissJump()
        {
            _root.transform.position = new Vector3(
                ProductionCourseLoop.FirstObstacleX, 0f, 0f);
            _judge.EvaluateNow();
        }

        private IEnumerator RecoverAndExpireGrace()
        {
            _loop.CompleteRecovery();
            yield return new WaitForSeconds(
                ProductionGameLoop.RecoveryGraceSeconds + 0.05f);
        }

        private static void SetPrivate(object target, string field, object value)
        {
            FieldInfo info = target.GetType().GetField(field,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(info, field);
            info.SetValue(target, value);
        }
    }
}
