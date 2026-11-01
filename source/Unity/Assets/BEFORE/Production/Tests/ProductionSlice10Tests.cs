using NUnit.Framework;

namespace Before.Production.Tests
{
    public class ProductionSlice10Tests
    {
        [TestCase(-1, ProductionRoute.Left)]
        [TestCase(0, ProductionRoute.None)]
        [TestCase(1, ProductionRoute.Right)]
        public void DirectionMapsToStableRouteState(int direction, ProductionRoute expected)
        {
            Assert.AreEqual(expected, ProductionCourseLoop.RouteForDirection(direction));
        }
    }
}
