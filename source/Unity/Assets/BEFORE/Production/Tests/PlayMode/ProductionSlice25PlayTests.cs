using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Before.Production.Tests
{
    public class ProductionSlice25PlayTests
    {
        private GameObject _root;
        private ProductionCourseDefinition _definition;
        private int _commandId;

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.Destroy(_root);
            if (_definition != null) Object.Destroy(_definition);
        }

        [UnityTest]
        public IEnumerator CrouchAndStopEachProtectOnlyTheirFirstProofAndAcceptOneRepair()
        {
            BuildRig(out NulProductionController controller,
                out ProductionCourseLoop course, out ProductionGameLoop loop,
                out ProductionTeachingFlow teaching);
            yield return null;

            yield return TeachProtectAndRepair(controller, course, teaching,
                4, ProductionObstacleKind.Crouch, "CROUCH", 1);
            yield return TeachProtectAndRepair(controller, course, teaching,
                13, ProductionObstacleKind.Stop, "STOP_ON", 2);

            Assert.AreEqual(2, teaching.CompletedTeachingCount);
            Assert.AreEqual(2, teaching.ProtectedProofCount);
            Assert.AreEqual(ProductionGameLoop.StartingLives, loop.Lives);

            loop.ResetLoop();
            yield return null;
            Assert.AreEqual(0, teaching.CompletedTeachingCount);
            Assert.AreEqual(0, teaching.ProtectedProofCount);
            Assert.IsFalse(teaching.IsActive);
            Assert.IsFalse(controller.StorySuspended);
            Assert.IsFalse(controller.MovementSuspended);
        }

        private IEnumerator TeachProtectAndRepair(
            NulProductionController controller, ProductionCourseLoop course,
            ProductionTeachingFlow teaching, int beatId,
            ProductionObstacleKind kind, string commandType, int expectedProtected)
        {
            ProductionCourseLoop.ObstacleSlot slot = SlotForBeat(course, beatId);
            teaching.BeginTeaching(beatId, kind, slot.WorldX);
            for (int sample = 0; sample < ProductionTeachingFlow.SamplesPerTeaching; sample++)
                controller.InjectDatagram(Cmd(++_commandId, commandType));
            yield return new WaitForSeconds(ProductionTeachingFlow.ConfirmationSeconds + 0.1f);
            Assert.IsFalse(teaching.IsActive);

            var miss = new ProductionObstacleJudge.ObstacleResult
            {
                StableId = slot.StableId,
                PassCount = slot.PassCount,
                Kind = slot.Kind,
                WorldX = slot.WorldX,
                Success = false,
                Direction = 0
            };
            Assert.IsTrue(teaching.TryProtectFirstProof(miss));
            Assert.IsTrue(teaching.IsRepair);
            Assert.AreEqual(expectedProtected, teaching.ProtectedProofCount);
            Assert.AreEqual(slot.WorldX - ProductionTeachingFlow.SafeRunUp,
                controller.transform.position.x, 0.01f);

            controller.InjectDatagram(Cmd(++_commandId, commandType));
            Assert.AreEqual(ProductionTeachingStage.RepairConfirming, teaching.Stage);
            yield return new WaitForSeconds(ProductionTeachingFlow.ConfirmationSeconds + 0.1f);
            Assert.IsFalse(teaching.IsActive);
            Assert.IsFalse(teaching.TryProtectFirstProof(miss),
                "the same teaching proof is protected only once");
        }

        private void BuildRig(out NulProductionController controller,
                              out ProductionCourseLoop course, out ProductionGameLoop loop,
                              out ProductionTeachingFlow teaching)
        {
            _root = new GameObject("slice25-generic-proof-rig");
            _root.SetActive(false);
            _root.AddComponent<Rigidbody2D>().gravityScale = 0f;
            var stand = new GameObject("stand").AddComponent<BoxCollider2D>();
            stand.transform.SetParent(_root.transform, false);
            var crouch = new GameObject("crouch").AddComponent<BoxCollider2D>();
            crouch.transform.SetParent(_root.transform, false);
            crouch.enabled = false;

            controller = _root.AddComponent<NulProductionController>();
            controller.udpPort = 8900;
            controller.autoRun = false;
            controller.autoLaunchSidecar = false;
            SetPrivate(controller, "standingCollider", stand);
            SetPrivate(controller, "crouchCollider", crouch);
            course = _root.AddComponent<ProductionCourseLoop>();
            _definition = ScriptableObject.CreateInstance<ProductionCourseDefinition>();
            _definition.LoadProductionDefaults();
            course.Definition = _definition;
            _root.AddComponent<ProductionObstacleJudge>();
            loop = _root.AddComponent<ProductionGameLoop>();
            teaching = _root.AddComponent<ProductionTeachingFlow>();
            _root.SetActive(true);
        }

        private static ProductionCourseLoop.ObstacleSlot SlotForBeat(
            ProductionCourseLoop course, int beatId)
        {
            foreach (ProductionCourseLoop.ObstacleSlot slot in course.Slots)
                if (slot.BeatId == beatId) return slot;
            Assert.Fail("teaching slot missing for beat " + beatId);
            return default;
        }

        private static string Cmd(int id, string type)
            => "{\"v\":1,\"kind\":\"cmd\",\"session\":\"proof-generic\"," +
               "\"sstart\":21000,\"cmdId\":" + id + ",\"seq\":" + id +
               ",\"tUs\":0,\"type\":\"" + type + "\",\"phase\":\"fire\"}";

        private static void SetPrivate(object target, string field, object value)
        {
            FieldInfo info = target.GetType().GetField(field,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(info, field);
            info.SetValue(target, value);
        }
    }
}
