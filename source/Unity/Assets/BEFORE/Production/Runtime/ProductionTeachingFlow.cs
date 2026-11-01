using System;
using System.Collections.Generic;
using UnityEngine;

namespace Before.Production
{
    public enum ProductionTeachingStage
    {
        None,
        Collecting,
        Confirming,
        RepairCollecting,
        RepairConfirming
    }

    /// <summary>
    /// Owns Production V2 teaching/proof progression. Production samples arrive
    /// through the dedicated Sidecar control-plane; the frozen gameplay command
    /// route remains an offline-test seam only.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProductionTeachingFlow : MonoBehaviour
    {
        public const int SamplesPerTeaching = 3;
        public const int SamplesPerRepair = 1;
        public const float SafeRunUp = 72f;
        public const float ConfirmationSeconds = 0.8f;
        public const float ResetNoticeSeconds = 1.6f;

        private static readonly int[] TeachingBeatIds = { 1, 4, 13 };
        private static readonly ProductionObstacleKind[] TeachingKinds =
        {
            ProductionObstacleKind.Jump,
            ProductionObstacleKind.Crouch,
            ProductionObstacleKind.Stop
        };

        [SerializeField] private NulProductionController controller;
        [SerializeField] private ProductionCourseLoop course;
        [SerializeField] private ProductionObstacleJudge judge;
        [SerializeField] private ProductionGameLoop gameLoop;
        [SerializeField] private SidecarTeachingControlClient teachingControl;
        [SerializeField] private ProductionTeachingInput teachingInput;

        private readonly HashSet<int> _completedBeats = new HashSet<int>();
        private readonly HashSet<int> _protectedProofs = new HashSet<int>();
        private int _nextTeachingIndex;
        private float _confirmationRemaining;
        private Vector2 _anchorPosition;
        private bool _lastSampleRejected;
        private string _lastRejectionReason = string.Empty;
        private bool _resetPending;
        private int _resetRequestId;
        private string _resetNotice = string.Empty;
        private float _resetNoticeUntil = float.NegativeInfinity;

        public ProductionTeachingStage Stage { get; private set; }
        public ProductionObstacleKind TargetKind { get; private set; }
        public int ActiveBeatId { get; private set; }
        public int AcceptedSamples { get; private set; }
        public int CompletedTeachingCount => _completedBeats.Count;
        public int ProtectedProofCount => _protectedProofs.Count;
        public bool IsActive => Stage != ProductionTeachingStage.None;
        public bool IsRepair => Stage == ProductionTeachingStage.RepairCollecting ||
                                Stage == ProductionTeachingStage.RepairConfirming;
        public bool LastSampleRejected => _lastSampleRejected;
        public string LastRejectionReason => _lastRejectionReason;
        public string CapturePhase => teachingControl != null && teachingControl.IsReady
            ? teachingControl.Phase : "ready";
        public int RequiredSamples => IsRepair ? SamplesPerRepair : SamplesPerTeaching;
        public bool AllTeachingsCompleted => _completedBeats.Count == TeachingBeatIds.Length;
        public bool ResetPending => _resetPending;
        public bool CanResetCurrentSign => IsActive && !_resetPending &&
            (gameLoop == null || !gameLoop.IsGameOver);
        public string ResetNotice => Time.unscaledTime < _resetNoticeUntil
            ? _resetNotice : string.Empty;

        public NulProductionController Controller { get => controller; set => controller = value; }
        public ProductionCourseLoop Course { get => course; set => course = value; }
        public ProductionObstacleJudge Judge { get => judge; set => judge = value; }
        public ProductionGameLoop GameLoop { get => gameLoop; set => gameLoop = value; }
        public SidecarTeachingControlClient TeachingControl
        {
            get => teachingControl;
            set => teachingControl = value;
        }
        public ProductionTeachingInput TeachingInput
        {
            get => teachingInput;
            set => teachingInput = value;
        }

        private void Awake()
        {
            if (controller == null) controller = GetComponent<NulProductionController>();
            if (course == null) course = GetComponent<ProductionCourseLoop>();
            if (judge == null) judge = GetComponent<ProductionObstacleJudge>();
            if (gameLoop == null) gameLoop = GetComponent<ProductionGameLoop>();
            ResolveControlComponents();
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
            teachingControl?.EndTeaching();
        }

        private void Update()
        {
            ResolveControlComponents();
            ConsumeResetAck();
            ConsumeControlEvent();
            if (!IsActive)
            {
                TryEnterNextTeaching();
                return;
            }
            if (controller == null || controller.Nul.Paused) return;
            if (_resetPending)
            {
                StabilizeAtAnchorPreservingDirection();
                return;
            }
            if (Stage != ProductionTeachingStage.Confirming &&
                Stage != ProductionTeachingStage.RepairConfirming) return;

            _confirmationRemaining -= Time.unscaledDeltaTime;
            if (_confirmationRemaining <= 0f) FinishActiveInteraction();
        }

        private void ResolveControlComponents()
        {
            if (teachingControl == null)
                teachingControl = GetComponent<SidecarTeachingControlClient>();
            if (teachingInput == null)
                teachingInput = GetComponent<ProductionTeachingInput>();
            if (teachingInput == null)
                teachingInput = gameObject.AddComponent<ProductionTeachingInput>();
            teachingInput.Flow = this;
            teachingInput.Control = teachingControl;
        }

        private void ConsumeControlEvent()
        {
            if (teachingControl == null || _resetPending) return;
            if (teachingControl.IsReady && teachingControl.Phase != "rejected")
            {
                _lastSampleRejected = false;
                _lastRejectionReason = string.Empty;
            }
            if (!teachingControl.TryConsumeEvent(out SidecarTeachingEvent evt) || !IsActive)
                return;
            if (!TryMapSidecarKind(evt.Kind, out ProductionObstacleKind mapped) ||
                mapped != TargetKind) return;
            StabilizeAtAnchorPreservingDirection();
            if (!evt.WasAccepted)
            {
                RejectCurrentAttempt(evt.Reason);
                return;
            }

            _lastSampleRejected = false;
            _lastRejectionReason = string.Empty;
            AcceptedSamples = Mathf.Clamp(evt.Accepted, 0, RequiredSamples);
            if (evt.Finalized && AcceptedSamples >= RequiredSamples)
            {
                Stage = IsRepair ? ProductionTeachingStage.RepairConfirming
                                 : ProductionTeachingStage.Confirming;
                _confirmationRemaining = ConfirmationSeconds;
            }
        }

        private void ConsumeResetAck()
        {
            if (!_resetPending || teachingControl == null) return;
            if (!teachingControl.TryConsumeResetCommandAck(out string target,
                    out int requestId) || requestId != _resetRequestId) return;
            if (!TryMapSidecarKind(target, out ProductionObstacleKind mapped) ||
                mapped != TargetKind) return;

            _resetPending = false;
            _resetRequestId = 0;
            _completedBeats.Remove(ActiveBeatId);
            _protectedProofs.Remove(ActiveBeatId);
            RecalculateNextTeachingIndex();
            AcceptedSamples = 0;
            Stage = ProductionTeachingStage.Collecting;
            _lastSampleRejected = false;
            _lastRejectionReason = string.Empty;
            _confirmationRemaining = 0f;
            _resetNotice = ResetNoticeFor(TargetKind);
            _resetNoticeUntil = Time.unscaledTime + ResetNoticeSeconds;
            StabilizeAtAnchorPreservingDirection();
        }

        private static bool TryMapSidecarKind(string kind, out ProductionObstacleKind mapped)
        {
            switch (kind)
            {
                case "jump": mapped = ProductionObstacleKind.Jump; return true;
                case "crouch": mapped = ProductionObstacleKind.Crouch; return true;
                case "stop": mapped = ProductionObstacleKind.Stop; return true;
                default: mapped = default; return false;
            }
        }

        public static bool IsTeachingBeat(int beatId)
            => Array.IndexOf(TeachingBeatIds, beatId) >= 0;

        public static bool TryMapCommand(CommandKind kind, out ProductionObstacleKind mapped)
        {
            switch (kind)
            {
                case CommandKind.Jump: mapped = ProductionObstacleKind.Jump; return true;
                case CommandKind.Crouch: mapped = ProductionObstacleKind.Crouch; return true;
                case CommandKind.StopOn: mapped = ProductionObstacleKind.Stop; return true;
                default: mapped = default; return false;
            }
        }

        public void TryEnterNextTeaching()
        {
            if (controller == null || course == null || _nextTeachingIndex >= TeachingBeatIds.Length)
                return;
            if (gameLoop != null && (gameLoop.InRecovery || gameLoop.IsGameOver)) return;

            int beatId = TeachingBeatIds[_nextTeachingIndex];
            ProductionCourseLoop.ObstacleSlot[] slots = course.Slots;
            for (int i = 0; i < slots.Length; i++)
            {
                ProductionCourseLoop.ObstacleSlot slot = slots[i];
                if (slot.BeatId != beatId || slot.PassCount != 0) continue;
                float anchorX = slot.WorldX - SafeRunUp;
                if (controller.transform.position.x < anchorX) return;
                BeginInteraction(beatId, TeachingKinds[_nextTeachingIndex], anchorX, false);
                return;
            }
        }

        public void BeginTeaching(int beatId, ProductionObstacleKind kind, float hazardWorldX)
            => BeginInteraction(beatId, kind, hazardWorldX - SafeRunUp, false);

        public void SubmitAcknowledgement(ProductionObstacleKind kind)
        {
            if (!IsActive || controller == null || controller.Nul.Paused) return;
            if (Stage == ProductionTeachingStage.Confirming ||
                Stage == ProductionTeachingStage.RepairConfirming) return;

            StabilizeAtAnchor();
            if (kind != TargetKind)
            {
                RejectCurrentAttempt("inconsistent");
                return;
            }

            _lastSampleRejected = false;
            _lastRejectionReason = string.Empty;
            AcceptedSamples++;
            if (AcceptedSamples < RequiredSamples) return;
            Stage = IsRepair ? ProductionTeachingStage.RepairConfirming
                             : ProductionTeachingStage.Confirming;
            _confirmationRemaining = ConfirmationSeconds;
        }

        public bool RequestCurrentSignReset()
        {
            ResolveControlComponents();
            if (!CanResetCurrentSign || teachingControl == null) return false;
            _resetNotice = string.Empty;
            _resetNoticeUntil = float.NegativeInfinity;
            _resetRequestId = teachingControl.ResetCommand(TargetKind);
            _resetPending = _resetRequestId > 0;
            if (_resetPending) StabilizeAtAnchorPreservingDirection();
            return _resetPending;
        }

        public static string ResetNoticeFor(ProductionObstacleKind kind)
        {
            switch (kind)
            {
                case ProductionObstacleKind.Jump:
                    return "ジャンプの合図をリセットしました";
                case ProductionObstacleKind.Crouch:
                    return "しゃがむ合図をリセットしました";
                default:
                    return "止まる合図をリセットしました";
            }
        }

        public bool TryProtectFirstProof(ProductionObstacleJudge.ObstacleResult result)
        {
            if (result.Success || course == null || controller == null) return false;
            ProductionCourseLoop.ObstacleSlot[] slots = course.Slots;
            for (int i = 0; i < slots.Length; i++)
            {
                ProductionCourseLoop.ObstacleSlot slot = slots[i];
                if (slot.StableId != result.StableId || slot.PassCount != result.PassCount) continue;
                if (!IsTeachingBeat(slot.BeatId) || !_completedBeats.Contains(slot.BeatId) ||
                    _protectedProofs.Contains(slot.BeatId)) return false;

                _protectedProofs.Add(slot.BeatId);
                if (judge != null) judge.RearmCurrentPass(slot.StableId);
                BeginInteraction(slot.BeatId, slot.Kind, slot.WorldX - SafeRunUp, true);
                return true;
            }
            return false;
        }

        public void ResetFlow()
        {
            _completedBeats.Clear();
            _protectedProofs.Clear();
            _nextTeachingIndex = 0;
            AcceptedSamples = 0;
            ActiveBeatId = 0;
            Stage = ProductionTeachingStage.None;
            _lastSampleRejected = false;
            _lastRejectionReason = string.Empty;
            _confirmationRemaining = 0f;
            _resetPending = false;
            _resetRequestId = 0;
            _resetNotice = string.Empty;
            _resetNoticeUntil = float.NegativeInfinity;
            teachingInput?.ClearContext();
            teachingControl?.ResetLearning();
            if (controller != null)
            {
                controller.SetStorySuspended(false);
                controller.SetMovementSuspended(false);
            }
        }

        private void HandleCommand(CommandPacket packet)
        {
            if (!IsActive || controller == null || controller.Nul.Paused) return;
            // Production acknowledgements come only from the SPACE control-plane.
            // Offline hermetic rigs retain the old direct semantic seam.
            if (controller.autoLaunchSidecar ||
                (teachingControl != null && teachingControl.IsReady)) return;
            if (TryMapCommand(packet.Kind, out ProductionObstacleKind mapped))
                SubmitAcknowledgement(mapped);
            else if (packet.Kind == CommandKind.StopOff)
                StabilizeAtAnchor();
        }

        private void BeginInteraction(int beatId, ProductionObstacleKind kind,
                                      float anchorX, bool repair)
        {
            ActiveBeatId = beatId;
            TargetKind = kind;
            AcceptedSamples = 0;
            _lastSampleRejected = false;
            _lastRejectionReason = string.Empty;
            _confirmationRemaining = 0f;
            _resetPending = false;
            _resetRequestId = 0;
            _resetNotice = string.Empty;
            _resetNoticeUntil = float.NegativeInfinity;
            _anchorPosition = new Vector2(anchorX, 0f);
            Stage = repair ? ProductionTeachingStage.RepairCollecting
                           : ProductionTeachingStage.Collecting;
            controller.SetStorySuspended(true);
            controller.SetMovementSuspended(true);
            StabilizeAtAnchor();
        }

        private void StabilizeAtAnchor()
        {
            controller.RecoverAt(_anchorPosition);
            controller.SetStorySuspended(true);
            controller.SetMovementSuspended(true);
        }

        private void StabilizeAtAnchorPreservingDirection()
        {
            bool directionMode = controller.Nul.DirectionMode;
            int lastDirection = controller.Nul.LastDirection;
            StabilizeAtAnchor();
            controller.Nul.DirectionMode = directionMode;
            controller.Nul.LastDirection = lastDirection;
        }

        private void FinishActiveInteraction()
        {
            bool repair = IsRepair;
            int finishedBeat = ActiveBeatId;
            if (!repair)
            {
                _completedBeats.Add(finishedBeat);
                while (_nextTeachingIndex < TeachingBeatIds.Length &&
                       _completedBeats.Contains(TeachingBeatIds[_nextTeachingIndex]))
                    _nextTeachingIndex++;
            }

            AcceptedSamples = 0;
            ActiveBeatId = 0;
            Stage = ProductionTeachingStage.None;
            _lastSampleRejected = false;
            _lastRejectionReason = string.Empty;
            _confirmationRemaining = 0f;
            teachingControl?.EndTeaching();
            teachingInput?.ClearContext();
            controller.SetStorySuspended(false);
            controller.SetMovementSuspended(false);
        }

        private void RecalculateNextTeachingIndex()
        {
            _nextTeachingIndex = 0;
            while (_nextTeachingIndex < TeachingBeatIds.Length &&
                   _completedBeats.Contains(TeachingBeatIds[_nextTeachingIndex]))
                _nextTeachingIndex++;
        }

        public void RejectCurrentAttempt(string reason)
        {
            _lastSampleRejected = true;
            _lastRejectionReason = reason ?? "inconsistent";
        }
    }
}
