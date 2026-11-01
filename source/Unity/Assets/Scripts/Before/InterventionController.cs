using UnityEngine;

namespace BeforeExperience
{
    public sealed class InterventionController : MonoBehaviour
    {
        [SerializeField] private int minimumLearnedSelections = 6;
        [SerializeField] private float confidenceThresholdAtMaturityZero = 0.97f;
        [SerializeField] private float confidenceThresholdAtMaturityOne = 0.62f;
        [SerializeField] private float leadTimeAtMaturityZero = 0.05f;
        [SerializeField] private float leadTimeAtMaturityOne = 0.38f;
        [SerializeField] private float pendingWindowSeconds = 0.85f;
        [SerializeField] private float cooldownSeconds = 0.45f;
        [SerializeField] private float minimumCursorSpeed = 0.12f;
        [SerializeField] private float minimumDirectionStability = 0.5f;
        [SerializeField] private float minimumApproachSpeed = 0.05f;

        private bool pending;
        private double pendingDeadline;
        private double cooldownUntil;
        private Vector2 savedWorldCenter;
        private float savedWorldRadius;

        public bool HasPending => pending;
        public int PendingTargetId { get; private set; } = -1;
        public IntentFrame SavedFrame { get; private set; }
        public int InterventionCount { get; private set; }
        public int SuccessCount { get; private set; }
        public int FailureCount { get; private set; }
        public int TimeoutCount { get; private set; }

        public float CurrentConfidenceThreshold(float maturity)
        {
            return Mathf.Lerp(confidenceThresholdAtMaturityZero, confidenceThresholdAtMaturityOne, Mathf.Clamp01(maturity));
        }

        public float CurrentLeadTime(float maturity)
        {
            return Mathf.Lerp(leadTimeAtMaturityZero, leadTimeAtMaturityOne, Mathf.Clamp01(maturity));
        }

        public bool TryBegin(
            IntentFrame frame,
            CursorMotionState motion,
            OnlineIntentPredictor predictor,
            TargetNode target,
            double now)
        {
            if (pending || frame == null || predictor == null || target == null)
            {
                return false;
            }

            if (now < cooldownUntil
                || !target.IsSelectable
                || predictor.LearnedSelectionCount < minimumLearnedSelections
                || !motion.IsInsideScreen)
            {
                return false;
            }

            float maturity = predictor.Maturity;
            CandidateMetrics metrics = frame.Metrics[target.TargetId];

            if (frame.Probabilities[target.TargetId] < CurrentConfidenceThreshold(maturity)
                || metrics.ApproachSpeed <= minimumApproachSpeed
                || motion.Speed < minimumCursorSpeed
                || motion.DirectionStability < minimumDirectionStability
                || metrics.EstimatedArrivalTime > CurrentLeadTime(maturity))
            {
                return false;
            }

            pending = true;
            PendingTargetId = target.TargetId;
            SavedFrame = frame;
            savedWorldCenter = target.WorldPosition;
            savedWorldRadius = target.WorldRadius * 1.15f;
            pendingDeadline = now + pendingWindowSeconds;
            InterventionCount++;
            return true;
        }

        public bool IsWithinSavedRegion(Vector2 worldPoint)
        {
            return pending && Vector2.Distance(worldPoint, savedWorldCenter) <= savedWorldRadius;
        }

        public void ResolveSuccess(double now)
        {
            if (!pending)
            {
                return;
            }

            SuccessCount++;
            ClearPending(now);
        }

        public void ResolveFailure(double now)
        {
            if (!pending)
            {
                return;
            }

            FailureCount++;
            ClearPending(now);
        }

        public bool CheckTimeout(double now)
        {
            if (!pending || now < pendingDeadline)
            {
                return false;
            }

            TimeoutCount++;
            ClearPending(now);
            return true;
        }

        public void ResetState()
        {
            pending = false;
            PendingTargetId = -1;
            SavedFrame = null;
            pendingDeadline = 0.0;
            cooldownUntil = 0.0;
            InterventionCount = 0;
            SuccessCount = 0;
            FailureCount = 0;
            TimeoutCount = 0;
        }

        private void ClearPending(double now)
        {
            pending = false;
            PendingTargetId = -1;
            SavedFrame = null;
            cooldownUntil = now + cooldownSeconds;
        }
    }
}
