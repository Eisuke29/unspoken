using System;
using System.Collections.Generic;

namespace Before.App
{
    public enum BeforeStoryState
    {
        Boot,
        CameraUnavailable,
        AwaitHand,
        ReconnectCharge,
        WakeNul,
        RunToJumpTeach,
        JumpTeachSample1,
        JumpTeachSample2,
        JumpTeachExecute,
        JumpProofRun,        // Jump proof A
        JumpProofWait,
        RunToStopTeach,
        StopTeachSample1,
        StopTeachSample2,
        StopTeachExecute,
        StopProofARun,
        StopProofAWait,
        JumpProofBRun,
        JumpProofBWait,
        StopProofBRun,
        StopProofBWait,
        RunToDirectionTeach,
        DirectionTeachMap,
        DirectionTeachExecute,
        RunToFinalFork,
        FinalPlanMap,
        FinalCourseExecute,
        FinalRewind,
        CoreArrival,
        CoreTransfer,
        Farewell,
        CompleteHold
    }

    public enum StoryEvent
    {
        CameraAvailable,
        CameraLost,
        HandStable,
        ChargeComplete,
        WakeComplete,
        ReachedAnchor,
        SampleAccepted,
        ClassClosed,
        ActionExecuted,
        ProofRevealed,
        DirectionChosen,
        BranchTraversed,
        PlanCommitted,
        CourseSucceeded,
        CourseFailed,
        RewindComplete,
        CoreReached,
        TransferComplete,
        FarewellComplete,
        FullReset
    }

    public enum CommandIntakeMode
    {
        None,
        TeachJump,
        TeachStop,
        LiveSymbolic,
        Direction,
        FinalPlan
    }

    /// <summary>
    /// Single authoritative story state machine. Transitions name their event and target;
    /// there are no generic Next() calls and action-gated waits have no timeout.
    /// </summary>
    public sealed class BeforeStoryMachine
    {
        public BeforeStoryState State { get; private set; } = BeforeStoryState.Boot;
        public event Action<BeforeStoryState, BeforeStoryState, StoryEvent> OnTransition;

        private static readonly Dictionary<(BeforeStoryState, StoryEvent), BeforeStoryState> Table =
            new Dictionary<(BeforeStoryState, StoryEvent), BeforeStoryState>
        {
            { (BeforeStoryState.Boot, StoryEvent.CameraAvailable), BeforeStoryState.AwaitHand },
            { (BeforeStoryState.Boot, StoryEvent.CameraLost), BeforeStoryState.CameraUnavailable },
            { (BeforeStoryState.CameraUnavailable, StoryEvent.CameraAvailable), BeforeStoryState.AwaitHand },
            { (BeforeStoryState.AwaitHand, StoryEvent.HandStable), BeforeStoryState.ReconnectCharge },
            { (BeforeStoryState.AwaitHand, StoryEvent.CameraLost), BeforeStoryState.CameraUnavailable },
            { (BeforeStoryState.ReconnectCharge, StoryEvent.ChargeComplete), BeforeStoryState.WakeNul },
            { (BeforeStoryState.WakeNul, StoryEvent.WakeComplete), BeforeStoryState.RunToJumpTeach },
            { (BeforeStoryState.RunToJumpTeach, StoryEvent.ReachedAnchor), BeforeStoryState.JumpTeachSample1 },
            { (BeforeStoryState.JumpTeachSample1, StoryEvent.SampleAccepted), BeforeStoryState.JumpTeachSample2 },
            { (BeforeStoryState.JumpTeachSample2, StoryEvent.ClassClosed), BeforeStoryState.JumpTeachExecute },
            { (BeforeStoryState.JumpTeachExecute, StoryEvent.ActionExecuted), BeforeStoryState.JumpProofRun },
            { (BeforeStoryState.JumpProofRun, StoryEvent.ProofRevealed), BeforeStoryState.JumpProofWait },
            { (BeforeStoryState.JumpProofWait, StoryEvent.ActionExecuted), BeforeStoryState.RunToStopTeach },
            { (BeforeStoryState.JumpProofWait, StoryEvent.CourseFailed), BeforeStoryState.FinalRewind },
            { (BeforeStoryState.RunToStopTeach, StoryEvent.ReachedAnchor), BeforeStoryState.StopTeachSample1 },
            { (BeforeStoryState.StopTeachSample1, StoryEvent.SampleAccepted), BeforeStoryState.StopTeachSample2 },
            { (BeforeStoryState.StopTeachSample2, StoryEvent.ClassClosed), BeforeStoryState.StopTeachExecute },
            { (BeforeStoryState.StopTeachExecute, StoryEvent.ActionExecuted), BeforeStoryState.StopProofARun },
            { (BeforeStoryState.StopProofARun, StoryEvent.ProofRevealed), BeforeStoryState.StopProofAWait },
            { (BeforeStoryState.StopProofAWait, StoryEvent.ActionExecuted), BeforeStoryState.JumpProofBRun },
            { (BeforeStoryState.StopProofAWait, StoryEvent.CourseFailed), BeforeStoryState.FinalRewind },
            { (BeforeStoryState.JumpProofBRun, StoryEvent.ProofRevealed), BeforeStoryState.JumpProofBWait },
            { (BeforeStoryState.JumpProofBWait, StoryEvent.ActionExecuted), BeforeStoryState.StopProofBRun },
            { (BeforeStoryState.JumpProofBWait, StoryEvent.CourseFailed), BeforeStoryState.FinalRewind },
            { (BeforeStoryState.StopProofBRun, StoryEvent.ProofRevealed), BeforeStoryState.StopProofBWait },
            { (BeforeStoryState.StopProofBWait, StoryEvent.ActionExecuted), BeforeStoryState.RunToDirectionTeach },
            { (BeforeStoryState.StopProofBWait, StoryEvent.CourseFailed), BeforeStoryState.FinalRewind },
            { (BeforeStoryState.RunToDirectionTeach, StoryEvent.ReachedAnchor), BeforeStoryState.DirectionTeachMap },
            { (BeforeStoryState.DirectionTeachMap, StoryEvent.DirectionChosen), BeforeStoryState.DirectionTeachExecute },
            { (BeforeStoryState.DirectionTeachExecute, StoryEvent.BranchTraversed), BeforeStoryState.RunToFinalFork },
            { (BeforeStoryState.RunToFinalFork, StoryEvent.ReachedAnchor), BeforeStoryState.FinalPlanMap },
            { (BeforeStoryState.FinalPlanMap, StoryEvent.PlanCommitted), BeforeStoryState.FinalCourseExecute },
            { (BeforeStoryState.FinalCourseExecute, StoryEvent.CourseSucceeded), BeforeStoryState.CoreArrival },
            { (BeforeStoryState.FinalCourseExecute, StoryEvent.CourseFailed), BeforeStoryState.FinalRewind },
            { (BeforeStoryState.FinalRewind, StoryEvent.RewindComplete), BeforeStoryState.FinalPlanMap },
            { (BeforeStoryState.CoreArrival, StoryEvent.CoreReached), BeforeStoryState.CoreTransfer },
            { (BeforeStoryState.CoreTransfer, StoryEvent.TransferComplete), BeforeStoryState.Farewell },
            { (BeforeStoryState.Farewell, StoryEvent.FarewellComplete), BeforeStoryState.CompleteHold },
        };

        /// <summary>Fires a named event. Returns true if a transition happened. FullReset works from every state.</summary>
        public bool Fire(StoryEvent evt)
        {
            if (evt == StoryEvent.FullReset)
            {
                var previous = State;
                State = BeforeStoryState.Boot;
                OnTransition?.Invoke(previous, State, evt);
                return true;
            }
            if (Table.TryGetValue((State, evt), out var next))
            {
                var previous = State;
                State = next;
                OnTransition?.Invoke(previous, State, evt);
                return true;
            }
            return false;
        }

        public CommandIntakeMode IntakeMode
        {
            get
            {
                switch (State)
                {
                    case BeforeStoryState.JumpTeachSample1:
                    case BeforeStoryState.JumpTeachSample2:
                        return CommandIntakeMode.TeachJump;
                    case BeforeStoryState.StopTeachSample1:
                    case BeforeStoryState.StopTeachSample2:
                        return CommandIntakeMode.TeachStop;
                    case BeforeStoryState.DirectionTeachMap:
                        return CommandIntakeMode.Direction;
                    case BeforeStoryState.FinalPlanMap:
                        return CommandIntakeMode.FinalPlan;
                    case BeforeStoryState.JumpTeachExecute:
                    case BeforeStoryState.JumpProofRun:
                    case BeforeStoryState.JumpProofWait:
                    case BeforeStoryState.StopTeachExecute:
                    case BeforeStoryState.StopProofARun:
                    case BeforeStoryState.StopProofAWait:
                    case BeforeStoryState.JumpProofBRun:
                    case BeforeStoryState.JumpProofBWait:
                    case BeforeStoryState.StopProofBRun:
                    case BeforeStoryState.StopProofBWait:
                    case BeforeStoryState.RunToStopTeach:
                    case BeforeStoryState.RunToDirectionTeach:
                    case BeforeStoryState.RunToFinalFork:
                    case BeforeStoryState.DirectionTeachExecute:
                    case BeforeStoryState.FinalCourseExecute:
                        return CommandIntakeMode.LiveSymbolic;
                    default:
                        return CommandIntakeMode.None;
                }
            }
        }

        /// <summary>Recovery anchor world-x per story beat (course-geometry.md).</summary>
        public static float SafeAnchorX(BeforeStoryState state, Config.BeforeCourseConfig course)
        {
            switch (state)
            {
                case BeforeStoryState.Boot:
                case BeforeStoryState.CameraUnavailable:
                case BeforeStoryState.AwaitHand:
                case BeforeStoryState.ReconnectCharge:
                case BeforeStoryState.WakeNul:
                    return course.xCollapsed;
                case BeforeStoryState.RunToJumpTeach:
                case BeforeStoryState.JumpTeachSample1:
                case BeforeStoryState.JumpTeachSample2:
                case BeforeStoryState.JumpTeachExecute:
                    return course.xJumpTeachAnchor;
                case BeforeStoryState.JumpProofRun:
                case BeforeStoryState.JumpProofWait:
                    return course.xProofAnchor;
                case BeforeStoryState.RunToStopTeach:
                case BeforeStoryState.StopTeachSample1:
                case BeforeStoryState.StopTeachSample2:
                case BeforeStoryState.StopTeachExecute:
                    return course.xStopAnchor;
                case BeforeStoryState.StopProofARun:
                case BeforeStoryState.StopProofAWait:
                    return course.xStopProofAAnchor;
                case BeforeStoryState.JumpProofBRun:
                case BeforeStoryState.JumpProofBWait:
                    return course.xJumpProofBAnchor;
                case BeforeStoryState.StopProofBRun:
                case BeforeStoryState.StopProofBWait:
                    return course.xStopProofBAnchor;
                case BeforeStoryState.RunToDirectionTeach:
                case BeforeStoryState.DirectionTeachMap:
                case BeforeStoryState.DirectionTeachExecute:
                    return course.xForkAnchor;
                case BeforeStoryState.RunToFinalFork:
                case BeforeStoryState.FinalPlanMap:
                case BeforeStoryState.FinalCourseExecute:
                case BeforeStoryState.FinalRewind:
                    return course.xFinalAnchor;
                default:
                    return course.xCoreApproach;
            }
        }

        /// <summary>All states, for transition-table completeness tests.</summary>
        public static IEnumerable<BeforeStoryState> AllStates()
        {
            return (BeforeStoryState[])Enum.GetValues(typeof(BeforeStoryState));
        }

        /// <summary>States that must have at least one outgoing transition (terminal hold excluded).</summary>
        public static bool HasOutgoing(BeforeStoryState state)
        {
            foreach (var key in Table.Keys)
            {
                if (key.Item1 == state) return true;
            }
            return false;
        }
    }
}
