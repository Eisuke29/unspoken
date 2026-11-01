using Before.App;
using Before.Course;
using UnityEngine;

namespace Before.Presentation
{
    /// <summary>
    /// Sprite-frame selection for NUL from actual motor/story/recovery state (never sprite-driven motion).
    /// Sheet layout per art-production.md: 8 columns x 11 rows, row 0 = IdleEnergy at the bottom.
    /// </summary>
    public sealed class NulView
    {
        private readonly Sprite[][] rows; // [row][frame]
        public readonly GameObject Root;
        private readonly SpriteRenderer renderer;
        private double animStart;
        private int lastRow = -1;

        private static readonly int[] FrameCounts = { 4, 8, 4, 6, 3, 6, 4, 6, 8, 8, 8 };
        private static readonly float[] Fps = { 6, 12, 10, 12, 12, 8, 8, 8, 10, 10, 8 };
        private static readonly bool[] Loop = { true, true, false, false, false, true, false, false, false, false, false };

        public NulView(Texture2D sheet, Transform parent)
        {
            Root = new GameObject("NUL");
            Root.transform.SetParent(parent, false);
            renderer = Root.AddComponent<SpriteRenderer>();
            renderer.sortingOrder = 10;
            rows = new Sprite[11][];
            for (int row = 0; row < 11; row++)
            {
                rows[row] = new Sprite[FrameCounts[row]];
                for (int f = 0; f < FrameCounts[row]; f++)
                {
                    // sheet rows are stored bottom-up with row 0 at top block index 10
                    var rect = new Rect(f * 48, (10 - row) * 48, 48, 48);
                    rows[row][f] = Sprite.Create(sheet, rect, new Vector2(0.5f, 4f / 48f), 16f, 0, SpriteMeshType.FullRect);
                }
            }
        }

        public void UpdateFrom(BeforeDirector director, double realNow)
        {
            var motor = director.Motor;
            int row = SelectRow(director);
            if (row != lastRow)
            {
                lastRow = row;
                animStart = realNow;
            }
            float fps = Fps[row];
            if (row == 1)
            {
                fps = 12f * Mathf.Clamp(motor.Velocity.x / director.CourseCfg.runSpeed, 0.4f, 1.2f);
            }
            int count = FrameCounts[row];
            int frame = (int)((realNow - animStart) * fps);
            frame = Loop[row] ? frame % count : Mathf.Min(frame, count - 1);
            renderer.sprite = rows[row][frame];

            // integer pixel snapping at 16 ppu
            Vector2 p = motor.Position;
            Root.transform.position = new Vector3(Mathf.Round(p.x * 16f) / 16f, Mathf.Round(p.y * 16f) / 16f, 0f);
        }

        private int SelectRow(BeforeDirector d)
        {
            if (d.Recovery == TrackingRecoveryState.Collapsed) return 8;
            if (d.Recovery == TrackingRecoveryState.Recharging) return 9;
            switch (d.Story.State)
            {
                case BeforeStoryState.Boot:
                case BeforeStoryState.CameraUnavailable:
                case BeforeStoryState.AwaitHand:
                case BeforeStoryState.ReconnectCharge:
                    return 8; // collapsed silhouette
                case BeforeStoryState.WakeNul:
                    return 9;
                case BeforeStoryState.Farewell:
                    return 10;
            }
            switch (d.Motor.State)
            {
                case MotorState.Collapsed: return 8;
                case MotorState.Accelerating:
                case MotorState.Running: return 1;
                case MotorState.Braking: return 2;
                case MotorState.JumpCharge:
                case MotorState.Airborne: return 3;
                case MotorState.Landing: return 4;
                case MotorState.Stopped:
                case MotorState.Idle:
                case MotorState.Suspended:
                default:
                    // waiting at an anchor: attention loop
                    return d.Story.IntakeMode == Before.App.CommandIntakeMode.None ? 0 : 5;
            }
        }
    }
}
