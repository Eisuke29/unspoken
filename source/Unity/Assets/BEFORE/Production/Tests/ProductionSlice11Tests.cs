using NUnit.Framework;

namespace Before.Production.Tests
{
    public class ProductionSlice11Tests
    {
        [TestCase(-1, 0)]
        [TestCase(0, 0)]
        [TestCase(2, 2)]
        [TestCase(3, 3)]
        [TestCase(4, 3)]
        public void HeartCountIsAlwaysWithinThreeLifeContract(int lives, int expected)
        {
            Assert.AreEqual(expected, ProductionHud.ClampHeartCount(lives));
        }
    }
}
