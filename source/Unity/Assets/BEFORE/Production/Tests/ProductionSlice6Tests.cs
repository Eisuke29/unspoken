using NUnit.Framework;

namespace Before.Production.Tests
{
    public class ProductionSlice6Tests
    {
        [TestCase(0f, false)]
        [TestCase(1.19f, false)]
        [TestCase(1.2f, false)]
        [TestCase(2.99f, false)]
        [TestCase(3.0f, true)]
        [TestCase(5.19f, true)]
        [TestCase(5.2f, false)]
        public void EnergyWall_UsesTelegraphClosedOpenCycle(float time, bool expectedOpen)
        {
            Assert.AreEqual(expectedOpen, ProductionCourseLoop.EnergyWallIsOpen(time));
        }

        [Test]
        public void StopReactionRequiresPreparationAndOpenWall()
        {
            Assert.IsFalse(ProductionObstacleJudge.IsSatisfied(
                ProductionObstacleKind.Stop, false, false, false, 0, true));
            Assert.IsFalse(ProductionObstacleJudge.IsSatisfied(
                ProductionObstacleKind.Stop, false, false, true, 0, false));
            Assert.IsTrue(ProductionObstacleJudge.IsSatisfied(
                ProductionObstacleKind.Stop, false, false, true, 0, true));
        }
    }
}
