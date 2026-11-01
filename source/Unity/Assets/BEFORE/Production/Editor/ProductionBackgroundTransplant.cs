using System;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace Before.Production.Editor
{
    public static class ProductionBackgroundTransplant
    {
        public const string SourceRelativePath =
            ".claude/skills/build-before-gesture-coop/assets/staging/world/" +
            "r1-connection-outskirts/bg_r1_connection_outskirts_concept_src-v2.png";
        public const string ExpectedSourceSha256 =
            "E7BE0357BA67B87FB16B02A1A53E2073BEAC19083E5EDC8E8C5C89BE2F374D55";
        public const string OutputFolder =
            "Assets/BEFORE/Production/Art/Integrated/Background/R1";
        public const string FarPath = OutputFolder +
            "/bg_r1_connection_outskirts_far_640x180.png";
        public const string MidPath = OutputFolder +
            "/bg_r1_connection_outskirts_mid_640x180.png";
        public const string NearPath = OutputFolder +
            "/bg_r1_connection_outskirts_near_640x180.png";

        private const int HalfWidth = 320;
        private const int Height = 180;
        private static readonly Color32[] Palette =
        {
            Hex(0x05, 0x07, 0x0A), Hex(0x0D, 0x11, 0x17),
            Hex(0x1A, 0x20, 0x28), Hex(0x39, 0x41, 0x4C),
            Hex(0x7B, 0x85, 0x8F), Hex(0xD8, 0xDD, 0xD9),
            Hex(0x00, 0xD9, 0xE8)
        };

        [InitializeOnLoadMethod]
        private static void ScheduleBuildMissingOutputs()
        {
            EditorApplication.delayCall -= BuildMissingOutputs;
            EditorApplication.delayCall += BuildMissingOutputs;
        }

        private static void BuildMissingOutputs()
        {
            if (File.Exists(FarPath) && File.Exists(MidPath) && File.Exists(NearPath) &&
                AssetImporter.GetAtPath(FarPath) != null &&
                AssetImporter.GetAtPath(MidPath) != null &&
                AssetImporter.GetAtPath(NearPath) != null)
            {
                ConfigureImporter(FarPath, false);
                ConfigureImporter(MidPath, true);
                ConfigureImporter(NearPath, true);
                return;
            }
            Build();
        }

        [MenuItem("BEFORE/Production/Build Transplant Background")]
        public static void Build()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string sourcePath = Path.Combine(projectRoot,
                SourceRelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(sourcePath)) throw new FileNotFoundException(sourcePath);
            string hash = ComputeSha256(sourcePath);
            if (!string.Equals(hash, ExpectedSourceSha256,
                StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Unexpected transplant source SHA-256: " + hash);

            var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(source, File.ReadAllBytes(sourcePath), false))
                throw new InvalidDataException("Could not decode transplant source.");

            Color32[,] reduced = ReduceAndQuantize(source);
            UnityEngine.Object.DestroyImmediate(source);
            SplitMasks(reduced, out bool[,] midMask, out bool[,] nearMask);

            var far = new Color32[HalfWidth, Height];
            var mid = new Color32[HalfWidth, Height];
            var near = new Color32[HalfWidth, Height];
            Color32 voidColor = Palette[0];
            for (int y = 0; y < Height; y++)
            for (int x = 0; x < HalfWidth; x++)
            {
                Color32 pixel = reduced[x, y];
                bool isNear = nearMask[x, y];
                bool isMid = midMask[x, y] && !isNear;
                far[x, y] = isNear || isMid ? voidColor : pixel;
                mid[x, y] = isMid ? pixel : new Color32(0, 0, 0, 0);
                near[x, y] = isNear ? pixel : new Color32(0, 0, 0, 0);
            }

            Directory.CreateDirectory(Path.Combine(projectRoot,
                OutputFolder.Replace('/', Path.DirectorySeparatorChar)));
            WriteMirroredPng(projectRoot, FarPath, far);
            WriteMirroredPng(projectRoot, MidPath, mid);
            WriteMirroredPng(projectRoot, NearPath, near);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ConfigureImporter(FarPath, false);
            ConfigureImporter(MidPath, true);
            ConfigureImporter(NearPath, true);
            AssetDatabase.SaveAssets();
            Debug.Log("D-185 transplant background built: far/mid/near 640x180.");
        }

        private static Color32[,] ReduceAndQuantize(Texture2D source)
        {
            var output = new Color32[HalfWidth, Height];
            for (int y = 0; y < Height; y++)
            for (int x = 0; x < HalfWidth; x++)
            {
                Color sample = source.GetPixelBilinear(
                    (x + 0.5f) / HalfWidth, (y + 0.5f) / Height);
                output[x, y] = NearestPalette((Color32)sample);
            }
            return output;
        }

        private static void SplitMasks(Color32[,] pixels, out bool[,] mid,
            out bool[,] near)
        {
            mid = new bool[HalfWidth, Height];
            near = new bool[HalfWidth, Height];
            float[] rowMedian = new float[Height];
            float[] row = new float[HalfWidth];
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < HalfWidth; x++) row[x] = Luminance(pixels[x, y]);
                Array.Sort(row);
                rowMedian[y] = row[HalfWidth / 2];
            }

            for (int y = 0; y < Height; y++)
            for (int x = 0; x < HalfWidth; x++)
            {
                float lum = Luminance(pixels[x, y]);
                near[x, y] = y < 23 ||
                    (y < 58 && (x < 48 || x > 272) && lum > 0.035f);
                mid[x, y] = !near[x, y] && y >= 35 && y < 174 &&
                    lum > 0.018f && Mathf.Abs(lum - rowMedian[y]) > 0.026f;
            }
            mid = Dilate(mid, 1);
            for (int y = 0; y < Height; y++)
            for (int x = 0; x < HalfWidth; x++)
                if (near[x, y]) mid[x, y] = false;
        }

        private static bool[,] Dilate(bool[,] input, int radius)
        {
            var output = new bool[HalfWidth, Height];
            for (int y = 0; y < Height; y++)
            for (int x = 0; x < HalfWidth; x++)
            {
                bool value = false;
                for (int oy = -radius; oy <= radius && !value; oy++)
                for (int ox = -radius; ox <= radius && !value; ox++)
                {
                    int sx = x + ox;
                    int sy = y + oy;
                    value = sx >= 0 && sx < HalfWidth && sy >= 0 &&
                            sy < Height && input[sx, sy];
                }
                output[x, y] = value;
            }
            return output;
        }

        private static void WriteMirroredPng(string projectRoot, string assetPath,
            Color32[,] half)
        {
            var texture = new Texture2D(640, Height, TextureFormat.RGBA32, false);
            var pixels = new Color32[640 * Height];
            for (int y = 0; y < Height; y++)
            for (int x = 0; x < HalfWidth; x++)
            {
                Color32 pixel = half[x, y];
                pixels[y * 640 + x] = pixel;
                pixels[y * 640 + (639 - x)] = pixel;
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            string fullPath = Path.Combine(projectRoot,
                assetPath.Replace('/', Path.DirectorySeparatorChar));
            File.WriteAllBytes(fullPath, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
        }

        private static void ConfigureImporter(string path, bool hasAlpha)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 1f;
            importer.spritePivot = Vector2.zero;
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = hasAlpha;
            importer.maxTextureSize = 1024;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMode = (int)SpriteImportMode.Single;
            settings.spriteAlignment = (int)SpriteAlignment.Custom;
            settings.spritePivot = Vector2.zero;
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.spriteImportMode = SpriteImportMode.Single;
            EditorUtility.SetDirty(importer);
            importer.SaveAndReimport();

            importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.ReadTextureSettings(settings);
            if (importer.spriteImportMode != SpriteImportMode.Single ||
                settings.spriteMode != (int)SpriteImportMode.Single ||
                settings.spriteAlignment != (int)SpriteAlignment.Custom ||
                settings.spritePivot != Vector2.zero)
                throw new InvalidDataException(
                    "Background importer settings did not persist: " + path);
        }

        private static Color32 NearestPalette(Color32 source)
        {
            int bestDistance = int.MaxValue;
            Color32 best = Palette[0];
            for (int i = 0; i < Palette.Length; i++)
            {
                int dr = source.r - Palette[i].r;
                int dg = source.g - Palette[i].g;
                int db = source.b - Palette[i].b;
                int distance = dr * dr + dg * dg + db * db;
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = Palette[i];
            }
            return best;
        }

        private static float Luminance(Color32 color)
            => (0.2126f * color.r + 0.7152f * color.g + 0.0722f * color.b) / 255f;

        private static Color32 Hex(byte r, byte g, byte b)
            => new Color32(r, g, b, 255);

        private static string ComputeSha256(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
        }
    }
}
