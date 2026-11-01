// BEFORE Goal 2 — CommandLab core logic (pure C#, no scene needed; testable).
// D-113: accepted commands reach NUL immediately. No stock/queue.
// D-128: CROUCH is a discrete event; on receipt NUL crouches 0.75 s and a
// CROUCH received while crouching refreshes the remaining time to 0.75 s.
// D-099/D-122: this layer never touches camera RGB; only commands + skeleton.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Before.Production
{
    public enum CommandKind { Unknown, Jump, Crouch, StopOn, StopOff, Left, Right }

    public enum ProductionReadinessKind { None, Jump, Crouch, Stop }

    public enum ProductionHandSignal
    {
        Unknown,
        Ok,
        TrackingLost,
        LowQuality,
        OutOfFrame
    }

    public struct CommandPacket
    {
        public string Session;
        public long SessionStartUs;
        public int CommandId;
        public int Seq;
        public long TimestampUs;
        public CommandKind Kind;
        public int Direction; // -1 left / +1 right / 0

        public static CommandKind ParseKind(string t)
        {
            switch (t)
            {
                case "JUMP": return CommandKind.Jump;
                case "CROUCH": return CommandKind.Crouch;
                case "STOP_ON": return CommandKind.StopOn;
                case "STOP_OFF": return CommandKind.StopOff;
                case "LEFT": return CommandKind.Left;
                case "RIGHT": return CommandKind.Right;
                default: return CommandKind.Unknown;
            }
        }

        [Serializable]
        private class Dto
        {
            public string kind;
            public string session;
            public long sstart;
            public int cmdId;
            public int seq;
            public long tUs;
            public string type;
            public int dir;
        }

        // Returns true and fills packet only for a well-formed "cmd" datagram.
        public static bool TryParseCommand(string json, out CommandPacket packet)
        {
            packet = default;
            if (string.IsNullOrEmpty(json)) return false;
            Dto d;
            try { d = JsonUtility.FromJson<Dto>(json); }
            catch { return false; }
            if (d == null || d.kind != "cmd") return false;
            var kind = ParseKind(d.type);
            if (kind == CommandKind.Unknown) return false;
            if (string.IsNullOrEmpty(d.session) || d.cmdId <= 0) return false;
            packet = new CommandPacket
            {
                Session = d.session,
                SessionStartUs = d.sstart,
                CommandId = d.cmdId,
                Seq = d.seq,
                TimestampUs = d.tUs,
                Kind = kind,
                Direction = d.dir,
            };
            return true;
        }
    }

    // Latches the active session by its start time. A newer session switches
    // (and signals the dedup memory to clear); an older/stale session is
    // discarded; the current session is accepted.
    public sealed class SessionGate
    {
        public string ActiveSession { get; private set; }
        public long ActiveStartUs { get; private set; } = long.MinValue;
        public int DiscardedStale { get; private set; }

        // Returns true if the packet's session is the (possibly newly adopted)
        // active session. `switched` is set when a new session was adopted.
        public bool Accept(in CommandPacket p, out bool switched)
        {
            switched = false;
            if (ActiveSession == null || p.SessionStartUs > ActiveStartUs)
            {
                ActiveSession = p.Session;
                ActiveStartUs = p.SessionStartUs;
                switched = true;
                return true;
            }
            if (p.Session == ActiveSession && p.SessionStartUs == ActiveStartUs)
                return true;
            DiscardedStale++;   // older session, or a different equal-start session
            return false;
        }

        public void Reset()
        {
            ActiveSession = null;
            ActiveStartUs = long.MinValue;
            DiscardedStale = 0;
        }
    }

    // Marks (session,commandId) pairs so each command executes exactly once even
    // though the sidecar sends every commandId three times.
    public sealed class CommandDeduplicator
    {
        private readonly HashSet<string> _seen = new HashSet<string>();

        public bool IsFirst(in CommandPacket p)
        {
            string key = p.Session + "#" + p.CommandId;
            return _seen.Add(key);
        }

        public void Clear() => _seen.Clear();
        public int Count => _seen.Count;
    }

    // Session gate + dedup. Ingest returns true only for the first datagram of a
    // command from the active session. Stale-session datagrams are discarded.
    public sealed class CommandRouter
    {
        public readonly SessionGate Session = new SessionGate();
        public readonly CommandDeduplicator Dedup = new CommandDeduplicator();

        public bool Ingest(in CommandPacket p)
        {
            if (!Session.Accept(p, out bool switched)) return false;
            if (switched) Dedup.Clear();   // new session -> fresh dedup memory
            return Dedup.IsFirst(p);
        }
    }

    // Receiver -> router -> CommandBus -> NUL (D-113 / §11). Executed commands
    // are published here; subscribers react immediately.
    public sealed class CommandBus
    {
        public event Action<CommandPacket> OnCommand;
        public int Published { get; private set; }
        public void Publish(in CommandPacket p) { Published++; OnCommand?.Invoke(p); }
    }

    // Minimal NUL reaction state (D-113 immediate; empty space). Pure & testable.
    public sealed class NulState
    {
        public const double CrouchSeconds = 0.75;

        public int JumpCount;
        public bool StopActive;
        public bool Crouching;
        public double CrouchUntil;
        public bool DirectionMode;
        public int LastDirection;   // -1 / +1 / 0
        public bool Paused;

        // Apply one already-deduped command. While paused, the command is
        // consumed (it was dedup-marked at ingest) but produces no effect and is
        // never replayed. Only Left/Right act in Direction mode; Jump/Crouch/Stop
        // act only outside it.
        public void Apply(in CommandPacket p, double now)
        {
            if (Paused) return;
            if (DirectionMode)
            {
                if (p.Kind == CommandKind.Left) LastDirection = -1;
                else if (p.Kind == CommandKind.Right) LastDirection = 1;
                return; // Jump/Crouch/Stop suppressed in the exclusive input mode
            }
            switch (p.Kind)
            {
                case CommandKind.Jump: JumpCount++; break;
                case CommandKind.Crouch: Crouching = true; CrouchUntil = now + CrouchSeconds; break;
                case CommandKind.StopOn: StopActive = true; break;
                case CommandKind.StopOff: StopActive = false; break;
                // Left/Right ignored outside Direction mode
            }
        }

        public void Tick(double now)
        {
            if (Crouching && now >= CrouchUntil) Crouching = false;
        }

        public double CrouchRemaining(double now)
            => Crouching ? Math.Max(0.0, CrouchUntil - now) : 0.0;
    }

    // Skeleton-only landmark datagram: 21 mirrored normalized points, flat
    // [x0,y0,...x20,y20]. Never any RGB/depth.
    public static class LandmarkPacket
    {
        [Serializable]
        private class Dto
        {
            public string kind;
            public long tUs;
            public float[] pts;
            public string ready;
            public string signal;
            public float trackQ;
            public int dir;
            public float dirHold;
            public string owner;
            public float stopD;
            public float stopTh;
            public float stopCandidateMs;
            public int stopStable;
            public float stopHoldMs;
            public float stopOnMs;
            public float stopOffMs;
            public string stopReject;
            public bool stopActive;
        }

        public struct StopStatus
        {
            public float PoseDistance;
            public float Threshold;
            public float CandidateStartedMs;
            public int StableFrames;
            public float HoldMs;
            public float StopOnIssuedMs;
            public float StopOffIssuedMs;
            public string RejectReason;
            public bool Active;
        }

        public struct Status
        {
            public long TimestampUs;
            public float[] Points;
            public ProductionReadinessKind Readiness;
            public ProductionHandSignal Signal;
            public float TrackingQuality;
            public int DirectionPreview;
            public float DirectionHoldProgress;
            public string RecognitionOwner;
            public StopStatus Stop;
        }

        public static bool TryParse(string json, out float[] pts)
        {
            pts = null;
            if (!TryParseStatus(json, out Status status) || status.Points == null ||
                status.Points.Length != 42) return false;
            pts = status.Points;
            return true;
        }

        public static bool TryParseStatus(string json, out Status status)
        {
            status = default;
            if (string.IsNullOrEmpty(json)) return false;
            Dto d;
            try { d = JsonUtility.FromJson<Dto>(json); }
            catch { return false; }
            if (d == null || d.kind != "lm" || d.pts == null ||
                (d.pts.Length != 0 && d.pts.Length != 42))
                return false;
            status = new Status
            {
                TimestampUs = d.tUs,
                Points = d.pts,
                Readiness = ParseReadiness(d.ready),
                Signal = ParseSignal(d.signal, d.pts.Length == 42),
                TrackingQuality = Mathf.Clamp01(d.trackQ),
                DirectionPreview = d.dir < 0 ? -1 : d.dir > 0 ? 1 : 0,
                DirectionHoldProgress = Mathf.Clamp01(d.dirHold),
                RecognitionOwner = string.IsNullOrEmpty(d.owner) ? "live" : d.owner,
                Stop = new StopStatus
                {
                    PoseDistance = d.stopD,
                    Threshold = d.stopTh,
                    CandidateStartedMs = d.stopCandidateMs,
                    StableFrames = Mathf.Max(0, d.stopStable),
                    HoldMs = Mathf.Max(0f, d.stopHoldMs),
                    StopOnIssuedMs = Mathf.Max(0f, d.stopOnMs),
                    StopOffIssuedMs = Mathf.Max(0f, d.stopOffMs),
                    RejectReason = d.stopReject ?? string.Empty,
                    Active = d.stopActive
                }
            };
            return true;
        }

        private static ProductionReadinessKind ParseReadiness(string value)
        {
            switch (value)
            {
                case "JUMP": return ProductionReadinessKind.Jump;
                case "CROUCH": return ProductionReadinessKind.Crouch;
                case "STOP": return ProductionReadinessKind.Stop;
                default: return ProductionReadinessKind.None;
            }
        }

        private static ProductionHandSignal ParseSignal(string value,
            bool hasPoints)
        {
            switch (value)
            {
                case "ok": return ProductionHandSignal.Ok;
                case "tracking_lost": return ProductionHandSignal.TrackingLost;
                case "low_quality": return ProductionHandSignal.LowQuality;
                case "out_of_frame": return ProductionHandSignal.OutOfFrame;
                default: return hasPoints
                    ? ProductionHandSignal.Ok : ProductionHandSignal.TrackingLost;
            }
        }
    }
}
