using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Before.Production.Tests
{
    public class ProductionSlice44Tests
    {
        private const string ScenePath = "Assets/BEFORE/Scenes/BEFORE_ProductionV2.unity";

        [Test]
        public void LogicalSurfaceConstantsMatchTheProductionWorld()
        {
            Assert.AreEqual(320, ProductionLogicalRenderPresenter.LogicalWidth);
            Assert.AreEqual(180, ProductionLogicalRenderPresenter.LogicalHeight);
            Assert.AreEqual(ProductionCameraFollow.LogicalWidth,
                ProductionLogicalRenderPresenter.LogicalWidth);
            Assert.AreEqual(ProductionCameraFollow.LogicalHeight,
                ProductionLogicalRenderPresenter.LogicalHeight);
        }

        [Test]
        public void ViewportUsesFullTargetWhenCameraRendersOffscreen()
        {
            var go = new GameObject("slice44-edit-camera");
            var camera = go.AddComponent<Camera>();
            var viewport = go.AddComponent<ProductionViewportPresenter>();
            var target = new RenderTexture(320, 180, 0);
            try
            {
                camera.targetTexture = target;
                viewport.ApplyNow(1024, 768);
                Assert.AreEqual(new Rect(0f, 0f, 1f, 1f), camera.rect);

                camera.targetTexture = null;
                viewport.ApplyNow(1024, 768);
                Rect expected = new Rect(0.03125f, 0.1484375f, 0.9375f, 0.703125f);
                Assert.AreEqual(expected, camera.rect);
            }
            finally
            {
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void SavedProductionCameraOwnsOneEnabledLogicalSurfacePresenter()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            GameObject[] roots = scene.GetRootGameObjects();
            Assert.AreEqual(1, roots.Length);
            var cameras = roots[0].GetComponentsInChildren<Camera>(true);
            Assert.AreEqual(1, cameras.Length);
            var presenters = cameras[0]
                .GetComponents<ProductionLogicalRenderPresenter>();
            Assert.AreEqual(1, presenters.Length);
            Assert.IsTrue(presenters[0].RenderToLogicalSurface);
        }
    }
}
