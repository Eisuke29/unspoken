using System.Collections.Generic;
using UnityEngine;

namespace BeforeExperience
{
    public sealed class OnlineIntentPredictor : MonoBehaviour
    {
        public const int CandidateCount = 6;
        public const int FeatureCount = 21;

        [SerializeField] private float learningRate = 0.08f;
        [SerializeField] private float l2Regularization = 0.0005f;
        [SerializeField] private float gradientLimit = 2f;
        [SerializeField] private float softmaxTemperature = 0.65f;

        private readonly float[] weights = new float[FeatureCount];
        private readonly int[] idSelections = new int[CandidateCount];

        public int LearnedSelectionCount { get; private set; }
        public float Maturity => 1f - Mathf.Exp(-LearnedSelectionCount / 22f);

        public IntentFrame BuildFrame(CursorMotionState motion, IReadOnlyList<TargetNode> targets)
        {
            IntentFrame frame = new IntentFrame(CandidateCount, FeatureCount);
            float bestLogit = float.NegativeInfinity;
            int bestId = 0;

            for (int id = 0; id < CandidateCount; id++)
            {
                TargetNode target = FindTarget(targets, id);
                Vector2 targetPosition = target != null ? target.ViewportPosition : new Vector2(0.5f, 0.5f);
                BuildCandidateFeatures(frame.Features[id], out CandidateMetrics metrics, motion, targetPosition, id);
                frame.Metrics[id] = metrics;

                float logit = Mathf.Clamp(Dot(frame.Features[id], weights) / Mathf.Max(0.1f, softmaxTemperature), -20f, 20f);
                frame.Probabilities[id] = logit;
                if (logit > bestLogit)
                {
                    bestLogit = logit;
                    bestId = id;
                }
            }

            float sum = 0f;
            for (int id = 0; id < CandidateCount; id++)
            {
                float probability = Mathf.Exp(frame.Probabilities[id] - bestLogit);
                frame.Probabilities[id] = float.IsFinite(probability) ? probability : 0f;
                sum += frame.Probabilities[id];
            }

            if (!float.IsFinite(sum) || sum <= 0.000001f)
            {
                sum = CandidateCount;
                for (int id = 0; id < CandidateCount; id++)
                {
                    frame.Probabilities[id] = 1f;
                }
            }

            for (int id = 0; id < CandidateCount; id++)
            {
                frame.Probabilities[id] /= sum;
            }

            frame.BestTargetId = bestId;
            frame.BestProbability = frame.Probabilities[bestId];
            return frame;
        }

        public void Learn(IntentFrame frame, int selectedId)
        {
            if (frame == null || selectedId < 0 || selectedId >= CandidateCount)
            {
                return;
            }

            for (int id = 0; id < CandidateCount; id++)
            {
                float score = Mathf.Clamp(Dot(frame.Features[id], weights), -20f, 20f);
                float prediction = 1f / (1f + Mathf.Exp(-score));
                float label = id == selectedId ? 1f : 0f;
                float error = prediction - label;

                for (int feature = 0; feature < FeatureCount; feature++)
                {
                    float gradient = error * frame.Features[id][feature] + l2Regularization * weights[feature];
                    gradient = Mathf.Clamp(float.IsFinite(gradient) ? gradient : 0f, -gradientLimit, gradientLimit);
                    weights[feature] = Mathf.Clamp(weights[feature] - learningRate * gradient, -12f, 12f);
                }
            }

            idSelections[selectedId]++;
            LearnedSelectionCount++;
        }

        public void ResetModel()
        {
            System.Array.Clear(weights, 0, weights.Length);
            System.Array.Clear(idSelections, 0, idSelections.Length);
            LearnedSelectionCount = 0;
        }

        private void BuildCandidateFeatures(
            float[] features,
            out CandidateMetrics metrics,
            CursorMotionState motion,
            Vector2 targetPosition,
            int targetId)
        {
            Vector2 offset = targetPosition - motion.Position;
            float distance = Mathf.Clamp(offset.magnitude, 0f, 1.4143f);
            Vector2 targetDirection = distance > 0.0001f ? offset / distance : Vector2.zero;
            float alignment = motion.Direction.sqrMagnitude > 0f ? Vector2.Dot(motion.Direction, targetDirection) : 0f;
            alignment = Mathf.Clamp(alignment, -1f, 1f);
            float angle = Mathf.Acos(alignment);
            float approachSpeed = Vector2.Dot(motion.Velocity, targetDirection);
            float eta = approachSpeed > 0.001f ? distance / approachSpeed : 10f;
            eta = Mathf.Clamp(float.IsFinite(eta) ? eta : 10f, 0f, 10f);
            float prior = (idSelections[targetId] + 1f) / (LearnedSelectionCount + CandidateCount);

            metrics = new CandidateMetrics
            {
                Distance = distance,
                Alignment = alignment,
                Angle = angle,
                ApproachSpeed = approachSpeed,
                EstimatedArrivalTime = eta
            };

            features[0] = 1f;
            features[1] = 1f - Mathf.Clamp01(distance / 1.4143f);
            features[2] = (alignment + 1f) * 0.5f;
            features[3] = Mathf.Clamp(approachSpeed / 2f, -1f, 1f);
            features[4] = 1f - Mathf.Clamp01(eta);
            features[5] = Mathf.Clamp01(motion.Speed / 2f);
            features[6] = Mathf.Clamp01(motion.Acceleration.magnitude / 12f);
            features[7] = Mathf.Clamp01(motion.DirectionStability);
            features[8] = 1f - Mathf.Clamp01(motion.Curvature);
            features[9] = Mathf.Clamp01(motion.Hesitation);
            features[10] = Mathf.Clamp01(prior);
            features[11] = Mathf.Clamp(offset.x, -1f, 1f);
            features[12] = Mathf.Clamp(offset.y, -1f, 1f);
            features[13] = Mathf.Clamp(targetPosition.x * 2f - 1f, -1f, 1f);
            features[14] = Mathf.Clamp(targetPosition.y * 2f - 1f, -1f, 1f);

            for (int id = 0; id < CandidateCount; id++)
            {
                features[15 + id] = id == targetId ? 1f : 0f;
            }
        }

        private static TargetNode FindTarget(IReadOnlyList<TargetNode> targets, int id)
        {
            for (int i = 0; i < targets.Count; i++)
            {
                if (targets[i] != null && targets[i].TargetId == id)
                {
                    return targets[i];
                }
            }

            return null;
        }

        private static float Dot(float[] a, float[] b)
        {
            float sum = 0f;
            int length = Mathf.Min(a.Length, b.Length);
            for (int i = 0; i < length; i++)
            {
                sum += a[i] * b[i];
            }

            return float.IsFinite(sum) ? sum : 0f;
        }
    }
}

