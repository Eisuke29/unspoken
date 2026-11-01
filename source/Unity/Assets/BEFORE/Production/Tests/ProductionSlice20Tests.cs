using NUnit.Framework;
using UnityEngine;

namespace Before.Production.Tests
{
    public class ProductionSlice20Tests
    {
        [Test]
        public void BeforeMuteIsTheSingleRoundTripPreference()
        {
            bool hadOriginal = PlayerPrefs.HasKey(ProductionAudioFeedback.MutePreferenceKey);
            int original = PlayerPrefs.GetInt(ProductionAudioFeedback.MutePreferenceKey, 0);
            try
            {
                PlayerPrefs.DeleteKey(ProductionAudioFeedback.MutePreferenceKey);
                Assert.IsFalse(ProductionAudioFeedback.LoadMutePreference(false));
                Assert.IsTrue(ProductionAudioFeedback.LoadMutePreference(true));

                ProductionAudioFeedback.SaveMutePreference(true);
                Assert.IsTrue(ProductionAudioFeedback.LoadMutePreference());
                ProductionAudioFeedback.SaveMutePreference(false);
                Assert.IsFalse(ProductionAudioFeedback.LoadMutePreference());
            }
            finally
            {
                if (hadOriginal)
                    PlayerPrefs.SetInt(ProductionAudioFeedback.MutePreferenceKey, original);
                else
                    PlayerPrefs.DeleteKey(ProductionAudioFeedback.MutePreferenceKey);
                PlayerPrefs.Save();
            }
        }
    }
}
