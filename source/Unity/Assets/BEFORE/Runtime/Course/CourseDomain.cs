using System;
using System.Collections.Generic;
using Before.Config;
using UnityEngine;

namespace Before.Course
{
    /// <summary>Deterministic scan-gate phase cycle on the gameplay clock (course-geometry.md).</summary>
    public enum GatePhase
    {
        Closed,
        Opening,
        Open,
        Closing,
        ClosedHold
    }

    public sealed class ScanGate
    {
        private readonly BeforeCourseConfig cfg;
        public float BoundaryX { get; }
        public double PhaseOffset { get; set; } // subtracted from clock for checkpoint restore

        public ScanGate(BeforeCourseConfig config, float boundaryX)
        {
            cfg = config;
            BoundaryX = boundaryX;
        }

        public float CycleLength => cfg.gateClosed + cfg.gateOpening + cfg.gateOpen + cfg.gateClosing + cfg.gateClosedHold;

        public GatePhase PhaseAt(double gameplayTime)
        {
            double t = (gameplayTime - PhaseOffset) % CycleLength;
            if (t < 0) t += CycleLength;
            if (t < cfg.gateClosed) return GatePhase.Closed;
            t -= cfg.gateClosed;
            if (t < cfg.gateOpening) return GatePhase.Opening;
            t -= cfg.gateOpening;
            if (t < cfg.gateOpen) return GatePhase.Open;
            t -= cfg.gateOpen;
            if (t < cfg.gateClosing) return GatePhase.Closing;
            return GatePhase.ClosedHold;
        }

        public bool BeamActive(double gameplayTime)
        {
            return PhaseAt(gameplayTime) != GatePhase.Open;
        }

        /// <summary>Seconds of remaining fully-open time at the given moment (0 while not open).</summary>
        public float RemainingOpen(double gameplayTime)
        {
            double t = (gameplayTime - PhaseOffset) % CycleLength;
            if (t < 0) t += CycleLength;
            double openStart = cfg.gateClosed + cfg.gateOpening;
            double openEnd = openStart + cfg.gateOpen;
            if (t >= openStart && t < openEnd) return (float)(openEnd - t);
            return 0f;
        }

        /// <summary>Seconds until the next fully-open window starts (0 while already open).</summary>
        public float TimeUntilOpen(double gameplayTime)
        {
            double t = (gameplayTime - PhaseOffset) % CycleLength;
            if (t < 0) t += CycleLength;
            double openStart = cfg.gateClosed + cfg.gateOpening;
            double openEnd = openStart + cfg.gateOpen;
            if (t >= openStart && t < openEnd) return 0f;
            if (t < openStart) return (float)(openStart - t);
            return (float)(CycleLength - t + openStart);
        }
    }

    /// <summary>Authored gap with edges and landing target.</summary>
    public sealed class GapHazard
    {
        public float NearEdge;
        public float FarEdge;
        public float LandingTarget;
        public string Id;
    }

    /// <summary>Route selection for the final branch.</summary>
    public enum FinalRoute
    {
        None,
        Left,   // scan gate then gap
        Right   // gap then scan gate
    }

    /// <summary>
    /// Authored one-scene course model: platform ground segments, gaps, gates.
    /// Ground top is world y=0 everywhere a platform exists.
    /// </summary>
    public sealed class CourseModel
    {
        public readonly List<GapHazard> Gaps = new List<GapHazard>();
        public readonly List<ScanGate> Gates = new List<ScanGate>();
        public FinalRoute SelectedRoute { get; private set; } = FinalRoute.None;
        private readonly BeforeCourseConfig cfg;
        private GapHazard branchGap;
        private ScanGate branchGate;

        public CourseModel(BeforeCourseConfig config)
        {
            cfg = config;
            // D-096 reordered course: 3 gaps (Jump teach, Jump proof A, Jump proof B) + 3 gates
            // (Stop teach, Stop proof A, Stop proof B). Final-fork branch hazards are added on route select.
            Gaps.Add(new GapHazard { Id = "teach", NearEdge = cfg.xTeachGapNear, FarEdge = cfg.xTeachGapFar, LandingTarget = cfg.xTeachLanding });
            Gaps.Add(new GapHazard { Id = "proofA", NearEdge = cfg.xProofGapNear, FarEdge = cfg.xProofGapFar, LandingTarget = cfg.xProofLanding });
            Gaps.Add(new GapHazard { Id = "proofB", NearEdge = cfg.xJumpProofBGapNear, FarEdge = cfg.xJumpProofBGapFar, LandingTarget = cfg.xJumpProofBLanding });
            Gates.Add(new ScanGate(config, cfg.xGateBoundary));       // Stop teach
            Gates.Add(new ScanGate(config, cfg.xStopProofAGate));     // Stop proof A
            Gates.Add(new ScanGate(config, cfg.xStopProofBGate));     // Stop proof B
        }

        public void SelectRoute(FinalRoute route)
        {
            SelectedRoute = route;
            if (branchGap != null) { Gaps.Remove(branchGap); branchGap = null; }
            if (branchGate != null) { Gates.Remove(branchGate); branchGate = null; }
            if (route == FinalRoute.None) return;

            float gateX = route == FinalRoute.Left ? cfg.xBranchHazard1 : cfg.xBranchHazard2;
            float gapCenter = route == FinalRoute.Left ? cfg.xBranchHazard2 : cfg.xBranchHazard1;
            float half = cfg.finalGapWidth * 0.5f;
            branchGap = new GapHazard
            {
                Id = "branch",
                NearEdge = gapCenter - half,
                FarEdge = gapCenter + half,
                LandingTarget = gapCenter + half + cfg.landingMargin + 0.13f
            };
            branchGate = new ScanGate(cfg, gateX);
            Gaps.Add(branchGap);
            Gates.Add(branchGate);
        }

        /// <summary>True when a platform exists under the given x (before the branch, branch hazards excluded until selected).</summary>
        public bool HasGroundAt(float x)
        {
            foreach (var gap in Gaps)
            {
                if (x > gap.NearEdge && x < gap.FarEdge) return false;
            }
            return true;
        }

        public GapHazard GapAhead(float x)
        {
            GapHazard best = null;
            foreach (var gap in Gaps)
            {
                if (gap.FarEdge < x) continue;
                if (best == null || gap.NearEdge < best.NearEdge) best = gap;
            }
            return best;
        }

        public ScanGate GateAt(float x, float tolerance)
        {
            foreach (var gate in Gates)
            {
                if (Mathf.Abs(gate.BoundaryX - x) <= tolerance) return gate;
            }
            return null;
        }
    }

    public enum MotorState
    {
        Collapsed,
        Idle,
        Accelerating,
        Running,
        Braking,
        Stopped,
        JumpCharge,
        Airborne,
        Landing,
        Suspended
    }

    /// <summary>
    /// Deterministic fixed-step (60 Hz) motor. Horizontal autonomy is code-owned; vertical motion uses
    /// the three-phase game-like jump from course-geometry.md. No user steering force exists.
    /// </summary>
    public sealed class NulMotor2D
    {
        public const float FixedStep = 1f / 60f;

        private readonly BeforeCourseConfig cfg;
        private readonly CourseModel course;

        public Vector2 Position { get; private set; }
        public Vector2 Velocity { get; private set; }
        public MotorState State { get; private set; } = MotorState.Collapsed;
        public bool Grounded { get; private set; } = true;
        public float RunTarget { get; private set; }
        public event Action OnTakeoff;
        public event Action OnLanded;

        private float brakeTargetX = float.MaxValue;
        private float resumeBrakeTarget = float.MaxValue;
        private float takeoffX = float.MaxValue;
        private bool jumpQueuedImmediate;
        private float chargeRemaining;
        private double stopHoldRemaining;
        private bool resumeAfterHold;

        public NulMotor2D(BeforeCourseConfig config, CourseModel model)
        {
            cfg = config;
            course = model;
            Position = new Vector2(cfg.xCollapsed, 0f);
        }

        public void SetCollapsed(bool collapsed)
        {
            State = collapsed ? MotorState.Collapsed : MotorState.Idle;
            Velocity = Vector2.zero;
        }

        public void Suspend()
        {
            if (State != MotorState.Collapsed) State = MotorState.Suspended;
        }

        public void ResumeFromSuspend()
        {
            if (State == MotorState.Suspended) State = Velocity.x > 0.05f ? MotorState.Running : MotorState.Stopped;
        }

        public void BeginRun()
        {
            if (State == MotorState.Collapsed || State == MotorState.Airborne) return;
            State = MotorState.Accelerating;
            brakeTargetX = float.MaxValue;
        }

        /// <summary>Brake so the motor stops at or before targetX. Braking begins only when required,
        /// so a distant target lets NUL keep running until its stopping distance is reached.</summary>
        public void BrakeTo(float targetX)
        {
            brakeTargetX = targetX;
        }

        public float BrakeTargetX => brakeTargetX;

        /// <summary>Immediate literal stop: brake now, hold, then resume toward the prior brake target.</summary>
        public void LiteralStop()
        {
            resumeBrakeTarget = brakeTargetX;
            brakeTargetX = Position.x + StopDistance(Velocity.x);
            if (State == MotorState.Running || State == MotorState.Accelerating) State = MotorState.Braking;
            stopHoldRemaining = cfg.literalStopHold;
            resumeAfterHold = true;
        }

        public void HoldAtStop()
        {
            resumeAfterHold = false;
            stopHoldRemaining = 0;
        }

        /// <summary>Planner-chosen takeoff point; motor charges 0.12 s before reaching it.</summary>
        public void JumpAt(float plannedTakeoffX)
        {
            takeoffX = plannedTakeoffX;
            jumpQueuedImmediate = false;
        }

        /// <summary>Literal early jump: full jump on the next grounded step.</summary>
        public void JumpImmediate()
        {
            jumpQueuedImmediate = true;
        }

        public float StopDistance(float speed)
        {
            return speed * speed / (2f * cfg.brakeAcceleration);
        }

        private bool NeedsBrakingNow(float speed)
        {
            return brakeTargetX < float.MaxValue
                && Position.x + StopDistance(speed) + speed * FixedStep >= brakeTargetX;
        }

        /// <summary>Airtime and horizontal travel of the standard jump at the given speed (numeric integration).</summary>
        public (float airtime, float travel, float height) PredictJump(float speed)
        {
            float vy = cfg.jumpVelocity;
            float y = 0f, t = 0f, maxY = 0f;
            while (t < 5f)
            {
                float g = vy > cfg.apexBand ? cfg.riseGravity : (vy < -cfg.apexBand ? cfg.fallGravity : cfg.apexGravity);
                vy -= g * FixedStep;
                if (vy < -cfg.maxFallSpeed) vy = -cfg.maxFallSpeed;
                y += vy * FixedStep;
                t += FixedStep;
                if (y > maxY) maxY = y;
                if (y <= 0f && vy < 0f) break;
            }
            return (t, speed * t, maxY);
        }

        /// <summary>Planner: takeoff x for a gap such that landing clears the far edge by the margin and support remains.</summary>
        public float SolveTakeoffX(GapHazard gap, float speed)
        {
            var (_, travel, _) = PredictJump(speed);
            float desired = gap.LandingTarget + 0.0f; // land collider center at target
            float takeoff = desired - travel;
            float latest = gap.NearEdge - cfg.takeoffSupport;
            return Mathf.Min(takeoff, latest);
        }

        /// <summary>Advance one fixed step. Returns true while active.</summary>
        public void Step()
        {
            if (State == MotorState.Collapsed || State == MotorState.Suspended) return;

            Vector2 velocity = Velocity;
            switch (State)
            {
                case MotorState.Idle:
                case MotorState.Stopped:
                    velocity.x = 0f;
                    if (stopHoldRemaining > 0)
                    {
                        stopHoldRemaining -= FixedStep;
                        if (stopHoldRemaining <= 0 && resumeAfterHold)
                        {
                            resumeAfterHold = false;
                            brakeTargetX = resumeBrakeTarget;
                            resumeBrakeTarget = float.MaxValue;
                            State = MotorState.Accelerating;
                        }
                    }
                    break;

                case MotorState.Accelerating:
                    velocity.x = Mathf.MoveTowards(velocity.x, cfg.runSpeed, cfg.runAcceleration * FixedStep);
                    if (Mathf.Abs(velocity.x - cfg.runSpeed) < 0.01f) State = MotorState.Running;
                    if (NeedsBrakingNow(velocity.x)) State = MotorState.Braking;
                    break;

                case MotorState.Running:
                    velocity.x = cfg.runSpeed;
                    if (NeedsBrakingNow(velocity.x)) State = MotorState.Braking;
                    break;

                case MotorState.Braking:
                {
                    velocity.x = Mathf.MoveTowards(velocity.x, 0f, cfg.brakeAcceleration * FixedStep);
                    if (velocity.x <= 0.01f)
                    {
                        velocity.x = 0f;
                        State = MotorState.Stopped;
                    }
                    break;
                }

                case MotorState.JumpCharge:
                    chargeRemaining -= FixedStep;
                    velocity.x = cfg.runSpeed;
                    if (chargeRemaining <= 0f)
                    {
                        velocity.y = cfg.jumpVelocity;
                        State = MotorState.Airborne;
                        Grounded = false;
                        OnTakeoff?.Invoke();
                    }
                    break;

                case MotorState.Airborne:
                {
                    float g = velocity.y > cfg.apexBand ? cfg.riseGravity
                        : (velocity.y < -cfg.apexBand ? cfg.fallGravity : cfg.apexGravity);
                    velocity.y -= g * FixedStep;
                    if (velocity.y < -cfg.maxFallSpeed) velocity.y = -cfg.maxFallSpeed;
                    break;
                }
            }

            // takeoff trigger
            if ((State == MotorState.Running || State == MotorState.Accelerating || State == MotorState.Braking)
                && Grounded)
            {
                float chargeDistance = velocity.x * cfg.chargeSeconds;
                if (jumpQueuedImmediate)
                {
                    jumpQueuedImmediate = false;
                    takeoffX = float.MaxValue; // an immediate jump supersedes any planned takeoff
                    State = MotorState.JumpCharge;
                    chargeRemaining = cfg.chargeSeconds;
                }
                else if (takeoffX < float.MaxValue && Position.x + chargeDistance >= takeoffX)
                {
                    takeoffX = float.MaxValue;
                    State = MotorState.JumpCharge;
                    chargeRemaining = cfg.chargeSeconds;
                }
            }
            else if (jumpQueuedImmediate && Grounded && (State == MotorState.Stopped || State == MotorState.Idle))
            {
                jumpQueuedImmediate = false;
                State = MotorState.JumpCharge;
                chargeRemaining = cfg.chargeSeconds;
            }

            Vector2 next = Position + velocity * FixedStep;

            // brake target clamp: never pass the target while braking
            if (State == MotorState.Braking && brakeTargetX < float.MaxValue && next.x > brakeTargetX)
            {
                next.x = brakeTargetX;
                velocity.x = 0f;
                State = MotorState.Stopped;
            }

            // grounding
            if (State == MotorState.Airborne)
            {
                if (next.y <= 0f && velocity.y < 0f && course.HasGroundAt(next.x))
                {
                    next.y = 0f;
                    velocity.y = 0f;
                    Grounded = true;
                    State = MotorState.Landing;
                    OnLanded?.Invoke();
                }
            }
            else if (Grounded && !course.HasGroundAt(next.x) && next.y <= 0.001f)
            {
                // walked off an edge without a jump: begin falling
                Grounded = false;
                State = MotorState.Airborne;
            }

            if (State == MotorState.Landing)
            {
                State = velocity.x > 0.05f ? MotorState.Running : MotorState.Stopped;
            }

            Position = next;
            Velocity = velocity;
        }

        public void Teleport(Vector2 position, Vector2 velocity, MotorState state, bool grounded)
        {
            Position = position;
            Velocity = velocity;
            State = state;
            Grounded = grounded;
            takeoffX = float.MaxValue;
            jumpQueuedImmediate = false;
            brakeTargetX = float.MaxValue;
            resumeBrakeTarget = float.MaxValue;
            stopHoldRemaining = 0;
            resumeAfterHold = false;
        }
    }

    /// <summary>Snapshot for checkpoint rewind: position, velocity, hazard phase, story substate.</summary>
    public sealed class CheckpointSnapshot
    {
        public Vector2 Position;
        public Vector2 Velocity;
        public MotorState State;
        public bool Grounded;
        public double GatePhaseOffsetMain;
        public double GatePhaseOffsetBranch;
        public FinalRoute Route;
    }

    public sealed class CheckpointController
    {
        public CheckpointSnapshot Snapshot { get; private set; }

        public void Capture(NulMotor2D motor, CourseModel course)
        {
            Snapshot = new CheckpointSnapshot
            {
                Position = motor.Position,
                Velocity = motor.Velocity,
                State = motor.State,
                Grounded = motor.Grounded,
                Route = course.SelectedRoute,
                GatePhaseOffsetMain = course.Gates.Count > 0 ? course.Gates[0].PhaseOffset : 0,
                GatePhaseOffsetBranch = course.Gates.Count > 1 ? course.Gates[1].PhaseOffset : 0
            };
        }

        public void Restore(NulMotor2D motor, CourseModel course)
        {
            if (Snapshot == null) return;
            motor.Teleport(Snapshot.Position, Snapshot.Velocity, Snapshot.State, Snapshot.Grounded);
            if (course.Gates.Count > 0) course.Gates[0].PhaseOffset = Snapshot.GatePhaseOffsetMain;
            if (course.Gates.Count > 1) course.Gates[1].PhaseOffset = Snapshot.GatePhaseOffsetBranch;
        }
    }
}
