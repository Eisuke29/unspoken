using NUnit.Framework;

namespace Before.Production.Tests
{
    public class ProductionSlice37Tests
    {
        [Test]
        public void ThreeTeachingDeliveriesContinueAcrossNewSessionWithoutCountingStaleOld()
        {
            var router = new CommandRouter();
            int delivered = 0;

            Deliver(router, Cmd("old", 39000, 7, "JUMP"), ref delivered);
            Assert.AreEqual(1, delivered);

            Deliver(router, Cmd("new", 39001, 1, "JUMP"), ref delivered);
            Deliver(router, Cmd("new", 39001, 1, "JUMP"), ref delivered);
            Assert.AreEqual(2, delivered,
                "the first new-session sample is delivered exactly once");

            Deliver(router, Cmd("old", 39000, 8, "JUMP"), ref delivered);
            Assert.AreEqual(2, delivered,
                "late old-session input is not a teaching delivery");
            Assert.AreEqual(1, router.Session.DiscardedStale);

            Deliver(router, Cmd("new", 39001, 2, "JUMP"), ref delivered);
            Assert.AreEqual(ProductionTeachingFlow.SamplesPerTeaching, delivered);
            Assert.AreEqual("new", router.Session.ActiveSession);
            Assert.AreEqual(2, router.Dedup.Count);
        }

        private static void Deliver(CommandRouter router, string json, ref int delivered)
        {
            Assert.IsTrue(CommandPacket.TryParseCommand(json, out CommandPacket packet));
            if (router.Ingest(packet)) delivered++;
        }

        private static string Cmd(string session, int start, int id, string type)
            => "{\"v\":1,\"kind\":\"cmd\",\"session\":\"" + session +
               "\",\"sstart\":" + start + ",\"cmdId\":" + id +
               ",\"seq\":" + id + ",\"tUs\":0,\"type\":\"" + type +
               "\",\"phase\":\"fire\"}";
    }
}
