using System;
using Before.App;
using Before.Capture;
using Before.Config;
using Before.Course;
using Before.Recognition;
using NUnit.Framework;
using UnityEngine;

namespace Before.Tests
{
    /// <summary>
    /// Headless full-story demonstration on the fake hand stream (D-094/D-095/D-096 reordered course):
    /// Jump teach -> Jump proof A -> Stop teach (static sign) -> Stop proof A -> Jump proof B -> Stop proof B
    /// -> Direction teach (index-finger pointing) -> final fork/plan/core. Jump is a compound pose+motion word;
    /// Stop is a held static hand-sign; Direction is a 0.45 s point. All beats run through the real recognizers.
    /// </summary>
    public sealed class StorySim
    {
        public readonly BeforeDirector D;
        public readonly FakeHandSource Hand;
        private double now = 10.0;
        private bool space;

        public const double Dt = 1.0 / 60.0;
        public double Now => now;

        public const int JumpShape = 0;
        public const int StopShape = 1;
        public const int RelaxShape = 2;
        public const int PointLeftShape = 3;
        public const int PointRightShape = 4;

        public static readonly Vector2 Center = new Vector2(0.5f, 0.5f);
        public static readonly Vector2 UpFrom = new Vector2(0.5f, 0.3f);
        public static readonly Vector2 UpTo = new Vector2(0.5f, 0.8f);

        public StorySim()
        {
            Hand = new FakeHandSource { CameraAvailable = true, Tracked = false };
            D = new BeforeDirector(TestConfigs.Runtime(), TestConfigs.Recognition(), TestConfigs.Course(), Hand);
        }

        public void Step(bool resetHeld = false, bool pauseToggle = false, bool debugToggle = false)
        {
            now += Dt;
            D.Tick(now, space, pauseToggle, resetHeld, debugToggle, false, false, false);
        }

        public void Run(double seconds, bool resetHeld = false)
        {
            int n = (int)Math.Ceiling(seconds / Dt);
            for (int i = 0; i < n; i++) Step(resetHeld);
        }

        public bool RunUntil(Func<bool> condition, double timeoutSeconds)
        {
            int n = (int)Math.Ceiling(timeoutSeconds / Dt);
            for (int i = 0; i < n; i++)
            {
                if (condition()) return true;
                Step();
            }
            return condition();
        }

        public void HoldStill(Vector2 pos, double seconds)
        {
            Hand.Tracked = true;
            Hand.PalmMirrored = pos;
            Run(seconds);
        }

        public void Stroke(Vector2 from, Vector2 to, double duration)
        {
            Hand.Tracked = true;
            int n = (int)Math.Ceiling(duration / Dt);
            for (int i = 0; i <= n; i++) { Hand.PalmMirrored = Vector2.Lerp(from, to, i / (float)n); Step(); }
        }

        // --- Jump: compound pose (open) + upward stroke, taught under Space ---
        public void TeachJumpStroke(Vector2 from, Vector2 to, double duration)
        {
            Hand.PoseShape = JumpShape;
            HoldStill(from, 0.5);
            space = true;
            HoldStill(from, 0.4);
            Stroke(from, to, duration);
            space = false; Step();
        }

        public void JumpGestureLive()
        {
            Hand.PoseShape = JumpShape;
            HoldStill(UpFrom, 0.6);
            Stroke(UpFrom, UpTo, 0.9);
            HoldStill(UpTo, 0.3);
            Hand.PoseShape = RelaxShape; HoldStill(UpTo, 0.5);
        }

        // --- Stop: STATIC held sign (curled fist), taught by holding under Space >=0.8 s ---
        public void TeachStopHold()
        {
            Hand.PoseShape = StopShape;
            HoldStill(Center, 0.5);
            space = true;
            HoldStill(Center, 1.0);   // hold the shape >=0.8 s
            space = false; Step();
        }

        /// <summary>Hold the Stop sign to make NUL wait at a gate, then relax (NUL waits autonomously to safety).</summary>
        public void StopHold()
        {
            Hand.PoseShape = StopShape;
            HoldStill(Center, 0.9);       // activate (0.45 s) and let NUL brake at the gate
            Hand.PoseShape = RelaxShape;  // release; NUL keeps waiting until the gate is safe
            HoldStill(Center, 0.2);
        }

        // --- Direction: point the index finger and hold 0.45 s to commit ---
        public void PointCommit(int sign)
        {
            Hand.PoseShape = sign < 0 ? PointLeftShape : PointRightShape;
            HoldStill(Center, 0.9);       // hold > 0.45 s to commit
            Hand.PoseShape = RelaxShape; HoldStill(Center, 0.2);
        }

        public void PlayThroughDirectionTeach(int directionTeachSign)
        {
            Step();
            Assert.AreEqual(BeforeStoryState.AwaitHand, D.Story.State);
            HoldStill(Center, 0.7);
            Assert.IsTrue(RunUntil(() => D.Story.State == BeforeStoryState.JumpTeachSample1, 15.0), "reach jump teach");

            // Jump teach: three compound samples (open pose + up stroke)
            TeachJumpStroke(UpFrom, UpTo, 0.9);
            Assert.AreEqual(BeforeStoryState.JumpTeachSample2, D.Story.State);
            TeachJumpStroke(new Vector2(0.52f, 0.3f), new Vector2(0.48f, 0.8f), 0.9);
            TeachJumpStroke(new Vector2(0.49f, 0.31f), new Vector2(0.51f, 0.79f), 0.9);
            Assert.IsTrue(D.Recognition.Pcm.IsConfirmed(SymbolicClass.Jump), "Jump confirms");

            // Jump proof A
            Assert.IsTrue(RunUntil(() => D.Story.State == BeforeStoryState.JumpProofWait, 20.0), "reach jump proof A");
            Assert.IsTrue(RunUntil(() => D.Motor.State == MotorState.Stopped, 12.0), "self-preservation holds before proof-A gap");
            JumpGestureLive();
            Assert.IsTrue(RunUntil(() => D.Story.State == BeforeStoryState.StopTeachSample1, 20.0), "jump proof A -> stop teach");

            // Stop teach: three static holds (curled fist)
            TeachStopHold();
            Assert.AreEqual(BeforeStoryState.StopTeachSample2, D.Story.State);
            TeachStopHold();
            TeachStopHold();
            Assert.IsTrue(D.Recognition.StopSign.IsConfirmed, "Stop static sign confirms");

            // Stop teach demo passes the teach gate autonomously, then Stop proof A (player holds the sign)
            Assert.IsTrue(RunUntil(() => D.Story.State == BeforeStoryState.StopProofAWait, 25.0), "reach stop proof A");
            StopHold();
            Assert.IsTrue(RunUntil(() => D.Story.State == BeforeStoryState.JumpProofBWait, 25.0), "stop proof A -> jump proof B");

            // Jump proof B
            Assert.IsTrue(RunUntil(() => D.Motor.State == MotorState.Stopped, 12.0), "hold before proof-B gap");
            JumpGestureLive();
            Assert.IsTrue(RunUntil(() => D.Story.State == BeforeStoryState.StopProofBWait, 25.0), "jump proof B -> stop proof B");

            // Stop proof B
            StopHold();
            Assert.IsTrue(RunUntil(() => D.Story.State == BeforeStoryState.DirectionTeachMap, 25.0), "reach direction teach");

            // Direction teach by pointing (0 Jump/Stop events must occur here — see the dedicated test)
            PointCommit(directionTeachSign);
            Assert.AreEqual(BeforeStoryState.DirectionTeachExecute, D.Story.State, "point commits a direction");
            Assert.AreEqual(directionTeachSign, D.ChosenDirectionSign);

            Assert.IsTrue(RunUntil(() => D.Story.State == BeforeStoryState.FinalPlanMap, 25.0), "reach final plan map");
        }

        public void ComposeAndCommitPlan(int directionSign, bool firstJump, bool secondJump)
        {
            PointCommit(directionSign);
            Assert.AreEqual(directionSign < 0 ? FinalRoute.Left : FinalRoute.Right, D.PlannedRoute, "point sets route preview");
            ComposeSymbol(firstJump);
            ComposeSymbol(secondJump);
            Assert.IsTrue(D.Queue.IsFullPlan, "direction + two symbols fill the plan");
            Assert.IsTrue(RunUntil(() => D.Story.State == BeforeStoryState.FinalCourseExecute, 8.0), "stillness commits the plan");
        }

        private void ComposeSymbol(bool jump)
        {
            if (jump) JumpGestureLive();
            else { Hand.PoseShape = StopShape; HoldStill(Center, 0.9); Hand.PoseShape = RelaxShape; HoldStill(Center, 0.5); }
        }

        public void PlayEndingAndReset()
        {
            Assert.IsTrue(RunUntil(() => D.Story.State == BeforeStoryState.CoreArrival, 30.0), "final course into core approach");
            Assert.IsTrue(RunUntil(() => D.Story.State == BeforeStoryState.CoreTransfer, 15.0), "stop at core");
            Assert.IsTrue(RunUntil(() => D.Story.State == BeforeStoryState.Farewell, 6.0), "transfer");
            Assert.IsTrue(RunUntil(() => D.Story.State == BeforeStoryState.CompleteHold, 9.0), "farewell");
            Run(2.1, resetHeld: true);
            Assert.IsFalse(D.Recognition.Pcm.IsConfirmed(SymbolicClass.Jump), "reset clears Jump");
            Assert.IsFalse(D.Recognition.StopSign.IsConfirmed, "reset clears Stop");
            Assert.AreEqual(0, D.Queue.Count, "reset clears the queue");
            Step();
            Assert.AreEqual(BeforeStoryState.AwaitHand, D.Story.State);
        }
    }

    public sealed class StorySimulationTests
    {
        [Test]
        public void FullStory_LeftRoute_NewOrder()
        {
            var sim = new StorySim();
            sim.PlayThroughDirectionTeach(directionTeachSign: -1);
            sim.ComposeAndCommitPlan(directionSign: -1, firstJump: false, secondJump: true); // left: gate then gap
            Assert.AreEqual(FinalRoute.Left, sim.D.Course.SelectedRoute);
            sim.PlayEndingAndReset();
        }

        [Test]
        public void FullStory_RightRoute_NewOrder()
        {
            var sim = new StorySim();
            sim.PlayThroughDirectionTeach(directionTeachSign: +1);
            sim.ComposeAndCommitPlan(directionSign: +1, firstJump: true, secondJump: false); // right: gap then gate
            Assert.AreEqual(FinalRoute.Right, sim.D.Course.SelectedRoute);
            sim.PlayEndingAndReset();
        }

        [Test]
        public void ZeroJumpStopEvents_DuringDirectionBeat()
        {
            var sim = new StorySim();
            sim.PlayThroughDirectionTeach(directionTeachSign: -1); // internally reaches direction teach via pointing
            // reset a fresh sim and count Jump/Stop LiveAccepted during the direction beat only
            var s2 = new StorySim();
            int jumpStop = 0;
            s2.D.Recognition.OnEvent += e =>
            {
                if (s2.D.Story.State == BeforeStoryState.DirectionTeachMap
                    && e.Kind == RecognitionEventKind.LiveAccepted
                    && (e.Class == SymbolicClass.Jump || e.Class == SymbolicClass.Stop))
                    jumpStop++;
            };
            s2.PlayThroughDirectionTeach(directionTeachSign: +1);
            Assert.AreEqual(0, jumpStop, "no Jump/Stop events may fire during the direction beat");
        }

        [Test]
        public void OperatorPauseAndDebugToggle_AreObservable()
        {
            var sim = new StorySim();
            sim.Step();
            sim.Step(debugToggle: true);
            Assert.IsTrue(sim.D.DebugVisible);
            sim.Step(pauseToggle: true);
            Assert.IsTrue(sim.D.OperatorPaused);
            Assert.AreEqual(PublicTextId.Paused, sim.D.VisibleText);
            double frozen = sim.D.Gameplay.Now;
            sim.Run(0.5);
            Assert.AreEqual(frozen, sim.D.Gameplay.Now, 1e-9);
        }
    }
}
