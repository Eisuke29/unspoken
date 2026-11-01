using UnityEngine;

namespace Before.Config
{
    /// <summary>Camera/inference/runtime thresholds. Initial values fixed by design-spec.md.</summary>
    public sealed class BeforeRuntimeConfig : ScriptableObject
    {
        public string preferredDeviceName = "Integrated Camera";
        public int captureWidth = 640;
        public int captureHeight = 480;
        public int captureFps = 30;
        public float inferenceRateHz = 15f;
        public float presenceTrackMin = 0.55f;
        public float presenceCaptureMin = 0.65f;
        public int captureQualityFrames = 3;
        public float handStableBindSeconds = 0.5f;
        public float handRebindLossSeconds = 1.2f;
        public float lossJitterSeconds = 0.3f;
        public float lossFragmentSeconds = 0.8f;
        public float lossCollapseSeconds = 1.8f;
        public float reconnectStableSeconds = 0.5f;
        public float rechargeSeconds = 1.0f;
        public float lowLightLuma = 0.08f;
        public float lowLightClearLuma = 0.11f;
        public int logicalWidth = 320;
        public int logicalHeight = 180;
        public int logicalScale = 6;
    }
}
