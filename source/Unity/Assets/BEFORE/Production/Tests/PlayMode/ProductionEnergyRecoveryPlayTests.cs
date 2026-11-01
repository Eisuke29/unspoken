using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Before.Production.Tests
{
    public class ProductionEnergyRecoveryPlayTests
    {
        private GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null) UnityEngine.Object.Destroy(_root);
        }

        [UnityTest]
        public IEnumerator LossWindowsSuspendWithoutStopReleaseAndStableReturnRecharges()
        {
            BuildRig(8991, out NulProductionController controller,
                out ProductionEnergyRecoveryFlow recovery);
            yield return null;
            controller.Nul.StopActive = true;
            int publishedBefore = controller.Bus.Published;

            recovery.Tick(10f, false, false, true);
            Assert.AreEqual(ProductionEnergyState.Connected, recovery.State);
            Assert.IsFalse(controller.EnergySuspended);
            recovery.Tick(10.29f, false, false, true);
            Assert.AreEqual(ProductionEnergyState.Connected, recovery.State);
            Assert.IsFalse(controller.EnergySuspended);

            recovery.Tick(10.31f, false, false, true);
            Assert.AreEqual(ProductionEnergyState.Fragmenting, recovery.State);
            Assert.IsFalse(controller.EnergySuspended);
            Assert.Less(controller.EnergyMovementScale, 1f);

            recovery.Tick(10.81f, false, false, true);
            Assert.AreEqual(ProductionEnergyState.Depleted, recovery.State);
            Assert.IsTrue(controller.EnergySuspended);
            Assert.IsTrue(controller.Nul.StopActive,
                "tracking loss is not STOP release");
            Assert.AreEqual(publishedBefore, controller.Bus.Published,
                "recovery must not publish STOP_OFF");

            recovery.Tick(11.81f, false, false, true);
            Assert.AreEqual(ProductionEnergyState.Collapsed, recovery.State);
            Assert.IsTrue(controller.EnergySuspended);

            recovery.Tick(12f, true, false, true);
            recovery.Tick(12.49f, true, false, true);
            Assert.AreEqual(ProductionEnergyState.Collapsed, recovery.State);
            recovery.Tick(12.50f, true, false, true);
            Assert.AreEqual(ProductionEnergyState.Recharging, recovery.State);
            Assert.IsTrue(controller.EnergySuspended);
            recovery.Tick(13.49f, true, false, true);
            Assert.AreEqual(ProductionEnergyState.Recharging, recovery.State);
            recovery.Tick(13.50f, true, false, true);
            Assert.AreEqual(ProductionEnergyState.Connected, recovery.State);
            Assert.IsFalse(controller.EnergySuspended);
            Assert.IsTrue(controller.Nul.StopActive);
        }

        [UnityTest]
        public IEnumerator IndependentPowerAndPauseKeepRecoveryInputPathAlive()
        {
            BuildRig(8992, out NulProductionController controller,
                out ProductionEnergyRecoveryFlow recovery);
            yield return null;

            controller.Nul.Paused = true;
            recovery.Tick(20f, false, false, true);
            recovery.Tick(20.81f, false, false, true);
            Assert.AreEqual(ProductionEnergyState.Depleted, recovery.State,
                "unscaled recovery keeps updating while Pause is active");
            Assert.IsTrue(controller.Nul.Paused);

            recovery.Tick(21f, false, true, true);
            Assert.AreEqual(ProductionEnergyState.Independent, recovery.State);
            Assert.IsFalse(controller.EnergySuspended,
                "CORE independent power ignores later hand loss");

            recovery.ResetRecovery();
            Assert.AreEqual(ProductionEnergyState.Connected, recovery.State);
            Assert.IsFalse(controller.EnergySuspended);
            Assert.AreEqual(1f, controller.EnergyMovementScale);
        }

        [UnityTest]
        public IEnumerator StopPacketRecordsIssueReceiveAndSameFrameApplyTiming()
        {
            BuildRig(8993, out NulProductionController controller,
                out _);
            yield return null;
            long issuedUs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000L;
            string json = "{\"v\":1,\"kind\":\"cmd\",\"session\":\"stop-timing\"," +
                "\"sstart\":50000,\"cmdId\":1,\"seq\":1,\"tUs\":" + issuedUs +
                ",\"type\":\"STOP_ON\",\"phase\":\"fire\"}";
            controller.InjectDatagram(json);
            Assert.IsTrue(controller.Nul.StopActive);
            Assert.AreEqual(issuedUs, controller.LastStopCommand.TimestampUs);
            Assert.GreaterOrEqual(controller.LastStopTransportMs, 0f);
            Assert.Less(controller.LastStopTransportMs, 200f);
            Assert.GreaterOrEqual(controller.LastStopUnityApplyMs, 0f);
            Assert.Less(controller.LastStopUnityApplyMs, 20f);

            controller.InjectDatagram(json);
            Assert.AreEqual(1, controller.Bus.Published,
                "repeated datagrams keep one STOP_ON per hold");
        }

        private void BuildRig(int port, out NulProductionController controller,
            out ProductionEnergyRecoveryFlow recovery)
        {
            _root = new GameObject("energy-recovery-rig");
            _root.SetActive(false);
            _root.AddComponent<Rigidbody2D>().gravityScale = 0f;
            var stand = new GameObject("stand").AddComponent<BoxCollider2D>();
            stand.transform.SetParent(_root.transform, false);
            var crouch = new GameObject("crouch").AddComponent<BoxCollider2D>();
            crouch.transform.SetParent(_root.transform, false);
            crouch.enabled = false;
            controller = _root.AddComponent<NulProductionController>();
            controller.udpPort = port;
            controller.autoRun = false;
            controller.autoLaunchSidecar = false;
            SetPrivate(controller, "standingCollider", stand);
            SetPrivate(controller, "crouchCollider", crouch);
            recovery = _root.AddComponent<ProductionEnergyRecoveryFlow>();
            recovery.Controller = controller;
            _root.SetActive(true);
            recovery.enabled = false;
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
