using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace Before.Production.Tests
{
    public class ProductionSlice12PlayTests : InputTestFixture
    {
        private GameObject _root;
        private Keyboard _testKeyboard;

        private static void SetPrivate(object target, string field, object value)
        {
            FieldInfo f = target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(f, field);
            f.SetValue(target, value);
        }

        private static string Cmd(int id, string type)
            => "{\"v\":1,\"kind\":\"cmd\",\"session\":\"pause\",\"sstart\":9200," +
               "\"cmdId\":" + id + ",\"seq\":1,\"tUs\":0,\"type\":\"" + type +
               "\",\"phase\":\"fire\"}";

        [TearDown]
        public override void TearDown()
        {
            if (_root != null) Object.Destroy(_root);
            base.TearDown();
        }

        [UnityTest]
        public IEnumerator PKeyPressTogglesPauseAndResume()
        {
            _root = new GameObject("slice12-p-key-rig");
            _root.SetActive(false);
            _root.AddComponent<Rigidbody2D>().gravityScale = 0f;
            var stand = new GameObject("stand").AddComponent<BoxCollider2D>();
            stand.transform.SetParent(_root.transform, false);
            var crouch = new GameObject("crouch").AddComponent<BoxCollider2D>();
            crouch.transform.SetParent(_root.transform, false);
            crouch.enabled = false;

            var controller = _root.AddComponent<NulProductionController>();
            controller.udpPort = 8889;
            controller.autoRun = false;
            SetPrivate(controller, "standingCollider", stand);
            SetPrivate(controller, "crouchCollider", crouch);
            _root.SetActive(true);
            _testKeyboard = InputSystem.AddDevice<Keyboard>();
            yield return null;

            Press(_testKeyboard.pKey, queueEventOnly: true);
            yield return null;
            Assert.IsTrue(controller.Nul.Paused);

            Release(_testKeyboard.pKey, queueEventOnly: true);
            yield return null;
            Press(_testKeyboard.pKey, queueEventOnly: true);
            yield return null;
            Assert.IsFalse(controller.Nul.Paused);
        }

        [UnityTest]
        public IEnumerator PauseFreezesGameplayClockAndCrouch_WhileCommandsStayConsumed()
        {
            _root = new GameObject("slice12-pause-rig");
            _root.SetActive(false);
            _root.AddComponent<Rigidbody2D>().gravityScale = 0f;
            var stand = new GameObject("stand").AddComponent<BoxCollider2D>();
            stand.transform.SetParent(_root.transform, false);
            var crouch = new GameObject("crouch").AddComponent<BoxCollider2D>();
            crouch.transform.SetParent(_root.transform, false);
            crouch.enabled = false;

            var controller = _root.AddComponent<NulProductionController>();
            controller.udpPort = 8892;
            controller.autoRun = false;
            SetPrivate(controller, "standingCollider", stand);
            SetPrivate(controller, "crouchCollider", crouch);
            _root.SetActive(true);
            yield return null;

            controller.InjectDatagram(Cmd(1, "CROUCH"));
            Assert.IsTrue(controller.Nul.Crouching);
            controller.Nul.Paused = true;
            yield return null;
            float pausedAt = controller.GameplayTime;
            controller.InjectDatagram(Cmd(2, "JUMP"));
            yield return new WaitForSeconds((float)NulState.CrouchSeconds + 0.1f);

            Assert.AreEqual(pausedAt, controller.GameplayTime, 0.03f);
            Assert.IsTrue(controller.Nul.Crouching, "crouch duration must not elapse while paused");
            Assert.AreEqual(0, controller.Nul.JumpCount, "paused command is consumed, not applied");

            controller.Nul.Paused = false;
            yield return new WaitForSeconds((float)NulState.CrouchSeconds + 0.1f);
            Assert.IsFalse(controller.Nul.Crouching);
            Assert.AreEqual(0, controller.Nul.JumpCount, "consumed command must not replay after resume");
        }

        [UnityTest]
        public IEnumerator PauseFreezesRunnerCourseJudgeWallRecoveryGraceAndLives()
        {
            _root = new GameObject("slice12-full-pause-rig");
            _root.SetActive(false);
            _root.AddComponent<Rigidbody2D>().gravityScale = 0f;
            var stand = new GameObject("stand").AddComponent<BoxCollider2D>();
            stand.transform.SetParent(_root.transform, false);
            var crouch = new GameObject("crouch").AddComponent<BoxCollider2D>();
            crouch.transform.SetParent(_root.transform, false);
            crouch.enabled = false;

            var controller = _root.AddComponent<NulProductionController>();
            controller.udpPort = 8890;
            controller.autoRun = true;
            SetPrivate(controller, "standingCollider", stand);
            SetPrivate(controller, "crouchCollider", crouch);
            var course = _root.AddComponent<ProductionCourseLoop>();
            var judge = _root.AddComponent<ProductionObstacleJudge>();
            var loop = _root.AddComponent<ProductionGameLoop>();
            _root.SetActive(true);
            loop.ResetLoop();
            yield return new WaitForFixedUpdate();

            Assert.AreEqual(NulProductionController.RunSpeed, controller.HorizontalSpeed, 0.01f);
            controller.Nul.Paused = true;
            yield return null;
            Vector3 pausedPosition = _root.transform.position;
            float pausedTime = controller.GameplayTime;
            float firstSlotX = course.Slots[0].WorldX;
            bool wallOpen = ProductionCourseLoop.EnergyWallIsOpen(pausedTime);
            yield return new WaitForSeconds(0.25f);

            Assert.AreEqual(pausedPosition.x, _root.transform.position.x, 0.01f,
                "NUL must not advance while paused");
            Assert.AreEqual(firstSlotX, course.Slots[0].WorldX, 0.01f,
                "course slots must not advance or recycle while paused");
            Assert.AreEqual(pausedTime, controller.GameplayTime, 0.03f);
            Assert.AreEqual(wallOpen, ProductionCourseLoop.EnergyWallIsOpen(controller.GameplayTime),
                "energy-wall phase must remain frozen");

            _root.transform.position = new Vector3(firstSlotX, 0f, 0f);
            judge.EvaluateNowAt(controller.GameplayTime);
            Assert.AreEqual(0, judge.FailureCount, "obstacle judgement must not run while paused");
            Assert.AreEqual(ProductionGameLoop.StartingLives, loop.Lives,
                "paused judgement must not change lives");

            controller.Nul.Paused = false;
            controller.autoRun = false;
            yield return null;
            judge.EvaluateNowAt(controller.GameplayTime);
            Assert.AreEqual(1, judge.FailureCount);
            Assert.AreEqual(2, loop.Lives);
            Assert.IsTrue(loop.InRecovery);

            controller.Nul.Paused = true;
            yield return null;
            float recoveryPausedAt = controller.GameplayTime;
            yield return new WaitForSeconds(ProductionGameLoop.RecoveryFreezeSeconds + 0.1f);
            Assert.AreEqual(recoveryPausedAt, controller.GameplayTime, 0.03f);
            Assert.IsTrue(loop.InRecovery, "recovery timer must not elapse while paused");
            Assert.AreEqual(2, loop.Lives, "recovery pause must not change lives");

            controller.Nul.Paused = false;
            yield return new WaitForSeconds(ProductionGameLoop.RecoveryFreezeSeconds + 0.1f);
            Assert.IsFalse(loop.InRecovery);
            Assert.IsTrue(loop.Invulnerable, "grace begins only after recovery completes");

            controller.Nul.Paused = true;
            yield return null;
            yield return new WaitForSeconds(ProductionGameLoop.RecoveryGraceSeconds + 0.1f);
            Assert.IsTrue(loop.Invulnerable, "grace timer must not elapse while paused");
            Assert.AreEqual(2, loop.Lives);

            controller.Nul.Paused = false;
            yield return new WaitForSeconds(ProductionGameLoop.RecoveryGraceSeconds + 0.1f);
            Assert.IsFalse(loop.Invulnerable);
            Assert.IsFalse(loop.IsGameOver);
        }
    }
}
