using UnityEngine;

namespace Before.Production
{
    public enum ProductionEnergyState
    {
        Connected,
        Fragmenting,
        Depleted,
        Collapsed,
        Recharging,
        Independent
    }

    /// <summary>
    /// Restores the approved tracking/energy recovery timing without changing
    /// gesture recognition. It freezes only gameplay; UDP, Sidecar, hand HUD and
    /// unscaled presentation continue to update.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProductionEnergyRecoveryFlow : MonoBehaviour
    {
        public const float JitterSeconds = 0.30f;
        public const float DepletedSeconds = 0.80f;
        public const float CollapsedSeconds = 1.80f;
        public const float ReturnStableSeconds = 0.50f;
        public const float RechargeSeconds = 1.00f;

        [SerializeField] private NulProductionController controller;
        [SerializeField] private ProductionShellFlow shell;
        [SerializeField] private ProductionCoreFlow core;
        [SerializeField] private ProductionGameLoop gameLoop;

        private float _lossStartedAt = float.NegativeInfinity;
        private float _returnStableStartedAt = float.NegativeInfinity;
        private float _rechargeStartedAt = float.NegativeInfinity;

        public NulProductionController Controller
        {
            get => controller;
            set => controller = value;
        }
        public ProductionShellFlow Shell { get => shell; set => shell = value; }
        public ProductionCoreFlow Core { get => core; set => core = value; }
        public ProductionGameLoop GameLoop
        {
            get => gameLoop;
            set => gameLoop = value;
        }

        public ProductionEnergyState State { get; private set; } =
            ProductionEnergyState.Connected;
        public float LossSeconds => _lossStartedAt > float.NegativeInfinity
            ? Mathf.Max(0f, Time.unscaledTime - _lossStartedAt) : 0f;
        public float RecoveryTimer => State == ProductionEnergyState.Recharging &&
            _rechargeStartedAt > float.NegativeInfinity
                ? Mathf.Max(0f, Time.unscaledTime - _rechargeStartedAt)
                : _returnStableStartedAt > float.NegativeInfinity
                    ? Mathf.Max(0f, Time.unscaledTime - _returnStableStartedAt)
                    : 0f;
        public bool GameplaySuspended => controller != null &&
                                         controller.EnergySuspended;
        public bool IsIndependent => State == ProductionEnergyState.Independent;

        private void Awake()
        {
            if (controller == null) controller = GetComponent<NulProductionController>();
            if (shell == null) shell = GetComponent<ProductionShellFlow>();
            if (core == null) core = GetComponent<ProductionCoreFlow>();
            if (gameLoop == null) gameLoop = GetComponent<ProductionGameLoop>();
        }

        private void OnEnable()
        {
            if (gameLoop == null) gameLoop = GetComponent<ProductionGameLoop>();
            if (gameLoop != null) gameLoop.OnLoopReset += ResetRecovery;
        }

        private void OnDisable()
        {
            if (gameLoop != null) gameLoop.OnLoopReset -= ResetRecovery;
            if (controller != null) controller.SetEnergySupply(false, 1f);
        }

        private void Update()
        {
            if (controller == null) return;
            Tick(Time.unscaledTime, controller.TrackedFresh,
                core != null && core.IndependentPower,
                shell == null || shell.EntryStage == ProductionEntryStage.Running);
        }

        public void Tick(float now, bool tracked, bool independent,
            bool gameplayStarted)
        {
            if (controller == null) return;
            if (independent)
            {
                State = ProductionEnergyState.Independent;
                ClearTimers();
                controller.SetEnergySupply(false, 1f);
                return;
            }

            if (!gameplayStarted)
            {
                State = ProductionEnergyState.Connected;
                ClearTimers();
                controller.SetEnergySupply(false, 1f);
                return;
            }

            if (tracked)
            {
                _lossStartedAt = float.NegativeInfinity;
                if (State == ProductionEnergyState.Depleted ||
                    State == ProductionEnergyState.Collapsed)
                {
                    if (_returnStableStartedAt == float.NegativeInfinity)
                        _returnStableStartedAt = now;
                    if (now - _returnStableStartedAt >= ReturnStableSeconds)
                    {
                        State = ProductionEnergyState.Recharging;
                        _rechargeStartedAt = now;
                    }
                    controller.SetEnergySupply(true, 0f);
                    return;
                }

                if (State == ProductionEnergyState.Recharging)
                {
                    controller.SetEnergySupply(true, 0f);
                    if (now - _rechargeStartedAt >= RechargeSeconds)
                    {
                        State = ProductionEnergyState.Connected;
                        _returnStableStartedAt = float.NegativeInfinity;
                        _rechargeStartedAt = float.NegativeInfinity;
                        controller.SetEnergySupply(false, 1f);
                    }
                    return;
                }

                State = ProductionEnergyState.Connected;
                _returnStableStartedAt = float.NegativeInfinity;
                _rechargeStartedAt = float.NegativeInfinity;
                controller.SetEnergySupply(false, 1f);
                return;
            }

            _returnStableStartedAt = float.NegativeInfinity;
            _rechargeStartedAt = float.NegativeInfinity;
            if (_lossStartedAt == float.NegativeInfinity) _lossStartedAt = now;
            float lost = Mathf.Max(0f, now - _lossStartedAt);

            if (State == ProductionEnergyState.Recharging)
            {
                State = ProductionEnergyState.Depleted;
                controller.SetEnergySupply(true, 0f);
                return;
            }
            if (lost >= CollapsedSeconds)
            {
                State = ProductionEnergyState.Collapsed;
                controller.SetEnergySupply(true, 0f);
            }
            else if (lost >= DepletedSeconds)
            {
                State = ProductionEnergyState.Depleted;
                controller.SetEnergySupply(true, 0f);
            }
            else if (lost >= JitterSeconds)
            {
                State = ProductionEnergyState.Fragmenting;
                float movement = 1f - Mathf.InverseLerp(
                    JitterSeconds, DepletedSeconds, lost);
                controller.SetEnergySupply(false, movement);
            }
            else
            {
                State = ProductionEnergyState.Connected;
                controller.SetEnergySupply(false, 1f);
            }
        }

        public void ResetRecovery()
        {
            State = ProductionEnergyState.Connected;
            ClearTimers();
            if (controller != null) controller.SetEnergySupply(false, 1f);
        }

        private void ClearTimers()
        {
            _lossStartedAt = float.NegativeInfinity;
            _returnStableStartedAt = float.NegativeInfinity;
            _rechargeStartedAt = float.NegativeInfinity;
        }
    }
}
