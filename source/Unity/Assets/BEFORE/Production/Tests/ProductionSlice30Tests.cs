using NUnit.Framework;

namespace Before.Production.Tests
{
    public class ProductionSlice30Tests
    {
        [Test]
        public void BothRoutesHaveDifferentTraversalButShareOneTerminalContract()
        {
            Assert.AreNotEqual(ProductionBranchFlow.DemandSpacingFor(ProductionRoute.Left),
                ProductionBranchFlow.DemandSpacingFor(ProductionRoute.Right));
            Assert.AreNotEqual(ProductionBranchFlow.BeatSpacingFor(ProductionRoute.Left),
                ProductionBranchFlow.BeatSpacingFor(ProductionRoute.Right));
            Assert.AreEqual(ProductionEndingStage.EndingHold,
                (ProductionEndingStage)System.Enum.Parse(typeof(ProductionEndingStage),
                    "EndingHold"));
            Assert.AreEqual(0f,
                ProductionEndingFlow.DurationFor(ProductionEndingStage.EndingHold));
            Assert.AreEqual(1f,
                ProductionEndingFlow.ProgressFor(ProductionEndingStage.EndingHold, 999f));
        }
    }
}
