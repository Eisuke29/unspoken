using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Before.Production.Tests
{
    public class ProductionSlice21PlayTests
    {
        private GameObject _root;

        private static void SetPrivate(object target, string field, object value)
        {
            FieldInfo info = target.GetType().GetField(field,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(info, field);
            info.SetValue(target, value);
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.Destroy(_root);
        }

        [UnityTest]
        public IEnumerator CoLocatedProductionComponentsSelfBindOnceAtRuntime()
        {
            _root = new GameObject("slice21-production-contract");
            _root.SetActive(false);
            _root.AddComponent<Rigidbody2D>().gravityScale = 0f;
            var stand = new GameObject("stand").AddComponent<BoxCollider2D>();
            stand.transform.SetParent(_root.transform, false);
            var crouch = new GameObject("crouch").AddComponent<BoxCollider2D>();
            crouch.transform.SetParent(_root.transform, false);
            crouch.enabled = false;

            var cameraObject = new GameObject("camera");
            cameraObject.transform.SetParent(_root.transform, false);
            cameraObject.AddComponent<Camera>().orthographic = true;
            cameraObject.AddComponent<AudioListener>();
            var follow = cameraObject.AddComponent<ProductionCameraFollow>();

            var controller = _root.AddComponent<NulProductionController>();
            controller.udpPort = 8895;
            controller.autoRun = false;
            SetPrivate(controller, "standingCollider", stand);
            SetPrivate(controller, "crouchCollider", crouch);
            var course = _root.AddComponent<ProductionCourseLoop>();
            var judge = _root.AddComponent<ProductionObstacleJudge>();
            var loop = _root.AddComponent<ProductionGameLoop>();
            var teaching = _root.AddComponent<ProductionTeachingFlow>();
            var teachingPresenter = _root.AddComponent<ProductionTeachingPresenter>();
            var fork = _root.AddComponent<ProductionForkFlow>();
            var forkPresenter = _root.AddComponent<ProductionForkPresenter>();
            var hud = _root.AddComponent<ProductionHud>();
            var diagnostics = _root.AddComponent<ProductionDiagnosticsHud>();
            var feedback = _root.AddComponent<ProductionFeedbackHud>();
            var presenter = _root.AddComponent<ProductionCoursePresenter>();
            var audio = _root.AddComponent<ProductionAudioFeedback>();
            _root.AddComponent<ProductionPerformanceMonitor>();

            _root.SetActive(true);
            yield return null;
            yield return null;

            Assert.AreSame(_root.transform, course.Runner);
            Assert.AreSame(controller, judge.Controller);
            Assert.AreSame(course, judge.Course);
            Assert.AreSame(controller, loop.Controller);
            Assert.AreSame(judge, loop.Judge);
            Assert.AreSame(course, loop.Course);
            Assert.AreSame(teaching, loop.Teaching);
            Assert.AreSame(controller, teaching.Controller);
            Assert.AreSame(course, teaching.Course);
            Assert.AreSame(judge, teaching.Judge);
            Assert.AreSame(loop, teaching.GameLoop);
            Assert.AreSame(teaching, teachingPresenter.Flow);
            Assert.AreSame(controller, fork.Controller);
            Assert.AreSame(course, fork.Course);
            Assert.AreSame(judge, fork.Judge);
            Assert.AreSame(loop, fork.GameLoop);
            Assert.AreSame(fork, forkPresenter.Flow);
            Assert.AreSame(loop, hud.GameLoop);
            Assert.AreSame(controller, diagnostics.Controller);
            Assert.AreSame(controller, feedback.Controller);
            Assert.AreSame(judge, feedback.Judge);
            Assert.AreSame(controller, presenter.Controller);
            Assert.AreSame(course, presenter.Course);
            Assert.AreSame(controller, audio.Controller);
            Assert.AreSame(judge, audio.Judge);
            Assert.AreSame(loop, audio.GameLoop);
            Assert.AreSame(_root.transform, follow.Target);
            Assert.AreEqual(7, audio.CachedCueCount);
            Assert.AreEqual(1, _root.GetComponents<UdpCommandReceiver>().Length);
            Assert.AreEqual(1, _root.GetComponents<SidecarProcessManager>().Length);
            Assert.IsTrue(controller.ReceiverListening);
        }
    }
}
