using UnityEngine;

namespace Before.Presentation
{
    /// <summary>
    /// D-089 locked HUD layout in 320x180 logical pixels. Single source of truth for the presenter and
    /// the screen-coordinate acceptance tests, so every rect is verified to sit inside the logical frame
    /// and not overlap. Energy geometry lives here too (start=palm, end=NUL core) with a t=0/t=1 contract.
    /// </summary>
    public static class BeforeLayout
    {
        // --- system text (two lines, DotGothic16) ---
        public static readonly Rect SystemText = new Rect(12, 10, 220, 22);
        public const int SystemTextFontPx = 5;
        public const int SystemTextLineStep = 7;
        public const int SystemTextRow1Y = 10;
        public const int SystemTextRow2Y = 17; // row1Y + lineStep
        public const int SystemTextMaxRightX = 232; // must not exceed

        // --- hand panel (landmark skeleton only, D-088) ---
        public const int PanelX = 264;
        public const int PanelY = 36;
        public const int PanelW = 48;
        public const int PanelH = 64;
        public const int PanelSafeMargin = 4;
        public static Rect Panel => new Rect(PanelX, PanelY, PanelW, PanelH);

        // --- intent queue (left of the hand panel) ---
        public const int QueueX = 248;
        public const int QueueSlotSize = 8;
        public const int QueueNextMaxSize = 10;
        // index 0 = bottom = NEXT
        public static readonly int[] QueueSlotCenterY = { 88, 68, 48 };

        // --- NUL core (camera keeps NUL at a fixed logical screen position, D-071) ---
        public static readonly Vector2 NulCore = new Vector2(96, 128);

        // --- energy (D-093 re-emphasis; D-090 privacy/loss/low-speed policy kept, density ~10x) ---
        public const float EnergyLiftLogical = 8f;   // upward bow of the connection curve
        public const int EnergyCurveSegments = 16;   // >= 12 continuous segments
        public const float EnergyTravelSeconds = 2.2f;        // particle travel time (palm -> core), unchanged
        public const float EnergyNormalRate = 12f;            // particles / second when connected
        public const float EnergyActiveRate = 20f;            // particles / second during teach/armed/tracking
        public const int EnergyNormalCap = 28;                // concurrent cap, normal
        public const int EnergyActiveCap = 44;                // concurrent cap, active
        public const int EnergyPoolSize = 52;                 // fixed pool: 44 + transition headroom
        public const float EnergyNormalAlpha = 0.38f;         // particle alpha, normal
        public const float EnergyActiveAlpha = 0.50f;         // particle alpha, active
        public const float EnergyNormalBaseAlpha = 0.06f;     // thin base curve alpha, normal
        public const float EnergyActiveBaseAlpha = 0.14f;     // thin base curve alpha, active
        public const float EnergyTrackingBrightness = 0.12f;  // temporary additive brightness in MotionTracking
        public const float EnergyCurveSpreadLogical = 2f;     // +/- logical px spread across near-curves
        public const int EnergyBigEvery = 9;                  // D-096: rare 3x3 particle; base is 2x2
        public const float EnergyFadeSeconds = 0.6f;          // fade particles+base on hand loss
        public const float EnergyReconnectRampSeconds = 0.5f; // ramp back to normal rate on return
        public const float EnergySuccessPulseSeconds = 0.5f;  // single core pulse on recognition/trigger
        public const float EnergySuccessPulseScale = 1.35f;   // <= 1.35x

        /// <summary>Quadratic bow control point (logical) between palm start and NUL core end.</summary>
        public static Vector2 EnergyControl(Vector2 start, Vector2 end)
        {
            return Vector2.Lerp(start, end, 0.5f) + new Vector2(0f, -EnergyLiftLogical);
        }

        /// <summary>Point on the energy curve; guarantees Point(...,0)==start and Point(...,1)==end.</summary>
        public static Vector2 EnergyPoint(Vector2 start, Vector2 control, Vector2 end, float t)
        {
            Vector2 a = Vector2.Lerp(start, control, t);
            Vector2 b = Vector2.Lerp(control, end, t);
            return Vector2.Lerp(a, b, t);
        }

        /// <summary>Displayed palm-center in logical coordinates from mirrored-normalized panel coords.</summary>
        public static Vector2 PalmLogical(Vector2 mirroredNormalized)
        {
            return new Vector2(PanelX + mirroredNormalized.x * PanelW, PanelY + (1f - mirroredNormalized.y) * PanelH);
        }

        // --- F1 diagnostic panel ---
        public static readonly Rect DebugPanel = new Rect(8, 74, 232, 96);
        public const int DebugFontPx = 4;
        public const int DebugLineStep = 6;
        public const int DebugMaxRightX = 240;
        public const int DebugMaxBottomY = 170;
    }
}
