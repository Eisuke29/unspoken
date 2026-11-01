using NUnit.Framework;

namespace Before.Production.Tests
{
    public class ProductionSlice1Tests
    {
        [Test]
        public void CameraFollow_UsesLockedLogicalAnchor()
        {
            Assert.AreEqual(92f, ProductionCameraFollow.CameraXFor(0f), 0.0001f);
            Assert.AreEqual(212f, ProductionCameraFollow.CameraXFor(120f), 0.0001f);
        }

        [Test]
        public void Course_HasFourStableOrderedObstacleKinds()
        {
            var go = new UnityEngine.GameObject("course-test");
            try
            {
                var course = go.AddComponent<ProductionCourseLoop>();
                course.ResetCourse(10f);
                Assert.AreEqual(ProductionCourseLoop.ObstacleCount, course.Slots.Length);
                for (int i = 0; i < course.Slots.Length; i++)
                {
                    Assert.AreEqual(i + 1, course.Slots[i].StableId);
                    Assert.AreEqual((ProductionObstacleKind)i, course.Slots[i].Kind);
                    Assert.AreEqual(10f + ProductionCourseLoop.FirstObstacleX +
                                    i * ProductionCourseLoop.ObstacleSpacing,
                                    course.Slots[i].WorldX, 0.0001f);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Course_RecyclesBehindRunnerWithoutChangingStableId()
        {
            var go = new UnityEngine.GameObject("course-test");
            try
            {
                var course = go.AddComponent<ProductionCourseLoop>();
                course.ResetCourse(0f);
                course.RecyclePassedSlots(ProductionCourseLoop.FirstObstacleX +
                                          ProductionCourseLoop.RecycleBehindDistance + 1f);
                Assert.AreEqual(1, course.Slots[0].StableId);
                Assert.AreEqual(1, course.Slots[0].PassCount);
                Assert.AreEqual(ProductionCourseLoop.FirstObstacleX + ProductionCourseLoop.CycleLength,
                                course.Slots[0].WorldX, 0.0001f);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}
