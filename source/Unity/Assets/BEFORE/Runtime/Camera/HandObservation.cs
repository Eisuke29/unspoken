using UnityEngine;

namespace Before.Capture
{
    /// <summary>
    /// Immutable result of one completed hand-inference pass.
    /// Landmark positions are normalized [0,1] source-image coordinates (x right, y up), z in image-height units.
    /// </summary>
    public sealed class HandObservation
    {
        public const int LandmarkCount = 21;

        public readonly double Timestamp;
        public readonly bool HandPresent;
        public readonly float Score;
        public readonly float InferenceMs;
        private readonly Vector3[] landmarks;

        public HandObservation(double timestamp, bool handPresent, float score, float inferenceMs, Vector3[] landmarkSource)
        {
            Timestamp = timestamp;
            HandPresent = handPresent;
            Score = score;
            InferenceMs = inferenceMs;
            landmarks = new Vector3[LandmarkCount];
            if (landmarkSource != null)
            {
                int n = Mathf.Min(LandmarkCount, landmarkSource.Length);
                for (int i = 0; i < n; i++)
                {
                    landmarks[i] = landmarkSource[i];
                }
            }
        }

        public static HandObservation NoHand(double timestamp, float score, float inferenceMs)
        {
            return new HandObservation(timestamp, false, score, inferenceMs, null);
        }

        public Vector3 GetLandmark(int index)
        {
            return landmarks[Mathf.Clamp(index, 0, LandmarkCount - 1)];
        }
    }
}
