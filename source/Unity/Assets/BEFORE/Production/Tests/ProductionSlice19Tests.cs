using NUnit.Framework;

namespace Before.Production.Tests
{
    public class ProductionSlice19Tests
    {
        [TestCase(false, false, false)]
        [TestCase(false, true, false)]
        [TestCase(true, false, true)]
        [TestCase(true, true, false)]
        public void PausePresentationIsVisibleOnlyForNonGameOverPause(
            bool paused, bool gameOver, bool expected)
        {
            Assert.AreEqual(expected, ProductionHud.ShouldShowPause(paused, gameOver));
        }
    }
}
