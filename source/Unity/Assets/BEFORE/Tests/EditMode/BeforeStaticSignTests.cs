using System.Collections.Generic;
using Before.Recognition;
using NUnit.Framework;
using UnityEngine;

namespace Before.Tests
{
    /// <summary>
    /// D-095 static Stop hand-sign: 3-sample hold teaching, 0.45 s hold to activate, keeps stopping while
    /// held, releases only after a valid non-matching hand for >=0.35 s, ignores transient mismatch, and is
    /// NOT released by hand loss / low quality. A Stop shape too close to the Jump pose does not confirm.
    /// </summary>
    public sealed class BeforeStaticSignTests
    {
        private static Vector2[] Hand(bool open, Vector2 palm)
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

        private static PcmFrame F(double t, bool open, bool valid = true, float q = 0.95f)
            => new PcmFrame(t, valid ? Hand(open, new Vector2(0.5f, 0.5f)) : null, new Vector2(0.5f, 0.5f), q, valid);

        private static void TeachHold(StaticSignRecognizer r, bool open, double t0, PoseDescriptor jump = null)
        {
            var frames = new List<PcmFrame>();
            double t = t0;
            for (int i = 0; i < 16; i++) { frames.Add(F(t, open)); t += 0.066; } // ~1.0 s hold
            var res = r.SubmitHold(frames, jump);
            Assert.That(res, Is.EqualTo(StaticSignRecognizer.TeachResult.AcceptedNeedMore)
                .Or.EqualTo(StaticSignRecognizer.TeachResult.Confirmed), "hold sample must be accepted");
        }

        private static StaticSignRecognizer Taught()
        {
            var r = new StaticSignRecognizer();
            TeachHold(r, false, 0);   // curled fist = Stop sign
            TeachHold(r, false, 2);
            TeachHold(r, false, 4);
            Assert.IsTrue(r.IsConfirmed);
            return r;
        }

        [Test]
        public void ThreeHolds_Confirm()
        {
            Assert.IsTrue(Taught().IsConfirmed);
        }

        [Test]
        public void ShortHold_TooShort()
        {
            var r = new StaticSignRecognizer();
            var frames = new List<PcmFrame>();
            double t = 0; for (int i = 0; i < 6; i++) { frames.Add(F(t, false)); t += 0.066; } // ~0.4 s < 0.8
            Assert.AreEqual(StaticSignRecognizer.TeachResult.TooShort, r.SubmitHold(frames, null));
        }

        [Test]
        public void StopTooCloseToJumpPose_DoesNotConfirm()
        {
            var r = new StaticSignRecognizer();
            var jump = PoseDescriptor.Extract(Hand(false, new Vector2(0.5f, 0.5f))); // same shape as the Stop hold
            var frames = new List<PcmFrame>();
            double t = 0; for (int i = 0; i < 16; i++) { frames.Add(F(t, false)); t += 0.066; }
            Assert.AreEqual(StaticSignRecognizer.TeachResult.TooSimilarToJump, r.SubmitHold(frames, jump));
        }

        [Test]
        public void Hold_ActivatesAfterHoldTime_AndStaysWhileHeld()
        {
            var r = Taught();
            double t = 10;
            bool active = false;
            for (int i = 0; i < 5; i++) { active = r.Step(F(t, false)); t += 0.066; } // still filling the window
            Assert.IsFalse(active, "must not activate before the hold time");
            for (int i = 0; i < 10; i++) { active = r.Step(F(t, false)); t += 0.066; }  // stable + 0.45 s hold
            Assert.IsTrue(active, "must activate after the 0.45 s hold");
            for (int i = 0; i < 30; i++) { active = r.Step(F(t, false)); t += 0.066; }  // keep holding
            Assert.IsTrue(active, "must keep stopping while the shape is held");
        }

        [Test]
        public void Release_OnlyAfterValidNonMatchFor035s()
        {
            var r = Taught();
            double t = 20;
            for (int i = 0; i < 16; i++) { r.Step(F(t, false)); t += 0.066; } // activate
            Assert.IsTrue(r.Active);
            // transient mismatch < 0.35 s (open hand) then back to Stop shape -> must NOT release
            for (int i = 0; i < 4; i++) { r.Step(F(t, true)); t += 0.066; } // ~0.26 s open
            for (int i = 0; i < 4; i++) { r.Step(F(t, false)); t += 0.066; }
            Assert.IsTrue(r.Active, "transient mismatch < 0.35 s must be ignored");
            // sustained mismatch >= 0.35 s -> release
            for (int i = 0; i < 8; i++) { r.Step(F(t, true)); t += 0.066; } // ~0.53 s open
            Assert.IsFalse(r.Active, "a valid non-matching hand for >=0.35 s must release");
        }

        [Test]
        public void HandLoss_DoesNotRelease()
        {
            var r = Taught();
            double t = 30;
            for (int i = 0; i < 16; i++) { r.Step(F(t, false)); t += 0.066; } // activate
            Assert.IsTrue(r.Active);
            for (int i = 0; i < 20; i++) { r.Step(F(t, false, valid: false)); t += 0.066; } // long tracking loss
            Assert.IsTrue(r.Active, "hand loss / tracking loss must not release the hold");
        }
    }
}
