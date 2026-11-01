using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace Before.Production.Tests
{
    public class ProductionSlice40Tests
    {
        [Test]
        public void StorySuspensionLeavesTransportAndNulConsumptionAheadOfReactionGate()
        {
            string path = Path.Combine(Application.dataPath, "BEFORE", "Production",
                "Runtime", "NulProductionController.cs");
            string source = File.ReadAllText(path);

            int handlerStart = source.IndexOf("private void OnBusCommand",
                System.StringComparison.Ordinal);
            int updateStart = source.IndexOf("private void Update()", handlerStart,
                System.StringComparison.Ordinal);
            Assert.GreaterOrEqual(handlerStart, 0);
            Assert.Greater(updateStart, handlerStart);
            string handler = source.Substring(handlerStart, updateStart - handlerStart);
            int apply = handler.IndexOf("Nul.Apply(p, _gameplayTime);",
                System.StringComparison.Ordinal);
            int suspensionGate = handler.IndexOf("StorySuspended) return;",
                System.StringComparison.Ordinal);
            Assert.GreaterOrEqual(apply, 0);
            Assert.Greater(suspensionGate, apply,
                "story suspension may suppress physical reaction only after consumption");

            int fixedStart = source.IndexOf("private void FixedUpdate()", updateStart,
                System.StringComparison.Ordinal);
            Assert.Greater(fixedStart, updateStart);
            string update = source.Substring(updateStart, fixedStart - updateStart);
            int ingest = update.IndexOf("IngestUdp();", System.StringComparison.Ordinal);
            int clockGate = update.IndexOf(
                "if (!Nul.Paused && !StorySuspended && !EnergySuspended)",
                System.StringComparison.Ordinal);
            Assert.GreaterOrEqual(ingest, 0);
            Assert.Greater(clockGate, ingest,
                "StorySuspended must not stop receiver polling or session/dedup");
        }
    }
}
