using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Before.Production.Tests
{
    public class ProductionSlice47Tests
    {
        private const string ScenePath =
            "Assets/BEFORE/Scenes/BEFORE_ProductionV2.unity";
        private const string HeartPath =
            "Assets/BEFORE/Production/Art/UI/HUD_HeartFull_8x7.png";

        [TestCase(1920f, 1080f)]
        [TestCase(1280f, 720f)]
        [TestCase(2560f, 1080f)]
        [TestCase(1024f, 768f)]
        public void FinalHudUsesOneLogicalCanvasWithoutOverlapAtSupportedViews(
            float width, float height)
        {
            Rect content = ProductionHudLayout.ContentRect(width, height);
            Rect hearts = ProductionHud.HeartRowBounds(width, height);
            Rect hand = ProductionDiagnosticsHud.HandPanelRect(width, height);
            Rect teaching = ProductionTeachingPresenter.TeachingPanelRect(width, height);
            Rect fork = ProductionForkPresenter.TargetWindowRect(width, height);
            Rect feedback = ProductionFeedbackHud.FeedbackBounds(width, height);

            AssertContains(content, hearts, "hearts");
            AssertContains(content, hand, "hand");
            AssertContains(content, teaching, "teaching");
            AssertContains(content, fork, "fork");
            AssertContains(content, feedback, "feedback");
            Assert.IsFalse(hearts.Overlaps(hand));
            Assert.IsFalse(hearts.Overlaps(teaching));
            Assert.IsFalse(hearts.Overlaps(fork));
            Assert.IsFalse(teaching.Overlaps(hand));
            Assert.IsFalse(fork.Overlaps(hand));

            float scale = ProductionHudLayout.Scale(width, height);
            Assert.AreEqual(8f * scale,
                ProductionHud.HeartBounds(0, width, height).width, 0.01f);
            Assert.AreEqual(48f * scale, hand.width, 0.01f);
            Assert.AreEqual(64f * scale, hand.height, 0.01f);
        }

        [Test]
        public void FinalHeartIsACommittedPointFilteredTrueAlphaSprite()
        {
            var importer = AssetImporter.GetAtPath(HeartPath) as TextureImporter;
            Assert.IsNotNull(importer);
            Assert.AreEqual(TextureImporterType.Sprite, importer.textureType);
            Assert.AreEqual(SpriteImportMode.Single, importer.spriteImportMode);
            Assert.AreEqual(FilterMode.Point, importer.filterMode);
            Assert.AreEqual(TextureImporterCompression.Uncompressed,
                importer.textureCompression);
            Assert.IsFalse(importer.mipmapEnabled);
            Assert.IsTrue(importer.alphaIsTransparency);

            var bytes = File.ReadAllBytes(Path.GetFullPath(HeartPath));
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                Assert.IsTrue(ImageConversion.LoadImage(texture, bytes, false));
                Assert.AreEqual(8, texture.width);
                Assert.AreEqual(7, texture.height);
                Color32[] pixels = texture.GetPixels32();
                Assert.IsTrue(System.Array.Exists(pixels, p => p.a == 0));
                Assert.IsTrue(System.Array.Exists(pixels, p => p.a == 255));
                Assert.IsTrue(System.Array.Exists(pixels,
                    p => p.r == 255 && p.g == 90 && p.b == 98 && p.a == 255));
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void SavedSceneAssignsFinalHudAssetsAndPublicFont()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            GameObject root = scene.GetRootGameObjects()[0];
            var hud = root.GetComponent<ProductionHud>();
            var diagnostics = root.GetComponent<ProductionDiagnosticsHud>();
            var teaching = root.GetComponent<ProductionTeachingPresenter>();
            var fork = root.GetComponent<ProductionForkPresenter>();

            Assert.IsTrue(hud.HeartAssetReady);
            Assert.AreEqual("DotGothic16-Regular", hud.PublicFont.name);
            Assert.AreSame(hud.PublicFont, diagnostics.PublicFont);
            Assert.AreSame(hud.PublicFont, teaching.PublicFont);
            Assert.AreSame(hud.PublicFont, fork.PublicFont);

            Camera display = root.GetComponentInChildren<Camera>(true);
            Assert.IsNull(display.targetTexture,
                "saved DisplayCamera must render Display 1");
            Assert.AreEqual(0, display.targetDisplay);
            var compositor = root.GetComponent<ProductionFrameCompositor>();
            Assert.IsNotNull(compositor);
            Assert.AreSame(display.GetComponent<ProductionLogicalRenderPresenter>(),
                compositor.LogicalSurface);

            Component[] components = root.GetComponents<Component>();
            Assert.Greater(System.Array.IndexOf(components, compositor),
                System.Array.IndexOf(components, fork),
                "reverse-submitted compositor must be saved after public HUD Presenters");
        }

        [Test]
        public void FinalFeedbackUsesOnlyApprovedSignalPalette()
        {
            Assert.AreEqual(ProductionHudLayout.Cyan,
                ProductionFeedbackHud.FeedbackColor(Result(true,
                    ProductionObstacleKind.Jump)));
            Assert.AreEqual(ProductionHudLayout.Amber,
                ProductionFeedbackHud.FeedbackColor(Result(true,
                    ProductionObstacleKind.Crouch)));
            Assert.AreEqual(ProductionHudLayout.Magenta,
                ProductionFeedbackHud.FeedbackColor(Result(true,
                    ProductionObstacleKind.Stop)));
            Assert.AreEqual(ProductionHudLayout.Violet,
                ProductionFeedbackHud.FeedbackColor(Result(true,
                    ProductionObstacleKind.Direction)));
            Assert.AreEqual(ProductionHudLayout.Fault,
                ProductionFeedbackHud.FeedbackColor(Result(false,
                    ProductionObstacleKind.Jump)));
        }

        private static ProductionObstacleJudge.ObstacleResult Result(
            bool success, ProductionObstacleKind kind)
            => new ProductionObstacleJudge.ObstacleResult
            {
                Success = success,
                Kind = kind
            };

        private static void AssertContains(Rect outer, Rect inner, string label)
        {
            Assert.GreaterOrEqual(inner.xMin, outer.xMin - 0.01f, label);
            Assert.GreaterOrEqual(inner.yMin, outer.yMin - 0.01f, label);
            Assert.LessOrEqual(inner.xMax, outer.xMax + 0.01f, label);
            Assert.LessOrEqual(inner.yMax, outer.yMax + 0.01f, label);
        }
    }
}
