using System.IO;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Before.Production.Tests
{
    public class ProductionSlice48Tests
    {
        private const string ScenePath =
            "Assets/BEFORE/Scenes/BEFORE_ProductionV2.unity";

        [Test]
        public void EntryAndExitCopyMatchesAuthoritativeScriptExactly()
        {
            Assert.AreEqual("UNSPOKEN", ProductionShellPresenter.TitleLine);
            Assert.AreEqual("手を接続範囲に入れてください",
                ProductionShellPresenter.TitleInstruction);
            Assert.AreEqual("SYS// エネルギー経路を接続中",
                ProductionShellPresenter.ConnectionLine);
            Assert.AreEqual("手をそのまま保ってください",
                ProductionShellPresenter.ConnectionInstruction);
            Assert.AreEqual("SYS// 接続：安定",
                ProductionShellPresenter.LinkedLine);
            Assert.AreEqual("接続を維持してください",
                ProductionShellPresenter.LinkedInstruction);
            Assert.AreEqual("SYS// 実行を終了しますか",
                ProductionShellPresenter.ExitLine);
            Assert.AreEqual("ENTER：終了  ESC：戻る",
                ProductionShellPresenter.ExitInstruction);
        }

        [TestCase(1920f, 1080f)]
        [TestCase(1280f, 720f)]
        [TestCase(2560f, 1080f)]
        [TestCase(1024f, 768f)]
        public void ShellPanelsStayInsideContentAndClearOfHand(float width,
            float height)
        {
            Rect content = ProductionHudLayout.ContentRect(width, height);
            Rect title = ProductionShellPresenter.TitleBounds(width, height);
            Rect exit = ProductionShellPresenter.ExitBounds(width, height);
            Rect hand = ProductionDiagnosticsHud.HandPanelRect(width, height);
            AssertContains(content, title);
            AssertContains(content, exit);
            Assert.IsFalse(title.Overlaps(hand));
            Assert.IsFalse(exit.Overlaps(hand));
        }

        [Test]
        public void SavedSceneOwnsOneFinalShellWithPublicFont()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath,
                OpenSceneMode.Single);
            GameObject root = scene.GetRootGameObjects()[0];
            var flows = root.GetComponents<ProductionShellFlow>();
            var presenters = root.GetComponents<ProductionShellPresenter>();
            Assert.AreEqual(1, flows.Length);
            Assert.AreEqual(1, presenters.Length);
            Assert.AreSame(flows[0], presenters[0].Flow);
            Assert.AreEqual("DotGothic16-Regular", presenters[0].PublicFont.name);
            Assert.IsNotNull(presenters[0].TitleLogo);
            Assert.AreEqual("UNSPOKEN_Logo_06_RuinedMachine",
                presenters[0].TitleLogo.name);
            Assert.AreSame(flows[0], root.GetComponent<ProductionHud>().ShellFlow);
            Assert.AreSame(presenters[0],
                root.GetComponent<ProductionFrameCompositor>().ShellOverlay);
            var recovery = root.GetComponents<ProductionEnergyRecoveryFlow>();
            var energy = root.GetComponents<ProductionEnergyLinkPresenter>();
            Assert.AreEqual(1, recovery.Length);
            Assert.AreEqual(1, energy.Length);
            Assert.AreSame(recovery[0], energy[0].Recovery);
            Assert.AreSame(flows[0], energy[0].Shell);
            Assert.AreEqual("DotGothic16-Regular", energy[0].PublicFont.name);
            Assert.AreSame(energy[0],
                root.GetComponent<ProductionFrameCompositor>().EnergyLink);
        }

        [Test]
        public void UnityEditorDependencyExistsOnlyInEditorAdapter()
        {
            string runtime = File.ReadAllText(
                "Assets/BEFORE/Production/Runtime/ProductionShellFlow.cs");
            string adapter = File.ReadAllText(
                "Assets/BEFORE/Production/Editor/ProductionEditorExitAdapter.cs");
            Assert.IsFalse(runtime.Contains("using UnityEditor"));
            Assert.IsTrue(adapter.Contains("using UnityEditor"));
            Assert.IsTrue(adapter.Contains("EditorApplication.isPlaying = false"));
        }

        private static void AssertContains(Rect outer, Rect inner)
        {
            Assert.GreaterOrEqual(inner.xMin, outer.xMin - 0.01f);
            Assert.GreaterOrEqual(inner.yMin, outer.yMin - 0.01f);
            Assert.LessOrEqual(inner.xMax, outer.xMax + 0.01f);
            Assert.LessOrEqual(inner.yMax, outer.yMax + 0.01f);
        }
    }
}
