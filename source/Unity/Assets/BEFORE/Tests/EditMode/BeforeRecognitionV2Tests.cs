using System.Collections.Generic;
using Before.Config;
using Before.Recognition;
using NUnit.Framework;
using UnityEngine;

namespace Before.Tests
{
    /// <summary>
    /// D-091 Recognition V2 invariance and teaching/adaptation tests. A synthetic 21-landmark hand and
    /// two distinct dynamic gestures (Jump = upward palm motion, Stop = sideways palm motion) are generated
    /// with controllable speed, translation, scale, rotation, jitter, stillness padding, and boundary,
    /// so the recognizer is exercised without a camera and never uses story context for the label.
    /// </summary>
    public sealed class BeforeRecognitionV2Tests
    {
        private BeforeRecognitionConfig cfg;

        // hand-local template (x right, y up), wrist at origin, middle MCP (idx 9) along +Y
        private static readonly Vector2[] Template =
        {
            new Vector2(0f,0f),
            new Vector2(0.25f,0.15f), new Vector2(0.40f,0.30f), new Vector2(0.50f,0.45f), new Vector2(0.58f,0.58f),
            new Vector2(0.15f,0.50f), new Vector2(0.17f,0.80f), new Vector2(0.18f,1.00f), new Vector2(0.19f,1.18f),
            new Vector2(0.00f,0.55f), new Vector2(0.00f,0.90f), new Vector2(0.00f,1.12f), new Vector2(0.00f,1.32f),
            new Vector2(-0.15f,0.50f), new Vector2(-0.17f,0.82f), new Vector2(-0.18f,1.02f), new Vector2(-0.19f,1.18f),
            new Vector2(-0.28f,0.42f), new Vector2(-0.32f,0.68f), new Vector2(-0.34f,0.85f), new Vector2(-0.36f,1.00f),
        };

        [SetUp]
        public void SetUp() { cfg = ScriptableObject.CreateInstance<BeforeRecognitionConfig>(); }
        [TearDown]
        public void TearDown() { Object.DestroyImmediate(cfg); }

        private enum Kind { Jump, Stop }

        private static Vector2[] MakeHand(Vector2 palm, float scale, float rotDeg, float jitter, System.Random rng)
        {
            float c = Mathf.Cos(rotDeg * Mathf.Deg2Rad), s = Mathf.Sin(rotDeg * Mathf.Deg2Rad);
            var lm = new Vector2[21];
            for (int i = 0; i < 21; i++)
            {
                Vector2 p = Template[i] * scale;
                Vector2 r = new Vector2(p.x * c - p.y * s, p.x * s + p.y * c);
                Vector2 w = palm + r;
                if (jitter > 0f) w += new Vector2((float)(rng.NextDouble() - 0.5) * 2f * jitter, (float)(rng.NextDouble() - 0.5) * 2f * jitter);
                lm[i] = w;
            }
            return lm;
        }

        private List<LandmarkFrame> MakeGesture(Kind kind, float speed = 1f, Vector2 translate = default,
            float scaleMul = 1f, float rotDeg = 0f, float jitter = 0f, int padStart = 0, int padEnd = 0, int seed = 1)
        {
            var rng = new System.Random(seed);
            var frames = new List<LandmarkFrame>();
            Vector2 start = new Vector2(0.5f, 0.4f) + translate;
            Vector2 delta = kind == Kind.Jump ? new Vector2(0f, 0.28f) : new Vector2(0.28f, 0f);
            int steps = 15;
            float motionTime = 1.0f / speed;
            float dt = motionTime / (steps - 1);
            double t = 0;
            float baseScale = 0.5f * scaleMul;
            for (int i = 0; i < padStart; i++) { frames.Add(new LandmarkFrame(t, MakeHand(start, baseScale, rotDeg, jitter, rng))); t += dt; }
            for (int s = 0; s < steps; s++)
            {
                float prog = s / (float)(steps - 1);
                Vector2 palm = Vector2.Lerp(start, start + delta, prog);
                frames.Add(new LandmarkFrame(t, MakeHand(palm, baseScale, rotDeg, jitter, rng)));
                t += dt;
            }
            Vector2 endPalm = start + delta;
            for (int i = 0; i < padEnd; i++) { frames.Add(new LandmarkFrame(t, MakeHand(endPalm, baseScale, rotDeg, jitter, rng))); t += dt; }
            return frames;
        }

        private GestureFeatureV2 Feat(Kind kind, float speed = 1f, Vector2 translate = default,
            float scaleMul = 1f, float rotDeg = 0f, float jitter = 0f, int padStart = 0, int padEnd = 0, int seed = 1)
        {
            var raw = MakeGesture(kind, speed, translate, scaleMul, rotDeg, jitter, padStart, padEnd, seed);
            var trimmed = GestureV2Builder.Trim(raw, cfg);
            return GestureV2Builder.Build(trimmed, cfg);
        }

        private GestureRecognizerV2 TrainedRecognizer()
        {
            var r = new GestureRecognizerV2(cfg);
            for (int i = 0; i < 3; i++) r.SubmitTeaching(SymbolicClass.Jump, Feat(Kind.Jump, jitter: 0.004f, seed: 10 + i));
            for (int i = 0; i < 3; i++) r.SubmitTeaching(SymbolicClass.Stop, Feat(Kind.Stop, jitter: 0.004f, seed: 20 + i));
            Assert.IsTrue(r.IsConfirmed(SymbolicClass.Jump), "Jump should confirm after 3 cohesive samples");
            Assert.IsTrue(r.IsConfirmed(SymbolicClass.Stop), "Stop should confirm after 3 cohesive samples");
            return r;
        }

        private void AssertClass(GestureRecognizerV2 r, GestureFeatureV2 f, SymbolicClass expected, string ctx)
        {
            var res = r.Classify(f);
            Assert.AreNotEqual(LiveV2State.NoMatch, res.State, $"{ctx}: unexpected NoMatch");
            Assert.AreEqual(expected, res.Chosen, $"{ctx}: wrong class");
        }

        [Test]
        public void SpeedInvariance_075_10_13_SameClass()
        {
            var r = TrainedRecognizer();
            AssertClass(r, Feat(Kind.Jump, speed: 0.75f, seed: 99), SymbolicClass.Jump, "jump 0.75x");
            AssertClass(r, Feat(Kind.Jump, speed: 1.0f, seed: 98), SymbolicClass.Jump, "jump 1.0x");
            AssertClass(r, Feat(Kind.Jump, speed: 1.3f, seed: 97), SymbolicClass.Jump, "jump 1.3x");
        }

        [Test]
        public void TranslationScaleRotation_SameClass()
        {
            var r = TrainedRecognizer();
            AssertClass(r, Feat(Kind.Jump, translate: new Vector2(-0.15f, 0.1f), seed: 3), SymbolicClass.Jump, "jump translated");
            AssertClass(r, Feat(Kind.Jump, scaleMul: 1.4f, seed: 4), SymbolicClass.Jump, "jump scaled up");
            AssertClass(r, Feat(Kind.Jump, scaleMul: 0.7f, seed: 5), SymbolicClass.Jump, "jump scaled down");
            AssertClass(r, Feat(Kind.Jump, rotDeg: 10f, seed: 6), SymbolicClass.Jump, "jump +10deg");
            AssertClass(r, Feat(Kind.Jump, rotDeg: -10f, seed: 7), SymbolicClass.Jump, "jump -10deg");
        }

        [Test]
        public void StillnessPaddingAndBoundaryShift_SameClass()
        {
            var r = TrainedRecognizer();
            AssertClass(r, Feat(Kind.Jump, padStart: 6, padEnd: 6, seed: 8), SymbolicClass.Jump, "jump with stillness");
            // boundary-shifted trims still classify to the same class
            var raw = MakeGesture(Kind.Jump, padStart: 4, padEnd: 4, seed: 9);
            var shifted = GestureV2Builder.Build(GestureV2Builder.Trim(raw, cfg, startShift: -2, endShift: 2), cfg);
            AssertClass(r, shifted, SymbolicClass.Jump, "jump boundary shifted");
        }

        [Test]
        public void LandmarkJitter_SameClass()
        {
            var r = TrainedRecognizer();
            AssertClass(r, Feat(Kind.Jump, jitter: 0.012f, seed: 42), SymbolicClass.Jump, "jump jittered");
            AssertClass(r, Feat(Kind.Stop, jitter: 0.012f, seed: 43), SymbolicClass.Stop, "stop jittered");
        }

        [Test]
        public void JumpAndStop_AreSeparable()
        {
            var r = TrainedRecognizer();
            Assert.IsTrue(r.SeparationOk(), "Jump and Stop medoids must be sufficiently distinct");
            AssertClass(r, Feat(Kind.Stop, speed: 1.2f, seed: 55), SymbolicClass.Stop, "stop variant");
        }

        [Test]
        public void AdaptiveTeaching_DropsSingleOutlierThenConfirms()
        {
            var r = new GestureRecognizerV2(cfg);
            r.SubmitTeaching(SymbolicClass.Jump, Feat(Kind.Jump, seed: 1));
            r.SubmitTeaching(SymbolicClass.Jump, Feat(Kind.Jump, seed: 2));
            // third sample is an outlier (a Stop-shaped motion mixed into Jump teaching)
            var p3 = r.SubmitTeaching(SymbolicClass.Jump, Feat(Kind.Stop, seed: 3));
            Assert.AreEqual(TeachV2Status.DroppedOutlierNeedOne, p3.Status, "a lone outlier must be dropped, not confirmed");
            Assert.IsFalse(r.IsConfirmed(SymbolicClass.Jump));
            var p4 = r.SubmitTeaching(SymbolicClass.Jump, Feat(Kind.Jump, seed: 4));
            Assert.AreEqual(TeachV2Status.Confirmed, p4.Status, "after replacing the outlier the class should confirm");
            Assert.IsTrue(r.IsConfirmed(SymbolicClass.Jump));
        }

        [Test]
        public void AdaptiveTeaching_DivergentSecondSampleIsNotRejectedOnTheSpot()
        {
            var r = new GestureRecognizerV2(cfg);
            r.SubmitTeaching(SymbolicClass.Jump, Feat(Kind.Jump, seed: 1));
            var p2 = r.SubmitTeaching(SymbolicClass.Jump, Feat(Kind.Stop, seed: 2)); // very different 2nd
            Assert.AreEqual(TeachV2Status.NeedMore, p2.Status, "must not reject a divergent 2nd sample on the spot");
        }

        [Test]
        public void ContinualAdaptation_RejectsLowQualityOrLowMargin_AndCapsSamples()
        {
            var r = TrainedRecognizer();
            var good = Feat(Kind.Jump, seed: 200);
            Assert.IsFalse(r.AdaptOnSuccess(SymbolicClass.Jump, good, quality: 0.5f, margin: 0.5f), "low quality must not adapt");
            Assert.IsFalse(r.AdaptOnSuccess(SymbolicClass.Jump, good, quality: 0.95f, margin: 0.05f), "low margin must not adapt");
            int before = r.GetClass(SymbolicClass.Jump).Samples.Count;
            for (int i = 0; i < 20; i++)
                r.AdaptOnSuccess(SymbolicClass.Jump, Feat(Kind.Jump, jitter: 0.004f, seed: 300 + i), quality: 0.95f, margin: 0.5f);
            int after = r.GetClass(SymbolicClass.Jump).Samples.Count;
            Assert.GreaterOrEqual(after, before, "good samples should be accepted");
            Assert.LessOrEqual(after, cfg.v2MaxRealSamples, "real samples must be capped at 8 (medoid pruning)");
        }
    }
}
