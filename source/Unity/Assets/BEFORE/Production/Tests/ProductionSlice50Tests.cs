using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace Before.Production.Tests
{
    public class ProductionSlice50Tests
    {
        [Test]
        public void CourseFinishUsesLayeredRoadAndSixPartSemanticHazards()
        {
            Assert.AreEqual(5, ProductionCoursePresenter.GroundRendererCount);
            Assert.AreEqual(6, ProductionCoursePresenter.VisualPartsPerObstacle);
            Assert.AreNotEqual(
                ProductionCoursePresenter.RouteAccentFor(ProductionRoute.Left),
                ProductionCoursePresenter.RouteAccentFor(ProductionRoute.Right));
        }

        [Test]
        public void HoleOverheadHazardAndEnergyWallKeepDistinctSignals()
        {
            Color jump = ProductionCoursePresenter.ObstacleColorFor(
                ProductionObstacleKind.Jump);
            Color crouch = ProductionCoursePresenter.ObstacleColorFor(
                ProductionObstacleKind.Crouch);
            Color closed = ProductionCoursePresenter.ObstacleColorFor(
                ProductionObstacleKind.Stop, false);
            Color open = ProductionCoursePresenter.ObstacleColorFor(
                ProductionObstacleKind.Stop, true);
            Assert.AreNotEqual(jump, crouch);
            Assert.AreNotEqual(crouch, closed);
            Assert.Less(open.a, closed.a);
        }

        [Test]
        public void BranchFinishAddsRouteSpecificLandmarkDetails()
        {
            Assert.AreEqual(6, ProductionBranchPresenter.TemporaryRendererCount);
            Assert.AreEqual(12, ProductionBranchPresenter.DetailRendererCount);
            Assert.AreNotEqual(
                ProductionBranchPresenter.LandmarkAccentFor(ProductionRoute.Left),
                ProductionBranchPresenter.LandmarkAccentFor(ProductionRoute.Right));
        }

        [Test]
        public void ForkCoreAndEndingUseCompletedVisualPalettesAndDetailCounts()
        {
            Assert.AreEqual(ProductionHudLayout.Violet,
                ProductionForkPresenter.RouteColorFor(ProductionRoute.Left));
            Assert.AreEqual(ProductionHudLayout.Violet,
                ProductionForkPresenter.RouteColorFor(ProductionRoute.Right));
            Assert.AreEqual(12, ProductionCorePresenter.DetailRendererCount);
            Assert.AreEqual(12, ProductionEndingPresenter.DetailRendererCount);
            Assert.AreNotEqual(ProductionCorePresenter.StageColor(
                    ProductionCoreStage.Transferring),
                ProductionCorePresenter.StageColor(
                    ProductionCoreStage.IndependentHold));
            Assert.AreNotEqual(ProductionEndingPresenter.TraceColorAt(0),
                ProductionEndingPresenter.TraceColorAt(2));
        }

        [Test]
        public void VisualFinishSourcesDoNotCreatePhysicsOrMutateFlowFiles()
        {
            string[] presenters =
            {
                "ProductionCoursePresenter.cs",
                "ProductionBranchPresenter.cs",
                "ProductionForkPresenter.cs",
                "ProductionCorePresenter.cs",
                "ProductionEndingPresenter.cs"
            };
            const string folder = "Assets/BEFORE/Production/Runtime/";
            foreach (string presenter in presenters)
            {
                string source = File.ReadAllText(folder + presenter);
                Assert.IsFalse(source.Contains("AddComponent<Collider"), presenter);
                Assert.IsFalse(source.Contains("AddComponent<Rigidbody"), presenter);
            }
        }
    }
}
