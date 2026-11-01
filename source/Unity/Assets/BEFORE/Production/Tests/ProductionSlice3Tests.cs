using NUnit.Framework;

namespace Before.Production.Tests
{
    public class ProductionSlice3Tests
    {
        [Test]
        public void RecoveryConstants_MatchProductionV2Lock()
        {
            Assert.AreEqual(3, ProductionGameLoop.StartingLives);
            Assert.AreEqual(72f, ProductionGameLoop.RecoveryBacktrack);
            Assert.AreEqual(1.4f, ProductionGameLoop.RecoveryFreezeSeconds);
            Assert.AreEqual(0.6f, ProductionGameLoop.RecoveryGraceSeconds);
        }

        [Test]
        public void Checkpoint_IsSeventyTwoPixelsBeforeHazard()
        {
            Assert.AreEqual(308f, ProductionGameLoop.CheckpointFor(380f), 0.0001f);
        }
    }
}
