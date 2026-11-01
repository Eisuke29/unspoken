using System.Linq;
using NUnit.Framework;

namespace Before.Production.Tests
{
    public class ProductionSlice7Tests
    {
        [Test]
        public void DefaultCourse_HasExactlyTwentyEightValidBeats()
        {
            var beats = ProductionCourseDefinition.BuildProductionDefaults();
            Assert.AreEqual(28, beats.Length);
            CollectionAssert.IsEmpty(ProductionCourseDefinition.Validate(beats));
        }

        [Test]
        public void DefaultCourse_HasExactDemandSequence()
        {
            var beats = ProductionCourseDefinition.BuildProductionDefaults();
            CollectionAssert.AreEqual(new[] { ProductionObstacleKind.Jump }, beats[0].Demands);
            CollectionAssert.AreEqual(new[] { ProductionObstacleKind.Crouch }, beats[3].Demands);
            CollectionAssert.AreEqual(new[] { ProductionObstacleKind.Jump, ProductionObstacleKind.Crouch },
                                      beats[6].Demands);
            CollectionAssert.AreEqual(new[] { ProductionObstacleKind.Jump,
                                              ProductionObstacleKind.Crouch,
                                              ProductionObstacleKind.Stop }, beats[15].Demands);
            CollectionAssert.AreEqual(new[] { ProductionObstacleKind.Direction }, beats[21].Demands);
            CollectionAssert.AreEqual(new[] { ProductionObstacleKind.Crouch,
                                              ProductionObstacleKind.Stop,
                                              ProductionObstacleKind.Jump }, beats[26].Demands);
            Assert.IsEmpty(beats[27].Demands);
        }

        [Test]
        public void DefaultCourse_ContainsExpectedDemandCountsAndTeachingBeats()
        {
            var beats = ProductionCourseDefinition.BuildProductionDefaults();
            Assert.AreEqual(52, beats.Sum(b => b.Demands.Length));
            CollectionAssert.AreEqual(new[] { 1, 4, 13 },
                beats.Where(b => b.TeachingBeat).Select(b => b.Id).ToArray());
            Assert.AreEqual(120f, beats[27].SafeRunAfterBeat);
        }

        [Test]
        public void ValidationRejectsUnsafeStopSpacing()
        {
            var beats = ProductionCourseDefinition.BuildProductionDefaults();
            var bad = beats[15];
            bad.DemandSpacing = 47f;
            beats[15] = bad;
            CollectionAssert.Contains(ProductionCourseDefinition.Validate(beats),
                "beat 16 demand spacing is below 48");
        }
    }
}
