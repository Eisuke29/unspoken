using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Before.Production.Tests
{
    public class ProductionSlice10PlayTests
    {
        private GameObject _root;

        private static void SetPrivate(object target, string field, object value)
        {
            FieldInfo f = target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(f, field);
            f.SetValue(target, value);
        }

        private static string Cmd(int id, string type)
            => "{\"v\":1,\"kind\":\"cmd\",\"session\":\"route\",\"sstart\":9100," +
               "\"cmdId\":" + id + ",\"seq\":1,\"tUs\":0,\"type\":\"" + type +
               "\",\"phase\":\"fire\"}";

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.Destroy(_root);
        }

        [UnityTest]
        public IEnumerator DirectionCommitsRoute_RecoveryPreservesIt_RestartClearsIt()
        {
            _root = new GameObject("route-rig");
            _root.SetActive(false);
            _root.AddComponent<Rigidbody2D>().gravityScale = 0f;
            var stand = new GameObject("stand").AddComponent<BoxCollider2D>();
            stand.transform.SetParent(_root.transform, false);
            var crouch = new GameObject("crouch").AddComponent<BoxCollider2D>();
            crouch.transform.SetParent(_root.transform, false);
            crouch.enabled = false;

            var controller = _root.AddComponent<NulProductionController>();
            controller.udpPort = 8895;
            controller.autoRun = false;
            SetPrivate(controller, "standingCollider", stand);
            SetPrivate(controller, "crouchCollider", crouch);

            var course = _root.AddComponent<ProductionCourseLoop>();
            var judge = _root.AddComponent<ProductionObstacleJudge>();
            var loop = _root.AddComponent<ProductionGameLoop>();
            _root.SetActive(true);
            loop.ResetLoop();
            yield return null;

            for (int i = 0; i < course.Slots.Length; i++)
            {
                ProductionCourseLoop.ObstacleSlot slot = course.Slots[i];
                slot.WorldX = i == 3 ? 30f : 10000f + i;
                course.Slots[i] = slot;
            }

            judge.ResetJudgement();
            controller.transform.position = Vector3.zero;
            judge.EvaluateNowAt(3f);
            Assert.IsTrue(controller.Nul.DirectionMode);
            controller.InjectDatagram(Cmd(1, "LEFT"));
            judge.EvaluateNowAt(3f);

            Assert.AreEqual(ProductionRoute.Left, loop.SelectedRoute);
            Assert.AreEqual(ProductionRoute.Left, course.SelectedRoute);

            ProductionCourseLoop.ObstacleSlot jump = course.Slots[0];
            jump.WorldX = 0f;
            course.Slots[0] = jump;
            judge.EvaluateNowAt(3f);

            Assert.IsTrue(loop.InRecovery);
            Assert.AreEqual(ProductionRoute.Left, loop.SelectedRoute);
            Assert.AreEqual(ProductionRoute.Left, course.SelectedRoute);

            loop.RestartGame();
            Assert.AreEqual(ProductionRoute.None, loop.SelectedRoute);
            Assert.AreEqual(ProductionRoute.None, course.SelectedRoute);
        }
    }
}
