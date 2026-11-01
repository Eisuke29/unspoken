using NUnit.Framework;

namespace Before.Production.Tests
{
    public class ProductionSlice14Tests
    {
        [TestCase(true, ProductionObstacleKind.Jump, 0, "REACTION OK // Jump")]
        [TestCase(false, ProductionObstacleKind.Crouch, 0, "REACTION MISS // Crouch")]
        [TestCase(false, ProductionObstacleKind.Direction, 0, "REACTION MISS // Direction")]
        [TestCase(true, ProductionObstacleKind.Direction, -1, "REACTION OK // Direction LEFT")]
        [TestCase(true, ProductionObstacleKind.Direction, 1, "REACTION OK // Direction RIGHT")]
        public void FeedbackTextContainsResultButNoCommandOrigin(
            bool success, ProductionObstacleKind kind, int direction, string expected)
        {
            var result = new ProductionObstacleJudge.ObstacleResult
            {
                Success = success,
                Kind = kind,
                Direction = direction
            };
            string text = ProductionFeedbackHud.FormatResult(result);
            Assert.AreEqual(expected, text);
            StringAssert.DoesNotContain("HUMAN", text);
            StringAssert.DoesNotContain("AI", text);
        }
    }
}
