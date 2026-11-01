using Before.Recognition;
using NUnit.Framework;
using UnityEngine;

namespace Before.Tests
{
    /// <summary>
    /// D-094 direction by index-finger pointing: lateral point + 0.45 s hold commits Left/Right; a fist does
    /// not point; commit locks further input until Reset.
    /// </summary>
    public sealed class BeforeDirectionPointTests
    {
        // A pointing hand: index extended horizontally toward dirX (+1 right / -1 left), others curled short.
        private static Vector2[] Pointing(int dirX, Vector2 palm)
        {
            var lm = new Vector2[21];
            lm[0] = palm; // wrist
            lm[5] = palm + new Vector2(0.02f * dirX, 0.02f);              // index MCP
            lm[6] = palm + new Vector2(0.10f * dirX, 0.02f);
            lm[7] = palm + new Vector2(0.18f * dirX, 0.02f);
            lm[8] = palm + new Vector2(0.26f * dirX, 0.02f);              // index tip: far, lateral
            // middle / ring / pinky curled (tips near wrist)
            int[] baseIdx = { 9, 13, 17 };
            for (int k = 0; k < 3; k++)
            {
                int b = baseIdx[k];
                float off = 0.02f * (k - 1);
                lm[b + 0] = palm + new Vector2(off, 0.03f);
                lm[b + 1] = palm + new Vector2(off, 0.05f);
                lm[b + 2] = palm + new Vector2(off, 0.04f);
                lm[b + 3] = palm + new Vector2(off, 0.03f);              // curled tip near wrist
            }
            // thumb (indices 1..4)
            lm[1] = palm + new Vector2(-0.02f * dirX, -0.01f);
            lm[2] = palm + new Vector2(-0.03f * dirX, 0.0f);
            lm[3] = palm + new Vector2(-0.04f * dirX, 0.01f);
            lm[4] = palm + new Vector2(-0.05f * dirX, 0.02f);
            return lm;
        }

        private static Vector2[] Fist(Vector2 palm)
        {
            var lm = new Vector2[21];
            lm[0] = palm;
            for (int f = 0; f < 5; f++)
            {
                int b = 1 + f * 4;
                float off = 0.02f * (f - 2);
                lm[b + 0] = palm + new Vector2(off, 0.02f);
                lm[b + 1] = palm + new Vector2(off, 0.04f);
                lm[b + 2] = palm + new Vector2(off, 0.05f);
                lm[b + 3] = palm + new Vector2(off, 0.05f);
            }
            return lm;
        }

        private static PcmFrame P(double t, Vector2[] lm) => new PcmFrame(t, lm, new Vector2(0.5f, 0.5f), 0.95f, lm != null);

        [Test]
        public void ClassifiesLeftAndRight()
        {
            Assert.AreEqual(PointDir.Right, DirectionPointRecognizer.Classify(Pointing(+1, new Vector2(0.5f, 0.5f)), new DirectionPointRecognizer.Cfg()));
            Assert.AreEqual(PointDir.Left, DirectionPointRecognizer.Classify(Pointing(-1, new Vector2(0.5f, 0.5f)), new DirectionPointRecognizer.Cfg()));
            Assert.AreEqual(PointDir.None, DirectionPointRecognizer.Classify(Fist(new Vector2(0.5f, 0.5f)), new DirectionPointRecognizer.Cfg()));
        }

        [Test]
        public void HoldCommitsRight()
        {
            var r = new DirectionPointRecognizer();
            var lm = Pointing(+1, new Vector2(0.5f, 0.5f));
            double t = 0; bool committed = false;
            for (int i = 0; i < 5; i++) { committed |= r.Step(P(t, lm)); t += 0.066; }
            Assert.IsFalse(committed, "must not commit before the hold time");
            for (int i = 0; i < 10; i++) { committed |= r.Step(P(t, lm)); t += 0.066; }
            Assert.IsTrue(r.Committed);
            Assert.AreEqual(PointDir.Right, r.CommittedDir);
        }

        [Test]
        public void Fist_NeverCommits()
        {
            var r = new DirectionPointRecognizer();
            double t = 0;
            for (int i = 0; i < 30; i++) r.Step(P(t + i * 0.066, Fist(new Vector2(0.5f, 0.5f))));
            Assert.IsFalse(r.Committed);
        }

        [Test]
        public void Commit_LocksUntilReset()
        {
            var r = new DirectionPointRecognizer();
            var right = Pointing(+1, new Vector2(0.5f, 0.5f));
            double t = 0;
            for (int i = 0; i < 16; i++) { r.Step(P(t, right)); t += 0.066; }
            Assert.AreEqual(PointDir.Right, r.CommittedDir);
            // now point left: must NOT change the committed direction until Reset
            var left = Pointing(-1, new Vector2(0.5f, 0.5f));
            for (int i = 0; i < 16; i++) { r.Step(P(t, left)); t += 0.066; }
            Assert.AreEqual(PointDir.Right, r.CommittedDir, "commit locks until Reset");
            r.Reset();
            Assert.IsFalse(r.Committed);
        }
    }
}
