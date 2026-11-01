using System;
using UnityEngine;

namespace Before.Production
{
    public enum ProductionCoreStage
    {
        None,
        Unfolding,
        Transferring,
        Disconnecting,
        IndependentHold
    }

    /// <summary>
    /// Owns CORE arrival and independent-power transfer timing only. The frozen
    /// receiver/router remains live; story suspension prevents gameplay reaction,
    /// judgement, and clock progress without replaying consumed commands.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProductionCoreFlow : MonoBehaviour
    {
        public const float UnfoldSeconds = 0.8f;
        public const float TransferSeconds = 1.2f;
        public const float DisconnectSeconds = 1.0f;

        [SerializeField] private ProductionBranchFlow branch;
        [SerializeField] private ProductionGameLoop gameLoop;
        [SerializeField] private NulProductionController controller;

        public event Action<ProductionCoreStage> OnStageChanged;
        public event Action OnIndependentPowerReady;

        public ProductionBranchFlow Branch { get => branch; set => branch = value; }
        public ProductionGameLoop GameLoop { get => gameLoop; set => gameLoop = value; }
        public NulProductionController Controller { get => controller; set => controller = value; }
        public ProductionCoreStage Stage { get; private set; }
        public float StageElapsed { get; private set; }
        public bool IsActive => Stage != ProductionCoreStage.None;
        public bool IndependentPower => Stage == ProductionCoreStage.IndependentHold;
        public float CoreWorldX { get; private set; }
        public float StageProgress => ProgressFor(Stage, StageElapsed);

        private void Awake()
        {
            if (branch == null) branch = GetComponent<ProductionBranchFlow>();
            if (gameLoop == null) gameLoop = GetComponent<ProductionGameLoop>();
            if (controller == null) controller = GetComponent<NulProductionController>();
        }

        private void OnEnable()
        {
            if (branch == null) branch = GetComponent<ProductionBranchFlow>();
            if (gameLoop == null) gameLoop = GetComponent<ProductionGameLoop>();
            if (branch != null) branch.OnBranchCompleted += HandleBranchCompleted;
            if (gameLoop != null) gameLoop.OnLoopReset += ResetCore;
        }

        private void OnDisable()
        {
            if (branch != null) branch.OnBranchCompleted -= HandleBranchCompleted;
            if (gameLoop != null) gameLoop.OnLoopReset -= ResetCore;
        }

        private void Update()
        {
            if (Stage == ProductionCoreStage.None)
            {
                if (branch != null && branch.BranchCompleted)
                    BeginCore(branch.ActiveRoute);
                return;
            }
            if (Stage == ProductionCoreStage.IndependentHold || controller == null ||
                controller.Nul.Paused || controller.EnergySuspended) return;

            StageElapsed += Time.unscaledDeltaTime;
            switch (Stage)
            {
                case ProductionCoreStage.Unfolding:
                    if (StageElapsed >= UnfoldSeconds)
                        ChangeStage(ProductionCoreStage.Transferring);
                    break;
                case ProductionCoreStage.Transferring:
                    if (StageElapsed >= TransferSeconds)
                        ChangeStage(ProductionCoreStage.Disconnecting);
                    break;
                case ProductionCoreStage.Disconnecting:
                    if (StageElapsed >= DisconnectSeconds)
                    {
                        ChangeStage(ProductionCoreStage.IndependentHold);
                        OnIndependentPowerReady?.Invoke();
                    }
                    break;
            }
        }

        public static float ProgressFor(ProductionCoreStage stage, float elapsed)
        {
            switch (stage)
            {
                case ProductionCoreStage.Unfolding:
                    return Mathf.Clamp01(elapsed / UnfoldSeconds);
                case ProductionCoreStage.Transferring:
                    return Mathf.Clamp01(elapsed / TransferSeconds);
                case ProductionCoreStage.Disconnecting:
                    return Mathf.Clamp01(elapsed / DisconnectSeconds);
                case ProductionCoreStage.IndependentHold:
                    return 1f;
                default:
                    return 0f;
            }
        }

        public void BeginCore(ProductionRoute route)
        {
            if (IsActive || route == ProductionRoute.None || controller == null ||
                branch == null) return;
            CoreWorldX = branch.CoreApproachWorldX;
            controller.RecoverAt(new Vector2(CoreWorldX, 0f));
            controller.SetStorySuspended(true);
            controller.SetMovementSuspended(true);
            ChangeStage(ProductionCoreStage.Unfolding);
        }

        public void ResetCore()
        {
            Stage = ProductionCoreStage.None;
            StageElapsed = 0f;
            CoreWorldX = 0f;
            if (controller != null)
            {
                controller.SetStorySuspended(false);
                controller.SetMovementSuspended(false);
            }
            OnStageChanged?.Invoke(Stage);
        }

        private void HandleBranchCompleted(ProductionRoute route) => BeginCore(route);

        private void ChangeStage(ProductionCoreStage stage)
        {
            Stage = stage;
            StageElapsed = 0f;
            OnStageChanged?.Invoke(stage);
        }
    }
}
