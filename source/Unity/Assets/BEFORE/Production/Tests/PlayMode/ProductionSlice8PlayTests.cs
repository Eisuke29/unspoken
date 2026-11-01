using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Before.Production.Tests
{
    public class ProductionSlice8PlayTests
    {
        [UnityTest]
        public IEnumerator FiftyTwoSlotCourseRecyclesAsOneContinuousLoop()
        {
            var definition = ScriptableObject.CreateInstance<ProductionCourseDefinition>();
            definition.LoadProductionDefaults();
            var runner = new GameObject("course-runner");
            var course = runner.AddComponent<ProductionCourseLoop>();
            course.Definition = definition;
            course.Runner = runner.transform;
            yield return null;

            Assert.AreEqual(52, course.Slots.Length);
            float first = course.Slots[0].WorldX;
            runner.transform.position = new Vector3(
                first + ProductionCourseLoop.RecycleBehindDistance + 1f, 0f, 0f);
            yield return null;

            Assert.AreEqual(first + course.ActiveCycleLength,
                            course.Slots[0].WorldX, 0.001f);
            Assert.AreEqual(1, course.Slots[0].PassCount);

            Object.Destroy(runner);
            Object.Destroy(definition);
        }
    }
}
