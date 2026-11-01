using System;
using UnityEngine;

namespace Before.Production
{
    /// <summary>
    /// Resolves the current course beat against NUL's already-applied command
    /// state. This component never parses commands and never changes recognition,
    /// UDP session, or dedup behaviour.
    /// </summary>
    public sealed class ProductionObstacleJudge : MonoBehaviour
    {
        public const float PreparationLead = 48f;
        public const float StopArmMinimumLead = 8f;
        public const float JumpClearanceY = 6f;
        public const float FeedbackSeconds = 0.8f;

        [Serializable]
        public struct ObstacleResult
        {
            public int StableId;
            public int PassCount;
            public ProductionObstacleKind Kind;
            public float WorldX;
            public bool Success;
            public int Direction;
        }

        [SerializeField] private NulProductionController controller;
        [SerializeField] private ProductionCourseLoop course;

        private int[] _observedPass;
        private bool[] _resolved;
        private bool[] _stopPrepared;
        private bool[] _directionWindow;

        public event Action<ObstacleResult> OnResolved;

        public NulProductionController Controller
        {
            get => controller;
            set => controller = value;
        }

        public ProductionCourseLoop Course
        {
            get => course;
            set => course = value;
        }

        public int SuccessCount { get; private set; }
        public int FailureCount { get; private set; }
        public ObstacleResult LatestResult { get; private set; }

        private void Awake()
        {
            if (controller == null) controller = GetComponent<NulProductionController>();
            if (course == null) course = GetComponent<ProductionCourseLoop>();
            ResetJudgement();
        }

        private void OnEnable()
        {
            if (course != null && (_observedPass == null ||
                                   _observedPass.Length != course.Slots.Length))
                ResetJudgement();
        }

        private void LateUpdate() => EvaluateNow();

        public void ResetJudgement()
        {
            int n = course != null && course.Slots != null
                ? course.Slots.Length : ProductionCourseLoop.ObstacleCount;
            _observedPass = new int[n];
            _resolved = new bool[n];
            _stopPrepared = new bool[n];
            _directionWindow = new bool[n];
            for (int i = 0; i < n; i++) _observedPass[i] = -1;
            SuccessCount = 0;
            FailureCount = 0;
            LatestResult = default;
            if (controller != null)
            {
                controller.Nul.DirectionMode = false;
                controller.Nul.LastDirection = 0;
            }
        }

        public void EvaluateNow() => EvaluateNowAt(controller != null ? controller.GameplayTime : Time.time);

        public void EvaluateNowAt(float elapsedSeconds)
        {
            if (controller == null || course == null) return;
            if (controller.Nul.Paused || controller.StorySuspended ||
                controller.EnergySuspended) return;
            ProductionCourseLoop.ObstacleSlot[] slots = course.Slots;

            for (int i = 0; i < slots.Length; i++)
            {
                ProductionCourseLoop.ObstacleSlot slot = slots[i];
                if (_observedPass[i] != slot.PassCount)
                {
                    _observedPass[i] = slot.PassCount;
                    _resolved[i] = false;
                    _stopPrepared[i] = false;
                    _directionWindow[i] = false;
                }

                if (_resolved[i]) continue;
                float lead = slot.WorldX - controller.transform.position.x;

                if (slot.Kind == ProductionObstacleKind.Stop &&
                    lead <= PreparationLead && lead >= StopArmMinimumLead &&
                    controller.Nul.StopActive)
                {
                    _stopPrepared[i] = true;
                }

                if (slot.Kind == ProductionObstacleKind.Direction &&
                    lead <= PreparationLead && lead > 0f && !_directionWindow[i])
                {
                    _directionWindow[i] = true;
                    controller.Nul.LastDirection = 0;
                    controller.Nul.DirectionMode = true;
                }

                if (slot.Kind == ProductionObstacleKind.Direction &&
                    _directionWindow[i] && controller.Nul.LastDirection != 0)
                {
                    Resolve(i, slot, true, controller.Nul.LastDirection);
                    controller.Nul.DirectionMode = false;
                    continue;
                }

                if (lead > 0f) continue;

                bool success = IsSatisfied(slot.Kind,
                    controller.Airborne && controller.transform.position.y >= JumpClearanceY,
                    controller.CrouchColliderActive,
                    _stopPrepared[i],
                    controller.Nul.LastDirection,
                    ProductionCourseLoop.EnergyWallIsOpen(elapsedSeconds));
                Resolve(i, slot, success, controller.Nul.LastDirection);
                if (slot.Kind == ProductionObstacleKind.Direction)
                    controller.Nul.DirectionMode = false;
            }
        }

        public static bool IsSatisfied(ProductionObstacleKind kind, bool jumpClear,
                                       bool crouching, bool stopPrepared, int direction,
                                       bool stopWallOpen = true)
        {
            switch (kind)
            {
                case ProductionObstacleKind.Jump: return jumpClear;
                case ProductionObstacleKind.Crouch: return crouching;
                case ProductionObstacleKind.Stop: return stopPrepared && stopWallOpen;
                case ProductionObstacleKind.Direction: return direction != 0;
                default: return false;
            }
        }

        public void RearmCurrentPass(int stableId)
        {
            int index = stableId - 1;
            if (index < 0 || index >= _observedPass.Length) return;
            _observedPass[index] = -1;
            _resolved[index] = false;
            _stopPrepared[index] = false;
            _directionWindow[index] = false;
        }

        /// <summary>
        /// Resolves the beat-22 Direction slot while ForkMap owns the exclusive
        /// input window and ordinary LateUpdate judgement is story-suspended.
        /// The same source-agnostic result event still drives route state,
        /// feedback, and audio.
        /// </summary>
        public bool ResolveDirectionFork(int stableId, int passCount, int direction)
        {
            if (direction == 0 || course == null || _observedPass == null) return false;
            ProductionCourseLoop.ObstacleSlot[] slots = course.Slots;
            for (int i = 0; i < slots.Length; i++)
            {
                ProductionCourseLoop.ObstacleSlot slot = slots[i];
                if (slot.StableId != stableId || slot.PassCount != passCount ||
                    slot.Kind != ProductionObstacleKind.Direction) continue;
                if (_observedPass[i] != slot.PassCount)
                {
                    _observedPass[i] = slot.PassCount;
                    _resolved[i] = false;
                    _stopPrepared[i] = false;
                    _directionWindow[i] = false;
                }
                if (_resolved[i]) return false;
                Resolve(i, slot, true, direction);
                return true;
            }
            return false;
        }

        private void Resolve(int index, ProductionCourseLoop.ObstacleSlot slot,
                             bool success, int direction)
        {
            _resolved[index] = true;
            if (success) SuccessCount++;
            else FailureCount++;

            LatestResult = new ObstacleResult
            {
                StableId = slot.StableId,
                PassCount = slot.PassCount,
                Kind = slot.Kind,
                WorldX = slot.WorldX,
                Success = success,
                Direction = direction
            };
            OnResolved?.Invoke(LatestResult);
        }
    }
}
