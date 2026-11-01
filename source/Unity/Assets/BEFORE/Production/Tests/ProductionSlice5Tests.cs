using NUnit.Framework;

namespace Before.Production.Tests
{
    public class ProductionSlice5Tests
    {
        [Test]
        public void SidecarStartInfo_IsProjectLocalHiddenAndPortBound()
        {
            const string root = @"C:\UnityProjects\BEFORE";
            var info = SidecarProcessManager.CreateStartInfo(root, 8765);

            Assert.AreEqual("powershell.exe", info.FileName);
            StringAssert.Contains(@"Tools\BeforeGestureSidecar\run_bench.ps1", info.Arguments);
            StringAssert.Contains("--udp 8765", info.Arguments);
            Assert.AreEqual(root, info.WorkingDirectory);
            Assert.IsFalse(info.UseShellExecute);
            Assert.IsTrue(info.CreateNoWindow);
        }

        [Test]
        public void SidecarScriptPath_DoesNotEscapeProjectRoot()
        {
            string path = SidecarProcessManager.ScriptPathFor(@"D:\Game");
            Assert.AreEqual(@"D:\Game\Tools\BeforeGestureSidecar\run_bench.ps1", path);
        }
    }
}
