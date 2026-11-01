using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Before.Production.Tests
{
    public class ProductionUnspokenP0Tests
    {
        private const string LogoPath =
            "Assets/BEFORE/Production/Art/Branding/UNSPOKEN_Logo_06_RuinedMachine.png";

        [Test]
        public void LogoSixIsImportedAsTransparentUncompressedRgbaTexture()
        {
            Texture2D logo = AssetDatabase.LoadAssetAtPath<Texture2D>(LogoPath);
            Assert.IsNotNull(logo);
            Assert.AreEqual(3600, logo.width);
            Assert.AreEqual(1179, logo.height);
            var importer = AssetImporter.GetAtPath(LogoPath) as TextureImporter;
            Assert.IsNotNull(importer);
            Assert.IsTrue(importer.alphaIsTransparency);
            Assert.IsFalse(importer.mipmapEnabled);
            Assert.AreEqual(TextureImporterNPOTScale.None, importer.npotScale);
            Assert.AreEqual(4096, importer.maxTextureSize);
        }

        [TestCase(1920f, 1080f)]
        [TestCase(2560f, 1080f)]
        [TestCase(1024f, 768f)]
        public void EnergyEndpointsUseSharedContentMappingAtEveryRequiredAspect(
            float width, float height)
        {
            Rect content = ProductionHudLayout.ContentRect(width, height);
            Rect hand = ProductionHudLayout.HandPanelRect(width, height);
            Vector2 palmA = ProductionEnergyLinkPresenter.PalmScreenPoint(
                hand, new Vector2(0.25f, 0.75f));
            Vector2 palmB = ProductionEnergyLinkPresenter.PalmScreenPoint(
                hand, new Vector2(0.75f, 0.25f));
            Vector2 coreA = ProductionEnergyLinkPresenter.ScreenPointForViewport(
                content, new Vector2(0.25f, 0.45f));
            Vector2 coreB = ProductionEnergyLinkPresenter.ScreenPointForViewport(
                content, new Vector2(0.55f, 0.45f));

            Assert.IsTrue(hand.Contains(palmA));
            Assert.IsTrue(hand.Contains(palmB));
            Assert.AreNotEqual(palmA, palmB, "palm movement must move link start");
            Assert.IsTrue(content.Contains(coreA));
            Assert.IsTrue(content.Contains(coreB));
            Assert.AreNotEqual(coreA, coreB, "NUL movement must move link end");
        }

        [TestCase(1920f, 1080f)]
        [TestCase(2560f, 1080f)]
        [TestCase(1024f, 768f)]
        public void EndingBlackoutOwnsTheEntireFinalGameView(float width, float height)
        {
            Rect rect = ProductionEndingPresenter.FullBlackoutRect(width, height);
            Assert.AreEqual(0f, rect.x);
            Assert.AreEqual(0f, rect.y);
            Assert.AreEqual(width, rect.width);
            Assert.AreEqual(height, rect.height);
            Assert.IsTrue(rect.Contains(new Vector2(0f, 0f)));
            Assert.IsTrue(rect.Contains(new Vector2(width - 0.01f,
                height - 0.01f)));
            Assert.IsFalse(ProductionDiagnosticsHud.SuppressForEndingStage(
                ProductionEndingStage.Departing));
            Assert.IsTrue(ProductionDiagnosticsHud.SuppressForEndingStage(
                ProductionEndingStage.WorldFade));
            Assert.IsTrue(ProductionDiagnosticsHud.SuppressForEndingStage(
                ProductionEndingStage.EndingHold));
        }

        [Test]
        public void DirectionPreviewAndCommitCopyAreTableDrivenAndNeverSwipe()
        {
            var rows = new[]
            {
                (direction: -1, route: ProductionRoute.Left,
                    received: ProductionTextCatalog.LeftReceived,
                    proceed: ProductionTextCatalog.LeftProceed),
                (direction: 1, route: ProductionRoute.Right,
                    received: ProductionTextCatalog.RightReceived,
                    proceed: ProductionTextCatalog.RightProceed)
            };
            foreach (var row in rows)
            {
                Assert.AreEqual(row.route,
                    ProductionForkPresenter.PreviewRouteForDirection(row.direction));
                Assert.AreEqual(row.received,
                    ProductionForkPresenter.ReceivedCopyFor(row.route));
                Assert.AreEqual(row.proceed,
                    ProductionForkPresenter.ProceedCopyFor(row.route));
            }
            string publicDirection = ProductionTextCatalog.ForkInstruction +
                                     ProductionTextCatalog.LeftProceed +
                                     ProductionTextCatalog.RightProceed;
            StringAssert.DoesNotContain("スワイプ", publicDirection);
            StringAssert.DoesNotContain("手を動かす", publicDirection);
        }

        [Test]
        public void TeachingCopyTableDistinguishesMotionFromStaticStop()
        {
            var rows = new[]
            {
                (kind: ProductionObstacleKind.Jump, motion: true),
                (kind: ProductionObstacleKind.Crouch, motion: true),
                (kind: ProductionObstacleKind.Stop, motion: false)
            };
            foreach (var row in rows)
            {
                string text = ProductionTextCatalog.TeachingInstructionFor(row.kind);
                Assert.AreEqual(row.motion, text.Contains("動き"));
                if (!row.motion)
                {
                    StringAssert.Contains("動かさず", text);
                    StringAssert.DoesNotContain("ゆっくり", text);
                }
            }
            Assert.AreEqual("記録しました　3/3",
                ProductionTextCatalog.Recorded(3, 3));
            StringAssert.DoesNotContain("もう一度",
                ProductionTextCatalog.Recorded(3, 3));
        }

        [Test]
        public void LandmarkDiagnosticsRemainAdditiveAndParseWithoutRgb()
        {
            const string json =
                "{\"kind\":\"lm\",\"tUs\":99,\"pts\":[],\"signal\":\"tracking_lost\"," +
                "\"trackQ\":0.42,\"dir\":-1,\"dirHold\":0.6,\"owner\":\"direction\"," +
                "\"stopD\":0.21,\"stopTh\":0.3,\"stopCandidateMs\":1000," +
                "\"stopStable\":3,\"stopHoldMs\":67,\"stopOnMs\":1067," +
                "\"stopOffMs\":0,\"stopReject\":\"candidate\",\"stopActive\":false}";
            Assert.IsTrue(LandmarkPacket.TryParseStatus(json, out var status));
            Assert.AreEqual(0.42f, status.TrackingQuality, 0.001f);
            Assert.AreEqual(-1, status.DirectionPreview);
            Assert.AreEqual(0.6f, status.DirectionHoldProgress, 0.001f);
            Assert.AreEqual("direction", status.RecognitionOwner);
            Assert.AreEqual(3, status.Stop.StableFrames);
            Assert.AreEqual("candidate", status.Stop.RejectReason);
            StringAssert.DoesNotContain("rgb", json.ToLowerInvariant());
        }

        [Test]
        public void ParticipantCatalogUsesUnspokenAndDebugIsOptIn()
        {
            Assert.AreEqual("UNSPOKEN", ProductionTextCatalog.BrandFallback);
            Assert.IsFalse(new GameObject("diagnostics-test")
                .AddComponent<ProductionDiagnosticsHud>().DetailsVisible);
            Object.DestroyImmediate(GameObject.Find("diagnostics-test"));
        }
    }
}
