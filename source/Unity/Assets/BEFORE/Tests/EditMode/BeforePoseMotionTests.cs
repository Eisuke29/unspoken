using System.Collections.Generic;
using Before.Recognition;
using NUnit.Framework;
using UnityEngine;

namespace Before.Tests
{
    /// <summary>
    /// D-092 pure-core tests: the compound Pose+Motion grammar. A hand-shape alone never fires; a stroke
    /// alone never fires; the taught pose held through the taught stroke fires at the prefix without waiting
    /// for final stillness; wrong pose or wrong motion do not fire; too-short strokes reject as
    /// InsufficientFrames; a single learned class needs no second-class margin; re-fire is locked until the
    /// pose releases. All driven through the real PoseArmed -> MotionTracking -> Triggered path.
    /// </summary>
    public sealed class BeforePoseMotionTests
    {
        // --- synthetic hands: an "open" pose (fingers extended) and a "fist" pose (fingers curled) ---
        // Landmarks are camera-normalized; a distinct pose shape is what separates Jump from Stop here.
        private static Vector2[] Pose(bool open, Vector2 palm)
        {
            var lm = new Vector2[21];
            lm[0] = palm + new Vector2(0f, -0.10f);            // wrist
            // MCP row (fixed palm width via lm[5]/lm[17])
            for (int fld = 0; fld < 5; fld++)
            {
                float bx = (fld - 2) * 0.035f;
                Vector2 mcp = palm + new Vector2(bx, 0f);
                lm[1 + fld * 4 + 0] = mcp;
                float ext = open ? 1f : 0.25f;                 // extended vs curled
                lm[1 + fld * 4 + 1] = mcp + new Vector2(0f, 0.04f * ext);
                lm[1 + fld * 4 + 2] = mcp + new Vector2(open ? 0f : 0.02f, 0.075f * ext);
                lm[1 + fld * 4 + 3] = mcp + new Vector2(open ? 0f : 0.03f, 0.10f * ext);
            }
            return lm;
        }

        private static PcmFrame Frame(double t, bool open, Vector2 palmScreen)
            => new PcmFrame(t, Pose(open, palmScreen), palmScreen, 0.95f);

        // Teach a class: ~0.5s of a still pose, then a directional stroke. `up` = vertical, else horizontal.
        private static void Teach(PoseConditionedMotionRecognizer rec, SymbolicClass c, bool open, bool up, Vector2 basePalm)
        {
            var frames = new List<PcmFrame>();
            double t = 0;
            for (int i = 0; i < 8; i++) { frames.Add(Frame(t, open, basePalm)); t += 0.05; }   // still pose (0.4s)
            Vector2 end = basePalm + (up ? new Vector2(0f, 0.5f) : new Vector2(0.5f, 0f));
            int n = 10;
            for (int i = 1; i <= n; i++) { frames.Add(Frame(t, open, Vector2.Lerp(basePalm, end, i / (float)n))); t += 0.05; }
            var outc = rec.SubmitTeaching(c, frames);
            Assert.IsTrue(outc.Accepted, $"teaching sample must be accepted (reason {outc.Reason})");
        }

        private static PoseConditionedMotionRecognizer TeachJumpOnly()
        {
            var rec = new PoseConditionedMotionRecognizer();
            for (int i = 0; i < 3; i++) Teach(rec, SymbolicClass.Jump, open: true, up: true, new Vector2(0.5f, 0.25f));
            Assert.IsTrue(rec.IsConfirmed(SymbolicClass.Jump));
            return rec;
        }

        private static PoseConditionedMotionRecognizer TeachBoth()
        {
            var rec = TeachJumpOnly();
            for (int i = 0; i < 3; i++) Teach(rec, SymbolicClass.Stop, open: false, up: false, new Vector2(0.4f, 0.5f));
            Assert.IsTrue(rec.IsConfirmed(SymbolicClass.Stop));
            return rec;
        }

        // Drive a live compound: hold pose still, then stroke. Returns whether Triggered fired and which class.
        private static (bool fired, SymbolicClass cls, PcmReject reject, PcmState finalState) Drive(
            PoseConditionedMotionRecognizer rec, bool open, bool up, Vector2 basePalm, double t0, int strokeFrames = 10, float strokeLen = 0.5f)
        {
            double t = t0;
            for (int i = 0; i < 8; i++) { rec.Step(Frame(t, open, basePalm)); t += 0.05; }   // arm the pose
            Vector2 end = basePalm + (up ? new Vector2(0f, strokeLen) : new Vector2(strokeLen, 0f));
            for (int i = 1; i <= strokeFrames; i++)
            {
                var r = rec.Step(Frame(t, open, Vector2.Lerp(basePalm, end, i / (float)strokeFrames)));
                t += 0.05;
                if (r.Fired) return (true, r.FiredClass, PcmReject.None, r.State);
            }
            return (false, SymbolicClass.None, rec.LastReject, rec.State);
        }

        [Test]
        public void PoseAlone_DoesNotFire()
        {
            var rec = TeachJumpOnly();
            double t = 100;
            for (int i = 0; i < 20; i++)   // hold the Jump pose perfectly still, no motion, for a full second
            {
                var r = rec.Step(Frame(t, true, new Vector2(0.5f, 0.25f)));
                Assert.IsFalse(r.Fired, "a held hand-shape alone must never fire");
                t += 0.05;
            }
        }

        [Test]
        public void MotionAlone_WrongPose_DoesNotFire()
        {
            var rec = TeachJumpOnly();
            // perform the correct upward stroke but with the WRONG (fist) hand shape
            var res = Drive(rec, open: false, up: true, new Vector2(0.5f, 0.25f), 200);
            Assert.IsFalse(res.fired, "the taught stroke with an unregistered pose must not fire");
        }

        [Test]
        public void CorrectPose_CorrectMotion_Fires()
        {
            var rec = TeachJumpOnly();
            var res = Drive(rec, open: true, up: true, new Vector2(0.5f, 0.25f), 300);
            Assert.IsTrue(res.fired, $"the taught pose + taught stroke must fire (state {res.finalState}, reject {res.reject})");
            Assert.AreEqual(SymbolicClass.Jump, res.cls);
        }

        [Test]
        public void CorrectPose_WrongMotion_DoesNotFire()
        {
            var rec = TeachJumpOnly();
            // correct open pose, but a horizontal stroke instead of the taught vertical one
            var res = Drive(rec, open: true, up: false, new Vector2(0.5f, 0.25f), 400);
            Assert.IsFalse(res.fired, "correct pose with the wrong stroke direction must not fire");
        }

        [Test]
        public void FiresAtPrefix_WithoutFinalStillness()
        {
            var rec = TeachJumpOnly();
            // feed only the first portion of the stroke (no settle frames): it must still fire mid-motion
            double t = 500;
            for (int i = 0; i < 8; i++) { rec.Step(Frame(t, true, new Vector2(0.5f, 0.25f))); t += 0.05; }
            bool fired = false;
            Vector2 bp = new Vector2(0.5f, 0.25f), end = bp + new Vector2(0f, 0.5f);
            for (int i = 1; i <= 6 && !fired; i++)   // stop early; never reaches the taught endpoint or stillness
            {
                var r = rec.Step(Frame(t, true, Vector2.Lerp(bp, end, i / 10f)));
                t += 0.05;
                fired = r.Fired;
            }
            Assert.IsTrue(fired, "must fire at the stroke prefix without waiting for the final stillness");
        }

        [Test]
        public void TooFewFrames_RejectsAsInsufficient()
        {
            var rec = TeachJumpOnly();
            // a big, instant jump in 2 frames: displacement is there but < min frames / < min elapsed
            double t = 600;
            for (int i = 0; i < 8; i++) { rec.Step(Frame(t, true, new Vector2(0.5f, 0.25f))); t += 0.05; }
            rec.Step(Frame(t, true, new Vector2(0.5f, 0.5f))); t += 0.05;
            rec.Step(Frame(t, true, new Vector2(0.5f, 0.75f))); t += 0.05;
            // a few still frames let the settle-detector reject the flick (still within the re-arm window)
            for (int i = 0; i < 6; i++) { var r = rec.Step(Frame(t, true, new Vector2(0.5f, 0.75f))); t += 0.05; if (r.Fired) Assert.Fail("must not fire on a 2-frame flick"); }
            Assert.AreEqual(PcmReject.InsufficientFrames, rec.LastReject);
        }

        [Test]
        public void SingleLearnedClass_NeedsNoSecondClassMargin()
        {
            // Jump learned alone: PoseArmed must be reachable and firing must succeed with no 2nd class present.
            var rec = TeachJumpOnly();
            Assert.IsFalse(rec.IsLearned(SymbolicClass.Stop));
            var res = Drive(rec, open: true, up: true, new Vector2(0.5f, 0.25f), 700);
            Assert.IsTrue(res.fired, "a single-class Jump proof must not require a nonexistent second-class margin");
        }

        [Test]
        public void BothLearned_WrongPoseCorrectMotion_NeverArmsWrongClass()
        {
            var rec = TeachBoth();
            // do Stop's horizontal stroke but with Jump's open pose held -> should fire Jump-ish only if its
            // motion matches; here motion is horizontal (Stop's) so it must NOT fire Jump.
            var res = Drive(rec, open: true, up: false, new Vector2(0.5f, 0.25f), 800);
            Assert.IsFalse(res.fired, "open pose + horizontal stroke must not fire (Jump motion is vertical)");
        }

        [Test]
        public void NoRefire_UntilPoseReleased()
        {
            var rec = TeachJumpOnly();
            var res = Drive(rec, open: true, up: true, new Vector2(0.5f, 0.25f), 900);
            Assert.IsTrue(res.fired);
            // keep holding the same pose and repeat the stroke WITHOUT releasing: must not fire again
            double t = 950;
            bool refired = false;
            Vector2 bp = new Vector2(0.5f, 0.25f), end = bp + new Vector2(0f, 0.5f);
            for (int i = 1; i <= 10; i++) { var r = rec.Step(Frame(t, true, Vector2.Lerp(bp, end, i / 10f))); t += 0.05; refired |= r.Fired; }
            Assert.IsFalse(refired, "the same command must not re-fire until the pose is released");
        }

        [Test]
        public void BothClasses_Separate_ByPoseAndMotion()
        {
            var rec = TeachBoth();
            var jump = Drive(rec, open: true, up: true, new Vector2(0.5f, 0.25f), 1000);
            Assert.IsTrue(jump.fired); Assert.AreEqual(SymbolicClass.Jump, jump.cls);
            var stop = Drive(rec, open: false, up: false, new Vector2(0.4f, 0.5f), 1100);
            Assert.IsTrue(stop.fired, $"Stop compound must fire (reject {stop.reject})"); Assert.AreEqual(SymbolicClass.Stop, stop.cls);
        }
    }
}
