using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Before.Production.Tests
{
    public class ProductionSlice1PlayTests
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
        public IEnumerator Camera_FollowsHorizontalProgressAtStableAnchor()
        {
            _runner = new GameObject("runner");
            _cameraObject = new GameObject("camera");
            var camera = _cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 90f;
            var follow = _cameraObject.AddComponent<ProductionCameraFollow>();
            follow.Target = _runner.transform;
            follow.FixedWorldY = 60f;

            _runner.transform.position = new Vector3(37f, 12f, 0f);
            yield return null;

            Assert.AreEqual(ProductionCameraFollow.CameraXFor(37f),
                            _cameraObject.transform.position.x, 0.001f);
            Assert.AreEqual(60f, _cameraObject.transform.position.y, 0.001f,
                            "jump height must not move the course camera vertically");
        }

        [UnityTest]
        public IEnumerator Course_RecyclesContinuouslyDuringPlay()
        {
            _runner = new GameObject("runner");
            var course = _runner.AddComponent<ProductionCourseLoop>();
            course.Runner = _runner.transform;
            yield return null;

            float original = course.Slots[0].WorldX;
            _runner.transform.position = new Vector3(
                original + ProductionCourseLoop.RecycleBehindDistance + 1f, 0f, 0f);
            yield return null;

            Assert.AreEqual(original + ProductionCourseLoop.CycleLength,
                            course.Slots[0].WorldX, 0.001f);
            Assert.AreEqual(1, course.Slots[0].PassCount);
        }
    }
}
