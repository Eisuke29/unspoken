using System;
using UnityEngine;

namespace Before.Production
{
    public enum ProductionEndingStage
    {
        None,
        TestSteps,
        LookBack,
        MemoryTraces,
        Bow,
        Departing,
        WorldFade,
        TraceFade,
        CompleteMessage,
        EndingHold
    }

    /// <summary>
    /// Owns farewell timing and the indefinite reset-ready ending state only.
    /// It never exits the application or Unity Play Mode. Existing R restart
    /// delegates to ProductionGameLoop.ResetLoop.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProductionEndingFlow : MonoBehaviour
    {
        public const float TestStepsSeconds = 0.8f;
        public const float LookBackSeconds = 0.6f;
        public const float TraceDrawSeconds = 0.55f;
        public const float TraceGapSeconds = 0.2f;
        public const int TraceCount = 3;
        public const float MemoryTracesSeconds = TraceDrawSeconds * TraceCount +
                                                  TraceGapSeconds * (TraceCount - 1);
        public const float BowSeconds = 1.1f;
        public const float DepartSeconds = 1.8f;
        public const float WorldFadeSeconds = 0.8f;
        public const float TraceFadeSeconds = 0.7f;
        public const float CompleteMessageSeconds = 0.8f;

        [SerializeField] private ProductionCoreFlow core;
        [SerializeField] private ProductionGameLoop gameLoop;
        [SerializeField] private NulProductionController controller;

        public event Action<ProductionEndingStage> OnStageChanged;
        public event Action OnEndingHoldEntered;

        public ProductionCoreFlow Core { get => core; set => core = value; }
        public ProductionGameLoop GameLoop { get => gameLoop; set => gameLoop = value; }
        public NulProductionController Controller { get => controller; set => controller = value; }
        public ProductionEndingStage Stage { get; private set; }
        public float StageElapsed { get; private set; }
        public bool IsActive => Stage != ProductionEndingStage.None;
        public bool GameplayEnded => Stage == ProductionEndingStage.EndingHold;
        public bool GameplayLocked => IsActive;
        public float LockedWorldX { get; private set; }
        public float StageProgress => ProgressFor(Stage, StageElapsed);

        private void Awake()
        {
            if (core == null) core = GetComponent<ProductionCoreFlow>();
            if (gameLoop == null) gameLoop = GetComponent<ProductionGameLoop>();
            if (controller == null) controller = GetComponent<NulProductionController>();
        }

        private void OnEnable()
        {
            if (core == null) core = GetComponent<ProductionCoreFlow>();
            if (gameLoop == null) gameLoop = GetComponent<ProductionGameLoop>();
            if (core != null) core.OnIndependentPowerReady += BeginFarewell;
            if (gameLoop != null) gameLoop.OnLoopReset += ResetEnding;
        }

        private void OnDisable()
        {
            if (core != null) core.OnIndependentPowerReady -= BeginFarewell;
            if (gameLoop != null) gameLoop.OnLoopReset -= ResetEnding;
        }

        private void Update()
        {
            if (Stage == ProductionEndingStage.None)
            {
                if (core != null && core.IndependentPower) BeginFarewell();
                return;
            }

            if (Stage == ProductionEndingStage.EndingHold)
            {
#if ENABLE_INPUT_SYSTEM
                var keyboard = UnityEngine.InputSystem.Keyboard.current;
                if (keyboard != null && keyboard.rKey.wasPressedThisFrame)
                    RestartFromEnding();
#endif
                return;
            }

            if (controller == null || controller.Nul.Paused) return;
            StageElapsed += Time.unscaledDeltaTime;
            if (StageElapsed < DurationFor(Stage)) return;

            switch (Stage)
            {
                case ProductionEndingStage.TestSteps: ChangeStage(ProductionEndingStage.LookBack); break;
                case ProductionEndingStage.LookBack: ChangeStage(ProductionEndingStage.MemoryTraces); break;
                case ProductionEndingStage.MemoryTraces: ChangeStage(ProductionEndingStage.Bow); break;
                case ProductionEndingStage.Bow: ChangeStage(ProductionEndingStage.Departing); break;
                case ProductionEndingStage.Departing: ChangeStage(ProductionEndingStage.WorldFade); break;
                case ProductionEndingStage.WorldFade: ChangeStage(ProductionEndingStage.TraceFade); break;
                case ProductionEndingStage.TraceFade: ChangeStage(ProductionEndingStage.CompleteMessage); break;
                case ProductionEndingStage.CompleteMessage:
                    ChangeStage(ProductionEndingStage.EndingHold);
                    OnEndingHoldEntered?.Invoke();
                    break;
            }
        }

        public static float DurationFor(ProductionEndingStage stage)
        {
            switch (stage)
            {
                case ProductionEndingStage.TestSteps: return TestStepsSeconds;
                case ProductionEndingStage.LookBack: return LookBackSeconds;
                case ProductionEndingStage.MemoryTraces: return MemoryTracesSeconds;
                case ProductionEndingStage.Bow: return BowSeconds;
                case ProductionEndingStage.Departing: return DepartSeconds;
                case ProductionEndingStage.WorldFade: return WorldFadeSeconds;
                case ProductionEndingStage.TraceFade: return TraceFadeSeconds;
                case ProductionEndingStage.CompleteMessage: return CompleteMessageSeconds;
                default: return 0f;
            }
        }

        public static float ProgressFor(ProductionEndingStage stage, float elapsed)
        {
            if (stage == ProductionEndingStage.EndingHold) return 1f;
            float duration = DurationFor(stage);
            return duration <= 0f ? 0f : Mathf.Clamp01(elapsed / duration);
        }

        public void BeginFarewell()
        {
            if (IsActive || core == null || !core.IndependentPower || controller == null) return;
            LockedWorldX = controller.transform.position.x;
            controller.SetStorySuspended(true);
            controller.SetMovementSuspended(true);
            ChangeStage(ProductionEndingStage.TestSteps);
        }

        public void RestartFromEnding()
        {
            if (!GameplayEnded || gameLoop == null) return;
            gameLoop.RestartGame();
        }

        public void ResetEnding()
        {
            Stage = ProductionEndingStage.None;
            StageElapsed = 0f;
            LockedWorldX = 0f;
            if (controller != null)
            {
                controller.Nul.Paused = false;
                controller.SetStorySuspended(false);
                controller.SetMovementSuspended(false);
            }
            OnStageChanged?.Invoke(Stage);
        }

        private void ChangeStage(ProductionEndingStage stage)
        {
            Stage = stage;
            StageElapsed = 0f;
            OnStageChanged?.Invoke(stage);
        }
    }
}
