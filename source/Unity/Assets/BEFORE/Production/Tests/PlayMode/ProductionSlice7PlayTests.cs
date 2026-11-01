using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Before.Production.Tests
{
    public class ProductionSlice7PlayTests
    {
        [UnityTest]
        public IEnumerator RuntimeDefinitionBuildsTheValidatedTwentyEightBeatCourse()
        {
            var definition = ScriptableObject.CreateInstance<ProductionCourseDefinition>();
            definition.LoadProductionDefaults();
            yield return null;

            Assert.AreEqual(ProductionCourseDefinition.RequiredBeatCount, definition.Beats.Count);
            CollectionAssert.IsEmpty(ProductionCourseDefinition.Validate(definition.Beats));
            Object.Destroy(definition);
        }
    }
}
