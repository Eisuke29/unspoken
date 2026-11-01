// BEFORE Goal 2 — PlayMode UDP loopback: real datagrams through the receiver.
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Before.Production;

namespace Before.Production.Tests
{
    public class CommandLabPlayTests
    {
        private static byte[] Cmd(string session, long sstart, int cmdId, int seq,
                                  string type)
        {
            string j = "{\"v\":1,\"kind\":\"cmd\",\"session\":\"" + session +
                       "\",\"sstart\":" + sstart + ",\"cmdId\":" + cmdId + ",\"seq\":" +
                       seq + ",\"tUs\":0,\"type\":\"" + type + "\",\"phase\":\"fire\"}";
            return Encoding.UTF8.GetBytes(j);
        }

        private static IEnumerator SendAndSettle(UdpClient tx, IPEndPoint ep, byte[] d)
        {
            tx.Send(d, d.Length, ep);
            for (int i = 0; i < 8; i++) yield return null;  // let the rx thread + Update run
        }

        [UnityTest]
        public IEnumerator Triple_send_and_five_reps_and_stale_session()
        {
            int port = 8791;
            var go = new GameObject("CommandLab");
            var ctl = go.AddComponent<CommandLabController>();
            ctl.udpPort = port;
            ctl.autoLaunchSidecar = false;
            yield return null;              // Awake + Start
            for (int i = 0; i < 5; i++) yield return null;
            Assert.IsTrue(ctl.ReceiverListening, "receiver should be listening");

            var tx = new UdpClient();
            var ep = new IPEndPoint(IPAddress.Loopback, port);

            // triple send of one commandId -> exactly one jump
            for (int seq = 1; seq <= 3; seq++)
                yield return SendAndSettle(tx, ep, Cmd("s1", 100, 1, seq, "JUMP"));
            Assert.AreEqual(1, ctl.Nul.JumpCount, "triple-send must execute once");

            // five distinct commandIds -> five jumps (empty-space repeat)
            for (int id = 2; id <= 6; id++)
                for (int seq = 1; seq <= 3; seq++)
                    yield return SendAndSettle(tx, ep, Cmd("s1", 100, id, seq, "JUMP"));
            Assert.AreEqual(6, ctl.Nul.JumpCount, "five more distinct jumps");

            // stale (older) session is discarded
            yield return SendAndSettle(tx, ep, Cmd("old", 50, 1, 1, "JUMP"));
            Assert.AreEqual(6, ctl.Nul.JumpCount, "stale session must be discarded");
            Assert.GreaterOrEqual(ctl.Router.Session.DiscardedStale, 1);

            tx.Close();
            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Crouch_stop_and_direction_over_udp()
        {
            int port = 8792;
            var go = new GameObject("CommandLab2");
            var ctl = go.AddComponent<CommandLabController>();
            ctl.udpPort = port;
            ctl.autoLaunchSidecar = false;
            yield return null;
            for (int i = 0; i < 5; i++) yield return null;

            var tx = new UdpClient();
            var ep = new IPEndPoint(IPAddress.Loopback, port);

            yield return SendAndSettle(tx, ep, Cmd("s", 10, 1, 1, "CROUCH"));
            Assert.IsTrue(ctl.Nul.Crouching, "CROUCH turns crouch on");

            yield return SendAndSettle(tx, ep, Cmd("s", 10, 2, 1, "STOP_ON"));
            Assert.IsTrue(ctl.Nul.StopActive);
            yield return SendAndSettle(tx, ep, Cmd("s", 10, 3, 1, "STOP_OFF"));
            Assert.IsFalse(ctl.Nul.StopActive);

            // Direction-exclusive mode: Jump suppressed, Right recorded
            ctl.Nul.DirectionMode = true;
            int jc = ctl.Nul.JumpCount;
            yield return SendAndSettle(tx, ep, Cmd("s", 10, 4, 1, "JUMP"));
            yield return SendAndSettle(tx, ep, Cmd("s", 10, 5, 1, "RIGHT"));
            Assert.AreEqual(jc, ctl.Nul.JumpCount, "Jump suppressed in Direction mode");
            Assert.AreEqual(1, ctl.Nul.LastDirection);

            tx.Close();
            Object.Destroy(go);
            yield return null;
        }
    }
}
