using UnityEngine;

namespace Before.Production
{
    /// <summary>
    /// Converts source-agnostic gameplay events into a short logical display
    /// impulse. The logical RenderTexture remains pixel-aligned; only its final
    /// integer-scaled screen blit is offset by ProductionLogicalRenderPresenter.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProductionCameraImpulsePresenter : MonoBehaviour
    {
        public enum ImpulseKind
        {
            None,
            Takeoff,
            Landing,
            Failure
        }

        public const float TakeoffAmplitude = 1f;
        public const float TakeoffSeconds = 0.07f;
        public const float LandingAmplitude = 1.5f;
        public const float LandingSeconds = 0.09f;
        public const float FailureAmplitude = 3f;
        public const float FailureSeconds = 0.16f;

        [SerializeField] private NulProductionController controller;
        [SerializeField] private ProductionObstacleJudge judge;
        [SerializeField] private ProductionGameLoop gameLoop;
        [SerializeField] private bool shakeEnabled = true;

        private NulProductionController.NulAnimState _lastState;
        private bool _hasState;
        private float _elapsed;

        public NulProductionController Controller
        {
            get => controller;
            set => controller = value;
        }

        public ProductionObstacleJudge Judge
        {
            get => judge;
            set => judge = value;
        }

        public ProductionGameLoop GameLoop
        {
            get => gameLoop;
            set => gameLoop = value;
        }

        public bool ShakeEnabled
        {
            get => shakeEnabled;
            set
            {
                shakeEnabled = value;
                if (!value) ClearImpulse();
            }
        }

        public ImpulseKind ActiveImpulse { get; private set; }
        public Vector2 CurrentLogicalOffset => OffsetAt(ActiveImpulse, _elapsed);

        private void Awake() => ResolveOwners();

        private void OnEnable()
        {
            ResolveOwners();
            if (judge != null) judge.OnResolved += HandleResolved;
            if (gameLoop != null) gameLoop.OnLoopReset += ClearImpulse;
            if (controller != null)
            {
                _lastState = controller.State;
                _hasState = true;
            }
        }

        private void OnDisable()
        {
            if (judge != null) judge.OnResolved -= HandleResolved;
            if (gameLoop != null) gameLoop.OnLoopReset -= ClearImpulse;
            ClearImpulse();
            _hasState = false;
        }

        private void Update()
        {
            DetectControllerTransition();
            if (ActiveImpulse == ImpulseKind.None) return;

            bool paused = controller != null && controller.Nul.Paused;
            bool terminal = gameLoop != null && gameLoop.IsGameOver;
            if (!PresentationClockFrozen(paused, terminal))
                _elapsed += Time.unscaledDeltaTime;
            if (_elapsed >= DurationFor(ActiveImpulse)) ClearImpulse();
        }

        private void ResolveOwners()
        {
            if (controller == null) controller = GetComponentInParent<NulProductionController>();
            if (judge == null) judge = GetComponentInParent<ProductionObstacleJudge>();
            if (gameLoop == null) gameLoop = GetComponentInParent<ProductionGameLoop>();
        }

        private void DetectControllerTransition()
        {
            if (controller == null) return;
            NulProductionController.NulAnimState state = controller.State;
            if (!_hasState)
            {
                _lastState = state;
                _hasState = true;
                return;
            }
            if (state == _lastState) return;

            if (state == NulProductionController.NulAnimState.Jump)
                TriggerImpulse(ImpulseKind.Takeoff);
            else if (state == NulProductionController.NulAnimState.Landing)
                TriggerImpulse(ImpulseKind.Landing);
            _lastState = state;
        }

        private void HandleResolved(ProductionObstacleJudge.ObstacleResult result)
        {
            if (!result.Success) TriggerImpulse(ImpulseKind.Failure);
        }

        public void TriggerImpulse(ImpulseKind kind)
        {
            if (!shakeEnabled || kind == ImpulseKind.None) return;
            ActiveImpulse = kind;
            _elapsed = 0f;
        }

        public void ClearImpulse()
        {
            ActiveImpulse = ImpulseKind.None;
            _elapsed = 0f;
        }

        public static bool PresentationClockFrozen(bool paused, bool terminal)
            => paused && !terminal;

        public static float DurationFor(ImpulseKind kind)
        {
            switch (kind)
            {
                case ImpulseKind.Takeoff: return TakeoffSeconds;
                case ImpulseKind.Landing: return LandingSeconds;
                case ImpulseKind.Failure: return FailureSeconds;
                default: return 0f;
            }
        }

        public static Vector2 OffsetAt(ImpulseKind kind, float elapsedSeconds)
        {
            float duration = DurationFor(kind);
            if (duration <= 0f || elapsedSeconds < 0f || elapsedSeconds >= duration)
                return Vector2.zero;

            float pulse = Pulse(elapsedSeconds / duration);
            switch (kind)
            {
                case ImpulseKind.Takeoff:
                    return new Vector2(0f, TakeoffAmplitude * pulse);
                case ImpulseKind.Landing:
                    return new Vector2(0f, LandingAmplitude * pulse);
                case ImpulseKind.Failure:
                    return new Vector2(FailureAmplitude * pulse,
                        -FailureAmplitude * pulse);
                default:
                    return Vector2.zero;
            }
        }

        private static float Pulse(float normalized)
        {
            if (normalized <= 0f) return 0f;
            if (normalized < 0.125f)
                return Mathf.Lerp(0f, 1f, normalized / 0.125f);
            if (normalized < 0.375f)
                return Mathf.Lerp(1f, -0.65f, (normalized - 0.125f) / 0.25f);
            if (normalized < 0.625f)
                return Mathf.Lerp(-0.65f, 0.35f, (normalized - 0.375f) / 0.25f);
            return Mathf.Lerp(0.35f, 0f, (normalized - 0.625f) / 0.375f);
        }
    }
}
