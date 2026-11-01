using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Before.Production.Tests
{
    public class ProductionSlice21Tests
    {
        private const string ScenePath = "Assets/BEFORE/Scenes/BEFORE_ProductionV2.unity";

        [Test]
        public void ProductionSceneKeepsTheVerifiedResponsibilityContract()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            GameObject[] roots = scene.GetRootGameObjects();
            Assert.AreEqual(1, roots.Length);
            GameObject root = roots[0];
            Assert.AreEqual("NUL_Production", root.name);

            Assert.IsNotNull(root.GetComponent<Rigidbody2D>());
            Assert.IsNotNull(root.GetComponent<NulProductionController>());
            var course = root.GetComponent<ProductionCourseLoop>();
            Assert.IsNotNull(course);
            Assert.IsNotNull(root.GetComponent<ProductionObstacleJudge>());
            Assert.IsNotNull(root.GetComponent<ProductionGameLoop>());
            Assert.IsNotNull(root.GetComponent<ProductionTeachingFlow>());
            Assert.IsNotNull(root.GetComponent<ProductionTeachingPresenter>());
            Assert.IsNotNull(root.GetComponent<ProductionForkFlow>());
            Assert.IsNotNull(root.GetComponent<ProductionForkPresenter>());
            Assert.IsNotNull(root.GetComponent<ProductionBranchFlow>());
            var branchPresenter = root.GetComponent<ProductionBranchPresenter>();
            Assert.IsNotNull(branchPresenter);
            Assert.IsTrue(branchPresenter.DrawTemporaryBranch);
            Assert.IsNotNull(root.GetComponent<ProductionCoreFlow>());
            var corePresenter = root.GetComponent<ProductionCorePresenter>();
            Assert.IsNotNull(corePresenter);
            Assert.IsTrue(corePresenter.DrawTemporaryCore);
            Assert.IsNotNull(root.GetComponent<ProductionEndingFlow>());
            var endingPresenter = root.GetComponent<ProductionEndingPresenter>();
            Assert.IsNotNull(endingPresenter);
            Assert.IsTrue(endingPresenter.DrawTemporaryEnding);
            Assert.IsNotNull(root.GetComponent<ProductionHud>());
            var diagnostics = root.GetComponent<ProductionDiagnosticsHud>();
            Assert.IsNotNull(diagnostics);
            Assert.IsFalse(diagnostics.DetailsVisible);
            Assert.IsNotNull(root.GetComponent<ProductionFeedbackHud>());
            var presenter = root.GetComponent<ProductionCoursePresenter>();
            Assert.IsNotNull(presenter);
            Assert.IsTrue(presenter.DrawTemporaryCourse);
            Assert.IsNotNull(root.GetComponent<ProductionAudioFeedback>());
            var performance = root.GetComponent<ProductionPerformanceMonitor>();
            Assert.IsNotNull(performance);
            Assert.AreEqual(ProductionPerformanceMonitor.DefaultWarmupSeconds,
                performance.WarmupSeconds, 0.001f);

            Assert.IsNotNull(course.Definition);
            Assert.AreEqual(ProductionCourseDefinition.RequiredBeatCount,
                course.Definition.Beats.Count);
            Assert.AreEqual(1, root.GetComponentsInChildren<Camera>(true).Length);
            Assert.AreEqual(1, root.GetComponentsInChildren<AudioListener>(true).Length);
            var follow = root.GetComponentInChildren<ProductionCameraFollow>(true);
            Assert.IsNotNull(follow);
        }
    }
}
