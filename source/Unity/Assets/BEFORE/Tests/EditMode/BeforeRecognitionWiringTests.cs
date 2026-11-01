using System.Collections.Generic;
using Before.App;
using Before.Config;
using Before.Recognition;
using NUnit.Framework;
using UnityEngine;

namespace Before.Tests
{
    /// <summary>
    /// D-092 runtime-wiring tests: 21-landmark compound frames reach the Pose-conditioned Motion recognizer
    /// through the coordinator; the production teach/live path never populates the legacy palm-only classifier
    /// nor the V2 single-DTW model; the teaching readout count matches internal state; a compound gesture
    /// propagates LiveAccepted through the real PoseArmed->MotionTracking->Triggered path; and F2 QA never
    /// feeds the expected label to the recognizer nor changes the model.
    /// </summary>
    public sealed class BeforeRecognitionWiringTests
    {
        private BeforeRecognitionConfig rcfg;
        private BeforeRuntimeConfig runtime;

        [SetUp]
        public void SetUp()
        {
            rcfg = ScriptableObject.CreateInstance<BeforeRecognitionConfig>();
            runtime = ScriptableObject.CreateInstance<BeforeRuntimeConfig>();
        }
        [TearDown]
        public void TearDown() { Object.DestroyImmediate(rcfg); Object.DestroyImmediate(runtime); }

        // 21-point hand at a palm, with a distinct SHAPE per command (open vs curled fingers).
        private static Vector2[] Hand(Vector2 palm, bool open)
        {
            var lm = new Vector2[21];
            lm[0] = palm + new Vector2(0f, -0.10f);
            for (int f = 0; f < 5; f++)
            {
                float bx = (f - 2) * 0.035f;
                Vector2 mcp = palm + new Vector2(bx, 0f);
                lm[1 + f * 4 + 0] = mcp;
                float ext = open ? 1f : 0.25f;
                lm[1 + f * 4 + 1] = mcp + new Vector2(0f, 0.04f * ext);
                lm[1 + f * 4 + 2] = mcp + new Vector2(open ? 0f : 0.02f, 0.075f * ext);
                lm[1 + f * 4 + 3] = mcp + new Vector2(open ? 0f : 0.03f, 0.10f * ext);
            }
            return lm;
        }

        // One compound teaching sample: hold the shape still (pose stage) then stroke (motion stage).
        private static void TeachCompound(RecognitionCoordinator co, bool open, double t0, Vector2 from, Vector2 to)
        {
            co.SetSpaceHeld(true, t0, true);
            double t = t0; double dt = 0.066;
            for (int i = 0; i < 8; i++) { co.Feed(new PalmSample { Time = t, Position = from, Landmarks = Hand(from, open), Quality = 0.95f }, true); t += dt; }
            int n = 12;
            for (int i = 1; i <= n; i++) { co.Feed(new PalmSample { Time = t, Position = Vector2.Lerp(from, to, i / (float)n), Landmarks = Hand(from, open), Quality = 0.95f }, true); t += dt; }
            co.SetSpaceHeld(false, t, true);
        }

        // A live compound: arm the pose (still) then stroke; returns true when a LiveAccepted fired.
        private static void DriveLive(RecognitionCoordinator co, bool open, double t0, Vector2 from, Vector2 to)
        {
            double t = t0; double dt = 0.066;
            for (int i = 0; i < 10; i++) { co.Feed(new PalmSample { Time = t, Position = from, Landmarks = Hand(from, open), Quality = 0.95f }, true); t += dt; }
            int n = 12;
            for (int i = 1; i <= n; i++) { co.Feed(new PalmSample { Time = t, Position = Vector2.Lerp(from, to, i / (float)n), Landmarks = Hand(from, open), Quality = 0.95f }, true); t += dt; }
        }

        private static readonly Vector2 UpFrom = new Vector2(0.5f, 0.3f), UpTo = new Vector2(0.5f, 0.8f);
        private static readonly Vector2 Center = new Vector2(0.5f, 0.5f);

        // D-095: one static Stop hold sample (curled shape held still under Space, no stroke).
        private static void TeachStaticHold(RecognitionCoordinator co, double t0)
        {
            co.SetSpaceHeld(true, t0, true);
            double t = t0;
            for (int i = 0; i < 16; i++) { co.Feed(new PalmSample { Time = t, Position = Center, Landmarks = Hand(Center, open: false), Quality = 0.95f }, true); t += 0.066; }
            co.SetSpaceHeld(false, t, true);
        }

        private RecognitionCoordinator TeachedCoordinator()
        {
            var co = new RecognitionCoordinator(rcfg, runtime);
            co.SetMode(CommandIntakeMode.TeachJump);
            TeachCompound(co, open: true, 1.0, UpFrom, UpTo);
            TeachCompound(co, open: true, 3.0, new Vector2(0.52f, 0.3f), new Vector2(0.48f, 0.8f));
            TeachCompound(co, open: true, 5.0, new Vector2(0.49f, 0.31f), new Vector2(0.51f, 0.79f));
            co.SetMode(CommandIntakeMode.TeachStop);
            TeachStaticHold(co, 7.0);
            TeachStaticHold(co, 9.0);
            TeachStaticHold(co, 11.0);
            return co;
        }

        [Test]
        public void TwentyOneLandmarks_ReachRecognizers_AndConfirmClasses()
        {
            var co = TeachedCoordinator();
            Assert.IsTrue(co.Pcm.IsConfirmed(SymbolicClass.Jump), "Jump must confirm via the compound PCM recognizer");
            Assert.IsTrue(co.StopSign.IsConfirmed, "Stop must confirm via the static-sign recognizer");
        }

        [Test]
        public void ProductionTeaching_DoesNotPopulateLegacyOrV2()
        {
            var co = TeachedCoordinator();
            Assert.IsFalse(co.Dictionary.IsClassClosed(SymbolicClass.Jump), "legacy dictionary must stay empty");
            Assert.IsFalse(co.V2.IsConfirmed(SymbolicClass.Jump), "V2 single-DTW model must stay empty (superseded)");
        }

        [Test]
        public void TeachingReadoutCount_MatchesInternalState()
        {
            var co = new RecognitionCoordinator(rcfg, runtime);
            co.SetMode(CommandIntakeMode.TeachJump);
            TeachCompound(co, open: true, 1.0, UpFrom, UpTo);
            Assert.AreEqual(1, co.TeachCollected, "after one sample the readout must show 1");
            Assert.AreEqual(3, co.TeachTarget);
            TeachCompound(co, open: true, 3.0, new Vector2(0.52f, 0.3f), new Vector2(0.48f, 0.8f));
            Assert.AreEqual(2, co.TeachCollected);
        }

        [Test]
        public void LiveCompound_PropagatesAccepted_ThroughRealPath()
        {
            var co = TeachedCoordinator();
            var kinds = new List<RecognitionEventKind>();
            SymbolicClass accepted = SymbolicClass.None;
            bool sawArmed = false, sawTracking = false;
            co.OnEvent += e => { kinds.Add(e.Kind); if (e.Kind == RecognitionEventKind.LiveAccepted) accepted = e.Class; };
            co.OnPcmLive += r => { if (r.State == PcmState.PoseArmed) sawArmed = true; if (r.State == PcmState.MotionTracking) sawTracking = true; };
            co.SetMode(CommandIntakeMode.LiveSymbolic);

            DriveLive(co, open: true, 20.0, UpFrom, UpTo);
            Assert.IsTrue(sawArmed, "the pose must arm before firing");
            Assert.IsTrue(sawTracking, "the motion must be tracked before firing");
            Assert.IsTrue(kinds.Contains(RecognitionEventKind.LiveAccepted), "a compound gesture must accept");
            Assert.AreEqual(SymbolicClass.Jump, accepted);
        }

        [Test]
        public void F2Qa_ExpectedLabelNeverEntersRecognizer_AndModelUnchanged()
        {
            var co = TeachedCoordinator();
            int jumpBefore = co.Pcm.Pose(SymbolicClass.Jump).Samples.Count;
            int stopBefore = co.Pcm.Pose(SymbolicClass.Stop).Samples.Count;

            co.ClassifyOnlyQa = true;
            co.SetMode(CommandIntakeMode.LiveSymbolic);
            var fired = new List<SymbolicClass>();
            co.OnPcmLive += r => { if (r.Fired) fired.Add(r.FiredClass); };
            // The recognizer takes only frames; classification cannot depend on any expected label. Two
            // identical Jump compounds must classify identically and must not enqueue (QA) — no LiveAccepted.
            bool anyAccepted = false;
            co.OnEvent += e => { if (e.Kind == RecognitionEventKind.LiveAccepted) anyAccepted = true; };
            DriveLive(co, open: true, 30.0, UpFrom, UpTo);
            co.Pcm.ResetLive(); // release the re-fire lock between deterministic attempts
            DriveLive(co, open: true, 40.0, UpFrom, UpTo);

            Assert.GreaterOrEqual(fired.Count, 2, "QA still recognizes (fires internally) for both attempts");
            Assert.AreEqual(fired[0], fired[1], "recognition must be deterministic (no label input)");
            Assert.IsFalse(anyAccepted, "QA must not enqueue intents (no LiveAccepted)");
            Assert.AreEqual(jumpBefore, co.Pcm.Pose(SymbolicClass.Jump).Samples.Count, "QA must not change Jump templates");
            Assert.AreEqual(stopBefore, co.Pcm.Pose(SymbolicClass.Stop).Samples.Count, "QA must not change Stop templates");
        }
    }
}
