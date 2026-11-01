using NUnit.Framework;

namespace Before.Production.Tests
{
    public class ProductionSlice31Tests
    {
        [Test]
        public void RouteProfilesUseOppositeJumpCrouchWeightsAndTheSameStopLoad()
        {
            CountKinds(ProductionRoute.Left, out int leftJump,
                out int leftCrouch, out int leftStop);
            CountKinds(ProductionRoute.Right, out int rightJump,
                out int rightCrouch, out int rightStop);

            Assert.AreEqual(ProductionBranchFlow.BranchDemandCount,
                leftJump + leftCrouch + leftStop);
            Assert.AreEqual(ProductionBranchFlow.BranchDemandCount,
                rightJump + rightCrouch + rightStop);
            Assert.AreEqual(3, leftJump);
            Assert.AreEqual(5, leftCrouch);
            Assert.AreEqual(4, leftStop);
            Assert.AreEqual(5, rightJump);
            Assert.AreEqual(3, rightCrouch);
            Assert.AreEqual(4, rightStop);
        }

        private static void CountKinds(ProductionRoute route, out int jump,
            out int crouch, out int stop)
        {
            jump = 0;
            crouch = 0;
            stop = 0;
            for (int i = 0; i < ProductionBranchFlow.BranchDemandCount; i++)
            {
                switch (ProductionBranchFlow.KindAt(route, i))
                {
                    case ProductionObstacleKind.Jump:
                        jump++;
                        break;
                    case ProductionObstacleKind.Crouch:
                        crouch++;
                        break;
                    case ProductionObstacleKind.Stop:
                        stop++;
                        break;
                    default:
                        Assert.Fail("branch profiles may contain only symbolic demands");
                        break;
                }
            }
        }
    }
}
