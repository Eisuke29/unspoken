using System;
using System.Collections.Generic;
using Before.App;
using Before.Config;
using UnityEngine;

namespace Before.Recognition
{
    public enum RecognitionEventKind
    {
        TeachingSampleAccepted,
        TeachingClassClosed,
        TeachingSampleTooShort,
        TeachingSampleInconsistent,
        TeachingClassOverlap,
        LiveAccepted,
        LiveAmbiguous,
        LiveNoMatch,
        DirectionDetected
    }

    public readonly struct RecognitionEvent
    {
        public readonly RecognitionEventKind Kind;
        public readonly SymbolicClass Class;
        public readonly int DirectionSign;
        public readonly GestureFeature Feature;       // legacy palm-only feature (kept for trace rendering)
        public readonly GestureFeatureV2 FeatureV2;   // D-091 multichannel feature (production classifier)
        public readonly float N1;
        public readonly float N2;
        public readonly float Quality;
        public readonly double SegmentClosedAt;

        public RecognitionEvent(RecognitionEventKind kind, SymbolicClass cls, int sign, GestureFeature feature,
            float n1, float n2, double closedAt, GestureFeatureV2 featureV2 = null, float quality = 0f)
        {
            Kind = kind;
            Class = cls;
            DirectionSign = sign;
            Feature = feature;
            FeatureV2 = featureV2;
            N1 = n1;
            N2 = n2;
            Quality = quality;
            SegmentClosedAt = closedAt;
        }
    }

    /// <summary>
    /// Selects teaching/live/direction mode from story context and publishes one IntentRecognition
    /// event stream. Space-bounded teaching only in the two authorized teaching states.
    /// </summary>
    public sealed class RecognitionCoordinator
    {
        private readonly BeforeRecognitionConfig cfg;
        private readonly BeforeRuntimeConfig runtimeCfg;
        // Legacy palm-only classifier: retained but isolated. Never called by the production teach/live path
        // once V2 is wired (D-091). Kept only so old EditMode tests and diagnostics still reference it.
        public readonly GestureDictionary Dictionary;
        public readonly SymbolicGestureClassifier Classifier;
        public readonly GestureRecognizerV2 V2;
        // D-092: production live/teaching now runs on the Pose-conditioned Motion recognizer. The V2 single
        // -DTW live classification is SUPERSEDED (kept only as the feature/normalization base + old tests).
        public readonly PoseConditionedMotionRecognizer Pcm;
        // D-095 static Stop hand-sign; D-094 index-finger Direction pointing.
        public readonly StaticSignRecognizer StopSign = new StaticSignRecognizer();
        public readonly DirectionPointRecognizer Pointer = new DirectionPointRecognizer();
        public readonly MotionSegmenter Segmenter;

        public bool StopActive { get; private set; }
        private bool prevStopActive;
        public PointDir PointerPreview => Pointer.Preview;
        /// <summary>Who currently owns input, for the F1 OWNER readout.</summary>
        public string InputOwner { get; private set; } = "-";

        public event Action<RecognitionEvent> OnEvent;
        public event Action OnCaptureOpened;
        public event Action<IReadOnlyList<PalmSample>> OnTracePoint; // full current capture for trail rendering
        // Fired for every live classification (production and F2 QA); the QA tally reads this. The expected
        // label is never passed to the classifier.
        public event Action<LiveV2Result, GestureFeatureV2, float> OnLiveClassified;
        // D-092: every live PCM frame result (production and F2 QA). Fire/PoseArmed/MotionTracking/Rejected
        // states drive F1 diagnostics, half-ring visualization, and the A/B/C/D QA tally.
        public event Action<PcmResult> OnPcmLive;
        public PcmResult LastPcm { get; private set; }

        // adaptive-3+1 teaching progress for the public "動作を記録 N/M" readout
        public int TeachCollected { get; private set; }
        public int TeachTarget { get; private set; }
        // when true (F2 QA), classification still runs but never enqueues intents or adapts the model
        public bool ClassifyOnlyQa { get; set; }

        private readonly List<PalmSample> teachingCapture = new List<PalmSample>(128);
        private bool spaceHeld;
        private bool teachingCaptureOpen;
        private double teachingCaptureStart;
        private int qualityFrames;
        private bool directionArmedStill;
        private double directionStillSince = -1;
        private Vector2 lastPalm;
        private double lastPalmTime = -1;
        private CommandIntakeMode mode = CommandIntakeMode.None;
        private bool directionConsumedThisFork;

        public CommandIntakeMode Mode => mode;
        public bool TeachingCaptureOpen => teachingCaptureOpen;
        public IReadOnlyList<PalmSample> CurrentTeachingCapture => teachingCapture;
        public double LastSegmentClosedAt { get; private set; }
        public float LastLiveSpeed => Segmenter.LastSpeed;

        /// <summary>D-092: during a teaching capture, true once the leading still-pose stage is complete
        /// (>=5 low-speed frames) so the public text can switch from "hold the shape" to "now move".</summary>
        public bool TeachPoseStageComplete
        {
            get
            {
                if (!teachingCaptureOpen || teachingCapture.Count < 5) return false;
                float diag = Pcm.Cfg.Diagonal, thr = Pcm.Cfg.MotionStartSpeed;
                int stable = 1;
                for (int i = 1; i < teachingCapture.Count; i++)
                {
                    float dt = Mathf.Max(1e-4f, (float)(teachingCapture[i].Time - teachingCapture[i - 1].Time));
                    float sp = Vector2.Distance(teachingCapture[i].Position, teachingCapture[i - 1].Position) / dt / diag;
                    if (sp < thr) stable++; else break;
                }
                return stable >= 5;
            }
        }

        public RecognitionCoordinator(BeforeRecognitionConfig config, BeforeRuntimeConfig runtimeConfig)
        {
            cfg = config;
            runtimeCfg = runtimeConfig;
            Dictionary = new GestureDictionary(config);
            Classifier = new SymbolicGestureClassifier(Dictionary, config);
            V2 = new GestureRecognizerV2(config);
            Pcm = new PoseConditionedMotionRecognizer();
            Segmenter = new MotionSegmenter(config);
            TeachTarget = config.v2MinTeachSamples;
        }

        public void SetMode(CommandIntakeMode newMode)
        {
            if (mode == newMode) return;
            mode = newMode;
            // Segmenter now only detects horizontal direction swipes inside the final plan; symbolic live
            // recognition is per-frame PCM. Reset the live PCM machine whenever the intake context changes.
            Segmenter.SetEnabled(false);
            // reset every live recognizer on a context change; Direction owns input exclusively (Jump/Stop
            // recognizers are held at Neutral so zero Jump/Stop events occur during the direction beat).
            Pcm.ResetLive();
            StopSign.ResetLive();
            Pointer.Reset();
            StopActive = false; prevStopActive = false;
            switch (newMode)
            {
                case CommandIntakeMode.TeachJump: InputOwner = "TEACH-JUMP"; break;
                case CommandIntakeMode.TeachStop: InputOwner = "TEACH-STOP"; break;
                case CommandIntakeMode.Direction: InputOwner = "POINT"; break;
                case CommandIntakeMode.LiveSymbolic: InputOwner = "JUMP+STOP"; break;
                case CommandIntakeMode.FinalPlan: InputOwner = "JUMP+STOP+POINT"; break;
                default: InputOwner = "-"; break;
            }
            if (newMode == CommandIntakeMode.Direction || newMode == CommandIntakeMode.FinalPlan)
            {
                directionConsumedThisFork = false;
                directionStillSince = -1;
                directionArmedStill = false;
            }
            CancelTeachingCapture();
        }

        /// <summary>Re-enable direction detection inside the final plan after slot-1 replacement semantics allow a new one.</summary>
        public void RearmDirection()
        {
            directionConsumedThisFork = false;
            Pointer.Reset();
        }

        public void SetSpaceHeld(bool held, double now, bool trackingValidForCapture)
        {
            bool wasHeld = spaceHeld;
            spaceHeld = held;
            bool teaching = mode == CommandIntakeMode.TeachJump || mode == CommandIntakeMode.TeachStop;
            if (!teaching) { CancelTeachingCapture(); return; }

            if (held && !wasHeld && trackingValidForCapture)
            {
                teachingCaptureOpen = true;
                teachingCaptureStart = now;
                teachingCapture.Clear();
                OnCaptureOpened?.Invoke();
            }
            else if (!held && wasHeld && teachingCaptureOpen)
            {
                CloseTeachingCapture(now);
            }
        }

        /// <summary>Feed one filtered sample (already validated tracked). qualityOkForCapture reflects the 0.65 x3 rule.</summary>
        public void Feed(PalmSample sample, bool qualityOkForCapture)
        {
            if (qualityOkForCapture) qualityFrames++; else qualityFrames = 0;

            switch (mode)
            {
                case CommandIntakeMode.TeachJump:
                case CommandIntakeMode.TeachStop:
                    if (teachingCaptureOpen)
                    {
                        if (!qualityOkForCapture)
                        {
                            CancelTeachingCapture();
                            return;
                        }
                        teachingCapture.Add(sample);
                        OnTracePoint?.Invoke(teachingCapture);
                        if (sample.Time - teachingCaptureStart >= cfg.maxSegmentSeconds)
                        {
                            CloseTeachingCapture(sample.Time);
                        }
                    }
                    break;

                case CommandIntakeMode.LiveSymbolic:
                    PcmLive(sample);      // Jump (compound pose+motion)
                    StopStep(sample);     // Stop (static hand-sign) -> StopActive
                    break;

                case CommandIntakeMode.Direction:
                    HandlePointing(sample); // D-094: pointing owns input exclusively here
                    break;

                case CommandIntakeMode.FinalPlan:
                    // final plan composes: Jump orbs (PCM), one Stop orb on hold->release, and a pointing direction
                    if (!directionConsumedThisFork) HandlePointing(sample);
                    PcmLive(sample);
                    StopStep(sample, finalPlan: true);
                    break;
            }
        }

        public void NotifyTrackingInvalid()
        {
            CancelTeachingCapture();
            Segmenter.CancelActive();
            Pcm.ResetLive();
            directionStillSince = -1;
            directionArmedStill = false;
        }

        // D-092 production live recognition: one PCM frame per sample. Pose and Motion are judged separately
        // inside the recognizer; only a taught pose held through a taught stroke fires. On a fire we emit the
        // SAME LiveAccepted contract the planner already consumes.
        private void PcmLive(PalmSample sample)
        {
            bool valid = sample.Landmarks != null;
            var frame = new PcmFrame(sample.Time, sample.Landmarks, sample.Position, sample.Quality, valid);
            PcmResult r = Pcm.Step(frame);
            LastPcm = r;
            OnPcmLive?.Invoke(r);

            if (ClassifyOnlyQa) return; // F2 QA tallies from OnPcmLive; never enqueues/adapts
            if (r.Fired)
            {
                LastSegmentClosedAt = sample.Time;
                OnEvent?.Invoke(new RecognitionEvent(RecognitionEventKind.LiveAccepted, r.FiredClass, 0, null,
                    r.PoseNorm, r.PoseSecondNorm, sample.Time, null, sample.Quality));
            }
        }

        // D-095 static Stop: advance the sign recognizer. StopActive drives the director's gate waits. In the
        // final map only, the hold->release edge registers exactly one Stop orb.
        private void StopStep(PalmSample sample, bool finalPlan = false)
        {
            bool valid = sample.Landmarks != null;
            var frame = new PcmFrame(sample.Time, sample.Landmarks, sample.Position, sample.Quality, valid);
            StopActive = StopSign.Step(frame);
            if (!ClassifyOnlyQa && finalPlan && prevStopActive && !StopActive)
            {
                // hold -> release: one Stop orb (execution waits autonomously until the gate is safe)
                LastSegmentClosedAt = sample.Time;
                OnEvent?.Invoke(new RecognitionEvent(RecognitionEventKind.LiveAccepted, SymbolicClass.Stop, 0, null, 0, 0, sample.Time, null, sample.Quality));
            }
            prevStopActive = StopActive;
        }

        // D-094 Direction pointing: DirectionPoint owns input; a 0.45 s hold commits a route.
        private void HandlePointing(PalmSample sample)
        {
            bool valid = sample.Landmarks != null;
            var frame = new PcmFrame(sample.Time, sample.Landmarks, sample.Position, sample.Quality, valid);
            bool committed = Pointer.Step(frame);
            if (!ClassifyOnlyQa && committed && Pointer.CommittedDir != PointDir.None)
            {
                LastSegmentClosedAt = sample.Time;
                OnEvent?.Invoke(new RecognitionEvent(RecognitionEventKind.DirectionDetected, SymbolicClass.Jump, (int)Pointer.CommittedDir, null, 0, 0, sample.Time));
            }
        }

        private void HandleLive(PalmSample sample)
        {
            SegmentEvent evt = Segmenter.Feed(sample);
            if (evt != SegmentEvent.SegmentClosed) return;
            var segment = Segmenter.LastClosedSegment;
            LastSegmentClosedAt = sample.Time;

            // in final plan, a horizontal swipe segment is direction, not symbolic
            if (mode == CommandIntakeMode.FinalPlan)
            {
                int sign = DirectionSwipeDetector.Detect(segment, cfg);
                if (sign != 0)
                {
                    OnEvent?.Invoke(new RecognitionEvent(RecognitionEventKind.DirectionDetected, SymbolicClass.Jump, sign, null, 0, 0, sample.Time));
                    return;
                }
            }

            // D-091: classify with the multichannel V2 recognizer over boundary-shifted candidates.
            GestureFeature legacyTrace = TrajectoryNormalizer.Build(segment, cfg); // trace rendering only
            ClassifyBestV2(segment, out LiveV2Result result, out GestureFeatureV2 featV2);
            if (featV2 == null) return;
            float quality = SegmentQuality(segment);
            OnLiveClassified?.Invoke(result, featV2, quality);

            // In F2 QA we classify but never enqueue/adapt in the story.
            if (ClassifyOnlyQa) return;

            switch (result.State)
            {
                case LiveV2State.Accepted:
                    OnEvent?.Invoke(new RecognitionEvent(RecognitionEventKind.LiveAccepted, result.Chosen, 0, legacyTrace, result.N1, result.N2, sample.Time, featV2, quality));
                    break;
                case LiveV2State.Ambiguous:
                    OnEvent?.Invoke(new RecognitionEvent(RecognitionEventKind.LiveAmbiguous, result.Chosen, 0, legacyTrace, result.N1, result.N2, sample.Time, featV2, quality));
                    break;
                case LiveV2State.NoMatch:
                    OnEvent?.Invoke(new RecognitionEvent(RecognitionEventKind.LiveNoMatch, result.Chosen, 0, legacyTrace, result.N1, result.N2, sample.Time, featV2, quality));
                    break;
            }
        }

        // --- V2 feature helpers (D-091) ---------------------------------------------------------------

        private static GestureFeatureV2 BuildV2(IReadOnlyList<PalmSample> seg, BeforeRecognitionConfig cfg, int startShift, int endShift)
        {
            var frames = new List<LandmarkFrame>(seg.Count);
            foreach (var s in seg)
                if (s.Landmarks != null) frames.Add(new LandmarkFrame(s.Time, s.Landmarks, s.Handedness));
            if (frames.Count < 2) return null;
            var trimmed = GestureV2Builder.Trim(frames, cfg, startShift, endShift);
            return GestureV2Builder.Build(trimmed, cfg);
        }

        private void ClassifyBestV2(IReadOnlyList<PalmSample> seg, out LiveV2Result best, out GestureFeatureV2 bestFeat)
        {
            best = default; bestFeat = null;
            float bestN1 = float.MaxValue;
            int sh = cfg.v2BoundaryShiftMax;
            int[] shifts = { 0, -sh, sh };
            foreach (int ss in shifts)
            {
                var f = BuildV2(seg, cfg, ss, 0);
                if (f == null) continue;
                var r = V2.Classify(f);
                if (bestFeat == null || r.N1 < bestN1) { bestN1 = r.N1; best = r; bestFeat = f; }
            }
        }

        private static float SegmentQuality(IReadOnlyList<PalmSample> seg)
        {
            float min = 1f;
            foreach (var s in seg) if (s.Quality > 0f) min = Mathf.Min(min, s.Quality);
            return min;
        }

        private void HandleDirection(PalmSample sample)
        {
            if (directionConsumedThisFork) return;

            float speed = 0f;
            if (lastPalmTime >= 0)
            {
                float dt = Mathf.Max(1e-4f, (float)(sample.Time - lastPalmTime));
                speed = Vector2.Distance(sample.Position, lastPalm) / dt;
            }
            lastPalm = sample.Position;
            lastPalmTime = sample.Time;

            if (mode == CommandIntakeMode.Direction)
            {
                // direction mode uses its own simple armed capture: still -> swipe -> evaluate
                if (!directionArmedStill)
                {
                    if (speed < cfg.armSpeed)
                    {
                        if (directionStillSince < 0) directionStillSince = sample.Time;
                        if (sample.Time - directionStillSince >= cfg.armSeconds)
                        {
                            directionArmedStill = true;
                            directionCapture.Clear();
                        }
                    }
                    else directionStillSince = -1;
                    return;
                }

                if (speed > cfg.startSpeed || directionCapture.Count > 0)
                {
                    directionCapture.Add(sample);
                    float elapsed = directionCapture.Count > 1
                        ? (float)(sample.Time - directionCapture[0].Time) : 0f;
                    bool ended = (speed < cfg.armSpeed && elapsed >= cfg.minSegmentSeconds) || elapsed >= cfg.maxSegmentSeconds;
                    if (ended)
                    {
                        int sign = DirectionSwipeDetector.Detect(directionCapture, cfg);
                        directionArmedStill = false;
                        directionStillSince = -1;
                        directionCapture.Clear();
                        if (sign != 0)
                        {
                            directionConsumedThisFork = true;
                            LastSegmentClosedAt = sample.Time;
                            OnEvent?.Invoke(new RecognitionEvent(RecognitionEventKind.DirectionDetected, SymbolicClass.Jump, sign, null, 0, 0, sample.Time));
                        }
                    }
                }
            }
        }

        private readonly List<PalmSample> directionCapture = new List<PalmSample>(128);

        private void CloseTeachingCapture(double now)
        {
            teachingCaptureOpen = false;
            float duration = teachingCapture.Count > 1
                ? (float)(teachingCapture[teachingCapture.Count - 1].Time - teachingCapture[0].Time)
                : 0f;
            if (duration < cfg.minSegmentSeconds)
            {
                OnEvent?.Invoke(new RecognitionEvent(RecognitionEventKind.TeachingSampleTooShort, TargetClass, 0, null, 0, 0, now));
                return;
            }

            LastSegmentClosedAt = now;

            // D-092 compound teaching: one Space-hold split internally into a stable-pose stage then a single
            // -stroke motion stage. Pose and Motion clusters are pruned separately (adaptive 3+1). A divergent
            // sample is never label-corrected. Progress drives the "合図を記録 N/M" readout.
            GestureFeature legacyTrace = TrajectoryNormalizer.Build(teachingCapture, cfg); // trace only
            var frames = new List<PcmFrame>(teachingCapture.Count);
            foreach (var s in teachingCapture)
                frames.Add(new PcmFrame(s.Time, s.Landmarks, s.Position, s.Quality, s.Landmarks != null));

            TeachTarget = 3;

            if (TargetClass == SymbolicClass.Stop)
            {
                // D-095: Stop is a STATIC hand-sign. Add one held-shape sample; reject a shape too close to the
                // taught Jump start-pose. No motion is required or evaluated.
                PoseDescriptor jumpPose = Pcm.Pose(SymbolicClass.Jump).Learned ? Pcm.Pose(SymbolicClass.Jump).Prototype : null;
                var sres = StopSign.SubmitHold(frames, jumpPose);
                TeachCollected = StopSign.Model.Samples.Count;
                switch (sres)
                {
                    case StaticSignRecognizer.TeachResult.Confirmed:
                        OnEvent?.Invoke(new RecognitionEvent(RecognitionEventKind.TeachingClassClosed, SymbolicClass.Stop, 0, legacyTrace, 0, 0, now));
                        break;
                    case StaticSignRecognizer.TeachResult.AcceptedNeedMore:
                        OnEvent?.Invoke(new RecognitionEvent(RecognitionEventKind.TeachingSampleAccepted, SymbolicClass.Stop, 0, legacyTrace, 0, 0, now));
                        break;
                    case StaticSignRecognizer.TeachResult.TooSimilarToJump:
                        OnEvent?.Invoke(new RecognitionEvent(RecognitionEventKind.TeachingClassOverlap, SymbolicClass.Stop, 0, null, 0, 0, now));
                        break;
                    default:
                        OnEvent?.Invoke(new RecognitionEvent(RecognitionEventKind.TeachingSampleInconsistent, SymbolicClass.Stop, 0, null, 0, 0, now));
                        break;
                }
                return;
            }

            var outc = Pcm.SubmitTeaching(TargetClass, frames);
            TeachCollected = Pcm.Pose(TargetClass).Samples.Count;
            if (!outc.Accepted)
            {
                OnEvent?.Invoke(new RecognitionEvent(RecognitionEventKind.TeachingSampleInconsistent, TargetClass, 0, null, 0, 0, now));
                return;
            }
            if (outc.Confirmed)
                OnEvent?.Invoke(new RecognitionEvent(RecognitionEventKind.TeachingClassClosed, TargetClass, 0, legacyTrace, 0, 0, now));
            else
                OnEvent?.Invoke(new RecognitionEvent(RecognitionEventKind.TeachingSampleAccepted, TargetClass, 0, legacyTrace, 0, 0, now));
        }

        private void CancelTeachingCapture()
        {
            teachingCaptureOpen = false;
            teachingCapture.Clear();
        }

        private SymbolicClass TargetClass =>
            mode == CommandIntakeMode.TeachStop ? SymbolicClass.Stop : SymbolicClass.Jump;

        public int QualityFrames => qualityFrames;
        public bool QualityGateOpen => qualityFrames >= runtimeCfg.captureQualityFrames;

        public void ResetAll()
        {
            Dictionary.ResetAll();
            V2.ResetAll();
            Pcm.ResetAll();
            StopSign.ResetAll();
            Pointer.Reset();
            StopActive = false; prevStopActive = false;
            TeachCollected = 0;
            TeachTarget = cfg.v2MinTeachSamples;
            ClassifyOnlyQa = false;
            CancelTeachingCapture();
            Segmenter.SetEnabled(false);
            mode = CommandIntakeMode.None;
            directionConsumedThisFork = false;
            directionStillSince = -1;
            directionArmedStill = false;
            directionCapture.Clear();
            lastPalmTime = -1;
            qualityFrames = 0;
        }
    }
}
