using NUnit.Framework;
using UnityEngine;

namespace Before.Production.Tests
{
    public class ProductionSlice26Tests
    {
        [TestCase(1920f, 1080f)]
        [TestCase(1280f, 720f)]
        public void HeartsHandTeachingAndForkOwnNonOverlappingRegions(float width, float height)
        {
            Rect hearts = ProductionHud.HeartRowBounds(width, height);
            Rect hand = ProductionDiagnosticsHud.HandPanelRect(width, height);
            Rect teaching = ProductionTeachingPresenter.TeachingPanelRect(width, height);
            Rect fork = ProductionForkPresenter.TargetWindowRect(width, height);

            Assert.IsFalse(hearts.Overlaps(hand), "upper-center hearts must not overlap hand");
            Assert.IsFalse(hearts.Overlaps(teaching), "hearts must remain above teaching copy");
            Assert.IsFalse(hearts.Overlaps(fork), "hearts must remain above ForkMap");
            Assert.IsFalse(teaching.Overlaps(hand), "teaching stays outside hand panel");
            Assert.IsFalse(fork.Overlaps(hand), "ForkMap stays outside hand panel");

            Assert.GreaterOrEqual(hearts.xMin, 0f);
            Assert.LessOrEqual(hearts.xMax, width);
            Assert.GreaterOrEqual(hand.xMin, 0f);
            Assert.LessOrEqual(hand.xMax, width);
            Assert.GreaterOrEqual(fork.xMin, 0f);
            Assert.LessOrEqual(fork.xMax, width);
        }
    }
}
