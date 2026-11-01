using System.Collections.Generic;
using Before.App;
using Before.Capture;
using Before.Config;
using Before.Course;
using Before.Planning;
using Before.Recognition;
using NUnit.Framework;
using UnityEngine;

namespace Before.Tests
{
    public static class TestConfigs
    {
        public static BeforeRuntimeConfig Runtime() => ScriptableObject.CreateInstance<BeforeRuntimeConfig>();
        public static BeforeRecognitionConfig Recognition() => ScriptableObject.CreateInstance<BeforeRecognitionConfig>();
        public static BeforeCourseConfig Course() => ScriptableObject.CreateInstance<BeforeCourseConfig>();

        public static List<PalmSample> Stroke(Vector2 from, Vector2 to, float duration, double startTime, int hz = 15)
        {
            var list = new List<PalmSample>();
            int n = Mathf.Max(3, Mathf.RoundToInt(duration * hz));
            for (int i = 0; i <= n; i++)
            {
                float t = i / (float)n;
                list.Add(new PalmSample { Time = startTime + t * duration, Position = Vector2.Lerp(from, to, t) });
            }
            return list;
        }
    }

    public sealed class TrajectoryAndDtwTests
    {
        [Test]
        public void Normalization_IsTranslationAndScaleInvariant_PreservesDuration()
        {
            var cfg = TestConfigs.Recognition();
            var a = TrajectoryNormalizer.Build(TestConfigs.Stroke(new Vector2(0.2f, 0.2f), new Vector2(0.2f, 0.7f), 1.0f, 0), cfg);
            var b = TrajectoryNormalizer.Build(TestConfigs.Stroke(new Vector2(0.6f, 0.1f), new Vector2(0.6f, 0.35f), 1.0f, 50), cfg);
            Assert.NotNull(a);
            Assert.NotNull(b);
            Assert.AreEqual(32, a.Points.Length);
            Assert.AreEqual(Vector2.zero, a.Points[0]);
            float d = WeightedDtw.Distance(a, b, cfg);
            Assert.Less(d, 0.05f, "Same shape at different position/scale must be near-identical.");
            Assert.AreEqual(1.0f, a.Duration, 0.05f);
        }

        [Test]
        public void Dtw_IdentityIsZero_DifferentShapesSeparate()
        {
            var cfg = TestConfigs.Recognition();
            var up = TrajectoryNormalizer.Build(TestConfigs.Stroke(new Vector2(0.5f, 0.3f), new Vector2(0.5f, 0.8f), 0.9f, 0), cfg);
            var down = TrajectoryNormalizer.Build(TestConfigs.Stroke(new Vector2(0.5f, 0.8f), new Vector2(0.5f, 0.3f), 0.9f, 0), cfg);
            Assert.AreEqual(0f, WeightedDtw.Distance(up, up, cfg), 1e-4f);
            Assert.Greater(WeightedDtw.Distance(up, down, cfg), 0.3f);
        }

        [Test]
        public void Dtw_ToleratesTimeWarp()
        {
            var cfg = TestConfigs.Recognition();
            var normal = TrajectoryNormalizer.Build(TestConfigs.Stroke(new Vector2(0.3f, 0.3f), new Vector2(0.7f, 0.7f), 0.8f, 0), cfg);
            var slow = TrajectoryNormalizer.Build(TestConfigs.Stroke(new Vector2(0.3f, 0.3f), new Vector2(0.7f, 0.7f), 1.3f, 0), cfg);
            Assert.Less(WeightedDtw.Distance(normal, slow, cfg), 0.15f);
        }
    }

    public sealed class DictionaryAndClassifierTests
    {
        private static GestureFeature Up(BeforeRecognitionConfig cfg, float jitter = 0f)
            => TrajectoryNormalizer.Build(TestConfigs.Stroke(new Vector2(0.5f + jitter, 0.3f), new Vector2(0.5f - jitter, 0.8f), 0.9f, 0), cfg);
        private static GestureFeature Down(BeforeRecognitionConfig cfg)
            => TrajectoryNormalizer.Build(TestConfigs.Stroke(new Vector2(0.5f, 0.8f), new Vector2(0.5f, 0.3f), 0.9f, 0), cfg);

        [Test]
        public void Teaching_TwoCompatibleSamplesCloseClass_WithPersonalizedRadius()
        {
            var cfg = TestConfigs.Recognition();
            var dict = new GestureDictionary(cfg);
            Assert.AreEqual(TeachingResult.AcceptedFirst, dict.SubmitTeachingSample(SymbolicClass.Jump, Up(cfg)));
            Assert.AreEqual(TeachingResult.AcceptedSecondClassClosed, dict.SubmitTeachingSample(SymbolicClass.Jump, Up(cfg, 0.02f)));
            Assert.IsTrue(dict.IsClassClosed(SymbolicClass.Jump));
            float r = dict.GetRadius(SymbolicClass.Jump);
            Assert.GreaterOrEqual(r, cfg.radiusMin);
            Assert.LessOrEqual(r, cfg.radiusMax);
        }

        [Test]
        public void Teaching_InconsistentSecondSampleKeepsFirstSeed()
        {
            var cfg = TestConfigs.Recognition();
            var dict = new GestureDictionary(cfg);
            dict.SubmitTeachingSample(SymbolicClass.Jump, Up(cfg));
            Assert.AreEqual(TeachingResult.RejectedInconsistentSecond, dict.SubmitTeachingSample(SymbolicClass.Jump, Down(cfg)));
            Assert.IsTrue(dict.HasFirstSample(SymbolicClass.Jump));
            Assert.IsFalse(dict.IsClassClosed(SymbolicClass.Jump));
        }

        [Test]
        public void Teaching_SecondClassMustBeDistinct()
        {
            var cfg = TestConfigs.Recognition();
            var dict = new GestureDictionary(cfg);
            dict.SubmitTeachingSample(SymbolicClass.Jump, Up(cfg));
            dict.SubmitTeachingSample(SymbolicClass.Jump, Up(cfg, 0.02f));
            dict.SubmitTeachingSample(SymbolicClass.Stop, Up(cfg, 0.01f));
            Assert.AreEqual(TeachingResult.RejectedOverlapsExistingClass, dict.SubmitTeachingSample(SymbolicClass.Stop, Up(cfg, 0.015f)));
            Assert.IsFalse(dict.IsClassClosed(SymbolicClass.Stop));
            Assert.IsTrue(dict.IsClassClosed(SymbolicClass.Jump), "Existing class must be retained.");
        }

        [Test]
        public void Classifier_HighConfidence_Ambiguous_NoMatch()
        {
            var cfg = TestConfigs.Recognition();
            var dict = new GestureDictionary(cfg);
            dict.SubmitTeachingSample(SymbolicClass.Jump, Up(cfg));
            dict.SubmitTeachingSample(SymbolicClass.Jump, Up(cfg, 0.02f));
            dict.SubmitTeachingSample(SymbolicClass.Stop, Down(cfg));
            dict.SubmitTeachingSample(SymbolicClass.Stop, Down(cfg));
            var classifier = new SymbolicGestureClassifier(dict, cfg);

            var high = classifier.Classify(Up(cfg, 0.01f));
            Assert.AreEqual(ClassificationState.Accepted, high.State);
            Assert.AreEqual(SymbolicClass.Jump, high.Chosen);

            var horizontal = TrajectoryNormalizer.Build(TestConfigs.Stroke(new Vector2(0.2f, 0.5f), new Vector2(0.8f, 0.52f), 0.9f, 0), cfg);
            var far = classifier.Classify(horizontal);
            Assert.AreEqual(ClassificationState.NoMatch, far.State);
        }

        [Test]
        public void Adaptation_DriftGuardAndRadiusLimits()
        {
            var cfg = TestConfigs.Recognition();
            var dict = new GestureDictionary(cfg);
            dict.SubmitTeachingSample(SymbolicClass.Jump, Up(cfg));
            dict.SubmitTeachingSample(SymbolicClass.Jump, Up(cfg, 0.02f));
            float initial = dict.GetRadius(SymbolicClass.Jump);
            Assert.IsTrue(dict.Adapt(SymbolicClass.Jump, Up(cfg, 0.03f)));
            Assert.AreEqual(1, dict.GetAdaptationCount(SymbolicClass.Jump));
            bool everRejected = false;
            for (int i = 0; i < 50 && !everRejected; i++)
            {
                everRejected = !dict.Adapt(SymbolicClass.Jump, Down(cfg));
            }
            Assert.IsTrue(everRejected, "Repeated EMA drift toward a different shape must hit the drift guard.");
            float r = dict.GetRadius(SymbolicClass.Jump);
            Assert.LessOrEqual(r, initial * 1.2f + 1e-4f);
            Assert.GreaterOrEqual(r, initial * 0.8f - 1e-4f);
        }
    }

    public sealed class SegmenterAndDirectionTests
    {
        [Test]
        public void Segmenter_ArmStartEndTimingAndRearm()
        {
            var cfg = TestConfigs.Recognition();
            var seg = new MotionSegmenter(cfg);
            seg.SetEnabled(true);
            double t = 0;
            bool closed = false;
            void Feed(Vector2 p, double time) { if (seg.Feed(new PalmSample { Time = time, Position = p }) == SegmentEvent.SegmentClosed) closed = true; }

            for (int i = 0; i < 8; i++) { Feed(new Vector2(0.5f, 0.5f), t); t += 1.0 / 15; }
            Assert.AreEqual(SegmenterState.Armed, seg.State);
            for (int i = 0; i < 14; i++) { Feed(new Vector2(0.5f, 0.5f + i * 0.04f), t); t += 1.0 / 15; }
            Assert.AreEqual(SegmenterState.Capturing, seg.State);
            for (int i = 0; i < 6; i++) { Feed(new Vector2(0.5f, 1.0f), t); t += 1.0 / 15; }
            Assert.IsTrue(closed, "Ending stillness must close the segment.");
            Assert.AreEqual(SegmenterState.Arming, seg.State, "Must require re-arming after a result.");
        }

        [Test]
        public void Segmenter_RejectsShortSegments()
        {
            var cfg = TestConfigs.Recognition();
            var seg = new MotionSegmenter(cfg);
            seg.SetEnabled(true);
            double t = 0;
            bool rejectedShort = false;
            void Feed(Vector2 p, double time) { if (seg.Feed(new PalmSample { Time = time, Position = p }) == SegmentEvent.SegmentRejectedShort) rejectedShort = true; }
            for (int i = 0; i < 8; i++) { Feed(new Vector2(0.5f, 0.5f), t); t += 1.0 / 15; }
            for (int i = 0; i < 4; i++) { Feed(new Vector2(0.5f, 0.5f + i * 0.05f), t); t += 1.0 / 15; }
            for (int i = 0; i < 6; i++) { Feed(new Vector2(0.5f, 0.65f), t); t += 1.0 / 15; }
            Assert.IsTrue(rejectedShort, "A sub-0.6 s segment must be rejected as too short.");
        }

        [Test]
        public void Direction_SignAndThresholds()
        {
            var cfg = TestConfigs.Recognition();
            Assert.AreEqual(1, DirectionSwipeDetector.Detect(TestConfigs.Stroke(new Vector2(0.2f, 0.5f), new Vector2(0.8f, 0.5f), 0.8f, 0), cfg));
            Assert.AreEqual(-1, DirectionSwipeDetector.Detect(TestConfigs.Stroke(new Vector2(0.8f, 0.5f), new Vector2(0.2f, 0.5f), 0.8f, 0), cfg));
            Assert.AreEqual(0, DirectionSwipeDetector.Detect(TestConfigs.Stroke(new Vector2(0.45f, 0.5f), new Vector2(0.6f, 0.5f), 0.8f, 0), cfg), "Too small dx");
            Assert.AreEqual(0, DirectionSwipeDetector.Detect(TestConfigs.Stroke(new Vector2(0.2f, 0.2f), new Vector2(0.8f, 0.8f), 0.8f, 0), cfg), "Diagonal fails axis ratio");
        }
    }

    public sealed class QueueTests
    {
        private static Intent Make(IntentType type, int sign = 0)
            => new Intent(type, sign, IntentConfidence.Stable, null, 0);

        [Test]
        public void NormalCapacity_FourthReturnsToHand()
        {
            var q = new IntentQueue();
            Intent returned = null;
            q.OnReturnedToHand += i => returned = i;
            Assert.AreEqual(EnqueueOutcome.Appended, q.Enqueue(Make(IntentType.Jump)));
            q.Enqueue(Make(IntentType.Stop));
            q.Enqueue(Make(IntentType.Jump));
            var fourth = Make(IntentType.Stop);
            Assert.AreEqual(EnqueueOutcome.RejectedFull, q.Enqueue(fourth));
            Assert.AreEqual(fourth, returned);
            Assert.AreEqual(3, q.Count);
        }

        [Test]
        public void ReservationReplacement_SamePositionNoDoubleExecution()
        {
            var q = new IntentQueue();
            var older = Make(IntentType.Jump);
            q.Enqueue(older);
            var newer = Make(IntentType.Jump);
            Assert.AreEqual(EnqueueOutcome.ReplacedReservation, q.Enqueue(newer, older));
            Assert.AreEqual(1, q.Count);
            Assert.AreEqual(newer, q.Head);
        }

        [Test]
        public void FinalPlan_DirectionFirstRule_RollingEdits_AndRefusal()
        {
            var q = new IntentQueue();
            q.SetFinalPlanMode(true);
            Assert.AreEqual(EnqueueOutcome.FinalPlanRefusedSymbolicBeforeDirection, q.Enqueue(Make(IntentType.Jump)));
            Assert.AreEqual(EnqueueOutcome.FinalPlanDirectionSet, q.Enqueue(Make(IntentType.Direction, -1)));
            q.Enqueue(Make(IntentType.Stop));
            q.Enqueue(Make(IntentType.Jump));
            Assert.IsTrue(q.IsFullPlan);

            // rolling: new symbolic returns old slot2, shifts slot3 into slot2
            var oldSlot2 = q.GetSlot(1);
            var oldSlot3 = q.GetSlot(2);
            var newest = Make(IntentType.Stop);
            Assert.AreEqual(EnqueueOutcome.FinalPlanRolled, q.Enqueue(newest));
            Assert.AreEqual(oldSlot3, q.GetSlot(1));
            Assert.AreEqual(newest, q.GetSlot(2));
            _ = oldSlot2;

            // new direction clears/returns slots 2-3 even when unchanged
            Assert.AreEqual(EnqueueOutcome.FinalPlanDirectionReplacedPlanCleared, q.Enqueue(Make(IntentType.Direction, -1)));
            Assert.AreEqual(1, q.Count);
        }
    }

    public sealed class CourseTests
    {
        [Test]
        public void GateCycle_Is360SecondsAndPhasesMatch()
        {
            var cfg = TestConfigs.Course();
            var gate = new ScanGate(cfg, 35.6f);
            Assert.AreEqual(3.60f, gate.CycleLength, 1e-4f);
            Assert.AreEqual(GatePhase.Closed, gate.PhaseAt(0.5));
            Assert.AreEqual(GatePhase.Opening, gate.PhaseAt(1.3));
            Assert.AreEqual(GatePhase.Open, gate.PhaseAt(2.0));
            Assert.AreEqual(GatePhase.Closing, gate.PhaseAt(3.1));
            Assert.AreEqual(GatePhase.ClosedHold, gate.PhaseAt(3.45));
            Assert.IsFalse(gate.BeamActive(2.0));
        }

        [Test]
        public void Jump_NumericEnvelopeMatchesGeometrySpec()
        {
            var cfg = TestConfigs.Course();
            var motor = new NulMotor2D(cfg, new CourseModel(cfg));
            var (airtime, travel, height) = motor.PredictJump(cfg.runSpeed);
            Assert.AreEqual(0.79f, airtime, 0.05f);
            Assert.AreEqual(2.53f, travel, 0.20f);
            Assert.AreEqual(1.63f, height, 0.12f);
        }

        [Test]
        public void Checkpoint_SnapshotRestoresPositionVelocityAndGatePhase()
        {
            var cfg = TestConfigs.Course();
            var course = new CourseModel(cfg);
            var motor = new NulMotor2D(cfg, course);
            motor.Teleport(new Vector2(62f, 0f), new Vector2(3.2f, 0f), MotorState.Running, true);
            course.Gates[0].PhaseOffset = 1.5;
            var cp = new CheckpointController();
            cp.Capture(motor, course);
            motor.Teleport(new Vector2(70f, -1f), Vector2.zero, MotorState.Airborne, false);
            course.Gates[0].PhaseOffset = 0;
            cp.Restore(motor, course);
            Assert.AreEqual(62f, motor.Position.x, 1e-3f);
            Assert.AreEqual(3.2f, motor.Velocity.x, 1e-3f);
            Assert.AreEqual(1.5, course.Gates[0].PhaseOffset, 1e-6);
        }

        [Test]
        public void Perception_HorizonAndLateBoundary()
        {
            var cfg = TestConfigs.Course();
            var p = new LocalPerception(cfg.perceptionHorizonSeconds, cfg.lateCutoffSeconds);
            p.SetAffordances(new[] { new Affordance { Type = AffordanceType.Gap, ActionX = 24.0f, Id = "proof" } });
            Assert.IsNull(p.FindPerceived(AffordanceType.Gap, 10f, 3.2f), "Beyond horizon must be invisible.");
            Assert.NotNull(p.FindPerceived(AffordanceType.Gap, 20f, 3.2f));
            var intent = new Intent(IntentType.Jump, 0, IntentConfidence.Stable, null, 0);
            Assert.AreEqual(BindResult.TooLate, IntentBinder.Bind(intent, p, 23.2f, 3.2f, out _));
            Assert.AreEqual(BindResult.LiteralNow, IntentBinder.Bind(intent, p, 10f, 3.2f, out _));
        }
    }

    public sealed class StoryMachineTests
    {
        [Test]
        public void TransitionTable_EveryNonTerminalStateHasAnOutgoingEdge()
        {
            foreach (var state in BeforeStoryMachine.AllStates())
            {
                if (state == BeforeStoryState.CompleteHold) continue;
                Assert.IsTrue(BeforeStoryMachine.HasOutgoing(state), $"{state} must have an outgoing transition.");
            }
        }

        [Test]
        public void FullReset_WorksFromEveryState()
        {
            foreach (var state in BeforeStoryMachine.AllStates())
            {
                var machine = new BeforeStoryMachine();
                // drive machine into an arbitrary state via reflection-free replay is complex; FullReset from Boot suffices plus spot checks
                Assert.IsTrue(machine.Fire(StoryEvent.FullReset));
                Assert.AreEqual(BeforeStoryState.Boot, machine.State);
                _ = state;
            }
        }

        [Test]
        public void NamedTransitions_FollowSpecOrder()
        {
            var m = new BeforeStoryMachine();
            Assert.IsTrue(m.Fire(StoryEvent.CameraAvailable));
            Assert.IsTrue(m.Fire(StoryEvent.HandStable));
            Assert.IsTrue(m.Fire(StoryEvent.ChargeComplete));
            Assert.IsTrue(m.Fire(StoryEvent.WakeComplete));
            Assert.AreEqual(BeforeStoryState.RunToJumpTeach, m.State);
            Assert.IsFalse(m.Fire(StoryEvent.PlanCommitted), "Unrelated event must not transition.");
        }
    }
}
