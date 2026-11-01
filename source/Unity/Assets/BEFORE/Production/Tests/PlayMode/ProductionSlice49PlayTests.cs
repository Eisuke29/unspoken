using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Before.Production.Tests
{
    public class ProductionSlice49PlayTests
    {
        private GameObject _root;
        private GameObject _cameraObject;
        private ProductionBackgroundPresenter _presenter;
        private Texture2D _texture;
        private Sprite _sprite;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _root = new GameObject("slice49-background");
            _root.SetActive(false);
            _cameraObject = new GameObject("world-camera");
            _cameraObject.transform.SetParent(_root.transform, false);
            Camera camera = _cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            _texture = new Texture2D(640, 180, TextureFormat.RGBA32, false);
            _sprite = Sprite.Create(_texture, new Rect(0, 0, 640, 180),
                Vector2.zero, 1f);
            _presenter = _root.AddComponent<ProductionBackgroundPresenter>();
            _presenter.WorldCamera = camera;
            _presenter.FarSprite = _sprite;
            _presenter.MidSprite = _sprite;
            _presenter.NearSprite = _sprite;
            _root.SetActive(true);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Object.Destroy(_root);
            Object.Destroy(_sprite);
            Object.Destroy(_texture);
            yield return null;
        }

        [UnityTest]
        public IEnumerator CreatesThreeContinuousLayersWithRequiredOrdering()
        {
            Assert.AreEqual(6, _presenter.VisualRendererCount);
            Assert.AreEqual(-20, _presenter.RendererSortingOrder(0, 1));
            Assert.AreEqual(-10, _presenter.RendererSortingOrder(1, 1));
            for (int layer = 0; layer < 2; layer++)
            {
                Assert.AreEqual(640f, _presenter.RendererPosition(layer, 1).x -
                    _presenter.RendererPosition(layer, 0).x);
                Assert.AreEqual(640f, _presenter.RendererPosition(layer, 2).x -
                    _presenter.RendererPosition(layer, 1).x);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator CameraTravelProducesDistinctPixelSnappedParallax()
        {
            _cameraObject.transform.position = new Vector3(100f, 60f, -10f);
            _presenter.RefreshNow();
            Assert.AreEqual(-75f, _presenter.RendererPosition(0, 1).x);
            Assert.AreEqual(-95f, _presenter.RendererPosition(1, 1).x);
            for (int layer = 0; layer < 2; layer++)
                Assert.AreEqual(0f,
                    _presenter.RendererPosition(layer, 1).x % 1f);
            yield return null;
        }
    }
}
