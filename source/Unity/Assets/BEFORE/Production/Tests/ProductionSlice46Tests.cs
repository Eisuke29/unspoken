using NUnit.Framework;

namespace Before.Production.Tests
{
    public class ProductionSlice46Tests
    {
        [Test]
        public void OnlyOrdinaryPauseFreezesTheImpulseClock()
        {
            Assert.IsFalse(ProductionCameraImpulsePresenter
                .PresentationClockFrozen(false, false));
            Assert.IsTrue(ProductionCameraImpulsePresenter
                .PresentationClockFrozen(true, false));
            Assert.IsFalse(ProductionCameraImpulsePresenter
                .PresentationClockFrozen(true, true),
                "terminal failure feedback must finish instead of shifting GAME OVER forever");
        }
    }
}
