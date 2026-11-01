using UnityEngine;

namespace BeforeExperience
{
    public sealed class FeedbackController : MonoBehaviour
    {
        private const int SampleRate = 44100;
        private readonly AudioClip[] clips = new AudioClip[OnlineIntentPredictor.CandidateCount];
        private AudioSource audioSource;

        public void Initialize()
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
            }

            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f;
            audioSource.volume = 0.45f;

            for (int id = 0; id < clips.Length; id++)
            {
                clips[id] = CreateTone(id);
            }
        }

        public void PlayReactionTone(int targetId)
        {
            if (audioSource == null || targetId < 0 || targetId >= clips.Length || clips[targetId] == null)
            {
                return;
            }

            audioSource.PlayOneShot(clips[targetId], 0.5f);
        }

        private static AudioClip CreateTone(int id)
        {
            const float duration = 0.12f;
            int sampleCount = Mathf.CeilToInt(SampleRate * duration);
            float[] data = new float[sampleCount];
            float frequency = 310f * Mathf.Pow(1.12246f, id);

            for (int i = 0; i < sampleCount; i++)
            {
                float t = i / (float)SampleRate;
                float normalized = i / (float)(sampleCount - 1);
                float envelope = Mathf.Sin(Mathf.PI * normalized) * Mathf.Exp(-normalized * 2.4f);
                float fundamental = Mathf.Sin(Mathf.PI * 2f * frequency * t);
                float overtone = Mathf.Sin(Mathf.PI * 4f * frequency * t) * 0.18f;
                data[i] = (fundamental + overtone) * envelope * 0.22f;
            }

            AudioClip clip = AudioClip.Create($"BeforeTone_{id}", sampleCount, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
