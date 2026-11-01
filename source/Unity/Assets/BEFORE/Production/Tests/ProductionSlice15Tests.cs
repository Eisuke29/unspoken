using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Before.Production.Tests
{
    public class ProductionSlice15Tests
    {
        [Test]
        public void CourseStateOwnsNoRendererFields()
        {
            FieldInfo[] fields = typeof(ProductionCourseLoop).GetFields(
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.IsFalse(fields.Any(field =>
                typeof(Renderer).IsAssignableFrom(field.FieldType) ||
                field.FieldType == typeof(Sprite)),
                "course state must not own temporary rendering objects");
        }

        [Test]
        public void PresenterMapsRouteWithoutChangingCourseState()
        {
            Color neutral = ProductionCoursePresenter.GroundColorFor(ProductionRoute.None);
            Color left = ProductionCoursePresenter.GroundColorFor(ProductionRoute.Left);
            Color right = ProductionCoursePresenter.GroundColorFor(ProductionRoute.Right);
            Assert.AreNotEqual(neutral, left);
            Assert.AreNotEqual(neutral, right);
            Assert.AreNotEqual(left, right);
        }

        [Test]
        public void PresenterMapsOpenWallToSameHueWithLowerOpacity()
        {
            Color closed = ProductionCoursePresenter.ObstacleColorFor(
                ProductionObstacleKind.Stop, false);
            Color open = ProductionCoursePresenter.ObstacleColorFor(
                ProductionObstacleKind.Stop, true);
            Assert.Less(open.a, closed.a);
            Assert.AreNotEqual(open, closed);
        }
    }
}
