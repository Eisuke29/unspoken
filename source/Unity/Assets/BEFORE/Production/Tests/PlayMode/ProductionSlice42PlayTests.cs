using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Before.Production.Tests
{
    public class ProductionSlice42PlayTests
    {
        private GameObject _cameraObject;

        [TearDown]
        public void TearDown()
        {
            if (_cameraObject != null) Object.Destroy(_cameraObject);
        }

        [UnityTest]
        public IEnumerator ViewportAppliesAspectFitAndRestoresCameraRectWhenDisabled()
        {
            _cameraObject = new GameObject("slice42-viewport-camera");
            _cameraObject.SetActive(false);
            var camera = _cameraObject.AddComponent<Camera>();
            var viewport = _cameraObject.AddComponent<ProductionViewportPresenter>();
            _cameraObject.SetActive(true);
            yield return null;

            viewport.ApplyNow(1024, 768);
            AssertRect(new Rect(0.03125f, 0.1484375f, 0.9375f, 0.703125f),
                camera.rect);

            viewport.ApplyNow(2560, 1080);
            AssertRect(new Rect(0.125f, 0f, 0.75f, 1f), camera.rect);

            viewport.enabled = false;
            AssertRect(new Rect(0f, 0f, 1f, 1f), camera.rect);
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
