using System.Collections.Generic;
using System.IO;
using Before.Editor.Art;
using NUnit.Framework;

namespace Before.Tests
{
    /// <summary>Determinism and palette gates for the project-owned art generator (art-production.md).</summary>
    public sealed class ArtGeneratorTests
    {
        private const string TempRootA = "Temp/BeforeArtTestA";
        private const string TempRootB = "Temp/BeforeArtTestB";

        [TearDown]
        public void Cleanup()
        {
            if (Directory.Exists(TempRootA)) Directory.Delete(TempRootA, true);
            if (Directory.Exists(TempRootB)) Directory.Delete(TempRootB, true);
        }

        [Test]
        public void Generation_IsByteDeterministicAcrossRuns()
        {
            Dictionary<string, string> first = BeforeArtGenerator.GenerateInto(TempRootA, true);
            Dictionary<string, string> second = BeforeArtGenerator.GenerateInto(TempRootB, true);
            Assert.AreEqual(first.Count, second.Count);
            foreach (var pair in first)
            {
                Assert.AreEqual(pair.Value, second[pair.Key], $"{pair.Key} must regenerate byte-identically (seed 0xB3F04E).");
            }
        }

        [Test]
        public void Generation_ProducesTheAuthoredFileSet()
        {
            var hashes = BeforeArtGenerator.GenerateInto(TempRootA, true);
            string[] expected =
            {
                "NUL_Side_48.png", "NUL_Top_48.png", "WorldTiles_16.png", "Hazards_32.png",
                "BG_Far_640x180.png", "BG_Mid_640x180.png", "BG_Near_640x180.png"
            };
            foreach (var name in expected)
            {
                Assert.IsTrue(hashes.ContainsKey(name), $"{name} must be generated.");
                Assert.IsTrue(File.Exists(Path.Combine(TempRootA, name)), $"{name} must exist on disk.");
            }
            Assert.AreEqual(expected.Length, hashes.Count, "No unexpected extra files.");
        }

        [Test]
        public void EveryOpaquePixel_UsesTheLockedPalette()
        {
            BeforeArtGenerator.GenerateInto(TempRootA, true);
            List<string> errors = BeforeArtGenerator.ValidatePalette(TempRootA);
            Assert.IsEmpty(errors, string.Join("; ", errors));
        }
    }
}
