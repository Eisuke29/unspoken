using System.Linq;
using NUnit.Framework;

namespace Before.Production.Tests
{
    public class ProductionSlice18Tests
    {
        [Test]
        public void TeachingContract_UsesThreeSamplesOneRepairAndSeventyTwoPixelRunUp()
        {
            Assert.AreEqual(3, ProductionTeachingFlow.SamplesPerTeaching);
            Assert.AreEqual(1, ProductionTeachingFlow.SamplesPerRepair);
            Assert.AreEqual(72f, ProductionTeachingFlow.SafeRunUp);
            Assert.AreEqual(0.8f, ProductionTeachingFlow.ConfirmationSeconds);
        }

        [Test]
        public void TeachingCommandAdapter_MapsOnlyFrozenSemanticFamilies()
        {
            Assert.IsTrue(ProductionTeachingFlow.TryMapCommand(CommandKind.Jump, out var jump));
            Assert.AreEqual(ProductionObstacleKind.Jump, jump);
            Assert.IsTrue(ProductionTeachingFlow.TryMapCommand(CommandKind.Crouch, out var crouch));
            Assert.AreEqual(ProductionObstacleKind.Crouch, crouch);
            Assert.IsTrue(ProductionTeachingFlow.TryMapCommand(CommandKind.StopOn, out var stop));
            Assert.AreEqual(ProductionObstacleKind.Stop, stop);
            Assert.IsFalse(ProductionTeachingFlow.TryMapCommand(CommandKind.StopOff, out _));
            Assert.IsFalse(ProductionTeachingFlow.TryMapCommand(CommandKind.Left, out _));
            Assert.IsFalse(ProductionTeachingFlow.TryMapCommand(CommandKind.Right, out _));
        }

        [Test]
        public void Presenter_ContainsExactApprovedTeachingAndRepairCopy()
        {
            Assert.AreEqual("SYS// NULは、この障害を越える方法を知りません",
                ProductionTeachingPresenter.HeadlineFor(ProductionObstacleKind.Jump));
            StringAssert.Contains("形を保ち",
                ProductionTeachingPresenter.InstructionFor(ProductionObstacleKind.Crouch));
            StringAssert.Contains("動き",
                ProductionTeachingPresenter.InstructionFor(ProductionObstacleKind.Crouch));
            StringAssert.Contains("動かさず",
                ProductionTeachingPresenter.InstructionFor(ProductionObstacleKind.Stop));
            Assert.AreEqual("SYS// 「待つ合図」を記憶しました",
                ProductionTeachingPresenter.ConfirmationFor(ProductionObstacleKind.Stop));
        }

        [Test]
        public void CourseTeachingFlags_MatchStoryFlowDependencyOrder()
        {
            int[] teaching = ProductionCourseDefinition.BuildProductionDefaults()
                .Where(beat => beat.TeachingBeat).Select(beat => beat.Id).ToArray();
            CollectionAssert.AreEqual(new[] { 1, 4, 13 }, teaching);
            Assert.IsTrue(teaching.All(ProductionTeachingFlow.IsTeachingBeat));
            Assert.IsFalse(ProductionTeachingFlow.IsTeachingBeat(22));
        }
    }
}
