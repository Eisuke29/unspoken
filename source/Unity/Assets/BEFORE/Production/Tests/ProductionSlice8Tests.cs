using NUnit.Framework;
using UnityEngine;

namespace Before.Production.Tests
{
    public class ProductionSlice8Tests
    {
        [Test]
        public void DefinitionFlattensToFiftyTwoStableDemandSlots()
        {
            var definition = ScriptableObject.CreateInstance<ProductionCourseDefinition>();
            definition.LoadProductionDefaults();
            var go = new GameObject("course-definition-test");
            try
            {
                var course = go.AddComponent<ProductionCourseLoop>();
                course.Definition = definition;
                course.ResetCourse(0f);

                Assert.AreEqual(52, course.Slots.Length);
                for (int i = 0; i < course.Slots.Length; i++)
                    Assert.AreEqual(i + 1, course.Slots[i].StableId);
                Assert.AreEqual(1, course.Slots[0].BeatId);
                Assert.AreEqual(ProductionObstacleKind.Jump, course.Slots[0].Kind);
                Assert.AreEqual(7, course.Slots[6].BeatId);
                Assert.AreEqual(0, course.Slots[6].DemandIndex);
                Assert.AreEqual(ProductionObstacleKind.Jump, course.Slots[6].Kind);
                Assert.AreEqual(7, course.Slots[7].BeatId);
                Assert.AreEqual(1, course.Slots[7].DemandIndex);
                Assert.AreEqual(ProductionObstacleKind.Crouch, course.Slots[7].Kind);
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(definition);
            }
        }

        [Test]
        public void DefinitionLayoutUsesLockedEarlyAndCompoundSpacing()
        {
            var definition = ScriptableObject.CreateInstance<ProductionCourseDefinition>();
            definition.LoadProductionDefaults();
            var go = new GameObject("course-definition-test");
            try
            {
                var course = go.AddComponent<ProductionCourseLoop>();
                course.Definition = definition;
                course.ResetCourse(0f);

                Assert.AreEqual(140f, course.Slots[0].WorldX, 0.001f);
                Assert.AreEqual(356f, course.Slots[1].WorldX, 0.001f);
                Assert.AreEqual(1436f, course.Slots[6].WorldX, 0.001f);
                Assert.AreEqual(1556f, course.Slots[7].WorldX, 0.001f);
                Assert.Greater(course.ActiveCycleLength, course.Slots[51].WorldX);
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(definition);
            }
        }
    }
}
