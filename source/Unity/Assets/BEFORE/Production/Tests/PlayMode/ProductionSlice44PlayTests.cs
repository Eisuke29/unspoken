using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Before.Production.Tests
{
    public class ProductionSlice44PlayTests
    {
        private GameObject _cameraObject;

        [TearDown]
        public void TearDown()
        {
            if (_cameraObject != null) Object.Destroy(_cameraObject);
        }

        [UnityTest]
        public IEnumerator LogicalSurfaceIsPointFilteredAndRestoresCameraOnDisable()
        {
            _cameraObject = new GameObject("slice44-play-camera");
            var camera = _cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.rect = new Rect(0.1f, 0.2f, 0.7f, 0.6f);
            var presenter = _cameraObject
                .AddComponent<ProductionLogicalRenderPresenter>();

            yield return null;

            RenderTexture surface = presenter.LogicalSurface;
            Assert.IsNotNull(surface);
            Assert.IsTrue(surface.IsCreated());
            Assert.AreEqual(320, surface.width);
            Assert.AreEqual(180, surface.height);
            Assert.AreEqual(FilterMode.Point, surface.filterMode);
            Assert.AreEqual(TextureWrapMode.Clamp, surface.wrapMode);
            Assert.AreEqual(1, surface.antiAliasing);
            Assert.Greater(surface.depth, 0,
                "Unity 6 URP Camera outputs require a depth/stencil attachment");
            Assert.IsFalse(surface.useMipMap);
            Assert.IsNull(camera.targetTexture,
                "the sole Camera must return to Display 1 after its logical render");
            Assert.AreEqual(new Rect(0.1f, 0.2f, 0.7f, 0.6f), camera.rect);
            Assert.IsTrue(presenter.DisplayOutputRestored);
            Assert.Greater(presenter.LogicalRenderCount, 0);

            presenter.enabled = false;
            Assert.IsNull(camera.targetTexture);
            Assert.AreEqual(new Rect(0.1f, 0.2f, 0.7f, 0.6f), camera.rect);
        }
    }
}
