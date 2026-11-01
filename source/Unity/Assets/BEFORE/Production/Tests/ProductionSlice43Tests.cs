using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Before.Production.Tests
{
    public class ProductionSlice43Tests
    {
        private const string ScenePath = "Assets/BEFORE/Scenes/BEFORE_ProductionV2.unity";

        [Test]
        public void CameraSnapRoundsOnlyTheAppliedLogicalPosition()
        {
            Assert.AreEqual(92.49f, ProductionCameraFollow.CameraXFor(0.49f),
                0.0001f, "the ideal anchor calculation remains continuous");
            Assert.AreEqual(92f,
                ProductionCameraFollow.PixelSnappedCameraXFor(0.49f), 0.0001f);
            Assert.AreEqual(93f,
                ProductionCameraFollow.PixelSnappedCameraXFor(0.51f), 0.0001f);
            Assert.AreEqual(92f,
                ProductionCameraFollow.PixelSnappedCameraXFor(0f), 0.0001f);
        }

        [Test]
        public void SavedProductionCameraEnablesPixelSnap()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            GameObject[] roots = scene.GetRootGameObjects();
            Assert.AreEqual(1, roots.Length);
            var follow = roots[0].GetComponentInChildren<ProductionCameraFollow>(true);
            Assert.IsNotNull(follow);
            Assert.IsTrue(follow.PixelSnapEnabled);
        }
    }
}
