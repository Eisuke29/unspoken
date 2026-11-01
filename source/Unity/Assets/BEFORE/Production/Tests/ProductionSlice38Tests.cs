using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace Before.Production.Tests
{
    public class ProductionSlice38Tests
    {
        [Test]
        public void MovementSuspensionDoesNotGateTheAlwaysOnCommandHandler()
        {
            string path = Path.Combine(Application.dataPath, "BEFORE", "Production",
                "Runtime", "NulProductionController.cs");
            string source = File.ReadAllText(path);
            int start = source.IndexOf("private void OnBusCommand", System.StringComparison.Ordinal);
            int end = source.IndexOf("private void Update()", start, System.StringComparison.Ordinal);
            Assert.GreaterOrEqual(start, 0);
            Assert.Greater(end, start);
            string handler = source.Substring(start, end - start);

            StringAssert.Contains("Nul.Paused", handler);
            StringAssert.Contains("Nul.DirectionMode", handler);
            StringAssert.Contains("StorySuspended", handler);
            StringAssert.DoesNotContain("MovementSuspended", handler,
                "recovery movement suspension must not disable command reaction");
        }
    }
}
