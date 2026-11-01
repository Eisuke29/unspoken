using NUnit.Framework;

namespace Before.Production.Tests
{
    public class ProductionSlice2Tests
    {
        [TestCase(ProductionObstacleKind.Jump, true, false, false, 0, true)]
        [TestCase(ProductionObstacleKind.Jump, false, true, true, 1, false)]
        [TestCase(ProductionObstacleKind.Crouch, false, true, false, 0, true)]
        [TestCase(ProductionObstacleKind.Crouch, true, false, true, 1, false)]
        [TestCase(ProductionObstacleKind.Stop, false, false, true, 0, true)]
        [TestCase(ProductionObstacleKind.Stop, true, true, false, 1, false)]
        [TestCase(ProductionObstacleKind.Direction, false, false, false, -1, true)]
        [TestCase(ProductionObstacleKind.Direction, true, true, true, 0, false)]
        public void ObstacleKinds_UseOnlyTheirMatchingReaction(
            ProductionObstacleKind kind, bool jumpClear, bool crouching,
            bool stopPrepared, int direction, bool expected)
        {
            Assert.AreEqual(expected, ProductionObstacleJudge.IsSatisfied(
                kind, jumpClear, crouching, stopPrepared, direction));
        }
    }
}
