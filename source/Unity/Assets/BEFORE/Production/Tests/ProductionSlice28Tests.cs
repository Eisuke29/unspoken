using NUnit.Framework;
using UnityEngine;

namespace Before.Production.Tests
{
    public class ProductionSlice28Tests
    {
        [Test]
        public void CoreTimingMatchesApprovedTransferSequence()
        {
            Assert.AreEqual(0.8f, ProductionCoreFlow.UnfoldSeconds, 0.001f);
            Assert.AreEqual(1.2f, ProductionCoreFlow.TransferSeconds, 0.001f);
            Assert.AreEqual(1.0f, ProductionCoreFlow.DisconnectSeconds, 0.001f);
            Assert.AreEqual(0.5f,
                ProductionCoreFlow.ProgressFor(ProductionCoreStage.Unfolding, 0.4f), 0.001f);
            Assert.AreEqual(1f,
                ProductionCoreFlow.ProgressFor(ProductionCoreStage.IndependentHold, 0f));
        }

        [Test]
        public void CorePresenterUsesExactSparseCopyAndStageSpecificSignalColors()
        {
            Assert.AreEqual("SYS// CORE NODEを確認", ProductionCorePresenter.CoreHeadline);
            Assert.AreEqual("接続を維持してください", ProductionCorePresenter.MaintainLink);
            Assert.AreEqual("SYS// 転送完了", ProductionCorePresenter.TransferComplete);
            Assert.AreEqual(8, ProductionCorePresenter.TemporaryRendererCount);
            Assert.AreNotEqual(ProductionCorePresenter.StageColor(
                    ProductionCoreStage.Unfolding),
                ProductionCorePresenter.StageColor(ProductionCoreStage.Transferring));
            Assert.AreEqual(Color.clear,
                ProductionCorePresenter.StageColor(ProductionCoreStage.None));
        }
    }
}
