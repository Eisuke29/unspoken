using UnityEngine;

namespace Before.Config
{
    /// <summary>Segmentation/DTW/classification thresholds. Initial values fixed by recognition-spec.md.</summary>
    public sealed class BeforeRecognitionConfig : ScriptableObject
    {
        public float oneEuroMinCutoff = 1.2f;
        public float oneEuroBeta = 0.08f;
        public float oneEuroDCutoff = 1.0f;
        public float armSpeed = 0.07f;
        public float armSeconds = 0.35f;
        public float startSpeed = 0.18f;
        public int startFrames = 3;
        public int endFrames = 4;
        public float maxSegmentSeconds = 1.4f;
        public float minSegmentSeconds = 0.6f;
        public int resampleCount = 32;
        public float minScaleDiagonal = 0.08f;
        public int dtwBand = 5;
        public float weightPosition = 0.65f;
        public float weightTangent = 0.20f;
        public float weightSpeed = 0.05f;
        public float weightDuration = 0.10f;
        public float seedConflictDistance = 0.38f;
        public float radiusSeedFactor = 2.2f;
        public float radiusBias = 0.06f;
        public float radiusMin = 0.18f;
        public float radiusMax = 0.42f;
        public float classSeparationFactor = 1.15f;
        public float highConfidenceN1 = 0.70f;
        public float highConfidenceMargin = 0.18f;
        public float noMatchN1 = 1.00f;
        public float adaptEma = 0.15f;
        public float adaptDriftGuardFactor = 0.50f;
        public float radiusEmaOld = 0.90f;
        public float radiusDriftLimit = 0.20f;
        public float directionMinDx = 0.35f;
        public float directionAxisRatio = 1.8f;
        public float directionSignShare = 0.70f;

        // --- Recognition V2 (D-091): multichannel 21-landmark dynamic gestures ---
        public int v2Frames = 32;                 // time-normalized frame count
        public int v2DtwBand = 8;                 // Sakoe-Chiba band (allows ~0.75-1.30 time warp over 32 frames)
        public float v2TrimSpeed = 0.06f;         // palm speed (norm/s) below which leading/trailing frames are stillness
        public int v2PreRoll = 2;                 // frames kept before detected motion start
        public int v2PostRoll = 2;                // frames kept after detected motion end
        public int v2BoundaryShiftMax = 2;        // evaluate boundary-shifted candidates +/- this many frames
        // channel weights
        public float v2WeightPalmMotion = 0.40f;
        public float v2WeightHandShape = 0.30f;
        public float v2WeightPose = 0.15f;        // joint angles + fingertip relations
        public float v2WeightTangent = 0.10f;
        public float v2WeightDuration = 0.05f;
        // classification / teaching
        public float v2RadiusFactor = 1.6f;       // radius = factor * cohesion (mean intra-class distance), clamped
        public float v2RadiusMin = 0.12f;
        public float v2RadiusMax = 0.60f;
        public float v2CohesionMax = 0.34f;       // max mean pairwise distance for a cohesive teaching set
        public float v2OutlierFactor = 1.8f;      // a sample is an outlier if its mean dist > factor * others' mean
        public int v2MinTeachSamples = 3;
        public int v2MaxTeachSamples = 5;
        public int v2MaxRealSamples = 8;          // continual-adaptation cap per class
        public float v2HighConfN1 = 0.80f;
        public float v2HighConfMargin = 0.15f;
        public float v2NoMatchN1 = 1.10f;
        public float v2ClassSeparationFactor = 1.15f;
        public float v2AdaptMinMargin = 0.20f;    // min (n2-n1) to allow continual adaptation
        public float v2AdaptQualityMin = 0.80f;   // min tracking quality to allow continual adaptation
    }
}
