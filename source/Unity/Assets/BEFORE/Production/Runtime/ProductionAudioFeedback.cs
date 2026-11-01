using System;
using System.Collections.Generic;
using UnityEngine;

namespace Before.Production
{
    /// <summary>
    /// Optional source-agnostic reinforcement for the production loop.
    /// Clips are generated once from deterministic PCM; no BGM, voice, input
    /// interpretation, obstacle judgement, or gameplay state is owned here.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProductionAudioFeedback : MonoBehaviour
    {
        public enum Cue
        {
            Command,
            Success,
            Miss,
            HeartLoss,
            Recovery,
            Route,
            GameOver
        }

        public const int SampleRate = 44100;
        public const float MasterDb = -16f;
        public const float FaultDb = -12f;
        public const string MutePreferenceKey = "BeforeMute";

        [SerializeField] private NulProductionController controller;
        [SerializeField] private ProductionObstacleJudge judge;
        [SerializeField] private ProductionGameLoop gameLoop;
        [SerializeField] private bool muted;

        private readonly Dictionary<Cue, AudioClip> _clips = new Dictionary<Cue, AudioClip>();
        private AudioSource _source;
        private int _previousLives = ProductionGameLoop.StartingLives;

        public NulProductionController Controller { get => controller; set => controller = value; }
        public ProductionObstacleJudge Judge { get => judge; set => judge = value; }
        public ProductionGameLoop GameLoop { get => gameLoop; set => gameLoop = value; }
        public bool Muted => muted;
        public int CachedCueCount => _clips.Count;
        public int PlayedCueCount { get; private set; }
        public Cue LastCue { get; private set; }

        private void Awake()
        {
            if (controller == null) controller = GetComponent<NulProductionController>();
            if (judge == null) judge = GetComponent<ProductionObstacleJudge>();
            if (gameLoop == null) gameLoop = GetComponent<ProductionGameLoop>();
            _previousLives = gameLoop != null ? gameLoop.Lives : ProductionGameLoop.StartingLives;
            muted = LoadMutePreference(muted);
            EnsureAudio();
        }

        private void OnEnable()
        {
            if (controller != null) controller.Bus.OnCommand += HandleCommand;
            if (judge != null) judge.OnResolved += HandleResolved;
            if (gameLoop != null)
            {
                gameLoop.OnLivesChanged += HandleLivesChanged;
                gameLoop.OnLivesExhausted += HandleLivesExhausted;
                gameLoop.OnRouteSelected += HandleRouteSelected;
                gameLoop.OnRecoveryCompleted += HandleRecoveryCompleted;
            }
        }

        private void OnDisable()
        {
            if (controller != null) controller.Bus.OnCommand -= HandleCommand;
            if (judge != null) judge.OnResolved -= HandleResolved;
            if (gameLoop != null)
            {
                gameLoop.OnLivesChanged -= HandleLivesChanged;
                gameLoop.OnLivesExhausted -= HandleLivesExhausted;
                gameLoop.OnRouteSelected -= HandleRouteSelected;
                gameLoop.OnRecoveryCompleted -= HandleRecoveryCompleted;
            }
        }

        private void Update()
        {
#if ENABLE_INPUT_SYSTEM
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null && keyboard.mKey.wasPressedThisFrame) SetMuted(!muted);
#endif
        }

        public void SetMuted(bool value)
        {
            muted = value;
            if (_source != null) _source.mute = muted;
            SaveMutePreference(muted);
        }

        public static bool LoadMutePreference(bool fallback = false)
            => PlayerPrefs.GetInt(MutePreferenceKey, fallback ? 1 : 0) == 1;

        public static void SaveMutePreference(bool value)
        {
            PlayerPrefs.SetInt(MutePreferenceKey, value ? 1 : 0);
            PlayerPrefs.Save();
        }

        public void PlayCue(Cue cue)
        {
            LastCue = cue;
            if (muted || _source == null || !_clips.TryGetValue(cue, out AudioClip clip)) return;
            _source.PlayOneShot(clip);
            PlayedCueCount++;
        }

        private void EnsureAudio()
        {
            if (_source == null)
            {
                _source = gameObject.GetComponent<AudioSource>();
                if (_source == null) _source = gameObject.AddComponent<AudioSource>();
                _source.playOnAwake = false;
                _source.loop = false;
                _source.spatialBlend = 0f;
                _source.mute = muted;
            }
            if (_clips.Count != 0) return;
            foreach (Cue cue in Enum.GetValues(typeof(Cue)))
            {
                float[] samples = BuildSamples(cue);
                AudioClip clip = AudioClip.Create("production_" + cue.ToString().ToLowerInvariant(),
                    samples.Length, 1, SampleRate, false);
                clip.SetData(samples, 0);
                _clips.Add(cue, clip);
            }
        }

        public static float[] BuildSamples(Cue cue)
        {
            switch (cue)
            {
                case Cue.Command: return Dyad(660f, 990f, 0.16f, DbToLinear(MasterDb));
                case Cue.Success: return Dyad(880f, 1320f, 0.14f, DbToLinear(MasterDb) * 0.8f);
                case Cue.Miss: return NoiseFall(0.12f, DbToLinear(MasterDb));
                case Cue.HeartLoss: return Glide(440f, 220f, 0.20f, DbToLinear(FaultDb));
                case Cue.Recovery: return Glide(330f, 660f, 0.18f, DbToLinear(MasterDb));
                case Cue.Route: return Dyad(520f, 780f, 0.18f, DbToLinear(MasterDb));
                default: return InterruptedPulse(180f, 0.30f, DbToLinear(FaultDb));
            }
        }

        public static float Peak(float[] samples)
        {
            float peak = 0f;
            for (int i = 0; i < samples.Length; i++) peak = Mathf.Max(peak, Mathf.Abs(samples[i]));
            return peak;
        }

        private void HandleCommand(CommandPacket _) => PlayCue(Cue.Command);
        private void HandleResolved(ProductionObstacleJudge.ObstacleResult result)
            => PlayCue(result.Success ? Cue.Success : Cue.Miss);

        private void HandleLivesChanged(int lives)
        {
            if (lives < _previousLives) PlayCue(Cue.HeartLoss);
            _previousLives = lives;
        }

        private void HandleLivesExhausted() => PlayCue(Cue.GameOver);
        private void HandleRecoveryCompleted() => PlayCue(Cue.Recovery);
        private void HandleRouteSelected(ProductionRoute route)
        {
            if (route != ProductionRoute.None) PlayCue(Cue.Route);
        }

        private static float DbToLinear(float db) => Mathf.Pow(10f, db / 20f);

        private static float Envelope(int i, int count, float edgeMs = 10f)
        {
            int edge = Mathf.Max(1, Mathf.RoundToInt(SampleRate * edgeMs / 1000f));
            return Mathf.Min(Mathf.Clamp01(i / (float)edge),
                Mathf.Clamp01((count - 1 - i) / (float)edge));
        }

        private static float[] Dyad(float a, float b, float seconds, float amplitude)
        {
            int count = Mathf.RoundToInt(SampleRate * seconds);
            var samples = new float[count];
            for (int i = 0; i < count; i++)
            {
                float value = Mathf.Sin(2f * Mathf.PI * a * i / SampleRate) +
                              Mathf.Sin(2f * Mathf.PI * b * i / SampleRate);
                samples[i] = value * 0.5f * amplitude * Envelope(i, count);
            }
            return samples;
        }

        private static float[] Glide(float from, float to, float seconds, float amplitude)
        {
            int count = Mathf.RoundToInt(SampleRate * seconds);
            var samples = new float[count];
            float phase = 0f;
            for (int i = 0; i < count; i++)
            {
                float frequency = Mathf.Lerp(from, to, i / (float)count);
                phase += 2f * Mathf.PI * frequency / SampleRate;
                samples[i] = Mathf.Sin(phase) * amplitude * Envelope(i, count);
            }
            return samples;
        }

        private static float[] NoiseFall(float seconds, float amplitude)
        {
            int count = Mathf.RoundToInt(SampleRate * seconds);
            var samples = new float[count];
            var random = new System.Random(0xB3F04E);
            float previous = 0f;
            for (int i = 0; i < count; i++)
            {
                float white = (float)(random.NextDouble() * 2d - 1d);
                previous = 0.7f * previous + 0.3f * white;
                samples[i] = previous * amplitude * (1f - i / (float)count) *
                             Envelope(i, count, 4f);
            }
            return samples;
        }

        private static float[] InterruptedPulse(float frequency, float seconds, float amplitude)
        {
            int count = Mathf.RoundToInt(SampleRate * seconds);
            var samples = new float[count];
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)count;
                if (t < 0.35f || (t > 0.5f && t < 0.85f))
                    samples[i] = Mathf.Sin(2f * Mathf.PI * frequency * i / SampleRate) *
                                 amplitude * Envelope(i, count);
            }
            return samples;
        }

        private void OnDestroy()
        {
            foreach (AudioClip clip in _clips.Values)
                if (clip != null) Destroy(clip);
            _clips.Clear();
        }
    }
}
