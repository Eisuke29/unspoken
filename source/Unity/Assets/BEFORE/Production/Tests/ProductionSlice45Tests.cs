using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Before.Production.Tests
{
    public class ProductionSlice45Tests
    {
        private const string ScenePath = "Assets/BEFORE/Scenes/BEFORE_ProductionV2.unity";

        [Test]
        public void ImpulseEnvelopeUsesTheSpecifiedAmplitudesAndDurations()
        {
            Vector2 takeoff = ProductionCameraImpulsePresenter.OffsetAt(
                ProductionCameraImpulsePresenter.ImpulseKind.Takeoff,
                ProductionCameraImpulsePresenter.TakeoffSeconds * 0.125f);
            Vector2 landing = ProductionCameraImpulsePresenter.OffsetAt(
                ProductionCameraImpulsePresenter.ImpulseKind.Landing,
                ProductionCameraImpulsePresenter.LandingSeconds * 0.125f);
            Vector2 failure = ProductionCameraImpulsePresenter.OffsetAt(
                ProductionCameraImpulsePresenter.ImpulseKind.Failure,
                ProductionCameraImpulsePresenter.FailureSeconds * 0.125f);

            Assert.AreEqual(new Vector2(0f, 1f), takeoff);
            Assert.AreEqual(new Vector2(0f, 1.5f), landing);
            Assert.AreEqual(new Vector2(3f, -3f), failure);
            Assert.AreEqual(Vector2.zero,
                ProductionCameraImpulsePresenter.OffsetAt(
                    ProductionCameraImpulsePresenter.ImpulseKind.Takeoff,
                    ProductionCameraImpulsePresenter.TakeoffSeconds));
            Assert.AreEqual(Vector2.zero,
                ProductionCameraImpulsePresenter.OffsetAt(
                    ProductionCameraImpulsePresenter.ImpulseKind.Landing,
                    ProductionCameraImpulsePresenter.LandingSeconds));
            Assert.AreEqual(Vector2.zero,
                ProductionCameraImpulsePresenter.OffsetAt(
                    ProductionCameraImpulsePresenter.ImpulseKind.Failure,
                    ProductionCameraImpulsePresenter.FailureSeconds));
        }

        [Test]
        public void LogicalImpulseBecomesWholePhysicalPixelsAtIntegerScale()
        {
            Assert.AreEqual(new Vector2(0f, 9f),
                ProductionLogicalRenderPresenter.PhysicalPixelOffset(
                    new Vector2(0f, 1.5f), new Rect(0f, 0f, 1920f, 1080f)));
            Assert.AreEqual(new Vector2(9f, -9f),
                ProductionLogicalRenderPresenter.PhysicalPixelOffset(
                    new Vector2(3f, -3f), new Rect(32f, 114f, 960f, 540f)));
        }

        [Test]
        public void SavedProductionCameraOwnsOneEnabledImpulsePresenter()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            GameObject[] roots = scene.GetRootGameObjects();
            Assert.AreEqual(1, roots.Length);
            var cameras = roots[0].GetComponentsInChildren<Camera>(true);
            Assert.AreEqual(1, cameras.Length);
            var presenters = cameras[0]
                .GetComponents<ProductionCameraImpulsePresenter>();
            Assert.AreEqual(1, presenters.Length);
            Assert.IsTrue(presenters[0].ShakeEnabled);
            Assert.IsNotNull(cameras[0]
                .GetComponent<ProductionLogicalRenderPresenter>());
        }
    }
}
