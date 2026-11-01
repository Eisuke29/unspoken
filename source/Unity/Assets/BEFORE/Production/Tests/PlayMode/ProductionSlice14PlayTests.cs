using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Before.Production.Tests
{
    public class ProductionSlice14PlayTests
    {
        [UnityTest]
        public IEnumerator FeedbackHudMirrorsJudgeResultWithoutOwningJudgement()
        {
            var root = new GameObject("slice14-feedback");
            root.SetActive(false);
            root.AddComponent<Rigidbody2D>().gravityScale = 0f;
            var stand = new GameObject("stand").AddComponent<BoxCollider2D>();
            stand.transform.SetParent(root.transform, false);
            var crouch = new GameObject("crouch").AddComponent<BoxCollider2D>();
            crouch.transform.SetParent(root.transform, false);
            crouch.enabled = false;

            var controller = root.AddComponent<NulProductionController>();
            controller.udpPort = 8888;
            controller.autoRun = false;
            SetPrivate(controller, "standingCollider", stand);
            SetPrivate(controller, "crouchCollider", crouch);
            var course = root.AddComponent<ProductionCourseLoop>();
            var judge = root.AddComponent<ProductionObstacleJudge>();
            var feedback = root.AddComponent<ProductionFeedbackHud>();
            feedback.Controller = controller;
            feedback.Judge = judge;
            root.SetActive(true);
            course.ResetCourse(0f);
            judge.ResetJudgement();
            yield return null;

            root.transform.position = new Vector3(ProductionCourseLoop.FirstObstacleX, 0f, 0f);
            judge.EvaluateNowAt(controller.GameplayTime);

            Assert.AreEqual(1, judge.FailureCount);
            Assert.IsTrue(feedback.Visible);
            Assert.AreEqual("REACTION MISS // Jump", feedback.LatestText);

            controller.Nul.Paused = true;
            yield return null;
            yield return new WaitForSeconds(ProductionObstacleJudge.FeedbackSeconds + 0.1f);
            Assert.IsTrue(feedback.Visible, "feedback lifetime must freeze with gameplay time");

            controller.Nul.Paused = false;
            yield return new WaitForSeconds(ProductionObstacleJudge.FeedbackSeconds + 0.1f);
            Assert.IsFalse(feedback.Visible);
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
