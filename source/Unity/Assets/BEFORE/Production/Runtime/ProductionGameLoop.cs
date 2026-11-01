using System;
using UnityEngine;

namespace Before.Production
{
    /// <summary>
    /// Owns lives and non-terminal recovery. Obstacle judging and command input
    /// remain separate; this layer reacts only to the judge's source-agnostic result.
    /// </summary>
    public sealed class ProductionGameLoop : MonoBehaviour
    {
        public const int StartingLives = 3;
        public const float RecoveryBacktrack = 72f;
        public const float RecoveryFreezeSeconds = 1.4f;
        public const float RecoveryGraceSeconds = 0.6f;

        [SerializeField] private NulProductionController controller;
        [SerializeField] private ProductionObstacleJudge judge;
        [SerializeField] private ProductionCourseLoop course;
        [SerializeField] private ProductionTeachingFlow teaching;

        private float _recoveryUntil;
        private float _graceUntil;
        private float _recoveryX;
        private int _recoveryStableId;
        private Vector2 _startWorldPosition;

        public event Action<int> OnLivesChanged;
        public event Action OnLivesExhausted;
        public event Action<ProductionRoute> OnRouteSelected;
        public event Action OnRecoveryCompleted;
        public event Action OnLoopReset;

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

        public ProductionCourseLoop Course
        {
            get => course;
            set => course = value;
        }

        public ProductionTeachingFlow Teaching
        {
            get => teaching;
            set => teaching = value;
        }

        public int Lives { get; private set; } = StartingLives;
        public int RecoveryCount { get; private set; }
        public bool InRecovery { get; private set; }
        public bool Invulnerable => InRecovery || CurrentGameplayTime < _graceUntil;
        public bool IsGameOver { get; private set; }
        public float CurrentCheckpointX => _recoveryX;
        public ProductionRoute SelectedRoute { get; private set; }
        private float CurrentGameplayTime => controller != null ? controller.GameplayTime : Time.time;

        private void Awake()
        {
            if (controller == null) controller = GetComponent<NulProductionController>();
            if (judge == null) judge = GetComponent<ProductionObstacleJudge>();
            if (course == null) course = GetComponent<ProductionCourseLoop>();
            if (teaching == null) teaching = GetComponent<ProductionTeachingFlow>();
            _startWorldPosition = controller != null ? controller.transform.position : transform.position;
        }

        private void OnEnable()
        {
            if (judge != null) judge.OnResolved += HandleResolved;
        }

        private void OnDisable()
        {
            if (judge != null) judge.OnResolved -= HandleResolved;
        }

        private void Update()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (!IsGameOver && kb != null && IsDebugLifeKeyPressed(
                    kb.numpadPlusKey.wasPressedThisFrame,
                    kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed,
                    kb.equalsKey.wasPressedThisFrame,
                    kb.semicolonKey.wasPressedThisFrame))
                TryAddDebugLife();
            if (IsGameOver && kb != null && kb.rKey.wasPressedThisFrame) RestartGame();
#endif
            if (InRecovery && CurrentGameplayTime >= _recoveryUntil) CompleteRecovery();
        }

        public static bool IsDebugLifeKeyPressed(bool numpadPlusPressed,
            bool shiftPressed, bool equalsPressed, bool semicolonPressed)
            => numpadPlusPressed ||
               (shiftPressed && (equalsPressed || semicolonPressed));

        public bool TryAddDebugLife()
        {
            if (IsGameOver || Lives >= StartingLives) return false;
            Lives++;
            OnLivesChanged?.Invoke(Lives);
            return true;
        }

        public static float CheckpointFor(float hazardWorldX)
            => hazardWorldX - RecoveryBacktrack;

        public static bool LivesExhausted(int lives) => lives <= 0;

        public void ResetLoop()
        {
            Lives = StartingLives;
            RecoveryCount = 0;
            InRecovery = false;
            IsGameOver = false;
            SelectedRoute = ProductionRoute.None;
            _recoveryUntil = 0f;
            _graceUntil = 0f;
            _recoveryX = _startWorldPosition.x;
            _recoveryStableId = 0;
            if (controller != null)
            {
                controller.RecoverAt(_startWorldPosition);
                controller.SetMovementSuspended(false);
            }
            if (course != null) course.ResetCourse(_startWorldPosition.x);
            if (judge != null) judge.ResetJudgement();
            OnLivesChanged?.Invoke(Lives);
            OnRouteSelected?.Invoke(SelectedRoute);
            OnLoopReset?.Invoke();
        }

        public void RestartGame() => ResetLoop();

        private void HandleResolved(ProductionObstacleJudge.ObstacleResult result)
        {
            if (!result.Success && teaching != null && teaching.TryProtectFirstProof(result))
                return;
            if (result.Success)
            {
                if (result.Kind == ProductionObstacleKind.Direction)
                {
                    SelectedRoute = ProductionCourseLoop.RouteForDirection(result.Direction);
                    if (course != null) course.SetSelectedRoute(result.Direction);
                    OnRouteSelected?.Invoke(SelectedRoute);
                }
                return;
            }
            if (Invulnerable || Lives <= 0) return;

            Lives--;
            RecoveryCount++;
            _recoveryX = CheckpointFor(result.WorldX);
            _recoveryStableId = result.StableId;
            OnLivesChanged?.Invoke(Lives);

            if (controller != null) controller.SetMovementSuspended(true);
            if (LivesExhausted(Lives))
            {
                InRecovery = false;
                IsGameOver = true;
                if (controller != null)
                {
                    controller.RecoverAt(new Vector2(controller.transform.position.x, 0f));
                    controller.SetMovementSuspended(true);
                    controller.Nul.Paused = true;
                }
                OnLivesExhausted?.Invoke();
                return;
            }

            InRecovery = true;
            _recoveryUntil = CurrentGameplayTime + RecoveryFreezeSeconds;
        }

        public void CompleteRecovery()
        {
            if (!InRecovery || controller == null) return;
            InRecovery = false;
            controller.RecoverAt(new Vector2(_recoveryX, 0f));
            controller.SetMovementSuspended(false);
            if (judge != null) judge.RearmCurrentPass(_recoveryStableId);
            _graceUntil = CurrentGameplayTime + RecoveryGraceSeconds;
            OnRecoveryCompleted?.Invoke();
        }

    }
}
