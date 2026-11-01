using System;
using System.Collections.Generic;
using Before.Capture;
using Before.Config;
using Before.Course;
using Before.Planning;
using Before.Recognition;
using UnityEngine;

namespace Before.App
{
    public enum TrackingRecoveryState
    {
        Connected,
        Jitter,       // 0-0.3 s
        Fragmented,   // 0.3-0.8 s
        SuspendedRewound, // 0.8-1.8 s
        Collapsed,    // >= 1.8 s
        Reconnecting, // stable return accumulating 0.5 s
        Recharging    // following 1.0 s
    }

    public enum PublicTextId
    {
        None, BootWait, CameraDenied, LowLight, Linking, LinkOk, EdgeWarn, SignalLost,
        TeachBegin, PoseConfirm, SampleOne, SampleShort, SampleConflict, SignalOverlap, Learned, TeachRecord,
        QueueFull, PlanWait, PlanFinal, Rewind, Independent, Complete, Paused, EndReset, ExitConfirm
    }

    /// <summary>
    /// Composition core: owns story, recognition, planning, course, recovery, and operator state.
    /// Pure C# and clock-injected so the complete story can run headless on a fake hand stream.
    /// Presentation observes; it never decides outcomes.
    /// </summary>
    public sealed class BeforeDirector
    {
        public readonly BeforeRuntimeConfig RuntimeCfg;
        public readonly BeforeRecognitionConfig RecognitionCfg;
        public readonly BeforeCourseConfig CourseCfg;
        public readonly RealtimeClock Realtime = new RealtimeClock();
        public readonly GameplayClock Gameplay = new GameplayClock();
        public readonly BeforeStoryMachine Story = new BeforeStoryMachine();
        public readonly RecognitionCoordinator Recognition;
        public readonly IntentQueue Queue = new IntentQueue();
        public readonly CourseModel Course;
        public readonly NulMotor2D Motor;
        public readonly LocalPerception Perception;
        public readonly CheckpointController Checkpoints = new CheckpointController();
        public IHandSignalSource Hand;

        // public observable state
        public TrackingRecoveryState Recovery { get; private set; } = TrackingRecoveryState.Connected;
        public PublicTextId CurrentText { get; private set; } = PublicTextId.None;
        public PublicTextId TransientText { get; private set; } = PublicTextId.None;
        public double TransientUntil { get; private set; }
        public bool OperatorPaused { get; private set; }
        public bool ExitConfirmOpen { get; private set; }
        public float ResetHoldProgress { get; private set; }
        public bool DebugVisible { get; private set; }
        public bool Muted { get; private set; }
        public FinalRoute PlannedRoute { get; private set; } = FinalRoute.None;
        public double PlanStillnessAccumulated { get; private set; }
        public Intent LastConsumedIntent { get; private set; }
        public int LiteralExecutionCount { get; private set; }
        public int RefusalCount { get; private set; }
        public string LastEventNote { get; private set; } = "";
        public float LastRecognitionResponseMs { get; private set; }
        public GestureFeature LearnedJumpTrace => Recognition.Dictionary.GetPrototype(SymbolicClass.Jump);
        public GestureFeature LearnedStopTrace => Recognition.Dictionary.GetPrototype(SymbolicClass.Stop);
        public int ChosenDirectionSign { get; private set; }

        // D-091 live-classification diagnostics (F1)
        public float LastLiveN1 { get; private set; }
        public float LastLiveN2 { get; private set; }
        public float LastLiveMargin { get; private set; }
        public int JumpTemplateCount => Recognition.Pcm.Pose(SymbolicClass.Jump).Samples.Count;
        public int StopTemplateCount => Recognition.Pcm.Pose(SymbolicClass.Stop).Samples.Count;

        // D-092 compound diagnostics (F1 POSE / MOTION / FUSE)
        public PcmResult LastPcm => Recognition.LastPcm;
        public PcmReject LastRejectReason { get; private set; }
        public SymbolicClass LastFiredClass { get; private set; } = SymbolicClass.None;

        // F2 operator Recognition QA (never shown in the public experience; never feeds the classifier)
        public bool RecognitionQaActive { get; private set; }
        public SymbolicClass QaExpected { get; private set; } = SymbolicClass.Jump;
        private readonly int[] qaCorrect = new int[2];   // A: correct-class compound fired
        private readonly int[] qaCross = new int[2];      // wrong-class fired
        private readonly int[] qaArmed = new int[2];      // reached PoseArmed at least once in the trial
        private readonly int[] qaNoFire = new int[2];     // attempt ended without firing (B/C/D)
        private readonly int[] qaTrials = new int[2];     // total scored trials
        public SymbolicClass QaLastChosen { get; private set; }
        public PcmReject QaLastReject { get; private set; }
        public bool QaLastArmed { get; private set; }
        public float QaLastTimeToTriggerMs { get; private set; }
        public float QaLastQuality { get; private set; }
        public int QaCorrect(SymbolicClass c) => qaCorrect[(int)c];
        public int QaCross(SymbolicClass c) => qaCross[(int)c];
        public int QaArmed(SymbolicClass c) => qaArmed[(int)c];
        public int QaNoFire(SymbolicClass c) => qaNoFire[(int)c];
        public int QaTrials(SymbolicClass c) => qaTrials[(int)c];

        // per-trial QA tracking of the live PCM stream
        private PcmState qaPrevState = PcmState.Neutral;
        private bool qaTrialArmed;
        private double qaTrialArmedAt;
        private bool qaTrialActive;

        public event Action<string> OnCue; // audio cue key
        public event Action OnFullReset;

        private double stateEnteredAt;          // gameplay clock
        private double stateEnteredRealtime;    // realtime clock
        private double handStableSince = -1;
        private double trackingLostSince = -1;
        private double reconnectStableSince = -1;
        private double rechargeStartedAt = -1;
        private double fixedAccumulator;
        private double lastRealNow;
        private bool spaceHeldPrev;
        private double resetHeldSince = -1;
        private Intent boundJumpReservation;
        private Affordance boundJumpAffordance;
        private Intent boundStopReservation;
        private Affordance boundStopAffordance;
        private Intent activeStopIntent;        // consumed, waiting at gate
        private ScanGate observedGate;
        private Intent airborneIntent;          // consumed jump in flight
        private Affordance airborneAffordance;
        private Intent incompleteConsumed;      // restorable on tracking rewind
        private int gateOverlapSteps;
        private double refusalBusyUntil;
        private readonly List<double> recentFrameMs = new List<double>(600);
        private bool waitingReturnToProof;
        private Before.Course.ScanGate stopWaitGate; // D-095: gate NUL is autonomously waiting at (until safe)

        public BeforeDirector(BeforeRuntimeConfig runtimeCfg, BeforeRecognitionConfig recognitionCfg, BeforeCourseConfig courseCfg, IHandSignalSource hand)
        {
            RuntimeCfg = runtimeCfg;
            RecognitionCfg = recognitionCfg;
            CourseCfg = courseCfg;
            Hand = hand;
            Recognition = new RecognitionCoordinator(recognitionCfg, runtimeCfg);
            Course = new CourseModel(courseCfg);
            Motor = new NulMotor2D(courseCfg, Course);
            Motor.SetCollapsed(true);
            Perception = new LocalPerception(courseCfg.perceptionHorizonSeconds, courseCfg.lateCutoffSeconds);
            RebuildAffordances();
            Recognition.OnEvent += HandleRecognitionEvent;
            Recognition.OnPcmLive += HandlePcmLive;
            Story.OnTransition += HandleTransition;
            Queue.OnReturnedToHand += _ => { };
        }

        // ---------------------------------------------------------- main tick

        public void Tick(double realNow, bool spaceHeld, bool pauseToggle, bool resetHeld, bool debugToggle, bool muteToggle, bool escPressed, bool enterPressed)
        {
            double dtReal = lastRealNow > 0 ? realNow - lastRealNow : 0;
            lastRealNow = realNow;
            if (dtReal > 0.25) dtReal = 0.25;
            Realtime.Advance(realNow);
            if (dtReal > 0) recentFrameMs.Add(dtReal * 1000.0);
            if (recentFrameMs.Count > 600) recentFrameMs.RemoveAt(0);

            HandleOperator(realNow, dtReal, pauseToggle, resetHeld, debugToggle, muteToggle, escPressed, enterPressed);
            if (ExitConfirmOpen) return;

            Hand.Tick(realNow);
            UpdateTrackingRecovery(realNow);
            UpdateCameraState();

            // hand samples into recognition
            bool captureQuality = Hand.Tracked && Hand.Quality >= RuntimeCfg.presenceCaptureMin;
            // F2 QA overrides intake so live gestures are segmented and classified without enqueue/adaptation
            if (RecognitionQaActive)
            {
                Recognition.ClassifyOnlyQa = true;
                Recognition.SetMode(IntakeAllowed() ? CommandIntakeMode.LiveSymbolic : CommandIntakeMode.None);
            }
            else
            {
                Recognition.ClassifyOnlyQa = false;
                Recognition.SetMode(IntakeAllowed() ? Story.IntakeMode : CommandIntakeMode.None);
            }
            Recognition.SetSpaceHeld(spaceHeld && !spaceHeldPrev ? true : spaceHeld, realNow, Recognition.QualityGateOpen);
            spaceHeldPrev = spaceHeld;
            if (Hand.ConsumeNewSample())
            {
                if (Hand.Tracked)
                {
                    // D-091: pass the full 21-landmark frame (cloned so later inference cannot corrupt buffered
                    // history) plus tracking quality and handedness into Recognition V2. Mirroring/handedness
                    // normalization is applied once inside V2; landmarks are already mirrored at the source.
                    var src = Hand.LandmarksMirrored;
                    Vector2[] lmClone = null;
                    if (src != null && src.Length >= 21)
                    {
                        lmClone = new Vector2[21];
                        System.Array.Copy(src, lmClone, 21);
                    }
                    Recognition.Feed(new PalmSample
                    {
                        Time = Hand.LastSampleTime,
                        Position = Hand.PalmMirrored,
                        Landmarks = lmClone,
                        Handedness = 0f, // BlazeHand handedness not exposed; V2 skips mirror when 0 (no double-mirror)
                        Quality = Hand.Quality
                    }, captureQuality);
                    UpdatePlanStillness(realNow);
                }
                else
                {
                    Recognition.NotifyTrackingInvalid();
                }
            }

            // gameplay clock
            bool gameplayActive = !OperatorPaused && Recovery != TrackingRecoveryState.SuspendedRewound
                && Recovery != TrackingRecoveryState.Collapsed && Recovery != TrackingRecoveryState.Reconnecting;
            if (gameplayActive) Gameplay.Tick(dtReal);

            // fixed-step motor
            if (gameplayActive && MotorShouldStep())
            {
                fixedAccumulator += dtReal;
                int guard = 0;
                while (fixedAccumulator >= NulMotor2D.FixedStep && guard++ < 8)
                {
                    fixedAccumulator -= NulMotor2D.FixedStep;
                    Motor.Step();
                    CheckHazards();
                }
            }

            UpdateStory(realNow);
            if (gameplayActive) UpdatePlanner();
        }

        private bool MotorShouldStep()
        {
            switch (Story.State)
            {
                case BeforeStoryState.Boot:
                case BeforeStoryState.CameraUnavailable:
                case BeforeStoryState.AwaitHand:
                case BeforeStoryState.ReconnectCharge:
                case BeforeStoryState.CompleteHold:
                    return false;
                default:
                    return true;
            }
        }

        private bool IntakeAllowed()
        {
            return !OperatorPaused && !ExitConfirmOpen
                && Recovery != TrackingRecoveryState.SuspendedRewound
                && Recovery != TrackingRecoveryState.Collapsed
                && Realtime.Now >= refusalBusyUntil;
        }

        // ---------------------------------------------------------- operator

        private void HandleOperator(double now, double dt, bool pauseToggle, bool resetHeld, bool debugToggle, bool muteToggle, bool escPressed, bool enterPressed)
        {
            if (debugToggle) DebugVisible = !DebugVisible;
            if (muteToggle) { Muted = !Muted; }

            if (escPressed)
            {
                ExitConfirmOpen = !ExitConfirmOpen;
            }
            if (ExitConfirmOpen)
            {
                if (enterPressed) RequestEditorExit?.Invoke();
                return;
            }

            if (pauseToggle)
            {
                OperatorPaused = !OperatorPaused;
                Gameplay.SetOperatorPause(OperatorPaused);
            }

            if (resetHeld)
            {
                if (resetHeldSince < 0) resetHeldSince = now;
                ResetHoldProgress = Mathf.Clamp01((float)((now - resetHeldSince) / 2.0));
                if (ResetHoldProgress >= 1f)
                {
                    resetHeldSince = -1;
                    ResetHoldProgress = 0f;
                    FullReset();
                }
            }
            else
            {
                resetHeldSince = -1;
                ResetHoldProgress = 0f;
            }
        }

        public Action RequestEditorExit;

        public void FullReset()
        {
            Recognition.ResetAll(); // clears V2 teaching + adaptation samples + statistics (D-091)
            RecognitionQaActive = false;
            QaExpected = SymbolicClass.Jump;
            ResetQaStats();
            Queue.SetFinalPlanMode(false);
            Queue.ClearPending();
            Course.SelectRoute(FinalRoute.None);
            PlannedRoute = FinalRoute.None;
            ChosenDirectionSign = 0;
            Motor.Teleport(new Vector2(CourseCfg.xCollapsed, 0f), Vector2.zero, MotorState.Collapsed, true);
            Gameplay.ResetTo(0);
            ClearReservations();
            incompleteConsumed = null;
            LiteralExecutionCount = 0;
            RefusalCount = 0;
            Recovery = TrackingRecoveryState.Connected;
            trackingLostSince = -1;
            handStableSince = -1;
            waitingReturnToProof = false;
            RebuildAffordances();
            Story.Fire(StoryEvent.FullReset);
            SetText(PublicTextId.BootWait);
            OnFullReset?.Invoke();
        }

        private void ClearReservations()
        {
            boundJumpReservation = null;
            boundJumpAffordance = null;
            boundStopReservation = null;
            boundStopAffordance = null;
            activeStopIntent = null;
            observedGate = null;
            airborneIntent = null;
            airborneAffordance = null;
            gateOverlapSteps = 0;
            Motor.OnTakeoff -= HandleTakeoff;
            pendingTakeoffIntent = null;
            pendingTakeoffAffordance = null;
        }

        // ---------------------------------------------------------- tracking recovery

        private void UpdateTrackingRecovery(double now)
        {
            bool afterWake = Story.State != BeforeStoryState.Boot && Story.State != BeforeStoryState.CameraUnavailable
                && Story.State != BeforeStoryState.AwaitHand && Story.State != BeforeStoryState.ReconnectCharge
                && Story.State != BeforeStoryState.CompleteHold && Story.State != BeforeStoryState.Farewell
                && Story.State != BeforeStoryState.CoreTransfer && Story.State != BeforeStoryState.CoreArrival;

            if (!afterWake)
            {
                Recovery = TrackingRecoveryState.Connected;
                trackingLostSince = -1;
                return;
            }

            if (Hand.Tracked && Hand.CameraAvailable)
            {
                switch (Recovery)
                {
                    case TrackingRecoveryState.SuspendedRewound:
                    case TrackingRecoveryState.Collapsed:
                        if (reconnectStableSince < 0) reconnectStableSince = now;
                        if (now - reconnectStableSince >= RuntimeCfg.reconnectStableSeconds)
                        {
                            Recovery = TrackingRecoveryState.Recharging;
                            rechargeStartedAt = now;
                        }
                        break;
                    case TrackingRecoveryState.Recharging:
                        if (now - rechargeStartedAt >= RuntimeCfg.rechargeSeconds)
                        {
                            Recovery = TrackingRecoveryState.Connected;
                            Motor.ResumeFromSuspend();
                            SetText(TextForState(Story.State));
                        }
                        break;
                    default:
                        Recovery = TrackingRecoveryState.Connected;
                        break;
                }
                trackingLostSince = -1;
                return;
            }

            reconnectStableSince = -1;
            if (trackingLostSince < 0) trackingLostSince = now;
            double lost = now - trackingLostSince;

            if (Recovery == TrackingRecoveryState.Collapsed) return;
            if (lost >= RuntimeCfg.lossCollapseSeconds)
            {
                Recovery = TrackingRecoveryState.Collapsed;
            }
            else if (lost >= RuntimeCfg.lossFragmentSeconds)
            {
                if (Recovery != TrackingRecoveryState.SuspendedRewound)
                {
                    Recovery = TrackingRecoveryState.SuspendedRewound;
                    RewindToSafeAnchor();
                    SetText(PublicTextId.SignalLost);
                }
            }
            else if (lost >= RuntimeCfg.lossJitterSeconds)
            {
                Recovery = TrackingRecoveryState.Fragmented;
            }
            else
            {
                Recovery = TrackingRecoveryState.Jitter;
            }
        }

        private void RewindToSafeAnchor()
        {
            float anchorX = BeforeStoryMachine.SafeAnchorX(Story.State, CourseCfg);
            Motor.Teleport(new Vector2(anchorX, 0f), Vector2.zero, MotorState.Stopped, true);
            Motor.Suspend();
            if (incompleteConsumed != null)
            {
                Queue.RestoreToHead(incompleteConsumed);
                incompleteConsumed = null;
            }
            Recognition.NotifyTrackingInvalid();
        }

        // ---------------------------------------------------------- camera / text

        private void UpdateCameraState()
        {
            if (Story.State == BeforeStoryState.Boot)
            {
                Story.Fire(Hand.CameraAvailable ? StoryEvent.CameraAvailable : StoryEvent.CameraLost);
                SetText(Hand.CameraAvailable ? PublicTextId.BootWait : PublicTextId.CameraDenied);
            }
            else if (Story.State == BeforeStoryState.CameraUnavailable && Hand.CameraAvailable)
            {
                Story.Fire(StoryEvent.CameraAvailable);
                SetText(PublicTextId.BootWait);
            }
            else if (Story.State == BeforeStoryState.AwaitHand && !Hand.CameraAvailable)
            {
                Story.Fire(StoryEvent.CameraLost);
                SetText(PublicTextId.CameraDenied);
            }
        }

        private void SetText(PublicTextId id)
        {
            CurrentText = id;
        }

        private void SetTransient(PublicTextId id, double holdSeconds)
        {
            TransientText = id;
            TransientUntil = Realtime.Now + holdSeconds;
        }

        public PublicTextId VisibleText
        {
            get
            {
                if (ExitConfirmOpen) return PublicTextId.ExitConfirm;
                if (OperatorPaused) return PublicTextId.Paused;
                if (TransientText != PublicTextId.None && Realtime.Now < TransientUntil) return TransientText;
                return CurrentText;
            }
        }

        private PublicTextId TextForState(BeforeStoryState s)
        {
            switch (s)
            {
                case BeforeStoryState.Boot:
                case BeforeStoryState.AwaitHand: return PublicTextId.BootWait;
                case BeforeStoryState.CameraUnavailable: return PublicTextId.CameraDenied;
                case BeforeStoryState.ReconnectCharge: return PublicTextId.Linking;
                case BeforeStoryState.JumpTeachSample1:
                case BeforeStoryState.StopTeachSample1: return PublicTextId.TeachBegin;
                case BeforeStoryState.JumpTeachSample2:
                case BeforeStoryState.StopTeachSample2: return PublicTextId.SampleOne;
                case BeforeStoryState.DirectionTeachMap: return PublicTextId.PlanWait;
                case BeforeStoryState.FinalPlanMap: return PublicTextId.PlanFinal;
                case BeforeStoryState.FinalRewind: return PublicTextId.Rewind;
                case BeforeStoryState.CoreTransfer: return PublicTextId.Independent;
                case BeforeStoryState.CompleteHold: return PublicTextId.EndReset;
                default: return PublicTextId.None;
            }
        }

        // ---------------------------------------------------------- story choreography

        private void HandleTransition(BeforeStoryState from, BeforeStoryState to, StoryEvent evt)
        {
            stateEnteredAt = Gameplay.Now;
            stateEnteredRealtime = Realtime.Now;
            SetText(TextForState(to));
            LastEventNote = $"{from}->{to} ({evt})";

            switch (to)
            {
                case BeforeStoryState.WakeNul:
                    Motor.SetCollapsed(false);
                    OnCue?.Invoke("link");
                    break;
                case BeforeStoryState.RunToJumpTeach:
                    Motor.BeginRun();
                    Motor.BrakeTo(CourseCfg.xJumpTeachAnchor);
                    SetTransient(PublicTextId.LinkOk, 0.8);
                    break;
                case BeforeStoryState.JumpProofRun:
                case BeforeStoryState.JumpProofBRun:
                case BeforeStoryState.StopProofARun:
                case BeforeStoryState.StopProofBRun:
                    Motor.BeginRun();
                    break;
                case BeforeStoryState.StopTeachExecute:
                    // static-Stop teaching demo: NUL autonomously waits at the teach gate and passes on a
                    // safe opening (no learned "word" is queued for a static sign).
                    Motor.BeginRun();
                    stopWaitGate = Course.GateAt(CourseCfg.xGateBoundary, 0.3f);
                    break;
                case BeforeStoryState.RunToStopTeach:
                    Motor.BeginRun();
                    Motor.BrakeTo(CourseCfg.xStopAnchor);
                    break;
                case BeforeStoryState.RunToDirectionTeach:
                    Motor.BeginRun();
                    Motor.BrakeTo(CourseCfg.xForkAnchor);
                    break;
                case BeforeStoryState.DirectionTeachExecute:
                    Motor.BeginRun();
                    break;
                case BeforeStoryState.RunToFinalFork:
                    Motor.BeginRun();
                    Motor.BrakeTo(CourseCfg.xFinalAnchor);
                    break;
                case BeforeStoryState.FinalPlanMap:
                    Queue.SetFinalPlanMode(true);
                    Recognition.RearmDirection();
                    PlanStillnessAccumulated = 0;
                    Checkpoints.Capture(Motor, Course);
                    break;
                case BeforeStoryState.FinalCourseExecute:
                    Queue.SetFinalPlanMode(false);
                    Motor.BeginRun();
                    break;
                case BeforeStoryState.FinalRewind:
                    Queue.ClearPending();
                    ClearReservations();
                    break;
                case BeforeStoryState.CoreArrival:
                    Motor.BeginRun();
                    Motor.BrakeTo(CourseCfg.xCoreChamber);
                    break;
                case BeforeStoryState.CompleteHold:
                    SetText(PublicTextId.EndReset);
                    break;
            }
        }

        private void UpdateStory(double now)
        {
            double inState = now - stateEnteredRealtime;
            switch (Story.State)
            {
                case BeforeStoryState.AwaitHand:
                    if (Hand.Tracked)
                    {
                        if (handStableSince < 0) handStableSince = now;
                        if (now - handStableSince >= RuntimeCfg.handStableBindSeconds)
                        {
                            Story.Fire(StoryEvent.HandStable);
                            SetText(PublicTextId.Linking);
                        }
                    }
                    else handStableSince = -1;
                    break;

                case BeforeStoryState.ReconnectCharge:
                    if (inState >= 0.8) Story.Fire(StoryEvent.ChargeComplete); // 0.35 gather + 0.45 beam
                    break;

                case BeforeStoryState.WakeNul:
                    if (inState >= 0.8 + 0.30 + 0.45) Story.Fire(StoryEvent.WakeComplete); // 8f@10fps + steady + hand look
                    break;

                case BeforeStoryState.RunToJumpTeach:
                    if (AtAnchor(CourseCfg.xJumpTeachAnchor)) Story.Fire(StoryEvent.ReachedAnchor);
                    break;

                case BeforeStoryState.JumpTeachSample1:
                case BeforeStoryState.JumpTeachSample2:
                case BeforeStoryState.StopTeachSample1:
                case BeforeStoryState.StopTeachSample2:
                    UpdateTeachingText();
                    break;

                case BeforeStoryState.JumpProofRun:
                    if (Motor.Position.x >= CourseCfg.xProofCue) Story.Fire(StoryEvent.ProofRevealed);
                    break;

                case BeforeStoryState.StopTeachExecute:
                    UpdateStopGateBeat(CourseCfg.xGateBoundary, CourseCfg.xGateExit, () => Story.Fire(StoryEvent.ActionExecuted));
                    break;

                case BeforeStoryState.StopProofARun:
                    if (Motor.Position.x >= CourseCfg.xStopProofACue) Story.Fire(StoryEvent.ProofRevealed);
                    break;
                case BeforeStoryState.StopProofAWait:
                    UpdateStopGateBeat(CourseCfg.xStopProofAGate, CourseCfg.xStopProofAExit, () => Story.Fire(StoryEvent.ActionExecuted));
                    break;

                case BeforeStoryState.JumpProofBRun:
                    if (Motor.Position.x >= CourseCfg.xJumpProofBCue) Story.Fire(StoryEvent.ProofRevealed);
                    break;

                case BeforeStoryState.StopProofBRun:
                    if (Motor.Position.x >= CourseCfg.xStopProofBCue) Story.Fire(StoryEvent.ProofRevealed);
                    break;
                case BeforeStoryState.StopProofBWait:
                    UpdateStopGateBeat(CourseCfg.xStopProofBGate, CourseCfg.xStopProofBExit, () => Story.Fire(StoryEvent.ActionExecuted));
                    break;

                case BeforeStoryState.RunToStopTeach:
                    if (AtAnchor(CourseCfg.xStopAnchor)) Story.Fire(StoryEvent.ReachedAnchor);
                    break;

                case BeforeStoryState.RunToDirectionTeach:
                    if (AtAnchor(CourseCfg.xForkAnchor)) Story.Fire(StoryEvent.ReachedAnchor);
                    break;

                case BeforeStoryState.DirectionTeachExecute:
                    if (Motor.Position.x >= CourseCfg.xForkRejoin) Story.Fire(StoryEvent.BranchTraversed);
                    break;

                case BeforeStoryState.RunToFinalFork:
                    if (AtAnchor(CourseCfg.xFinalAnchor)) Story.Fire(StoryEvent.ReachedAnchor);
                    break;

                case BeforeStoryState.FinalPlanMap:
                    if (Queue.IsFullPlan && PlanStillnessAccumulated >= 2.0)
                    {
                        CommitFinalPlan();
                    }
                    break;

                case BeforeStoryState.FinalRewind:
                    if (inState >= CourseCfg.faultFreezeSeconds + CourseCfg.rewindSeconds)
                    {
                        Checkpoints.Restore(Motor, Course);
                        if (waitingReturnToProof)
                        {
                            waitingReturnToProof = false;
                            // proof failure: resume proof wait without touching the final-map path
                            Motor.Teleport(new Vector2(CourseCfg.xProofAnchor, 0f), Vector2.zero, MotorState.Stopped, true);
                        }
                        Story.Fire(StoryEvent.RewindComplete);
                    }
                    break;

                case BeforeStoryState.CoreArrival:
                    if (Motor.State == MotorState.Stopped && Motor.Position.x >= CourseCfg.xCoreChamber - 0.2f)
                    {
                        Story.Fire(StoryEvent.CoreReached);
                        OnCue?.Invoke("core");
                    }
                    break;

                case BeforeStoryState.CoreTransfer:
                    if (inState >= 0.8 + 1.2 + 2.0) Story.Fire(StoryEvent.TransferComplete); // rings + pulse + INDEPENDENT
                    break;

                case BeforeStoryState.Farewell:
                    // steps + look back + three traces (0.55*3 + 0.20*2) + bow + run out
                    if (inState >= 0.6 + (0.55 * 3 + 0.20 * 2) + 1.1 + 1.8 + 0.8) Story.Fire(StoryEvent.FarewellComplete);
                    break;
            }
        }

        // D-092 compound teaching text: "hold the shape" -> (pose stable) "now move" -> between samples the
        // running "合図を記録 N/3" readout. Diegetic; no Pose/Motion/DTW terms.
        private void UpdateTeachingText()
        {
            if (Recognition.TeachingCaptureOpen)
                SetText(Recognition.TeachPoseStageComplete ? PublicTextId.PoseConfirm : PublicTextId.TeachBegin);
            else
                SetText(Recognition.TeachCollected > 0 ? PublicTextId.TeachRecord : PublicTextId.TeachBegin);
        }

        private Before.Course.ScanGate crossingGate; // committed to cross; ignore Stop re-latch until through

        // D-095 static-Stop gate beat: brake at the gate when the player Stops (or autonomously in the teach
        // demo), then pass ONLY on a safe opening — waiting autonomously even after the sign is released.
        private void UpdateStopGateBeat(float gateX, float exitX, System.Action onPassed)
        {
            if (Motor.Position.x >= exitX) { stopWaitGate = null; crossingGate = null; onPassed(); return; }
            var gate = Course.GateAt(gateX, 0.3f);
            if (gate == null) return;
            float traversal = (CourseCfg.gatePassageWidth + 0.8f) / CourseCfg.runSpeed;
            float anchor = gateX - 1.2f;

            if (crossingGate != gate && (Recognition.StopActive || stopWaitGate == gate) && Motor.Position.x < gateX - 0.1f)
            {
                stopWaitGate = gate;
                Motor.BrakeTo(anchor);
            }
            if (stopWaitGate == gate && Motor.State == MotorState.Stopped
                && gate.RemainingOpen(Gameplay.Now) > traversal + CourseCfg.gateEntryMargin)
            {
                Motor.BeginRun();
                stopWaitGate = null;
                crossingGate = gate; // the open window guarantees safe passage; don't re-latch
            }
        }

        private bool AtAnchor(float x)
        {
            return Motor.State == MotorState.Stopped && Mathf.Abs(Motor.Position.x - x) < 0.25f;
        }

        private void CommitFinalPlan()
        {
            Intent direction = Queue.GetSlot(0);
            PlannedRoute = direction.DirectionSign < 0 ? FinalRoute.Left : FinalRoute.Right;
            ChosenDirectionSign = direction.DirectionSign;
            Course.SelectRoute(PlannedRoute);
            RebuildAffordances();
            Queue.ConsumeHead(); // direction commits the route at map close
            Story.Fire(StoryEvent.PlanCommitted);
        }

        private void UpdatePlanStillness(double now)
        {
            if (Story.State != BeforeStoryState.FinalPlanMap) { PlanStillnessAccumulated = 0; return; }
            if (!Queue.IsFullPlan) { PlanStillnessAccumulated = 0; return; }
            if (OperatorPaused) return; // P freezes plan confirmation
            if (Recognition.LastLiveSpeed < RecognitionCfg.armSpeed && Hand.Tracked)
            {
                PlanStillnessAccumulated += 1.0 / Math.Max(1.0, RuntimeCfg.inferenceRateHz);
            }
            else
            {
                PlanStillnessAccumulated = 0;
            }
        }

        // ---------------------------------------------------------- recognition -> intents

        // --- F2 Recognition QA (operator-only; expected label is used ONLY for tallying) ---------------

        public void ToggleRecognitionQa()
        {
            RecognitionQaActive = !RecognitionQaActive;
            if (RecognitionQaActive) ResetQaStats();
        }

        public void SetQaExpected(SymbolicClass c) { if (RecognitionQaActive) QaExpected = c; }

        public void ResetQaStats()
        {
            for (int i = 0; i < 2; i++) { qaCorrect[i] = qaCross[i] = qaArmed[i] = qaNoFire[i] = qaTrials[i] = 0; }
            QaLastChosen = SymbolicClass.None; QaLastReject = PcmReject.None; QaLastArmed = false;
            QaLastTimeToTriggerMs = QaLastQuality = 0f;
            qaPrevState = PcmState.Neutral; qaTrialArmed = false; qaTrialActive = false;
        }

        // D-092: every live PCM frame. Drives F1 (POSE/MOTION/FUSE + reject reason) always, and — only in F2
        // QA — tallies A/B/C/D against the operator's expected label. The expected label is NEVER fed to the
        // recognizer; it is used purely for scoring.
        private void HandlePcmLive(PcmResult r)
        {
            // F1 production diagnostics
            LastLiveN1 = r.PoseNorm; LastLiveN2 = r.PoseSecondNorm; LastLiveMargin = r.PoseMargin;
            if (r.Reject != PcmReject.None) LastRejectReason = r.Reject;
            if (r.Fired) LastFiredClass = r.FiredClass;

            if (!RecognitionQaActive) return;
            int e = (int)QaExpected;

            // a QA "trial" starts when motion begins after arming and ends at Triggered or when the machine
            // returns to Neutral/Rearming without firing
            if (r.State == PcmState.PoseArmed || r.State == PcmState.MotionTracking)
            {
                if (!qaTrialActive) { qaTrialActive = true; qaTrialArmed = false; }
                if (!qaTrialArmed) { qaTrialArmed = true; qaTrialArmedAt = Realtime.Now; }
                QaLastArmed = true; QaLastQuality = r.PoseNorm;
            }

            bool enteredTriggered = r.State == PcmState.Triggered && qaPrevState != PcmState.Triggered;
            bool endedNoFire = (r.State == PcmState.Rearming || r.State == PcmState.Rejected)
                               && (qaPrevState == PcmState.MotionTracking || qaPrevState == PcmState.PoseArmed);

            if (enteredTriggered && qaTrialActive)
            {
                qaTrials[e]++;
                qaArmed[e] += qaTrialArmed ? 1 : 0;
                QaLastChosen = r.FiredClass;
                QaLastTimeToTriggerMs = (float)((Realtime.Now - qaTrialArmedAt) * 1000.0);
                if (r.FiredClass == QaExpected) qaCorrect[e]++; else qaCross[e]++;
                qaTrialActive = false; qaTrialArmed = false;
            }
            else if (endedNoFire && qaTrialActive)
            {
                qaTrials[e]++;
                qaArmed[e] += qaTrialArmed ? 1 : 0;
                qaNoFire[e]++;
                QaLastReject = r.Reject;
                qaTrialActive = false; qaTrialArmed = false;
            }
            qaPrevState = r.State;
        }

        private void HandleRecognitionEvent(RecognitionEvent e)
        {
            double now = Realtime.Now;
            LastRecognitionResponseMs = (float)((now - e.SegmentClosedAt) * 1000.0);

            switch (e.Kind)
            {
                case RecognitionEventKind.TeachingSampleAccepted:
                    // adaptive 3+1: stay in the collecting phase; show the real running count "動作を記録 N/M"
                    SetText(PublicTextId.TeachRecord);
                    OnCue?.Invoke("sample");
                    Story.Fire(StoryEvent.SampleAccepted); // no-op once already in the second collecting state
                    break;

                case RecognitionEventKind.TeachingClassClosed:
                {
                    SetTransient(PublicTextId.Learned, 0.8);
                    OnCue?.Invoke("sample");
                    Story.Fire(StoryEvent.ClassClosed);
                    // Jump: the learned compound word crosses the teach gap. Stop is a STATIC sign whose teach
                    // demo is an autonomous gate wait (no queued word).
                    if (e.Class == SymbolicClass.Jump)
                    {
                        var intent = new Intent(Intent.FromClass(SymbolicClass.Jump), 0, IntentConfidence.Stable, e.Feature, now);
                        Queue.Enqueue(intent);
                    }
                    break;
                }

                case RecognitionEventKind.TeachingSampleTooShort:
                    SetTransient(PublicTextId.SampleShort, 1.2);
                    break;

                case RecognitionEventKind.TeachingSampleInconsistent:
                    SetTransient(PublicTextId.SampleConflict, 1.4);
                    break;

                case RecognitionEventKind.TeachingClassOverlap:
                    SetText(PublicTextId.SignalOverlap);
                    break;

                case RecognitionEventKind.LiveAccepted:
                case RecognitionEventKind.LiveAmbiguous:
                {
                    var confidence = e.Kind == RecognitionEventKind.LiveAccepted ? IntentConfidence.Stable : IntentConfidence.Unstable;
                    var intent = new Intent(Intent.FromClass(e.Class), 0, confidence, e.Feature, now)
                    {
                        SourceFeatureV2 = e.FeatureV2,
                        SourceQuality = e.Quality,
                        SourceMargin = e.N2 - e.N1
                    };
                    OnCue?.Invoke(e.Kind == RecognitionEventKind.LiveAccepted ? "accept" : "ambiguous");
                    EnqueueWithReplacement(intent);
                    break;
                }

                case RecognitionEventKind.LiveNoMatch:
                    OnCue?.Invoke("nomatch");
                    LastEventNote = "no-match (ignored)";
                    break;

                case RecognitionEventKind.DirectionDetected:
                {
                    if (Story.IntakeMode != CommandIntakeMode.Direction && Story.IntakeMode != CommandIntakeMode.FinalPlan) break;
                    var intent = new Intent(IntentType.Direction, e.DirectionSign, IntentConfidence.Stable, null, now);
                    OnCue?.Invoke("accept");
                    if (Story.State == BeforeStoryState.DirectionTeachMap)
                    {
                        ChosenDirectionSign = e.DirectionSign;
                        Story.Fire(StoryEvent.DirectionChosen);
                    }
                    else
                    {
                        var outcome = Queue.Enqueue(intent);
                        if (outcome == EnqueueOutcome.FinalPlanDirectionSet || outcome == EnqueueOutcome.FinalPlanDirectionReplacedPlanCleared)
                        {
                            PlannedRoute = e.DirectionSign < 0 ? FinalRoute.Left : FinalRoute.Right;
                            Recognition.RearmDirection();
                        }
                    }
                    break;
                }
            }
        }

        private void EnqueueWithReplacement(Intent intent)
        {
            Intent replace = null;
            if (!Queue.FinalPlanMode)
            {
                // a newer same-semantic intent bound to the same affordance replaces the reservation
                if (intent.Type == IntentType.Jump && boundJumpReservation != null) replace = boundJumpReservation;
                if (intent.Type == IntentType.Stop && boundStopReservation != null) replace = boundStopReservation;
            }
            var outcome = Queue.Enqueue(intent, replace);
            if (outcome == EnqueueOutcome.RejectedFull)
            {
                SetTransient(PublicTextId.QueueFull, 1.0);
                OnCue?.Invoke("nomatch");
            }
            else if (outcome == EnqueueOutcome.ReplacedReservation)
            {
                if (intent.Type == IntentType.Jump) boundJumpReservation = intent;
                if (intent.Type == IntentType.Stop) boundStopReservation = intent;
            }
        }

        // ---------------------------------------------------------- planner / execution

        private void UpdatePlanner()
        {
            // stop wait at gate: resume when safe opening allows traversal
            if (activeStopIntent != null && observedGate != null && Motor.State == MotorState.Stopped)
            {
                float traversal = (CourseCfg.gatePassageWidth + 0.8f) / CourseCfg.runSpeed;
                if (observedGate.RemainingOpen(Gameplay.Now) > traversal + CourseCfg.gateEntryMargin)
                {
                    CompleteAdaptation(activeStopIntent);
                    OnCue?.Invoke("success");
                    var done = activeStopIntent;
                    activeStopIntent = null;
                    observedGate = null;
                    incompleteConsumed = null;
                    Motor.BeginRun();
                    AfterActionExecuted(done);
                }
                return;
            }

            var head = Queue.Head;
            if (head == null)
            {
                SelfPreservationBrake();
                return;
            }
            if (Story.State == BeforeStoryState.FinalPlanMap || Story.State == BeforeStoryState.DirectionTeachMap) return;
            if (Realtime.Now < refusalBusyUntil) return;

            // already-scheduled reservations are never re-bound: re-evaluating an intent while NUL
            // approaches its own planned takeoff/brake point would misread the plan as too late
            if (head == boundJumpReservation && pendingTakeoffIntent == head) return;
            if (head == boundStopReservation && boundStopAffordance != null)
            {
                float stopAnchor = boundStopAffordance.ActionX - 1.2f;
                Motor.BrakeTo(stopAnchor);
                if (Motor.State == MotorState.Stopped && Mathf.Abs(Motor.Position.x - stopAnchor) < 0.3f)
                {
                    ScanGate stopGate = Course.GateAt(boundStopAffordance.ActionX, 0.2f);
                    var stopConsumed = Queue.ConsumeHead();
                    LastConsumedIntent = stopConsumed;
                    incompleteConsumed = stopConsumed;
                    activeStopIntent = stopConsumed;
                    observedGate = stopGate;
                    boundStopAffordance.Consumed = true;
                    boundStopReservation = null;
                    boundStopAffordance = null;
                }
                return;
            }

            float speed = Mathf.Max(Motor.Velocity.x, CourseCfg.runSpeed * 0.5f);
            var bind = IntentBinder.Bind(head, Perception, Motor.Position.x, speed, out Affordance affordance);

            // direction intent outside a fork context is incompatible
            if (head.Type == IntentType.Direction)
            {
                Refuse(head);
                return;
            }

            // context refusal: matching affordance absent while a different hazard is imminent
            if (bind == BindResult.LiteralNow && IncompatibleContext(head))
            {
                Refuse(head);
                return;
            }

            // committed final-plan items are a plan, not live utterances: they wait until
            // their affordance enters the perception horizon instead of executing literally
            if (bind == BindResult.LiteralNow && Story.State == BeforeStoryState.FinalCourseExecute)
            {
                return;
            }

            switch (bind)
            {
                case BindResult.BoundToAffordance:
                    if (head.Type == IntentType.Jump)
                    {
                        boundJumpReservation = head;
                        boundJumpAffordance = affordance;
                        float takeoff = Motor.SolveTakeoffX((GapHazard)FindGap(affordance), CourseCfg.runSpeed);
                        if (Motor.Position.x + Motor.Velocity.x * NulMotor2D.FixedStep * 3 >= takeoff - 0.05f && Motor.Grounded)
                        {
                            ConsumeHeadForJump(affordance);
                        }
                        else
                        {
                            Motor.BeginRun();
                            Motor.JumpAt(takeoff);
                            // consumption happens at takeoff; watch for it
                            PendingTakeoffConsume(affordance);
                        }
                    }
                    else if (head.Type == IntentType.Stop)
                    {
                        boundStopReservation = head;
                        boundStopAffordance = affordance;
                        ScanGate gate = Course.GateAt(affordance.ActionX, 0.2f);
                        float anchor = affordance.ActionX - 1.2f;
                        Motor.BrakeTo(anchor);
                        if (Motor.State == MotorState.Stopped && Mathf.Abs(Motor.Position.x - anchor) < 0.3f)
                        {
                            var consumed = Queue.ConsumeHead();
                            LastConsumedIntent = consumed;
                            incompleteConsumed = consumed;
                            activeStopIntent = consumed;
                            observedGate = gate;
                            boundStopReservation = null;
                            boundStopAffordance = null;
                            affordance.Consumed = true;
                        }
                    }
                    break;

                case BindResult.LiteralNow:
                {
                    // literal early execution: successful communication, premature action, no adaptation
                    var consumed = Queue.ConsumeHead();
                    LastConsumedIntent = consumed;
                    LiteralExecutionCount++;
                    OnCue?.Invoke("accept");
                    if (consumed.Type == IntentType.Jump)
                    {
                        Motor.JumpImmediate();
                        airborneIntent = null; // literal jump adapts nothing and binds nothing
                    }
                    else
                    {
                        Motor.LiteralStop();
                    }
                    break;
                }

                case BindResult.TooLate:
                {
                    // too late: no rescue; attempt is consumed and physics decides (typically failure->rewind)
                    var consumed = Queue.ConsumeHead();
                    LastConsumedIntent = consumed;
                    if (consumed.Type == IntentType.Jump) Motor.JumpImmediate();
                    else Motor.LiteralStop();
                    break;
                }
            }
        }

        private Intent pendingTakeoffIntent;
        private Affordance pendingTakeoffAffordance;

        private void PendingTakeoffConsume(Affordance affordance)
        {
            if (pendingTakeoffIntent == Queue.Head) return;
            pendingTakeoffIntent = Queue.Head;
            pendingTakeoffAffordance = affordance;
            Motor.OnTakeoff += HandleTakeoff;
        }

        private void HandleTakeoff()
        {
            Motor.OnTakeoff -= HandleTakeoff;
            if (pendingTakeoffIntent != null && Queue.Head == pendingTakeoffIntent)
            {
                ConsumeHeadForJump(pendingTakeoffAffordance);
            }
            pendingTakeoffIntent = null;
            pendingTakeoffAffordance = null;
        }

        private void ConsumeHeadForJump(Affordance affordance)
        {
            var consumed = Queue.ConsumeHead();
            LastConsumedIntent = consumed;
            incompleteConsumed = consumed;
            airborneIntent = consumed;
            airborneAffordance = affordance;
            boundJumpReservation = null;
            boundJumpAffordance = null;
            if (affordance != null) affordance.Consumed = true;
            if (Motor.Grounded && Motor.State != MotorState.JumpCharge && Motor.State != MotorState.Airborne)
            {
                Motor.JumpImmediate();
            }
        }

        private GapHazard FindGap(Affordance a)
        {
            foreach (var gap in Course.Gaps)
            {
                if (gap.Id == a.Id) return gap;
            }
            return Course.GapAhead(Motor.Position.x);
        }

        private bool IncompatibleContext(Intent head)
        {
            // refuse only when the opposite hazard is imminent and the matching one is not perceived
            float speed = Mathf.Max(Motor.Velocity.x, 0.1f);
            if (head.Type == IntentType.Jump)
            {
                var gate = Perception.FindPerceived(AffordanceType.Gate, Motor.Position.x, speed);
                return gate != null;
            }
            if (head.Type == IntentType.Stop)
            {
                var gap = Perception.FindPerceived(AffordanceType.Gap, Motor.Position.x, speed);
                return gap != null;
            }
            return false;
        }

        private void Refuse(Intent head)
        {
            Queue.RefuseHead();
            RefusalCount++;
            refusalBusyUntil = Realtime.Now + 1.21; // context-refusal choreography envelope
            OnCue?.Invoke("refuse");
            LastEventNote = $"refused {head.Type}";
        }

        private void SelfPreservationBrake()
        {
            // NUL never runs into a gap it perceives without a jump reservation
            if (!Motor.Grounded || Motor.Velocity.x <= 0.05f) return;
            if (airborneIntent != null || boundJumpReservation != null) return;
            var gap = Course.GapAhead(Motor.Position.x);
            if (gap == null || gap.NearEdge < Motor.Position.x) return;
            // wait far enough back that a jump bound while stopped (binder speed floor = half run speed)
            // is not automatically too late; a nearer scripted brake target already guarantees safety
            float stopPoint = gap.NearEdge - CourseCfg.lateCutoffSeconds * (CourseCfg.runSpeed * 0.5f) - 0.32f;
            if (Motor.BrakeTargetX <= stopPoint) return;
            float distance = stopPoint - Motor.Position.x;
            if (distance < Motor.StopDistance(Motor.Velocity.x) + 0.3f)
            {
                Motor.BrakeTo(stopPoint);
            }
        }

        private void CheckHazards()
        {
            // landing completes an airborne bound jump
            if (airborneIntent != null && Motor.Grounded && Motor.State != MotorState.JumpCharge && Motor.State != MotorState.Airborne)
            {
                var gap = airborneAffordance != null ? FindGap(airborneAffordance) : null;
                bool cleared = gap == null || Motor.Position.x >= gap.FarEdge;
                if (cleared)
                {
                    CompleteAdaptation(airborneIntent);
                    OnCue?.Invoke("success");
                    var done = airborneIntent;
                    airborneIntent = null;
                    airborneAffordance = null;
                    incompleteConsumed = null;
                    AfterActionExecuted(done);
                }
                else
                {
                    airborneIntent = null;
                    airborneAffordance = null;
                }
            }

            // gap failure
            if (Motor.Position.y < CourseCfg.gapFailY)
            {
                HandleCourseFailure();
                return;
            }

            // gate beam overlap
            foreach (var gate in Course.Gates)
            {
                if (gate.BeamActive(Gameplay.Now)
                    && Mathf.Abs(Motor.Position.x - gate.BoundaryX) < 0.36f
                    && Motor.Grounded && Motor.Velocity.x > 0.05f)
                {
                    gateOverlapSteps++;
                    if (gateOverlapSteps >= 2)
                    {
                        gateOverlapSteps = 0;
                        HandleCourseFailure();
                        return;
                    }
                }
            }
        }

        private void HandleCourseFailure()
        {
            incompleteConsumed = null;
            if (Story.State == BeforeStoryState.FinalCourseExecute)
            {
                Story.Fire(StoryEvent.CourseFailed);
            }
            else if (Story.State == BeforeStoryState.JumpProofWait || Story.State == BeforeStoryState.JumpProofRun)
            {
                waitingReturnToProof = true;
                Story.Fire(StoryEvent.CourseFailed);
                if (Story.State != BeforeStoryState.FinalRewind)
                {
                    // event not accepted from JumpProofRun: recover in place
                    Motor.Teleport(new Vector2(CourseCfg.xProofAnchor, 0f), Vector2.zero, MotorState.Stopped, true);
                    waitingReturnToProof = false;
                }
            }
            else
            {
                float anchorX = BeforeStoryMachine.SafeAnchorX(Story.State, CourseCfg);
                Motor.Teleport(new Vector2(anchorX, 0f), Vector2.zero, MotorState.Stopped, true);
            }
        }

        private void CompleteAdaptation(Intent intent)
        {
            // D-091: adapt only from a high-confidence, context-successful sample; V2 additionally gates on
            // tracking quality and classification margin and caps/prunes samples. Never during F2 QA, never
            // from an ambiguous/failed/no-match intent. Legacy Dictionary.Adapt is no longer called.
            if (intent == null || intent.SourceFeatureV2 == null) return;
            if (Recognition.ClassifyOnlyQa) return;
            if (intent.Confidence != IntentConfidence.Stable) return;
            if (intent.Type == IntentType.Jump)
                Recognition.V2.AdaptOnSuccess(SymbolicClass.Jump, intent.SourceFeatureV2, intent.SourceQuality, intent.SourceMargin);
            else if (intent.Type == IntentType.Stop)
                Recognition.V2.AdaptOnSuccess(SymbolicClass.Stop, intent.SourceFeatureV2, intent.SourceQuality, intent.SourceMargin);
        }

        private void AfterActionExecuted(Intent done)
        {
            switch (Story.State)
            {
                case BeforeStoryState.JumpTeachExecute:
                case BeforeStoryState.JumpProofWait:
                case BeforeStoryState.JumpProofBWait:
                case BeforeStoryState.StopTeachExecute:
                case BeforeStoryState.StopProofAWait:
                case BeforeStoryState.StopProofBWait:
                    Story.Fire(StoryEvent.ActionExecuted);
                    break;
                case BeforeStoryState.FinalCourseExecute:
                    if (Motor.Position.x >= CourseCfg.xCoreApproach - 1.0f || AllBranchHazardsCleared())
                    {
                        Story.Fire(StoryEvent.CourseSucceeded);
                    }
                    break;
            }
        }

        private bool AllBranchHazardsCleared()
        {
            foreach (var a in Perception.All)
            {
                if (!a.Consumed && a.ActionX > CourseCfg.xBranchOrigin && a.ActionX < CourseCfg.xCoreApproach) return false;
            }
            return true;
        }

        private void RebuildAffordances()
        {
            var list = new List<Affordance>();
            foreach (var gap in Course.Gaps)
            {
                list.Add(new Affordance { Type = AffordanceType.Gap, ActionX = gap.NearEdge, Id = gap.Id });
            }
            foreach (var gate in Course.Gates)
            {
                list.Add(new Affordance { Type = AffordanceType.Gate, ActionX = gate.BoundaryX, Id = $"gate{gate.BoundaryX:F0}" });
            }
            Perception.SetAffordances(list);
        }

        // diagnostics
        public IReadOnlyList<double> RecentFrameMs => recentFrameMs;
    }
}
