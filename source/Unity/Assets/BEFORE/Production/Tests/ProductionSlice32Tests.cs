using NUnit.Framework;

namespace Before.Production.Tests
{
    public class ProductionSlice32Tests
    {
        [Test]
        public void StoryMilestonesRemainStrictlyOrderedBeforeTheTerminalMarker()
        {
            Assert.IsTrue(ProductionTeachingFlow.IsTeachingBeat(1));
            Assert.IsTrue(ProductionTeachingFlow.IsTeachingBeat(4));
            Assert.IsTrue(ProductionTeachingFlow.IsTeachingBeat(13));
            Assert.IsFalse(ProductionTeachingFlow.IsTeachingBeat(
                ProductionForkFlow.ForkBeatId));
            Assert.AreEqual(ProductionForkFlow.ForkBeatId + 1,
                ProductionBranchFlow.FirstBranchBeatId);
            Assert.Less(ProductionBranchFlow.FirstBranchBeatId,
                ProductionBranchFlow.LastBranchBeatId);
            Assert.Less(ProductionBranchFlow.LastBranchBeatId,
                ProductionCourseDefinition.RequiredBeatCount);
        }
    }
}
