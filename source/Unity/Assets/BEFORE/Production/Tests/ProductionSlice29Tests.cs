using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace Before.Production.Tests
{
    public class ProductionSlice29Tests
    {
        [Test]
        public void FarewellTimingAndTraceSequenceAreDeterministic()
        {
            Assert.AreEqual(0.6f, ProductionEndingFlow.LookBackSeconds, 0.001f);
            Assert.AreEqual(0.55f, ProductionEndingFlow.TraceDrawSeconds, 0.001f);
            Assert.AreEqual(0.2f, ProductionEndingFlow.TraceGapSeconds, 0.001f);
            Assert.AreEqual(1.8f, ProductionEndingFlow.DepartSeconds, 0.001f);
            Assert.AreEqual(0.8f, ProductionEndingFlow.WorldFadeSeconds, 0.001f);
            Assert.AreEqual(0.7f, ProductionEndingFlow.TraceFadeSeconds, 0.001f);
            Assert.AreEqual(3, ProductionEndingFlow.TraceCount);
            Assert.AreEqual(1, ProductionEndingPresenter.VisibleTraceCount(
                ProductionEndingStage.MemoryTraces, 0f));
            Assert.AreEqual(3, ProductionEndingPresenter.VisibleTraceCount(
                ProductionEndingStage.MemoryTraces, 1.6f));
        }

        [Test]
        public void EndingPresenterKeepsExactHoldCopyAndFullWorldFade()
        {
            Assert.AreEqual("SYS// 接続記録を終了", ProductionEndingPresenter.CompleteCopy);
            Assert.AreEqual("LINK TERMINATED", ProductionEndingPresenter.TerminatedCopy);
            Assert.AreEqual("R // RESTART", ProductionEndingPresenter.RestartCopy);
            Assert.AreEqual(4, ProductionEndingPresenter.TemporaryRendererCount);
            Assert.AreEqual(1f, ProductionEndingPresenter.FadeAlphaFor(
                ProductionEndingStage.EndingHold, 0f));
            Assert.AreNotEqual(ProductionEndingPresenter.TraceColorAt(0),
                ProductionEndingPresenter.TraceColorAt(2));
        }

        [Test]
        public void EndingRuntimeContainsNoAutomaticApplicationOrEditorExit()
        {
            string path = Path.Combine(Application.dataPath,
                "BEFORE/Production/Runtime/ProductionEndingFlow.cs");
            string source = File.ReadAllText(path);
            StringAssert.DoesNotContain("Application.Quit", source);
            StringAssert.DoesNotContain("EditorApplication", source);
            StringAssert.DoesNotContain("UnityEditor", source);
        }
    }
}
