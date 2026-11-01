using NUnit.Framework;

namespace Before.Production.Tests
{
    public class ProductionSlice36Tests
    {
        [Test]
        public void DirectionModeAcceptsNewSessionAndQuarantinesOldDirection()
        {
            var router = new CommandRouter();
            var nul = new NulState();

            CommandPacket.TryParseCommand(Cmd("old", 38000, 7, "STOP_OFF"),
                out CommandPacket establishOld);
            Assert.IsTrue(router.Ingest(establishOld));
            nul.Apply(establishOld, 0.0);

            nul.DirectionMode = true;
            CommandPacket.TryParseCommand(Cmd("new", 38001, 1, "RIGHT"),
                out CommandPacket right);
            Assert.IsTrue(router.Ingest(right));
            nul.Apply(right, 0.0);
            Assert.AreEqual("new", router.Session.ActiveSession);
            Assert.AreEqual(1, nul.LastDirection);
            Assert.AreEqual(1, router.Dedup.Count);

            Assert.IsFalse(router.Ingest(right),
                "the new-session direction remains exactly-once");
            CommandPacket.TryParseCommand(Cmd("old", 38000, 8, "LEFT"),
                out CommandPacket staleLeft);
            Assert.IsFalse(router.Ingest(staleLeft));
            Assert.AreEqual(1, nul.LastDirection,
                "late old-session direction cannot reverse the committed candidate");
            Assert.AreEqual(1, router.Session.DiscardedStale);
        }

        private static string Cmd(string session, int start, int id, string type)
            => "{\"v\":1,\"kind\":\"cmd\",\"session\":\"" + session +
               "\",\"sstart\":" + start + ",\"cmdId\":" + id +
               ",\"seq\":" + id + ",\"tUs\":0,\"type\":\"" + type +
               "\",\"phase\":\"fire\"}";
    }
}
