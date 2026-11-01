using System;
using System.Collections.Generic;
using Before.Recognition;
using UnityEngine;

namespace Before.Planning
{
    public enum IntentType
    {
        Jump,
        Stop,
        Direction
    }

    public enum IntentConfidence
    {
        Stable,
        Unstable
    }

    /// <summary>Immutable recognized intent.</summary>
    public sealed class Intent
    {
        private static int nextId = 1;

        public readonly int Id;
        public readonly IntentType Type;
        public readonly int DirectionSign;     // -1 left, +1 right, 0 for symbolic
        public readonly IntentConfidence Confidence;
        public readonly GestureFeature SourceFeature; // for adaptation on success (null for direction)
        // D-091 V2 adaptation payload (set by the director when a live gesture is enqueued)
        public GestureFeatureV2 SourceFeatureV2;
        public float SourceQuality;
        public float SourceMargin;
        public readonly double RecognizedAt;

        public Intent(IntentType type, int directionSign, IntentConfidence confidence, GestureFeature source, double recognizedAt)
        {
            Id = nextId++;
            Type = type;
            DirectionSign = directionSign;
            Confidence = confidence;
            SourceFeature = source;
            RecognizedAt = recognizedAt;
        }

        public static IntentType FromClass(SymbolicClass c) => c == SymbolicClass.Jump ? IntentType.Jump : IntentType.Stop;
    }

    public enum EnqueueOutcome
    {
        Appended,
        ReplacedReservation,
        RejectedFull,
        FinalPlanDirectionSet,
        FinalPlanDirectionReplacedPlanCleared,
        FinalPlanAppended,
        FinalPlanRolled,
        FinalPlanRefusedSymbolicBeforeDirection
    }

    /// <summary>
    /// FIFO capacity-3 intent queue with reservation replacement and final-map rolling-edit semantics.
    /// Presentation observes events; the queue owns only ordering rules.
    /// </summary>
    public sealed class IntentQueue
    {
        public const int Capacity = 3;

        private readonly List<Intent> slots = new List<Intent>(Capacity);
        public bool FinalPlanMode { get; private set; }

        public event Action<Intent, int> OnPlaced;               // intent, slot index
        public event Action<Intent> OnReturnedToHand;            // rejected 4th / replaced / rolled out
        public event Action<Intent> OnConsumed;                  // executed head
        public event Action<Intent> OnRefused;                   // context refusal at head
        public event Action OnPlanEdited;                        // any accepted final-plan edit (resets review timer)

        public int Count => slots.Count;
        public Intent Head => slots.Count > 0 ? slots[0] : null;
        public Intent GetSlot(int i) => i >= 0 && i < slots.Count ? slots[i] : null;

        public void SetFinalPlanMode(bool enabled)
        {
            FinalPlanMode = enabled;
        }

        /// <summary>Normal-play enqueue. reservedSameClassAtHeadAffordance: id of an existing queued intent reserved on the same affordance with the same semantic class, or null.</summary>
        public EnqueueOutcome Enqueue(Intent intent, Intent replaceReservation = null)
        {
            if (FinalPlanMode) return EnqueueFinal(intent);

            if (replaceReservation != null)
            {
                int idx = slots.IndexOf(replaceReservation);
                if (idx >= 0)
                {
                    Intent old = slots[idx];
                    slots[idx] = intent;
                    OnReturnedToHand?.Invoke(old);
                    OnPlaced?.Invoke(intent, idx);
                    return EnqueueOutcome.ReplacedReservation;
                }
            }

            if (slots.Count >= Capacity)
            {
                OnReturnedToHand?.Invoke(intent);
                return EnqueueOutcome.RejectedFull;
            }
            slots.Add(intent);
            OnPlaced?.Invoke(intent, slots.Count - 1);
            return EnqueueOutcome.Appended;
        }

        private EnqueueOutcome EnqueueFinal(Intent intent)
        {
            if (intent.Type == IntentType.Direction)
            {
                bool hadDirection = slots.Count > 0 && slots[0].Type == IntentType.Direction;
                // a new direction replaces slot 1 and clears/returns slots 2-3, even if unchanged
                for (int i = slots.Count - 1; i >= 1; i--)
                {
                    OnReturnedToHand?.Invoke(slots[i]);
                    slots.RemoveAt(i);
                }
                if (hadDirection)
                {
                    Intent old = slots[0];
                    slots[0] = intent;
                    OnReturnedToHand?.Invoke(old);
                }
                else
                {
                    slots.Insert(0, intent);
                }
                OnPlaced?.Invoke(intent, 0);
                OnPlanEdited?.Invoke();
                return hadDirection ? EnqueueOutcome.FinalPlanDirectionReplacedPlanCleared : EnqueueOutcome.FinalPlanDirectionSet;
            }

            // symbolic: refused unless direction occupies slot 1
            if (slots.Count == 0 || slots[0].Type != IntentType.Direction)
            {
                return EnqueueOutcome.FinalPlanRefusedSymbolicBeforeDirection;
            }

            if (slots.Count < Capacity)
            {
                slots.Add(intent);
                OnPlaced?.Invoke(intent, slots.Count - 1);
                OnPlanEdited?.Invoke();
                return EnqueueOutcome.FinalPlanAppended;
            }

            // rolling pair: return old slot 2 (index 1), shift old slot 3 to slot 2, place new in slot 3
            Intent returned = slots[1];
            slots[1] = slots[2];
            slots[2] = intent;
            OnReturnedToHand?.Invoke(returned);
            OnPlaced?.Invoke(intent, 2);
            OnPlanEdited?.Invoke();
            return EnqueueOutcome.FinalPlanRolled;
        }

        public Intent ConsumeHead()
        {
            if (slots.Count == 0) return null;
            Intent head = slots[0];
            slots.RemoveAt(0);
            OnConsumed?.Invoke(head);
            return head;
        }

        public Intent RefuseHead()
        {
            if (slots.Count == 0) return null;
            Intent head = slots[0];
            slots.RemoveAt(0);
            OnRefused?.Invoke(head);
            return head;
        }

        /// <summary>Restore an incomplete consumed intent to the head (tracking-loss rewind rule).</summary>
        public void RestoreToHead(Intent intent)
        {
            if (intent == null || slots.Count >= Capacity) return;
            slots.Insert(0, intent);
            OnPlaced?.Invoke(intent, 0);
        }

        /// <summary>Checkpoint recovery: clear unexecuted intents only. Dictionary is not owned here.</summary>
        public void ClearPending()
        {
            slots.Clear();
        }

        public bool IsFullPlan =>
            FinalPlanMode && slots.Count == Capacity && slots[0].Type == IntentType.Direction;
    }

    public enum AffordanceType
    {
        Gap,
        Gate,
        Fork
    }

    /// <summary>One perceivable world affordance (authored hazard/decision point).</summary>
    public sealed class Affordance
    {
        public AffordanceType Type;
        public float ActionX;          // world x where the action decision applies
        public string Id;
        public bool Consumed;
    }

    /// <summary>Visible affordances strictly inside NUL's local perception horizon (predicted time-to-action).</summary>
    public sealed class LocalPerception
    {
        private readonly List<Affordance> affordances = new List<Affordance>();
        public float HorizonSeconds;
        public float LateCutoffSeconds;

        public LocalPerception(float horizonSeconds, float lateCutoffSeconds)
        {
            HorizonSeconds = horizonSeconds;
            LateCutoffSeconds = lateCutoffSeconds;
        }

        public void SetAffordances(IEnumerable<Affordance> list)
        {
            affordances.Clear();
            affordances.AddRange(list);
        }

        public IReadOnlyList<Affordance> All => affordances;

        public static float TimeToAction(Affordance a, float nulX, float speed)
        {
            if (speed <= 0.01f) return float.MaxValue;
            return (a.ActionX - nulX) / speed;
        }

        /// <summary>Nearest unconsumed affordance of matching type currently inside the horizon, or null.</summary>
        public Affordance FindPerceived(AffordanceType type, float nulX, float speed)
        {
            Affordance best = null;
            float bestT = float.MaxValue;
            foreach (var a in affordances)
            {
                if (a.Consumed || a.Type != type) continue;
                float t = TimeToAction(a, nulX, speed);
                if (t < 0 || t > HorizonSeconds) continue;
                if (t < bestT) { bestT = t; best = a; }
            }
            return best;
        }
    }

    public enum BindResult
    {
        BoundToAffordance,
        LiteralNow,
        TooLate
    }

    /// <summary>Binds a head intent to a perceived matching affordance, or marks literal-now/too-late.</summary>
    public static class IntentBinder
    {
        public static BindResult Bind(Intent intent, LocalPerception perception, float nulX, float speed, out Affordance bound)
        {
            bound = null;
            AffordanceType want = intent.Type == IntentType.Jump ? AffordanceType.Gap
                : intent.Type == IntentType.Stop ? AffordanceType.Gate
                : AffordanceType.Fork;

            Affordance a = perception.FindPerceived(want, nulX, speed);
            if (a == null)
            {
                return BindResult.LiteralNow;
            }
            float t = LocalPerception.TimeToAction(a, nulX, speed);
            if (t < perception.LateCutoffSeconds)
            {
                return BindResult.TooLate;
            }
            bound = a;
            return BindResult.BoundToAffordance;
        }
    }
}
