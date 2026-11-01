using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Before.Production.Tests
{
    public class ProductionSlice15PlayTests
    {
        [UnityTest]
        public IEnumerator PresenterMirrorsCourseSlotsRouteAndPauseAwareWallPhase()
        {
            var root = new GameObject("slice15-course-presenter");
            root.SetActive(false);
            root.AddComponent<Rigidbody2D>().gravityScale = 0f;
            var controller = root.AddComponent<NulProductionController>();
            controller.udpPort = 8887;
            controller.autoRun = false;
            var course = root.AddComponent<ProductionCourseLoop>();
            var presenter = root.AddComponent<ProductionCoursePresenter>();
            presenter.Controller = controller;
            presenter.Course = course;
            root.SetActive(true);
            yield return null;

            Assert.AreEqual(course.Slots.Length, presenter.ObstacleRendererCount);
            Assert.AreEqual(ProductionCoursePresenter.GroundColorFor(ProductionRoute.None),
                presenter.GroundColor);

            course.SetSelectedRoute(-1);
            presenter.RefreshNow();
            Assert.AreEqual(ProductionCoursePresenter.GroundColorFor(ProductionRoute.Left),
                presenter.GroundColor);

            int stopIndex = System.Array.FindIndex(course.Slots,
                slot => slot.Kind == ProductionObstacleKind.Stop);
            Assert.GreaterOrEqual(stopIndex, 0);
            SetPrivate(controller, "_gameplayTime", 2.9d);
            presenter.RefreshNow();
            Color closed = presenter.ObstacleColorAt(stopIndex);

            controller.Nul.Paused = true;
            yield return new WaitForSeconds(0.25f);
            presenter.RefreshNow();
            Assert.AreEqual(2.9f, controller.GameplayTime, 0.03f);
            Assert.AreEqual(closed, presenter.ObstacleColorAt(stopIndex),
                "presenter must use pause-aware gameplay time");

            controller.Nul.Paused = false;
            yield return new WaitForSeconds(0.2f);
            presenter.RefreshNow();
            Assert.Less(presenter.ObstacleColorAt(stopIndex).a, closed.a,
                "wall presentation should enter open phase after gameplay resumes");

            Object.Destroy(root);
        }

        private static void SetPrivate(object target, string field, object value)
        {
            FieldInfo info = target.GetType().GetField(field,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(info, field);
            info.SetValue(target, value);
        }
    }
}
