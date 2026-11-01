using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Before.Production.Tests
{
    public class ProductionSlice49Tests
    {
        private const string ScenePath = "Assets/BEFORE/Scenes/BEFORE_ProductionV2.unity";
        private const string Folder = "Assets/BEFORE/Production/Art/Integrated/Background/R1/";
        private static readonly string[] Paths =
        {
            Folder + "bg_r1_connection_outskirts_far_640x180.png",
            Folder + "bg_r1_connection_outskirts_mid_640x180.png",
            Folder + "bg_r1_connection_outskirts_near_640x180.png"
        };
        private static readonly HashSet<uint> Palette = new HashSet<uint>
        {
            0x05070AFF, 0x0D1117FF, 0x1A2028FF, 0x39414CFF,
            0x7B858FFF, 0xD8DDD9FF, 0x00D9E8FF, 0x00000000
        };

        [Test]
        public void ImportedLayersMatchProductionTextureContract()
        {
            for (int i = 0; i < Paths.Length; i++)
            {
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(Paths[i]);
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(Paths[i]);
                var importer = (TextureImporter)AssetImporter.GetAtPath(Paths[i]);
                Assert.IsNotNull(texture, Paths[i]);
                Assert.IsNotNull(sprite, Paths[i]);
                Assert.AreEqual(640, texture.width);
                Assert.AreEqual(180, texture.height);
                Assert.AreEqual(TextureImporterType.Sprite, importer.textureType);
                Assert.AreEqual(SpriteImportMode.Single, importer.spriteImportMode);
                Assert.AreEqual(1f, importer.spritePixelsPerUnit);
                Assert.AreEqual(FilterMode.Point, importer.filterMode);
                Assert.AreEqual(TextureWrapMode.Repeat, importer.wrapMode);
                Assert.AreEqual(TextureImporterCompression.Uncompressed,
                    importer.textureCompression);
                Assert.IsFalse(importer.mipmapEnabled);
                Assert.IsTrue(importer.sRGBTexture);
                Assert.GreaterOrEqual(importer.maxTextureSize, 640);
                Assert.AreEqual(Vector2.zero, importer.spritePivot);
                Assert.AreEqual(640f, sprite.bounds.size.x);
                Assert.AreEqual(180f, sprite.bounds.size.y);
            }
        }

        [Test]
        public void PixelsUseLimitedPaletteTrueAlphaAndSeamlessEdges()
        {
            for (int layer = 0; layer < Paths.Length; layer++)
            {
                Color32[] pixels = Decode(Paths[layer]);
                bool sawClear = false;
                bool sawOpaque = false;
                for (int y = 0; y < 180; y++)
                {
                    Assert.AreEqual(pixels[y * 640], pixels[y * 640 + 639],
                        Paths[layer] + " seam row " + y);
                    for (int x = 0; x < 640; x++)
                    {
                        Color32 p = pixels[y * 640 + x];
                        Assert.IsTrue(Palette.Contains(Pack(p)), Paths[layer]);
                        Assert.IsTrue(p.a == 0 || p.a == 255, Paths[layer]);
                        if (p.a == 0)
                        {
                            sawClear = true;
                            Assert.AreEqual(0, p.r | p.g | p.b,
                                Paths[layer] + " alpha fringe");
                        }
                        else sawOpaque = true;
                    }
                }
                Assert.IsTrue(sawOpaque, Paths[layer]);
                if (layer == 0) Assert.IsFalse(sawClear, Paths[layer]);
                else Assert.IsTrue(sawClear, Paths[layer]);
            }
        }

        [Test]
        public void ThreeLayersHaveDistinctContent()
        {
            CollectionAssert.AreNotEqual(File.ReadAllBytes(Paths[0]),
                File.ReadAllBytes(Paths[1]));
            CollectionAssert.AreNotEqual(File.ReadAllBytes(Paths[1]),
                File.ReadAllBytes(Paths[2]));
            CollectionAssert.AreNotEqual(File.ReadAllBytes(Paths[0]),
                File.ReadAllBytes(Paths[2]));
        }

        [Test]
        public void PresenterKeepsAuthoritativeIndependentParallaxContract()
        {
            Assert.AreEqual(0.15f, ProductionBackgroundPresenter.FarParallax);
            Assert.AreEqual(0.35f, ProductionBackgroundPresenter.MidParallax);
            Assert.AreEqual(0.65f, ProductionBackgroundPresenter.NearParallax);
            Assert.AreEqual(-20, ProductionBackgroundPresenter.FarSortingOrder);
            Assert.AreEqual(-10, ProductionBackgroundPresenter.MidSortingOrder);
            Assert.AreEqual(30, ProductionBackgroundPresenter.NearSortingOrder);
            Assert.AreEqual(-75f,
                ProductionBackgroundPresenter.BaseLeftFor(100f, 0.15f));
            Assert.AreEqual(-95f,
                ProductionBackgroundPresenter.BaseLeftFor(100f, 0.35f));
            Assert.AreEqual(-125f,
                ProductionBackgroundPresenter.BaseLeftFor(100f, 0.65f));
        }

        [Test]
        public void SavedSceneOwnsExactlyOneWiredBackgroundPresenter()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            GameObject root = scene.GetRootGameObjects()[0];
            var presenters = root.GetComponents<ProductionBackgroundPresenter>();
            Assert.AreEqual(1, presenters.Length);
            Assert.IsNotNull(presenters[0].WorldCamera);
            Assert.IsNotNull(presenters[0].FarSprite);
            Assert.IsNotNull(presenters[0].MidSprite);
            Assert.IsNotNull(presenters[0].NearSprite);
            Assert.AreEqual(Paths[0], AssetDatabase.GetAssetPath(presenters[0].FarSprite));
            Assert.AreEqual(Paths[1], AssetDatabase.GetAssetPath(presenters[0].MidSprite));
            Assert.AreEqual(Paths[2], AssetDatabase.GetAssetPath(presenters[0].NearSprite));
        }

        private static Color32[] Decode(string path)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            Assert.IsTrue(ImageConversion.LoadImage(texture, File.ReadAllBytes(path), false));
            Color32[] pixels = texture.GetPixels32();
            Object.DestroyImmediate(texture);
            return pixels;
        }

        private static uint Pack(Color32 p)
            => ((uint)p.r << 24) | ((uint)p.g << 16) | ((uint)p.b << 8) | p.a;
    }
}
