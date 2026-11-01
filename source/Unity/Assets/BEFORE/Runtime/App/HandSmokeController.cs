using System;
using Before.Capture;
using Before.Diagnostics;
using Unity.InferenceEngine;
using UnityEngine;

namespace Before.App
{
    /// <summary>
    /// Supervised hand-inference smoke test driver (preflight step 4 only).
    /// Binds the exact preferred webcam, runs the two-stage BlazeHand pipeline at most at
    /// the configured rate with a single request in flight, and shows smoke-only diagnostics.
    /// Not part of the production game loop.
    /// </summary>
    public sealed class HandSmokeController : MonoBehaviour
    {
        [Header("Pinned model assets")]
        [SerializeField] private ModelAsset handDetector;
        [SerializeField] private ModelAsset handLandmarker;
        [SerializeField] private TextAsset anchorsCsv;

        [Header("Camera")]
        [SerializeField] private string preferredDeviceName = "Integrated Camera";
        [SerializeField] private int requestedWidth = 640;
        [SerializeField] private int requestedHeight = 480;
        [SerializeField] private int requestedFps = 30;

        [Header("Inference")]
        [SerializeField, Range(1f, 30f)] private float inferenceRateHz = 15f;
        [SerializeField, Range(0.1f, 2f)] private float presenceStaleSeconds = 0.6f;

        private WebCamTexture webcam;
        private BlazeHandTracker tracker;
        private readonly SingleFlightGate flightGate = new SingleFlightGate();
        private readonly PercentileBuffer durationsMs = new PercentileBuffer(600);
        private readonly RateMonitor inferenceRate = new RateMonitor();
        private readonly RateMonitor captureRate = new RateMonitor();

        private HandObservation latest;
        private string lastError = "";
        private string fatalError = "";
        private bool running;
        private double lastScheduleTime;
        private double runStartTime;

        public bool HandPresent
        {
            get
            {
                return latest != null && latest.HandPresent
                    && Time.realtimeSinceStartupAsDouble - latest.Timestamp <= presenceStaleSeconds;
            }
        }

        public string PreferredDeviceName => preferredDeviceName;
        public string BoundDeviceName => webcam != null ? webcam.deviceName : "";
        public int ActualWidth => webcam != null ? webcam.width : 0;
        public int ActualHeight => webcam != null ? webcam.height : 0;
        public float RequestedFps => requestedFps;
        public int InFlight => flightGate.InFlight;
        public int MaxInFlightObserved => flightGate.MaxObserved;
        public float P50Ms => durationsMs.Percentile(0.5f);
        public float P95Ms => durationsMs.Percentile(0.95f);
        public int DurationSampleCount => durationsMs.Count;
        public double CurrentInferenceHz => inferenceRate.CountInLastSecond(Time.realtimeSinceStartupAsDouble);
        public double MedianInferenceHz60s => inferenceRate.MedianHz(Time.realtimeSinceStartupAsDouble, 60.0);
        public double MedianCaptureFps60s => captureRate.MedianHz(Time.realtimeSinceStartupAsDouble, 60.0);
        public double SecondsSinceRunStart => runStartTime > 0 ? Time.realtimeSinceStartupAsDouble - runStartTime : 0;
        public string LastError => string.IsNullOrEmpty(fatalError) ? lastError : fatalError;
        public float LatestScore => latest != null ? latest.Score : 0f;

        private void Start()
        {
            if (handDetector == null || handLandmarker == null || anchorsCsv == null)
            {
                Fail("Missing required inspector reference (detector/landmarker/anchors).");
                return;
            }

            string exactName = null;
            foreach (var device in WebCamTexture.devices)
            {
                if (device.name == preferredDeviceName)
                {
                    exactName = device.name;
                    break;
                }
            }
            if (exactName == null)
            {
                Fail($"Preferred camera not found by exact name: '{preferredDeviceName}'. No fallback camera is selected.");
                return;
            }

            try
            {
                webcam = new WebCamTexture(exactName, requestedWidth, requestedHeight, requestedFps);
                webcam.Play();
                tracker = new BlazeHandTracker();
                tracker.Initialize(handDetector, handLandmarker, anchorsCsv.text);
            }
            catch (Exception e)
            {
                Fail($"Initialization failed: {e.GetType().Name}: {e.Message}");
                return;
            }

            running = true;
            runStartTime = Time.realtimeSinceStartupAsDouble;
            RunLoop();
        }

        private void Update()
        {
            if (webcam != null && webcam.didUpdateThisFrame)
            {
                captureRate.AddEvent(Time.realtimeSinceStartupAsDouble);
            }
        }

        private async void RunLoop()
        {
            try
            {
                while (running)
                {
                    double now = Time.realtimeSinceStartupAsDouble;
                    bool cameraReady = webcam != null && webcam.isPlaying && webcam.width > 16;
                    if (cameraReady && now - lastScheduleTime >= 1.0 / inferenceRateHz && flightGate.TryBegin())
                    {
                        lastScheduleTime = now;
                        try
                        {
                            HandObservation observation = await tracker.DetectAsync(webcam);
                            latest = observation;
                            durationsMs.Add(observation.InferenceMs);
                            inferenceRate.AddEvent(observation.Timestamp);
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception e)
                        {
                            lastError = $"{e.GetType().Name}: {e.Message}";
                        }
                        finally
                        {
                            flightGate.End();
                        }
                    }
                    else
                    {
                        await Awaitable.NextFrameAsync(destroyCancellationToken);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal on Play Mode exit / destruction.
            }
            finally
            {
                ReleaseResources();
            }
        }

        private void OnDestroy()
        {
            // Never force-cancel the in-flight awaitable: cancelling a running async
            // Awaitable double-completes it when the GPU readback later resumes (NRE).
            // Deferred tracker disposal + guards make the pipeline throw
            // OperationCanceledException naturally on its next resume point.
            running = false;
            tracker?.Dispose();
        }

        private void Fail(string message)
        {
            fatalError = message;
            Debug.LogError($"[BEFORE HandSmoke] {message}");
            enabled = false;
            ReleaseResources();
        }

        private void ReleaseResources()
        {
            if (tracker != null)
            {
                tracker.Dispose();
                tracker = null;
            }
            if (webcam != null)
            {
                if (webcam.isPlaying) webcam.Stop();
                Destroy(webcam);
                webcam = null;
            }
        }

        private void OnGUI()
        {
            const float panelW = 640f;
            const float panelH = 480f;
            float px = 12f;
            float py = 12f;
            Rect panel = new Rect(px, py, panelW, panelH);

            GUI.color = Color.black;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            if (webcam != null && webcam.width > 16)
            {
                // Mirrored (left-right flipped) camera view.
                GUI.DrawTextureWithTexCoords(panel, webcam, new Rect(1f, 0f, -1f, 1f));
            }
            else
            {
                GUI.Label(panel, "camera starting...");
            }

            bool present = HandPresent;
            if (present)
            {
                for (int i = 0; i < HandObservation.LandmarkCount; i++)
                {
                    Vector3 lm = latest.GetLandmark(i);
                    // Mirror X to match the mirrored view; image y-up -> GUI y-down.
                    float gx = px + (1f - lm.x) * panelW;
                    float gy = py + (1f - lm.y) * panelH;
                    GUI.color = Color.green;
                    GUI.DrawTexture(new Rect(gx - 3f, gy - 3f, 6f, 6f), Texture2D.whiteTexture);
                }
                GUI.color = Color.white;
            }

            var sb = new System.Text.StringBuilder(512);
            sb.AppendLine("BEFORE_HandSmoke (preflight step 4)");
            sb.AppendLine($"camera: '{BoundDeviceName}' (preferred: '{preferredDeviceName}')");
            sb.AppendLine($"requested: {requestedWidth}x{requestedHeight}@{requestedFps}  actual: {ActualWidth}x{ActualHeight}  captureFps(60s median): {MedianCaptureFps60s:F1}");
            sb.AppendLine($"presence: {(present ? "HAND" : "none")}  score: {LatestScore:F2}");
            sb.AppendLine($"inference: {CurrentInferenceHz:F0} Hz now, {MedianInferenceHz60s:F1} Hz median(60s), target {inferenceRateHz:F0} Hz, backend GPUCompute");
            sb.AppendLine($"latency: P50 {P50Ms:F1} ms  P95 {P95Ms:F1} ms  (n={DurationSampleCount})");
            sb.AppendLine($"in-flight: {InFlight} (max observed: {MaxInFlightObserved})");
            sb.AppendLine($"run time: {SecondsSinceRunStart:F0} s");
            sb.AppendLine($"last error: {(string.IsNullOrEmpty(LastError) ? "none" : LastError)}");

            GUI.color = Color.white;
            GUI.Label(new Rect(px + panelW + 16f, py, 620f, 400f), sb.ToString());
        }
    }
}
