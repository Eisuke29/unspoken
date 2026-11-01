using System;
using Before.Config;
using UnityEngine;

namespace Before.Recognition
{
    public enum SymbolicClass
    {
        Jump = 0,
        Stop = 1,
        None = -1   // D-092: "no armed/matched class" sentinel; never used as an array index
    }

    public enum TeachingResult
    {
        AcceptedFirst,
        AcceptedSecondClassClosed,
        RejectedTooShortOrInvalid,
        RejectedInconsistentSecond,
        RejectedOverlapsExistingClass
    }

    /// <summary>
    /// Owns the immutable seeds, personalized radii, and adaptive prototypes for the two symbolic classes.
    /// Memory only; full RESET is the only clearing event.
    /// </summary>
    public sealed class GestureDictionary
    {
        private readonly BeforeRecognitionConfig cfg;

        private sealed class ClassEntry
        {
            public GestureFeature Seed1;
            public GestureFeature Seed2;
            public GestureFeature Prototype;
            public GestureFeature AlignedSeedMean;
            public float Radius;
            public float InitialRadius;
            public float SeedDistance;
            public int AdaptationCount;
            public bool Closed => Seed2 != null;
        }

        private readonly ClassEntry[] classes = new ClassEntry[2] { new ClassEntry(), new ClassEntry() };

        public GestureDictionary(BeforeRecognitionConfig config)
        {
            cfg = config;
        }

        public bool IsClassClosed(SymbolicClass c) => classes[(int)c].Closed;
        public bool HasFirstSample(SymbolicClass c) => classes[(int)c].Seed1 != null;
        public float GetRadius(SymbolicClass c) => classes[(int)c].Radius;
        public float GetSeedDistance(SymbolicClass c) => classes[(int)c].SeedDistance;
        public int GetAdaptationCount(SymbolicClass c) => classes[(int)c].AdaptationCount;
        public GestureFeature GetPrototype(SymbolicClass c) => classes[(int)c].Prototype;
        public GestureFeature GetSeed(SymbolicClass c, int index) => index == 0 ? classes[(int)c].Seed1 : classes[(int)c].Seed2;

        /// <summary>Submit one teaching sample for the class currently being taught.</summary>
        public TeachingResult SubmitTeachingSample(SymbolicClass target, GestureFeature feature)
        {
            if (feature == null) return TeachingResult.RejectedTooShortOrInvalid;
            var entry = classes[(int)target];
            if (entry.Closed) return TeachingResult.RejectedTooShortOrInvalid;

            if (entry.Seed1 == null)
            {
                entry.Seed1 = feature;
                return TeachingResult.AcceptedFirst;
            }

            float d = WeightedDtw.Distance(entry.Seed1, feature, cfg);
            if (d > cfg.seedConflictDistance)
            {
                return TeachingResult.RejectedInconsistentSecond; // keep seed1, request replacement second sample
            }

            // provisional close: compute radius and prototype, then check distinctness from the other class
            float radius = Mathf.Clamp(cfg.radiusSeedFactor * d + cfg.radiusBias, cfg.radiusMin, cfg.radiusMax);
            GestureFeature prototype = WeightedDtw.AlignedAverage(entry.Seed1, feature, cfg, 0.5f);

            var other = classes[1 - (int)target];
            if (other.Closed)
            {
                float crossDistance = WeightedDtw.Distance(prototype, other.Prototype, cfg);
                if (crossDistance < cfg.classSeparationFactor * Mathf.Max(radius, other.Radius))
                {
                    // reject the entire new class attempt; existing class untouched, seed1 also discarded
                    entry.Seed1 = null;
                    return TeachingResult.RejectedOverlapsExistingClass;
                }
            }

            entry.Seed2 = feature;
            entry.SeedDistance = d;
            entry.Radius = radius;
            entry.InitialRadius = radius;
            entry.Prototype = prototype;
            entry.AlignedSeedMean = prototype;
            return TeachingResult.AcceptedSecondClassClosed;
        }

        /// <summary>class distance per spec: 0.5*minSeed + 0.5*prototype.</summary>
        public float ClassDistance(SymbolicClass c, GestureFeature input)
        {
            var entry = classes[(int)c];
            if (!entry.Closed) return float.MaxValue;
            float s1 = WeightedDtw.Distance(input, entry.Seed1, cfg);
            float s2 = WeightedDtw.Distance(input, entry.Seed2, cfg);
            float proto = WeightedDtw.Distance(input, entry.Prototype, cfg);
            return 0.5f * Mathf.Min(s1, s2) + 0.5f * proto;
        }

        public float NormalizedDistance(SymbolicClass c, GestureFeature input)
        {
            var entry = classes[(int)c];
            if (!entry.Closed) return float.MaxValue;
            return ClassDistance(c, input) / entry.Radius;
        }

        /// <summary>EMA adaptation after a context-appropriate success, with drift guard. Seeds never change.</summary>
        public bool Adapt(SymbolicClass c, GestureFeature successfulSegment)
        {
            var entry = classes[(int)c];
            if (!entry.Closed || successfulSegment == null) return false;

            GestureFeature proposed = WeightedDtw.AlignedAverage(entry.Prototype, successfulSegment, cfg, cfg.adaptEma);
            float drift = WeightedDtw.Distance(proposed, entry.AlignedSeedMean, cfg);
            if (drift > cfg.adaptDriftGuardFactor * entry.Radius)
            {
                return false; // reject drifted prototype
            }
            entry.Prototype = proposed;

            float observed = ClassDistance(c, successfulSegment);
            float target = Mathf.Clamp(observed, cfg.radiusMin, cfg.radiusMax);
            float updated = cfg.radiusEmaOld * entry.Radius + (1f - cfg.radiusEmaOld) * target;
            float lo = Mathf.Max(cfg.radiusMin, entry.InitialRadius * (1f - cfg.radiusDriftLimit));
            float hi = Mathf.Min(cfg.radiusMax, entry.InitialRadius * (1f + cfg.radiusDriftLimit));
            entry.Radius = Mathf.Clamp(updated, lo, hi);
            entry.AdaptationCount++;
            return true;
        }

        public void ResetAll()
        {
            classes[0] = new ClassEntry();
            classes[1] = new ClassEntry();
        }
    }

    public enum ClassificationState
    {
        Accepted,
        AmbiguousNearest,
        NoMatch
    }

    public readonly struct ClassificationResult
    {
        public readonly ClassificationState State;
        public readonly SymbolicClass Chosen;
        public readonly float N1;
        public readonly float N2;

        public ClassificationResult(ClassificationState state, SymbolicClass chosen, float n1, float n2)
        {
            State = state;
            Chosen = chosen;
            N1 = n1;
            N2 = n2;
        }
    }

    /// <summary>Deterministic classifier over the two learned classes. Never mutates context.</summary>
    public sealed class SymbolicGestureClassifier
    {
        private readonly GestureDictionary dictionary;
        private readonly BeforeRecognitionConfig cfg;

        public SymbolicGestureClassifier(GestureDictionary dict, BeforeRecognitionConfig config)
        {
            dictionary = dict;
            cfg = config;
        }

        public ClassificationResult Classify(GestureFeature input)
        {
            float nJump = dictionary.NormalizedDistance(SymbolicClass.Jump, input);
            float nStop = dictionary.NormalizedDistance(SymbolicClass.Stop, input);

            SymbolicClass best;
            float n1, n2;
            if (nJump < nStop || (nJump == nStop))
            {
                // exact tie breaks to the lower stable command ID (Jump=0)
                best = SymbolicClass.Jump; n1 = nJump; n2 = nStop;
            }
            else
            {
                best = SymbolicClass.Stop; n1 = nStop; n2 = nJump;
            }

            if (n1 > cfg.noMatchN1) return new ClassificationResult(ClassificationState.NoMatch, best, n1, n2);
            if (n1 <= cfg.highConfidenceN1 && (n2 - n1) >= cfg.highConfidenceMargin)
            {
                return new ClassificationResult(ClassificationState.Accepted, best, n1, n2);
            }
            return new ClassificationResult(ClassificationState.AmbiguousNearest, best, n1, n2);
        }
    }
}
