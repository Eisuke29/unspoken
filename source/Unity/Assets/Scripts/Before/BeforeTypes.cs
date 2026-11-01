using UnityEngine;

namespace BeforeExperience
{
    public enum BeforeEventSource
    {
        None,
        Human,
        AI
    }

    public struct CursorMotionState
    {
        public Vector2 Position;
        public Vector2 Velocity;
        public Vector2 Acceleration;
        public Vector2 Direction;
        public float Speed;
        public float DirectionStability;
        public float Curvature;
        public float Hesitation;
        public bool IsInsideScreen;
    }

    public struct CandidateMetrics
    {
        public float Distance;
        public float Alignment;
        public float Angle;
        public float ApproachSpeed;
        public float EstimatedArrivalTime;
    }

    public sealed class IntentFrame
    {
        public readonly float[][] Features;
        public readonly CandidateMetrics[] Metrics;
        public readonly float[] Probabilities;
        public int BestTargetId;
        public float BestProbability;

        public IntentFrame(int candidateCount, int featureCount)
        {
            Features = new float[candidateCount][];
            Metrics = new CandidateMetrics[candidateCount];
            Probabilities = new float[candidateCount];

            for (int i = 0; i < candidateCount; i++)
            {
                Features[i] = new float[featureCount];
            }
        }
    }
}

