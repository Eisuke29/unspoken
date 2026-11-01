using UnityEngine;

namespace Before.Config
{
    /// <summary>Course/game-feel values. Initial values fixed by course-geometry.md.</summary>
    public sealed class BeforeCourseConfig : ScriptableObject
    {
        public float runSpeed = 3.2f;
        public float runAcceleration = 12f;
        public float brakeAcceleration = 9f;
        public float jumpVelocity = 8.4f;
        public float riseGravity = 22f;
        public float apexGravity = 14f;
        public float fallGravity = 28f;
        public float apexBand = 1.2f;
        public float maxFallSpeed = 12f;
        public float chargeSeconds = 0.12f;
        public float landingMargin = 0.32f;
        public float takeoffSupport = 0.20f;
        public float literalStopHold = 0.75f;
        public float perceptionHorizonSeconds = 1.80f;
        public float lateCutoffSeconds = 0.65f;
        public float gateClosed = 1.20f;
        public float gateOpening = 0.30f;
        public float gateOpen = 1.50f;
        public float gateClosing = 0.30f;
        public float gateClosedHold = 0.30f;
        public float gatePassageWidth = 1.60f;
        public float gateEntryMargin = 0.25f;
        public float gapFailY = -2.0f;
        public float faultFreezeSeconds = 0.20f;
        public float rewindSeconds = 1.0f;
        // Section coordinates (world x, units) — D-096 reordered course:
        // Jump teach -> Jump proof A -> Stop teach -> Stop proof A -> Jump proof B -> Stop proof B ->
        // Direction teach -> Final fork -> CORE. ~24 units (~7.5 s at runSpeed 3.2) between action points;
        // measured 6-8 s wins over exact x. perception 1.80 / late cutoff 0.65 unchanged.
        public float xCollapsed = 0.0f;
        public float xRunBegins = 1.0f;
        // Jump teach (gap 11.0/12.45)
        public float xJumpTeachAnchor = 8.5f;
        public float xTeachGapNear = 11.0f;
        public float xTeachGapFar = 12.45f;
        public float xTeachLanding = 12.90f;
        // Jump proof A (gap 35.0/36.30)
        public float xProofCue = 30.0f;
        public float xProofAnchor = 32.0f;
        public float xProofGapNear = 35.0f;
        public float xProofGapFar = 36.30f;
        public float xProofLanding = 36.75f;
        // Stop teach (gate 59.2)
        public float xStopAnchor = 56.5f;
        public float xGateBoundary = 59.2f;
        public float xGateExit = 60.8f;
        // Stop proof A (gate 83.2)
        public float xStopProofACue = 78.5f;
        public float xStopProofAAnchor = 81.0f;
        public float xStopProofAGate = 83.2f;
        public float xStopProofAExit = 84.8f;
        // Jump proof B (gap 107.0/108.40)
        public float xJumpProofBCue = 102.0f;
        public float xJumpProofBAnchor = 104.0f;
        public float xJumpProofBGapNear = 107.0f;
        public float xJumpProofBGapFar = 108.40f;
        public float xJumpProofBLanding = 108.85f;
        // Stop proof B (gate 132.2)
        public float xStopProofBCue = 127.5f;
        public float xStopProofBAnchor = 130.0f;
        public float xStopProofBGate = 132.2f;
        public float xStopProofBExit = 133.8f;
        // Direction teach (anchor 159.0)
        public float xForkAnchor = 159.0f;
        public float xForkRejoin = 166.0f;
        // Final fork (anchor 184.0) + branch + CORE (210.0)
        public float xFinalAnchor = 184.0f;
        public float xBranchOrigin = 186.0f;
        public float xBranchHazard1 = 192.0f;
        public float xBranchHazard2 = 200.0f;
        public float xCoreApproach = 206.0f;
        public float xCoreChamber = 210.0f;
        public float finalGapWidth = 1.55f;
        public float teachGapWidth = 1.45f;
        public float proofGapWidth = 1.30f;
    }
}
