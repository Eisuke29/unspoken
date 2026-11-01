using System;
using NUnit.Framework;

namespace Before.Production.Tests
{
    public class ProductionSlice16Tests
    {
        [Test]
        public void EveryCueIsDeterministicNonEmptyAndWithinDocumentedPeak()
        {
            float max = (float)Math.Pow(10d, ProductionAudioFeedback.FaultDb / 20d) + 0.0001f;
            foreach (ProductionAudioFeedback.Cue cue in Enum.GetValues(typeof(ProductionAudioFeedback.Cue)))
            {
                float[] first = ProductionAudioFeedback.BuildSamples(cue);
                float[] second = ProductionAudioFeedback.BuildSamples(cue);
                Assert.Greater(first.Length, 0, cue.ToString());
                CollectionAssert.AreEqual(first, second, cue.ToString());
                Assert.LessOrEqual(ProductionAudioFeedback.Peak(first), max, cue.ToString());
            }
        }

        [Test]
        public void CueCatalogContainsNoBgmOrCommandOriginVariant()
        {
            foreach (string name in Enum.GetNames(typeof(ProductionAudioFeedback.Cue)))
            {
                StringAssert.DoesNotContain("BGM", name.ToUpperInvariant());
                StringAssert.DoesNotContain("HUMAN", name.ToUpperInvariant());
                StringAssert.DoesNotContain("AI", name.ToUpperInvariant());
            }
        }
    }
}
