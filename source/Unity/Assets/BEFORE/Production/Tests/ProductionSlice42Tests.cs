using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Before.Production.Tests
{
    public class ProductionSlice42Tests
    {
        private const string ScenePath = "Assets/BEFORE/Scenes/BEFORE_ProductionV2.unity";

        [Test]
        public void ViewportFitsExactWideAndTallWindowsWithoutStretching()
        {
            AssertRect(new Rect(0f, 0f, 1f, 1f),
                ProductionViewportPresenter.NormalizedCameraRect(1920, 1080));
            AssertRect(new Rect(0.125f, 0f, 0.75f, 1f),
                ProductionViewportPresenter.NormalizedCameraRect(2560, 1080));
            AssertRect(new Rect(0.03125f, 0.1484375f, 0.9375f, 0.703125f),
                ProductionViewportPresenter.NormalizedCameraRect(1024, 768));
        }

        [Test]
        public void ContentPixelRectUsesTopLeftGuiCoordinates()
        {
            AssertRect(new Rect(320f, 0f, 1920f, 1080f),
                ProductionViewportPresenter.ContentPixelRect(2560, 1080));
            AssertRect(new Rect(32f, 114f, 960f, 540f),
                ProductionViewportPresenter.ContentPixelRect(1024, 768));
        }

        [Test]
        public void SavedProductionCameraOwnsExactlyOneViewportPresenter()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            GameObject[] roots = scene.GetRootGameObjects();
            Assert.AreEqual(1, roots.Length);
            var cameras = roots[0].GetComponentsInChildren<Camera>(true);
            Assert.AreEqual(1, cameras.Length);
            Assert.AreEqual(1,
                cameras[0].GetComponents<ProductionViewportPresenter>().Length);
            Assert.IsTrue(cameras[0].GetComponent<ProductionViewportPresenter>()
                .DrawBlackBars);
        }

        private static void AssertRect(Rect expected, Rect actual)
        {
            Assert.AreEqual(expected.x, actual.x, 0.0001f);
            Assert.AreEqual(expected.y, actual.y, 0.0001f);
            Assert.AreEqual(expected.width, actual.width, 0.0001f);
            Assert.AreEqual(expected.height, actual.height, 0.0001f);
        }
    }
}
