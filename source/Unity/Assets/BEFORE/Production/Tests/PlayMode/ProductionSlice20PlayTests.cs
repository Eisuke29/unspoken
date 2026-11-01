using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Before.Production.Tests
{
    public class ProductionSlice20PlayTests
    {
        private bool _hadMutePreference;
        private int _originalMutePreference;

        [SetUp]
        public void SaveOriginalPreference()
        {
            _hadMutePreference = PlayerPrefs.HasKey(ProductionAudioFeedback.MutePreferenceKey);
            _originalMutePreference = PlayerPrefs.GetInt(ProductionAudioFeedback.MutePreferenceKey, 0);
        }

        [TearDown]
        public void RestoreOriginalPreference()
        {
            if (_hadMutePreference)
                PlayerPrefs.SetInt(ProductionAudioFeedback.MutePreferenceKey, _originalMutePreference);
            else
                PlayerPrefs.DeleteKey(ProductionAudioFeedback.MutePreferenceKey);
            PlayerPrefs.Save();
        }

        [UnityTest]
        public IEnumerator MutePreferenceSurvivesComponentRecreation()
        {
            ProductionAudioFeedback.SaveMutePreference(true);
            var listenerObject = new GameObject("slice20-audio-listener");
            listenerObject.AddComponent<AudioListener>();
            var first = new GameObject("slice20-audio-first");
            var firstAudio = first.AddComponent<ProductionAudioFeedback>();
            yield return null;
            Assert.IsTrue(firstAudio.Muted);

            firstAudio.SetMuted(false);
            Object.Destroy(first);
            yield return null;

            var second = new GameObject("slice20-audio-second");
            var secondAudio = second.AddComponent<ProductionAudioFeedback>();
            yield return null;
            Assert.IsFalse(secondAudio.Muted);
            Object.Destroy(second);
            Object.Destroy(listenerObject);
        }
    }
}
