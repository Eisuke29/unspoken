using System.Linq;
using NUnit.Framework;

namespace Before.Production.Tests
{
    public class ProductionSlice9Tests
    {
        [Test]
        public void FullCourseDemandInventory_IsNineteenNineteenThirteenOne()
        {
            var demands = ProductionCourseDefinition.BuildProductionDefaults()
                .SelectMany(b => b.Demands).ToArray();
            Assert.AreEqual(19, demands.Count(d => d == ProductionObstacleKind.Jump));
            Assert.AreEqual(19, demands.Count(d => d == ProductionObstacleKind.Crouch));
            Assert.AreEqual(13, demands.Count(d => d == ProductionObstacleKind.Stop));
            Assert.AreEqual(1, demands.Count(d => d == ProductionObstacleKind.Direction));
            Assert.AreEqual(52, demands.Length);
        }
    }
}
