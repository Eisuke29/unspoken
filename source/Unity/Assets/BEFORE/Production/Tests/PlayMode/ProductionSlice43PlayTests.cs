using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Before.Production.Tests
{
    public class ProductionSlice43PlayTests
    {
        private GameObject _runner;
        private GameObject _cameraObject;

        [TearDown]
        public void TearDown()
        {
            if (_cameraObject != null) Object.Destroy(_cameraObject);
            if (_runner != null) Object.Destroy(_runner);
        }

        [UnityTest]
        public IEnumerator FractionalRunnerMotionProducesWholeLogicalCameraSteps()
        {
            _runner = new GameObject("slice43-runner");
            _cameraObject = new GameObject("slice43-camera");
            _cameraObject.AddComponent<Camera>().orthographic = true;
            var follow = _cameraObject.AddComponent<ProductionCameraFollow>();
            follow.Target = _runner.transform;
            follow.FixedWorldY = 60.4f;
            follow.PixelSnapEnabled = true;

            _runner.transform.position = new Vector3(0.49f, 12f, 0f);
            yield return null;
            Assert.AreEqual(92f, _cameraObject.transform.position.x, 0.0001f);
            Assert.AreEqual(60f, _cameraObject.transform.position.y, 0.0001f);

            _runner.transform.position = new Vector3(0.51f, 20f, 0f);
            yield return null;
            Assert.AreEqual(93f, _cameraObject.transform.position.x, 0.0001f);
            Assert.AreEqual(60f, _cameraObject.transform.position.y, 0.0001f,
                "runner jump height never changes the horizontal course camera");

            follow.PixelSnapEnabled = false;
            _runner.transform.position = new Vector3(0.25f, 0f, 0f);
            yield return null;
            Assert.AreEqual(92.25f, _cameraObject.transform.position.x, 0.0001f,
                "the Presenter can disable snapping without changing anchor math");
            Assert.AreEqual(60.4f, _cameraObject.transform.position.y, 0.0001f);
        }
    }
}
