using System.Linq;
using NUnit.Framework;

namespace Before.Production.Tests
{
    public class ProductionSlice22Tests
    {
        [Test]
        public void ForkContract_UsesBeatTwentyTwoAndFixedWindowDurations()
        {
            Assert.AreEqual(22, ProductionForkFlow.ForkBeatId);
            Assert.AreEqual(0.8f, ProductionForkFlow.ExpandSeconds);
            Assert.AreEqual(1.0f, ProductionForkFlow.ConfirmSeconds);
            Assert.AreEqual(0.6f, ProductionForkFlow.CollapseSeconds);

            ProductionCourseDefinition.Beat beat = ProductionCourseDefinition
                .BuildProductionDefaults().Single(candidate => candidate.Id == 22);
            CollectionAssert.AreEqual(
                new[] { ProductionObstacleKind.Direction }, beat.Demands);
            Assert.GreaterOrEqual(beat.DistanceToNextBeat, 72f);
        }

        [Test]
        public void ForkAdapter_AcceptsOnlyFrozenDirectionCommands()
        {
            Assert.IsTrue(ProductionForkFlow.TryMapDirection(CommandKind.Left, out int left));
            Assert.AreEqual(-1, left);
            Assert.IsTrue(ProductionForkFlow.TryMapDirection(CommandKind.Right, out int right));
            Assert.AreEqual(1, right);
            Assert.IsFalse(ProductionForkFlow.TryMapDirection(CommandKind.Jump, out _));
            Assert.IsFalse(ProductionForkFlow.TryMapDirection(CommandKind.Crouch, out _));
            Assert.IsFalse(ProductionForkFlow.TryMapDirection(CommandKind.StopOn, out _));
            Assert.IsFalse(ProductionForkFlow.TryMapDirection(CommandKind.StopOff, out _));
        }

        [Test]
        public void ForkProgress_ExpandsHoldsAndCollapsesWithoutChangingGameplayTime()
        {
            Assert.AreEqual(0.5f,
                ProductionForkFlow.ProgressFor(ProductionForkStage.Expanding, 0.4f), 0.001f);
            Assert.AreEqual(1f,
                ProductionForkFlow.ProgressFor(ProductionForkStage.AwaitingDirection, 99f));
            Assert.AreEqual(1f,
                ProductionForkFlow.ProgressFor(ProductionForkStage.Confirmed, 0.5f));
            Assert.AreEqual(0.5f,
                ProductionForkFlow.ProgressFor(ProductionForkStage.Collapsing, 0.3f), 0.001f);
            Assert.AreEqual(0f,
                ProductionForkFlow.ProgressFor(ProductionForkStage.None, 0f));
        }

        [Test]
        public void ForkPresenter_UsesExactApprovedSparseCopy()
        {
            Assert.AreEqual("SYS// 経路が分岐しています", ProductionForkPresenter.Headline);
            Assert.AreEqual("進む方向を指してください", ProductionForkPresenter.Instruction);
            Assert.AreEqual("SYS// 左経路を受信",
                ProductionForkPresenter.ReceivedCopyFor(ProductionRoute.Left));
            Assert.AreEqual("左へ進みます",
                ProductionForkPresenter.ProceedCopyFor(ProductionRoute.Left));
            Assert.AreEqual("SYS// 右経路を受信",
                ProductionForkPresenter.ReceivedCopyFor(ProductionRoute.Right));
            Assert.AreEqual("右へ進みます",
                ProductionForkPresenter.ProceedCopyFor(ProductionRoute.Right));
            Assert.AreEqual(ProductionHudLayout.Violet,
                ProductionForkPresenter.RouteColorFor(ProductionRoute.Left));
            Assert.AreEqual(ProductionHudLayout.Violet,
                ProductionForkPresenter.RouteColorFor(ProductionRoute.Right));
        }
    }
}
