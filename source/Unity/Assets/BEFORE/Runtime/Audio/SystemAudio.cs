using System.Collections.Generic;
using UnityEngine;

namespace Before.Audio
{
    /// <summary>
    /// Procedural short system cues per narrative-presentation.md. No BGM/voice/external files.
    /// Master level -16 dBFS; fault cue may peak at -12 dBFS. Only BeforeMute uses PlayerPrefs.
    /// </summary>
    public sealed class SystemAudio : MonoBehaviour
    {
        private const int SampleRate = 44100;
        private const string MutePref = "BeforeMute";

        private readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        private AudioSource source;

        public bool Muted { get; private set; }

        public void Initialize()
        {
            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            Muted = PlayerPrefs.GetInt(MutePref, 0) == 1;

            float master = DbToLinear(-16f);
            float fault = DbToLinear(-12f);
            clips["link"] = SineGlide(440f, 880f, 0.18f, master);
            clips["capture"] = Tick(660f, 0.05f, master);
            clips["sample"] = TwoSines(660f, 990f, 0.12f, master);
            clips["accept"] = Dyad(660f, 990f, 0.16f, master);
            clips["ambiguous"] = Detuned(440f, 430f, 0.24f, master);
            clips["nomatch"] = NoiseFall(0.12f, master);
            clips["refuse"] = SquareChips(0.20f, fault);
            clips["loss"] = InterruptedPulse(180f, 0.30f, master);
            clips["core"] = LayeredRise(0.90f, master);
            clips["success"] = Dyad(880f, 1320f, 0.14f, master * 0.8f);
        }

        public void SetMuted(bool muted)
        {
            Muted = muted;
            PlayerPrefs.SetInt(MutePref, muted ? 1 : 0);
        }

        public void Play(string key)
        {
            if (Muted || source == null || !clips.TryGetValue(key, out var clip) || clip == null) return;
            source.PlayOneShot(clip);
        }

        private static float DbToLinear(float db) => Mathf.Pow(10f, db / 20f);

        private static AudioClip Make(string name, float[] data)
        {
            var clip = AudioClip.Create(name, data.Length, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static float Env(int i, int n, float edgeMs = 10f)
        {
            int edge = Mathf.Max(1, (int)(SampleRate * edgeMs / 1000f));
            float a = Mathf.Clamp01(i / (float)edge);
            float r = Mathf.Clamp01((n - 1 - i) / (float)edge);
            return Mathf.Min(a, r);
        }

        private static AudioClip SineGlide(float f0, float f1, float sec, float amp)
        {
            int n = (int)(SampleRate * sec);
            var d = new float[n];
            float phase = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;
                float f = Mathf.Lerp(f0, f1, t);
                phase += 2f * Mathf.PI * f / SampleRate;
                d[i] = Mathf.Sin(phase) * amp * Env(i, n);
            }
            return Make("cue_link", d);
        }

        private static AudioClip Tick(float f, float sec, float amp)
        {
            int n = (int)(SampleRate * sec);
            var d = new float[n];
            for (int i = 0; i < n; i++)
            {
                d[i] = Mathf.Sin(2f * Mathf.PI * f * i / SampleRate) * amp * Env(i, n, 4f);
            }
            return Make("cue_tick", d);
        }

        private static AudioClip TwoSines(float f0, float f1, float sec, float amp)
        {
            int n = (int)(SampleRate * sec);
            var d = new float[n];
            int half = n / 2;
            for (int i = 0; i < n; i++)
            {
                float f = i < half ? f0 : f1;
                d[i] = Mathf.Sin(2f * Mathf.PI * f * i / SampleRate) * amp * Env(i, n);
            }
            return Make("cue_sample", d);
        }

        private static AudioClip Dyad(float f0, float f1, float sec, float amp)
        {
            int n = (int)(SampleRate * sec);
            var d = new float[n];
            for (int i = 0; i < n; i++)
            {
                float s = Mathf.Sin(2f * Mathf.PI * f0 * i / SampleRate) + Mathf.Sin(2f * Mathf.PI * f1 * i / SampleRate);
                d[i] = 0.5f * s * amp * Env(i, n);
            }
            return Make("cue_accept", d);
        }

        private static AudioClip Detuned(float f0, float f1, float sec, float amp)
        {
            int n = (int)(SampleRate * sec);
            var d = new float[n];
            for (int i = 0; i < n; i++)
            {
                float pulse = (i / (float)n < 0.5f) ? 1f : 0.7f;
                float s = Mathf.Sin(2f * Mathf.PI * f0 * i / SampleRate) + Mathf.Sin(2f * Mathf.PI * f1 * i / SampleRate);
                d[i] = 0.5f * s * amp * pulse * Env(i, n);
            }
            return Make("cue_ambiguous", d);
        }

        private static AudioClip NoiseFall(float sec, float amp)
        {
            int n = (int)(SampleRate * sec);
            var d = new float[n];
            var rng = new System.Random(0xB3F04E);
            float previous = 0f;
            for (int i = 0; i < n; i++)
            {
                float white = (float)(rng.NextDouble() * 2 - 1);
                previous = 0.7f * previous + 0.3f * white; // band-limit
                float fall = 1f - i / (float)n;
                d[i] = previous * amp * fall * Env(i, n, 4f);
            }
            return Make("cue_nomatch", d);
        }

        private static AudioClip SquareChips(float sec, float amp)
        {
            int n = (int)(SampleRate * sec);
            var d = new float[n];
            float[] freqs = { 520f, 400f, 300f };
            int chip = n / 3;
            for (int i = 0; i < n; i++)
            {
                int c = Mathf.Min(2, i / chip);
                int j = i - c * chip;
                if (j > chip * 0.7f) continue;
                float f = freqs[c];
                float s = Mathf.Sign(Mathf.Sin(2f * Mathf.PI * f * i / SampleRate));
                d[i] = s * amp * 0.6f * Env(j, (int)(chip * 0.7f), 3f);
            }
            return Make("cue_refuse", d);
        }

        private static AudioClip InterruptedPulse(float f, float sec, float amp)
        {
            int n = (int)(SampleRate * sec);
            var d = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;
                bool on = t < 0.35f || (t > 0.5f && t < 0.85f);
                if (!on) continue;
                d[i] = Mathf.Sin(2f * Mathf.PI * f * i / SampleRate) * amp * Env(i, n);
            }
            return Make("cue_loss", d);
        }

        private static AudioClip LayeredRise(float sec, float amp)
        {
            int n = (int)(SampleRate * sec);
            var d = new float[n];
            float[] freqs = { 220f, 440f, 880f };
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;
                float s = 0f;
                for (int k = 0; k < 3; k++)
                {
                    float enter = k / 3f;
                    if (t >= enter) s += Mathf.Sin(2f * Mathf.PI * freqs[k] * i / SampleRate) * Mathf.Clamp01((t - enter) * 4f);
                }
                d[i] = s / 3f * amp * Env(i, n, 20f);
            }
            return Make("cue_core", d);
        }
    }
}
