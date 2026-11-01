using System;
using UnityEngine;

namespace Before.Production
{
    public enum ProductionEntryStage
    {
        Title,
        ConnectionDetected,
        LinkEstablished,
        Opening,
        Running
    }

    /// <summary>
    /// Owns only the participant entry and explicit operator-exit shell. The
    /// existing Pause contract discards commands received behind either shell,
    /// so no input can replay after entry/cancel. Gameplay and story state
    /// machines remain separate owners.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProductionShellFlow : MonoBehaviour
    {
        public const float HandStableSeconds = 0.5f;
        public const float ConnectionDetectedSeconds = 0.65f;
        public const float LinkEstablishedSeconds = 0.8f;
        public const float OpeningSeconds = 0.35f;
        public const float CameraFaultWaitSeconds = 4.0f;

        [SerializeField] private NulProductionController controller;
        [SerializeField] private ProductionGameLoop gameLoop;

        private float _stageElapsed;
        private float _stableHandElapsed;
        private bool _pauseBeforeExit;

        public static event Action EditorExitRequested;
        public event Action<ProductionEntryStage> OnEntryStageChanged;
        public event Action<bool> OnExitConfirmationChanged;

        public NulProductionController Controller
        {
            get => controller;
            set => controller = value;
        }

        public ProductionGameLoop GameLoop
        {
            get => gameLoop;
            set => gameLoop = value;
        }

        public ProductionEntryStage EntryStage { get; private set; } =
            ProductionEntryStage.Title;
        public bool ExitConfirmationVisible { get; private set; }
        public bool BlocksGameplayPresentation =>
            EntryStage != ProductionEntryStage.Running;
        public float StableHandElapsed => _stableHandElapsed;
        public float StageElapsed => _stageElapsed;
        public bool CameraFaultVisible => EntryStage == ProductionEntryStage.Title &&
            _stageElapsed >= CameraFaultWaitSeconds && controller != null &&
            !controller.HasReceivedLandmarkPacket;

        private void Awake()
        {
            if (controller == null) controller = GetComponent<NulProductionController>();
            if (gameLoop == null) gameLoop = GetComponent<ProductionGameLoop>();
        }

        private void OnEnable()
        {
            if (gameLoop != null) gameLoop.OnLoopReset += ReturnToTitle;
            ApplyEntryPause();
        }

        private void OnDisable()
        {
            if (gameLoop != null) gameLoop.OnLoopReset -= ReturnToTitle;
        }

        private void Update()
        {
            HandleOperatorKeys();
            if (ExitConfirmationVisible) return;
            TickEntry(Time.unscaledDeltaTime);
        }

        public void TickEntry(float unscaledDeltaTime)
        {
            if (EntryStage == ProductionEntryStage.Running) return;
            ApplyEntryPause();

            float delta = Mathf.Max(0f, unscaledDeltaTime);
            if (EntryStage == ProductionEntryStage.Title)
            {
                _stageElapsed += delta;
                _stableHandElapsed = controller != null && controller.TrackedFresh
                    ? _stableHandElapsed + delta
                    : 0f;
                if (_stableHandElapsed >= HandStableSeconds)
                    ChangeEntryStage(ProductionEntryStage.ConnectionDetected);
                return;
            }

            _stageElapsed += delta;
            if (EntryStage == ProductionEntryStage.ConnectionDetected &&
                _stageElapsed >= ConnectionDetectedSeconds)
                ChangeEntryStage(ProductionEntryStage.LinkEstablished);
            else if (EntryStage == ProductionEntryStage.LinkEstablished &&
                     _stageElapsed >= LinkEstablishedSeconds)
                ChangeEntryStage(ProductionEntryStage.Opening);
            else if (EntryStage == ProductionEntryStage.Opening &&
                     _stageElapsed >= OpeningSeconds)
                BeginGameplay();
        }

        public void RequestExitConfirmation()
        {
            if (ExitConfirmationVisible || controller == null) return;
            _pauseBeforeExit = controller.Nul.Paused;
            controller.Nul.Paused = true;
            ExitConfirmationVisible = true;
            OnExitConfirmationChanged?.Invoke(true);
        }

        public void CancelExitConfirmation()
        {
            if (!ExitConfirmationVisible) return;
            ExitConfirmationVisible = false;
            if (controller != null)
                controller.Nul.Paused = BlocksGameplayPresentation || _pauseBeforeExit;
            OnExitConfirmationChanged?.Invoke(false);
        }

        public void ConfirmExit()
        {
            if (!ExitConfirmationVisible) return;
            EditorExitRequested?.Invoke();
        }

        public void ReturnToTitle()
        {
            ExitConfirmationVisible = false;
            _pauseBeforeExit = false;
            _stableHandElapsed = 0f;
            _stageElapsed = 0f;
            EntryStage = ProductionEntryStage.Title;
            ApplyEntryPause();
            OnExitConfirmationChanged?.Invoke(false);
            OnEntryStageChanged?.Invoke(EntryStage);
        }

        private void BeginGameplay()
        {
            EntryStage = ProductionEntryStage.Running;
            _stageElapsed = 0f;
            if (controller != null) controller.Nul.Paused = false;
            OnEntryStageChanged?.Invoke(EntryStage);
        }

        private void ChangeEntryStage(ProductionEntryStage stage)
        {
            EntryStage = stage;
            _stageElapsed = 0f;
            OnEntryStageChanged?.Invoke(stage);
        }

        private void ApplyEntryPause()
        {
            if (controller != null && BlocksGameplayPresentation)
                controller.Nul.Paused = true;
        }

        private void HandleOperatorKeys()
        {
#if ENABLE_INPUT_SYSTEM
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard == null) return;
            if (ExitConfirmationVisible)
            {
                if (keyboard.escapeKey.wasPressedThisFrame)
                    CancelExitConfirmation();
                else if (keyboard.enterKey.wasPressedThisFrame ||
                         keyboard.numpadEnterKey.wasPressedThisFrame)
                    ConfirmExit();
            }
            else if (keyboard.escapeKey.wasPressedThisFrame)
            {
                RequestExitConfirmation();
            }
#endif
        }
    }
}
