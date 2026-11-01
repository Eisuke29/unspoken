using System;
using Before.Config;
using Unity.InferenceEngine;
using UnityEngine;

namespace Before.Capture
{
    /// <summary>
    /// One filtered hand signal per inference tick, in mirrored panel coordinates [0,1].
    /// Every consumer (recognition, energy, panel) reads this single abstraction so a fake
    /// stream can drive the complete experience without a camera.
    /// </summary>
    public interface IHandSignalSource
    {
        bool CameraAvailable { get; }
        bool Tracked { get; }            // presence >= track threshold and finite palm
        float Quality { get; }           // presence confidence 0..1
        Vector2 PalmMirrored { get; }    // filtered, mirrored panel coords [0,1]
        double LastSampleTime { get; }   // realtime clock
        bool ConsumeNewSample();         // true once per fresh sample
        Vector2[] LandmarksMirrored { get; } // 21 mirrored normalized, may be null
        float MeanLuma { get; }
        float LastInferenceMs { get; }
        float InferenceP95Ms { get; }
        double MedianHz { get; }
        // D-088 camera diagnostics (F1 only; never a raw video feed)
        string DeviceName { get; }
        int CameraWidth { get; }
        int CameraHeight { get; }
        float CameraFps { get; }
        bool PermissionGranted { get; }
        string StatusNote { get; }       // e.g. camera list + stop reason when the named device is absent
        void Tick(double realtimeNow);
        void Dispose();
    }

    /// <summary>Production source: exact-name webcam + BlazeHandTracker + One Euro filter + mirroring.</summary>
    public sealed class WebcamHandSource : IHandSignalSource
    {
        private readonly BeforeRuntimeConfig cfg;
        private readonly Recognition.OneEuroFilter2 filter;
        private readonly Diagnostics.SingleFlightGate gate = new Diagnostics.SingleFlightGate();
        private readonly Diagnostics.PercentileBuffer durations = new Diagnostics.PercentileBuffer(600);
        private readonly Diagnostics.RateMonitor rate = new Diagnostics.RateMonitor();

        private WebCamTexture webcam;
        private BlazeHandTracker tracker;
        private double lastSchedule;
        private double lastRescan;
        private bool newSample;
        private readonly Vector2[] landmarks = new Vector2[21];
        private bool running;

        public bool CameraAvailable { get; private set; }
        public bool Tracked { get; private set; }
        public float Quality { get; private set; }
        public Vector2 PalmMirrored { get; private set; } = new Vector2(0.5f, 0.5f);
        public double LastSampleTime { get; private set; }
        public Vector2[] LandmarksMirrored => landmarks;
        public float MeanLuma { get; private set; } = 0.5f;
        public float LastInferenceMs { get; private set; }
        public float InferenceP95Ms => durations.Percentile(0.95f);
        public double MedianHz => rate.MedianHz(LastSampleTime, 60.0);
        public string DeviceName => webcam != null && !string.IsNullOrEmpty(webcam.deviceName) ? webcam.deviceName : "-";
        public int CameraWidth => webcam != null ? webcam.width : 0;
        public int CameraHeight => webcam != null ? webcam.height : 0;
        public float CameraFps => webcam != null ? webcam.requestedFPS : 0f;
        public bool PermissionGranted => Application.HasUserAuthorization(UserAuthorization.WebCam);
        public string StatusNote { get; private set; } = "init";

        public WebcamHandSource(BeforeRuntimeConfig config, ModelAsset detector, ModelAsset landmarker, TextAsset anchors, Recognition.OneEuroFilter2 palmFilter)
        {
            cfg = config;
            filter = palmFilter;
            tracker = new BlazeHandTracker();
            tracker.Initialize(detector, landmarker, anchors.text);
            running = true;
            TryBind();
        }

        private void TryBind()
        {
            if (webcam != null) return;
            var devices = WebCamTexture.devices;
            foreach (var device in devices)
            {
                if (device.name == cfg.preferredDeviceName)
                {
                    // exact-name bind only; never auto-select a substitute (D-067)
                    webcam = new WebCamTexture(device.name, cfg.captureWidth, cfg.captureHeight, cfg.captureFps);
                    webcam.Play();
                    StatusNote = $"bound '{device.name}'";
                    return;
                }
            }
            // named device absent: report the detected list and the stop reason; do not bind another name
            var names = new System.Text.StringBuilder();
            for (int i = 0; i < devices.Length; i++)
            {
                if (i > 0) names.Append(", ");
                names.Append(devices[i].name);
            }
            StatusNote = devices.Length == 0
                ? "no cameras detected"
                : $"'{cfg.preferredDeviceName}' absent; detected: {names}";
        }

        public void Tick(double now)
        {
            if (!running) return;
            if (webcam == null || string.IsNullOrEmpty(webcam.deviceName))
            {
                CameraAvailable = false;
                if (now - lastRescan >= 2.0) { lastRescan = now; TryBind(); }
                return;
            }
            CameraAvailable = webcam.isPlaying && webcam.width > 16;
            if (!CameraAvailable) return;

            if (now - lastSchedule >= 1.0 / cfg.inferenceRateHz && gate.TryBegin())
            {
                lastSchedule = now;
                RunOne(now);
            }
        }

        private async void RunOne(double now)
        {
            try
            {
                HandObservation obs = await tracker.DetectAsync(webcam);
                durations.Add(obs.InferenceMs);
                rate.AddEvent(obs.Timestamp);
                LastInferenceMs = obs.InferenceMs;
                LastSampleTime = obs.Timestamp;
                Quality = obs.Score;
                Tracked = obs.HandPresent && obs.Score >= cfg.presenceTrackMin;
                if (obs.HandPresent)
                {
                    // palm center: mean of landmarks 0,5,9,13,17 in mirrored panel coordinates
                    Vector2 sum = Vector2.zero;
                    int[] anchorsIdx = { 0, 5, 9, 13, 17 };
                    bool finite = true;
                    for (int i = 0; i < 21; i++)
                    {
                        Vector3 lm = obs.GetLandmark(i);
                        landmarks[i] = new Vector2(1f - lm.x, lm.y);
                        if (!float.IsFinite(lm.x) || !float.IsFinite(lm.y)) finite = false;
                    }
                    foreach (int idx in anchorsIdx) sum += landmarks[idx];
                    Vector2 palm = sum / anchorsIdx.Length;
                    if (finite && palm.x >= -0.2f && palm.x <= 1.2f)
                    {
                        PalmMirrored = filter.Filter(palm, obs.Timestamp);
                        newSample = true;
                    }
                    else
                    {
                        Tracked = false;
                    }
                }
                else
                {
                    Tracked = false;
                }
            }
            catch (OperationCanceledException)
            {
                // normal during teardown
            }
            catch (Exception e)
            {
                Debug.LogError($"[BEFORE] Hand inference failed: {e.GetType().Name}: {e.Message}");
            }
            finally
            {
                gate.End();
            }
        }

        public bool ConsumeNewSample()
        {
            if (!newSample) return false;
            newSample = false;
            return true;
        }

        public void Dispose()
        {
            running = false;
            tracker?.Dispose();
            tracker = null;
            if (webcam != null)
            {
                if (webcam.isPlaying) webcam.Stop();
                UnityEngine.Object.Destroy(webcam);
                webcam = null;
            }
        }
    }

    /// <summary>Deterministic fake hand stream for camera-free tests and the story demonstration.</summary>
    public sealed class FakeHandSource : IHandSignalSource
    {
        public bool CameraAvailable { get; set; } = true;
        public bool Tracked { get; set; }
        public float Quality { get; set; } = 0.9f;
        public Vector2 PalmMirrored { get; set; } = new Vector2(0.5f, 0.5f);
        public double LastSampleTime { get; private set; }

        // Synthetic open-hand template (x right, y up) with palm center already at the origin, so a
        // deterministic skeleton can be rendered for screenshot QA without any camera. Not used by the
        // real pipeline. EmitSkeleton toggles whether LandmarksMirrored returns this shape.
        public bool EmitSkeleton { get; set; }
        public float SkeletonScale = 0.30f;
        // D-092: the fake participant makes a distinct hand SHAPE per command (0 = open, 1 = curled/fist,
        // 2 = neutral relax). Pose-conditioned Motion recognition needs the shape, not only the palm path.
        public int PoseShape { get; set; } = 0;
        private static readonly Vector2[] Template =
        {
            new Vector2(0.00f,-0.666f), // 0 wrist
            new Vector2(0.35f,-0.316f), new Vector2(0.50f,-0.016f), new Vector2(0.60f,0.234f), new Vector2(0.68f,0.434f), // thumb 1-4
            new Vector2(0.28f,0.184f), new Vector2(0.32f,0.584f), new Vector2(0.34f,0.834f), new Vector2(0.36f,1.034f), // index 5-8
            new Vector2(0.05f,0.214f), new Vector2(0.06f,0.634f), new Vector2(0.07f,0.914f), new Vector2(0.08f,1.134f), // middle 9-12
            new Vector2(-0.18f,0.184f), new Vector2(-0.20f,0.554f), new Vector2(-0.22f,0.814f), new Vector2(-0.24f,1.014f), // ring 13-16
            new Vector2(-0.40f,0.084f), new Vector2(-0.46f,0.384f), new Vector2(-0.50f,0.584f), new Vector2(-0.54f,0.754f), // pinky 17-20
        };
        private readonly Vector2[] skeleton = new Vector2[21];
        public Vector2[] LandmarksMirrored
        {
            get
            {
                // Emit a synthetic skeleton translated to the current palm whenever tracked, so the fake
                // stream drives Recognition V2 (the moving palm produces the gesture's palm path). EmitSkeleton
                // is retained for QA scripts but no longer gates landmark availability.
                if (!Tracked) return null;
                float s = SkeletonScale;
                // PoseShape 3 = point screen-left, 4 = point screen-right: index extended laterally, others curled
                if (PoseShape == 3 || PoseShape == 4)
                {
                    int dir = PoseShape == 4 ? 1 : -1;
                    skeleton[0] = PalmMirrored + new Vector2(0f, -0.20f) * s;                 // wrist
                    skeleton[5] = PalmMirrored + new Vector2(0.10f * dir, 0f) * s;            // index MCP
                    skeleton[6] = PalmMirrored + new Vector2(0.35f * dir, 0f) * s;
                    skeleton[7] = PalmMirrored + new Vector2(0.60f * dir, 0f) * s;
                    skeleton[8] = PalmMirrored + new Vector2(0.85f * dir, 0f) * s;            // index tip: far, lateral
                    int[] bases = { 9, 13, 17 };
                    for (int k = 0; k < 3; k++)
                    {
                        int b = bases[k];
                        float ox = (k - 1) * 0.06f;
                        skeleton[b + 0] = PalmMirrored + new Vector2(ox, 0.02f) * s;
                        skeleton[b + 1] = PalmMirrored + new Vector2(ox, 0.05f) * s;
                        skeleton[b + 2] = PalmMirrored + new Vector2(ox, 0.04f) * s;
                        skeleton[b + 3] = PalmMirrored + new Vector2(ox, 0.03f) * s;          // curled tip near palm
                    }
                    for (int j = 1; j <= 4; j++) skeleton[j] = PalmMirrored + new Vector2(-0.08f * dir, -0.02f * j) * s; // thumb
                    return skeleton;
                }
                for (int i = 0; i < 21; i++)
                {
                    Vector2 t = Template[i];
                    // curl / relax the four non-thumb fingers (indices 5..20) to create a distinct pose shape
                    if (i >= 5)
                    {
                        if (PoseShape == 1) t = new Vector2(t.x * 0.6f, t.y * 0.20f);       // curled fist
                        else if (PoseShape == 2) t = new Vector2(t.x * 1.15f, t.y * 0.60f); // neutral relax
                    }
                    skeleton[i] = PalmMirrored + t * s;
                }
                return skeleton;
            }
        }
        public float MeanLuma { get; set; } = 0.5f;
        public float LastInferenceMs => 1f;
        public float InferenceP95Ms => 1f;
        public double MedianHz => 15.0;
        public string DeviceName => "fake";
        public int CameraWidth => 0;
        public int CameraHeight => 0;
        public float CameraFps => 15f;
        public bool PermissionGranted => true;
        public string StatusNote => "fake stream";

        private bool newSample;
        private double lastTick;
        public double SampleInterval = 1.0 / 15.0;

        public void Tick(double now)
        {
            if (now - lastTick >= SampleInterval)
            {
                lastTick = now;
                LastSampleTime = now;
                newSample = true;
            }
        }

        /// <summary>Push an explicit sample (used by scripted trajectories).</summary>
        public void Push(Vector2 palm, double time, bool tracked = true, float quality = 0.9f)
        {
            PalmMirrored = palm;
            Tracked = tracked;
            Quality = quality;
            LastSampleTime = time;
            newSample = true;
        }

        public bool ConsumeNewSample()
        {
            if (!newSample) return false;
            newSample = false;
            return true;
        }

        public void Dispose() { }
    }
}
