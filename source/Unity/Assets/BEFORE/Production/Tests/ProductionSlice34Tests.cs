using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace Before.Production.Tests
{
    public class ProductionSlice34Tests
    {
        [Test]
        public void GameplayRestartDoesNotResetOrReplaceTransportSessionState()
        {
            string runtime = Path.Combine(Application.dataPath,
                "BEFORE", "Production", "Runtime");
            string controller = File.ReadAllText(Path.Combine(runtime,
                "NulProductionController.cs"));
            string loop = File.ReadAllText(Path.Combine(runtime,
                "ProductionGameLoop.cs"));
            string ending = File.ReadAllText(Path.Combine(runtime,
                "ProductionEndingFlow.cs"));

            foreach (string source in new[] { controller, loop, ending })
            {
                StringAssert.DoesNotContain("Session.Reset(", source,
                    "gameplay restart must not reset the active transport session");
                StringAssert.DoesNotContain("Dedup.Clear(", source,
                    "gameplay restart must not clear command-id history");
            }

            StringAssert.Contains("gameLoop.RestartGame();", ending,
                "ending restart must continue to use the ordinary gameplay reset");
        }
    }
}
