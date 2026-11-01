using NUnit.Framework;

namespace Before.Production.Tests
{
    public class ProductionSlice35Tests
    {
        [Test]
        public void NewSessionCommandReceivedDuringPauseIsConsumedAndNeverReplayed()
        {
            var router = new CommandRouter();
            var nul = new NulState();

            CommandPacket.TryParseCommand(Cmd("old", 37000, 7, "STOP_OFF"),
                out CommandPacket establishOld);
            Assert.IsTrue(router.Ingest(establishOld));
            nul.Apply(establishOld, 0.0);

            nul.Paused = true;
            CommandPacket.TryParseCommand(Cmd("new", 37001, 1, "JUMP"),
                out CommandPacket pausedNew);
            Assert.IsTrue(router.Ingest(pausedNew));
            nul.Apply(pausedNew, 0.0);
            Assert.AreEqual("new", router.Session.ActiveSession);
            Assert.AreEqual(1, router.Dedup.Count);
            Assert.AreEqual(0, nul.JumpCount);

            nul.Paused = false;
            Assert.IsFalse(router.Ingest(pausedNew),
                "unpausing must not replay the command consumed during pause");
            Assert.AreEqual(0, nul.JumpCount);

            CommandPacket.TryParseCommand(Cmd("new", 37001, 2, "JUMP"),
                out CommandPacket nextNew);
            Assert.IsTrue(router.Ingest(nextNew));
            nul.Apply(nextNew, 1.0);
            Assert.AreEqual(1, nul.JumpCount);

            CommandPacket.TryParseCommand(Cmd("old", 37000, 8, "JUMP"),
                out CommandPacket staleOld);
            Assert.IsFalse(router.Ingest(staleOld));
            Assert.AreEqual(1, router.Session.DiscardedStale);
        }

        private static string Cmd(string session, int start, int id, string type)
            => "{\"v\":1,\"kind\":\"cmd\",\"session\":\"" + session +
               "\",\"sstart\":" + start + ",\"cmdId\":" + id +
               ",\"seq\":" + id + ",\"tUs\":0,\"type\":\"" + type +
               "\",\"phase\":\"fire\"}";
    }
}
