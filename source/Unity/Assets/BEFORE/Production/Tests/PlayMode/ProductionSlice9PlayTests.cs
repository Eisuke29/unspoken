using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Before.Production.Tests
{
    public class ProductionSlice9PlayTests
    {
        private GameObject _root;
        private ProductionCourseDefinition _definition;

        private static void SetPrivate(object target, string field, object value)
        {
            FieldInfo f = target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(f, field);
            f.SetValue(target, value);
        }

        private static string Cmd(int id, string type)
            => "{\"v\":1,\"kind\":\"cmd\",\"session\":\"full-course\",\"sstart\":9000," +
               "\"cmdId\":" + id + ",\"seq\":1,\"tUs\":0,\"type\":\"" + type +
               "\",\"phase\":\"fire\"}";

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.Destroy(_root);
            if (_definition != null) Object.Destroy(_definition);
        }

        [UnityTest]
        public IEnumerator AllFiftyTwoDemandsResolveThroughTheFrozenCommandPath()
        {
            _root = new GameObject("full-course-rig");
            _root.SetActive(false);
            _root.AddComponent<Rigidbody2D>().gravityScale = 0f;
            var stand = new GameObject("stand").AddComponent<BoxCollider2D>();
            stand.transform.SetParent(_root.transform, false);
            var crouch = new GameObject("crouch").AddComponent<BoxCollider2D>();
            crouch.transform.SetParent(_root.transform, false);
            crouch.enabled = false;

            var controller = _root.AddComponent<NulProductionController>();
            controller.udpPort = 8896;
            controller.autoRun = false;
            SetPrivate(controller, "standingCollider", stand);
            SetPrivate(controller, "crouchCollider", crouch);

            _definition = ScriptableObject.CreateInstance<ProductionCourseDefinition>();
            _definition.LoadProductionDefaults();
            var course = _root.AddComponent<ProductionCourseLoop>();
            course.Definition = _definition;
            var judge = _root.AddComponent<ProductionObstacleJudge>();
            var loop = _root.AddComponent<ProductionGameLoop>();
            _root.SetActive(true);
            loop.ResetLoop();
            yield return null;

            Assert.AreEqual(52, course.Slots.Length);
            int commandId = 1;
            for (int i = 0; i < course.Slots.Length; i++)
            {
                ProductionCourseLoop.ObstacleSlot slot = course.Slots[i];
                controller.RecoverAt(new Vector2(slot.WorldX - 30f, 0f));

                switch (slot.Kind)
                {
                    case ProductionObstacleKind.Jump:
                        controller.InjectDatagram(Cmd(commandId++, "JUMP"));
                        controller.transform.position = new Vector3(slot.WorldX, 8f, 0f);
                        judge.EvaluateNowAt(3f);
                        break;
                    case ProductionObstacleKind.Crouch:
                        controller.InjectDatagram(Cmd(commandId++, "CROUCH"));
                        controller.transform.position = new Vector3(slot.WorldX, 0f, 0f);
                        judge.EvaluateNowAt(3f);
                        break;
                    case ProductionObstacleKind.Stop:
                        controller.InjectDatagram(Cmd(commandId++, "STOP_ON"));
                        judge.EvaluateNowAt(3f);
                        controller.InjectDatagram(Cmd(commandId++, "STOP_OFF"));
                        controller.transform.position = new Vector3(slot.WorldX, 0f, 0f);
                        judge.EvaluateNowAt(3f);
                        break;
                    case ProductionObstacleKind.Direction:
                        judge.EvaluateNowAt(3f);
                        Assert.IsTrue(controller.Nul.DirectionMode);
                        controller.InjectDatagram(Cmd(commandId++, "RIGHT"));
                        judge.EvaluateNowAt(3f);
                        break;
                }

                Assert.AreEqual(slot.StableId, judge.LatestResult.StableId,
                    "wrong result slot at course index " + i);
                Assert.IsTrue(judge.LatestResult.Success,
                    "demand failed at beat " + slot.BeatId + " index " + slot.DemandIndex);
            }

            Assert.AreEqual(52, judge.SuccessCount);
            Assert.AreEqual(0, judge.FailureCount);
            Assert.AreEqual(ProductionGameLoop.StartingLives, loop.Lives);
            Assert.IsFalse(loop.IsGameOver);
            Assert.Greater(controller.Bus.Published, 52,
                "Stop ON/OFF pairs should make published command count exceed demand count");
        }
    }
}
