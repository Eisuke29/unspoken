using System;
using UnityEngine;

namespace Before.Production
{
    public enum ProductionForkStage
    {
        None,
        Expanding,
        AwaitingDirection,
        Confirmed,
        Collapsing
    }

    /// <summary>
    /// Owns only the zero-distance fork interaction at beat 22. The frozen
    /// receiver/router/NulState path stays live in DirectionMode while world
    /// simulation is suspended. Presentation is delegated to the presenter.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProductionForkFlow : MonoBehaviour
    {
        public const int ForkBeatId = 22;
        public const float ExpandSeconds = 0.8f;
        public const float ConfirmSeconds = 1.0f;
        public const float CollapseSeconds = 0.6f;

        [SerializeField] private NulProductionController controller;
        [SerializeField] private ProductionCourseLoop course;
        [SerializeField] private ProductionObstacleJudge judge;
        [SerializeField] private ProductionGameLoop gameLoop;
        [SerializeField] private SidecarTeachingControlClient teachingControl;

        private float _stageElapsed;
        private int _activeStableId;
        private int _activePassCount;

        public ProductionForkStage Stage { get; private set; }
        public ProductionRoute ChosenRoute { get; private set; }
        public bool IsActive => Stage != ProductionForkStage.None;
        public float StageElapsed => _stageElapsed;
        public float WindowProgress => ProgressFor(Stage, _stageElapsed);

        public NulProductionController Controller { get => controller; set => controller = value; }
        public ProductionCourseLoop Course { get => course; set => course = value; }
        public ProductionObstacleJudge Judge { get => judge; set => judge = value; }
        public ProductionGameLoop GameLoop { get => gameLoop; set => gameLoop = value; }

        private void Awake()
        {
            if (controller == null) controller = GetComponent<NulProductionController>();
            if (course == null) course = GetComponent<ProductionCourseLoop>();
            if (judge == null) judge = GetComponent<ProductionObstacleJudge>();
            if (gameLoop == null) gameLoop = GetComponent<ProductionGameLoop>();
            if (teachingControl == null) teachingControl = GetComponent<SidecarTeachingControlClient>();
        }

        private void OnEnable()
        {
            if (controller != null) controller.Bus.OnCommand += HandleCommand;
            if (gameLoop != null) gameLoop.OnLoopReset += ResetFlow;
        }

        private void OnDisable()
        {
            if (controller != null) controller.Bus.OnCommand -= HandleCommand;
            if (gameLoop != null) gameLoop.OnLoopReset -= ResetFlow;
            if (IsActive) ReleaseExclusiveState();
        }

        private void Update()
        {
            if (!IsActive)
            {
                TryEnterFork();
                return;
            }
            if (controller == null || controller.Nul.Paused ||
                controller.EnergySuspended) return;

            _stageElapsed += Time.unscaledDeltaTime;
            if (Stage == ProductionForkStage.Expanding && _stageElapsed >= ExpandSeconds)
            {
                Stage = ProductionForkStage.AwaitingDirection;
                _stageElapsed = 0f;
            }
            else if (Stage == ProductionForkStage.Confirmed &&
                     _stageElapsed >= ConfirmSeconds)
            {
                BeginCollapse();
            }
            else if (Stage == ProductionForkStage.Collapsing &&
                     _stageElapsed >= CollapseSeconds)
            {
                FinishFork();
            }
        }

        public static bool TryMapDirection(CommandKind kind, out int direction)
        {
            if (kind == CommandKind.Left)
            {
                direction = -1;
                return true;
            }
            if (kind == CommandKind.Right)
            {
                direction = 1;
                return true;
            }
            direction = 0;
            return false;
        }

        public static float ProgressFor(ProductionForkStage stage, float elapsed)
        {
            switch (stage)
            {
                case ProductionForkStage.Expanding:
                    return Mathf.Clamp01(elapsed / ExpandSeconds);
                case ProductionForkStage.AwaitingDirection:
                case ProductionForkStage.Confirmed:
                    return 1f;
                case ProductionForkStage.Collapsing:
                    return 1f - Mathf.Clamp01(elapsed / CollapseSeconds);
                default:
                    return 0f;
            }
        }

        public void TryEnterFork()
        {
            if (IsActive || controller == null || course == null || judge == null) return;
            if (controller.Nul.Paused || controller.StorySuspended) return;
            if (gameLoop != null && (gameLoop.InRecovery || gameLoop.IsGameOver ||
                                     gameLoop.SelectedRoute != ProductionRoute.None)) return;
            if (course.SelectedRoute != ProductionRoute.None) return;

            ProductionCourseLoop.ObstacleSlot[] slots = course.Slots;
            for (int i = 0; i < slots.Length; i++)
            {
                ProductionCourseLoop.ObstacleSlot slot = slots[i];
                if (slot.BeatId != ForkBeatId ||
                    slot.Kind != ProductionObstacleKind.Direction) continue;
                if (controller.transform.position.x < slot.WorldX) return;
                BeginFork(slot);
                return;
            }
        }

        public void ResetFlow()
        {
            ReleaseExclusiveState();
            Stage = ProductionForkStage.None;
            ChosenRoute = ProductionRoute.None;
            _stageElapsed = 0f;
            _activeStableId = 0;
            _activePassCount = 0;
        }

        private void BeginFork(ProductionCourseLoop.ObstacleSlot slot)
        {
            if (teachingControl == null) teachingControl = GetComponent<SidecarTeachingControlClient>();
            controller.RecoverAt(new Vector2(slot.WorldX, 0f));
            controller.SetStorySuspended(true);
            controller.SetMovementSuspended(true);
            controller.Nul.DirectionMode = true;
            controller.Nul.LastDirection = 0;
            teachingControl?.SetDirectionMode(true);

            _activeStableId = slot.StableId;
            _activePassCount = slot.PassCount;
            ChosenRoute = ProductionRoute.None;
            Stage = ProductionForkStage.Expanding;
            _stageElapsed = 0f;
        }

        private void HandleCommand(CommandPacket packet)
        {
            if (!IsActive || ChosenRoute != ProductionRoute.None ||
                controller == null || controller.Nul.Paused) return;
            if (!TryMapDirection(packet.Kind, out int direction)) return;
            if (!judge.ResolveDirectionFork(_activeStableId, _activePassCount, direction)) return;

            ChosenRoute = ProductionCourseLoop.RouteForDirection(direction);
            controller.SetPresentationFacing(direction);
            Stage = ProductionForkStage.Confirmed;
            _stageElapsed = 0f;
        }

        private void BeginCollapse()
        {
            Stage = ProductionForkStage.Collapsing;
            _stageElapsed = 0f;
        }

        private void FinishFork()
        {
            ReleaseExclusiveState();
            Stage = ProductionForkStage.None;
            _stageElapsed = 0f;
            _activeStableId = 0;
            _activePassCount = 0;
        }

        private void ReleaseExclusiveState()
        {
            if (controller == null) return;
            controller.Nul.DirectionMode = false;
            controller.Nul.LastDirection = 0;
            controller.SetPresentationFacing(1);
            if (teachingControl == null) teachingControl = GetComponent<SidecarTeachingControlClient>();
            teachingControl?.SetDirectionMode(false);
            controller.SetStorySuspended(false);
            controller.SetMovementSuspended(false);
        }
    }
}
