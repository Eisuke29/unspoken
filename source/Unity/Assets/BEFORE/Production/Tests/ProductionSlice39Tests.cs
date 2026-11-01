using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace Before.Production.Tests
{
    public class ProductionSlice39Tests
    {
        [Test]
        public void GameOverPausesGameplayWhileRestartUsesTheOrdinaryLoopReset()
        {
            string path = Path.Combine(Application.dataPath, "BEFORE", "Production",
                "Runtime", "ProductionGameLoop.cs");
            string source = File.ReadAllText(path);

            int failureStart = source.IndexOf("private void HandleResolved",
                System.StringComparison.Ordinal);
            int recoveryStart = source.IndexOf("public void CompleteRecovery",
                failureStart, System.StringComparison.Ordinal);
            Assert.GreaterOrEqual(failureStart, 0);
            Assert.Greater(recoveryStart, failureStart);
            string failureHandler = source.Substring(failureStart,
                recoveryStart - failureStart);

            StringAssert.Contains("IsGameOver = true;", failureHandler);
            StringAssert.Contains("controller.Nul.Paused = true;", failureHandler,
                "terminal failure must use the same pause gate as other no-reaction holds");
            StringAssert.Contains("public void RestartGame() => ResetLoop();", source,
                "GAME OVER restart must remain the ordinary same-Play gameplay reset");
            StringAssert.DoesNotContain("Session.Reset(", source);
            StringAssert.DoesNotContain("Dedup.Clear(", source);
        }
    }
}
