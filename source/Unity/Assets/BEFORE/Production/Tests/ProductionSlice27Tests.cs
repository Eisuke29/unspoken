using NUnit.Framework;
using UnityEngine;

namespace Before.Production.Tests
{
    public class ProductionSlice27Tests
    {
        [Test]
        public void RouteProfilesAreDistinctAndKeepSafeObstacleSpacing()
        {
            Assert.AreEqual(12, ProductionBranchFlow.BranchDemandCount);
            Assert.GreaterOrEqual(ProductionBranchFlow.LeftDemandSpacing, 48f);
            Assert.GreaterOrEqual(ProductionBranchFlow.RightDemandSpacing, 48f);
            Assert.GreaterOrEqual(ProductionBranchFlow.LeftBeatSpacing, 72f);
            Assert.GreaterOrEqual(ProductionBranchFlow.RightBeatSpacing, 72f);
            Assert.AreNotEqual(ProductionBranchFlow.LeftDemandSpacing,
                ProductionBranchFlow.RightDemandSpacing);

            int differences = 0;
            for (int i = 0; i < ProductionBranchFlow.BranchDemandCount; i++)
            {
                if (ProductionBranchFlow.KindAt(ProductionRoute.Left, i) !=
                    ProductionBranchFlow.KindAt(ProductionRoute.Right, i)) differences++;
            }
            Assert.Greater(differences, 0);
        }

        [Test]
        public void BranchPresenterPalettesAreRouteSpecificAndTransparentWhenInactive()
        {
            Assert.AreNotEqual(ProductionBranchPresenter.PaletteFor(ProductionRoute.Left),
                ProductionBranchPresenter.PaletteFor(ProductionRoute.Right));
            Assert.AreEqual(Color.clear,
                ProductionBranchPresenter.PaletteFor(ProductionRoute.None));
            Assert.AreEqual(6, ProductionBranchPresenter.TemporaryRendererCount);
        }

        [TestCase(ProductionRoute.Left)]
        [TestCase(ProductionRoute.Right)]
        public void ApplyingRoutePreservesStableIdentityAndChangesOnlyBranchLayout(
            ProductionRoute route)
        {
            var go = new GameObject("slice27-edit-rig");
            var definition = ScriptableObject.CreateInstance<ProductionCourseDefinition>();
            try
            {
                var course = go.AddComponent<ProductionCourseLoop>();
                definition.LoadProductionDefaults();
                course.Definition = definition;
                var flow = go.AddComponent<ProductionBranchFlow>();
                flow.Course = course;

                ProductionCourseLoop.ObstacleSlot[] before =
                    (ProductionCourseLoop.ObstacleSlot[])course.Slots.Clone();
                Assert.IsTrue(flow.ApplyRoute(route));
                Assert.AreEqual(route, flow.ActiveRoute);
                Assert.Greater(flow.CoreApproachWorldX, flow.BranchStartWorldX);

                int branchIndex = 0;
                for (int i = 0; i < course.Slots.Length; i++)
                {
                    Assert.AreEqual(before[i].StableId, course.Slots[i].StableId);
                    Assert.AreEqual(before[i].PassCount, course.Slots[i].PassCount);
                    if (course.Slots[i].BeatId < ProductionBranchFlow.FirstBranchBeatId ||
                        course.Slots[i].BeatId > ProductionBranchFlow.LastBranchBeatId)
                    {
                        Assert.AreEqual(before[i].Kind, course.Slots[i].Kind);
                        Assert.AreEqual(before[i].WorldX, course.Slots[i].WorldX, 0.001f);
                        continue;
                    }
                    Assert.AreEqual(ProductionBranchFlow.KindAt(route, branchIndex),
                        course.Slots[i].Kind);
                    branchIndex++;
                }
                Assert.AreEqual(ProductionBranchFlow.BranchDemandCount, branchIndex);
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(definition);
            }
        }
    }
}
