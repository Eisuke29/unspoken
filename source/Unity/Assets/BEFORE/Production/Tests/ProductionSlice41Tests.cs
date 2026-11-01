using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace Before.Production.Tests
{
    public class ProductionSlice41Tests
    {
        [Test]
        public void GameplayRecoveryClearsPersistentReactionStateButNotTransportHistory()
        {
            string path = Path.Combine(Application.dataPath, "BEFORE", "Production",
                "Runtime", "NulProductionController.cs");
            string source = File.ReadAllText(path);
            int start = source.IndexOf("public void RecoverAt",
                System.StringComparison.Ordinal);
            int end = source.IndexOf("public void SetMovementSuspended", start,
                System.StringComparison.Ordinal);
            Assert.GreaterOrEqual(start, 0);
            Assert.Greater(end, start);
            string recovery = source.Substring(start, end - start);

            StringAssert.Contains("Nul.StopActive = false;", recovery);
            StringAssert.Contains("Nul.Crouching = false;", recovery);
            StringAssert.Contains("Nul.CrouchUntil = 0.0;", recovery);
            StringAssert.Contains("ApplyCollider(false);", recovery);
            StringAssert.DoesNotContain("Session.Reset(", recovery);
            StringAssert.DoesNotContain("Dedup.Clear(", recovery);
            StringAssert.DoesNotContain("new CommandRouter", recovery);
        }
    }
}
