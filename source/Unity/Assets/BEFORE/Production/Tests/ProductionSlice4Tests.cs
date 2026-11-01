using NUnit.Framework;

namespace Before.Production.Tests
{
    public class ProductionSlice4Tests
    {
        [TestCase(1, false)]
        [TestCase(0, true)]
        [TestCase(-1, true)]
        public void GameOverStartsOnlyWhenLivesAreExhausted(int lives, bool expected)
        {
            Assert.AreEqual(expected, ProductionGameLoop.LivesExhausted(lives));
        }
    }
}
