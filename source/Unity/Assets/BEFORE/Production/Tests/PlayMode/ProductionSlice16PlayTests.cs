using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Before.Production.Tests
{
    public class ProductionSlice16PlayTests
    {
        private bool _hadMutePreference;
        private int _originalMutePreference;

        [SetUp]
        public void SetUpMutePreference()
        {
            _hadMutePreference = PlayerPrefs.HasKey(ProductionAudioFeedback.MutePreferenceKey);
            _originalMutePreference = PlayerPrefs.GetInt(ProductionAudioFeedback.MutePreferenceKey, 0);
            ProductionAudioFeedback.SaveMutePreference(false);
        }

        [TearDown]
        public void RestoreMutePreference()
        {
            if (_hadMutePreference)
                PlayerPrefs.SetInt(ProductionAudioFeedback.MutePreferenceKey, _originalMutePreference);
            else
                PlayerPrefs.DeleteKey(ProductionAudioFeedback.MutePreferenceKey);
            PlayerPrefs.Save();
        }

        [UnityTest]
        public IEnumerator AudioCachesOnceAndMirrorsCommandResultLivesRecoveryAndMute()
        {
            var root = new GameObject("slice16-audio");
            root.SetActive(false);
            root.AddComponent<Rigidbody2D>().gravityScale = 0f;
            var controller = root.AddComponent<NulProductionController>();
            controller.udpPort = 8886;
            controller.autoRun = false;
            var course = root.AddComponent<ProductionCourseLoop>();
            var judge = root.AddComponent<ProductionObstacleJudge>();
            var loop = root.AddComponent<ProductionGameLoop>();
            var audio = root.AddComponent<ProductionAudioFeedback>();
            audio.Controller = controller;
            audio.Judge = judge;
            audio.GameLoop = loop;
            root.SetActive(true);
            yield return null;

            Assert.AreEqual(7, audio.CachedCueCount);
            controller.InjectDatagram(Cmd(1, "JUMP"));
            yield return null;
            Assert.AreEqual(ProductionAudioFeedback.Cue.Command, audio.LastCue);

            controller.transform.position = new Vector3(course.Slots[0].WorldX, 0f, 0f);
            judge.EvaluateNowAt(0f);
            Assert.AreEqual(2, loop.Lives);
            Assert.AreEqual(ProductionAudioFeedback.Cue.Miss, audio.LastCue);

            SetPrivate(loop, "_recoveryUntil", controller.GameplayTime);
            loop.CompleteRecovery();
            Assert.AreEqual(ProductionAudioFeedback.Cue.Recovery, audio.LastCue);

            int beforeMute = audio.PlayedCueCount;
            audio.SetMuted(true);
            audio.PlayCue(ProductionAudioFeedback.Cue.Success);
            Assert.AreEqual(beforeMute, audio.PlayedCueCount);
            Assert.AreEqual(ProductionAudioFeedback.Cue.Success, audio.LastCue);

            Object.Destroy(root);
        }

        private static string Cmd(int id, string type)
            => "{\"v\":1,\"kind\":\"cmd\",\"session\":\"audio\",\"sstart\":9300," +
               "\"cmdId\":" + id + ",\"seq\":1,\"tUs\":0,\"type\":\"" + type +
               "\",\"phase\":\"fire\"}";

        private static void SetPrivate(object target, string field, object value)
        {
            FieldInfo info = target.GetType().GetField(field,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(info, field);
            info.SetValue(target, value);
        }
    }
}
