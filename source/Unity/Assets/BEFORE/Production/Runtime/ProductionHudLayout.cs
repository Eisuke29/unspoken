using UnityEngine;

namespace Before.Production
{
    /// <summary>
    /// Shared, presentation-only mapping from the authored 320x180 HUD canvas
    /// into the integer-fitted Production content rectangle.
    /// </summary>
    public static class ProductionHudLayout
    {
        public const float LogicalWidth = ProductionLogicalRenderPresenter.LogicalWidth;
        public const float LogicalHeight = ProductionLogicalRenderPresenter.LogicalHeight;

        public static readonly Color VoidBlack = FromHex(0x05, 0x07, 0x0A);
        public static readonly Color Structural = FromHex(0x0D, 0x11, 0x17);
        public static readonly Color MidGray = FromHex(0x39, 0x41, 0x4C);
        public static readonly Color Ash = FromHex(0x7B, 0x85, 0x8F);
        public static readonly Color DirtyWhite = FromHex(0xD8, 0xDD, 0xD9);
        public static readonly Color Cyan = FromHex(0x00, 0xD9, 0xE8);
        public static readonly Color Amber = FromHex(0xF2, 0xB5, 0x44);
        public static readonly Color Magenta = FromHex(0xD8, 0x4A, 0x78);
        public static readonly Color Violet = FromHex(0xB2, 0x6C, 0xFF);
        public static readonly Color Fault = FromHex(0xFF, 0x3B, 0x4E);
        public static readonly Color HeartCoral = FromHex(0xFF, 0x5A, 0x62);

        public static Rect ContentRect(float screenWidth, float screenHeight)
            => ProductionViewportPresenter.ContentPixelRect(
                Mathf.RoundToInt(screenWidth), Mathf.RoundToInt(screenHeight));

        public static float Scale(float screenWidth, float screenHeight)
            => ContentRect(screenWidth, screenHeight).width / LogicalWidth;

        public static Rect LogicalRect(Rect logical, float screenWidth, float screenHeight)
        {
            Rect content = ContentRect(screenWidth, screenHeight);
            float scale = content.width / LogicalWidth;
            return new Rect(content.x + logical.x * scale,
                content.y + logical.y * scale,
                logical.width * scale, logical.height * scale);
        }

        public static Rect WorldRegionRect(float screenWidth, float screenHeight)
            => LogicalRect(new Rect(0f, 0f, 256f, LogicalHeight),
                screenWidth, screenHeight);

        public static Rect HandPanelRect(float screenWidth, float screenHeight)
            => LogicalRect(new Rect(264f, 36f, 48f, 64f),
                screenWidth, screenHeight);

        public static Rect HeartRowRect(float screenWidth, float screenHeight)
            => LogicalRect(new Rect(146f, 12f, 28f, 7f),
                screenWidth, screenHeight);

        public static Rect TeachingRect(float screenWidth, float screenHeight)
            => LogicalRect(new Rect(12f, 34f, 220f, 80f),
                screenWidth, screenHeight);

        public static Rect ForkRect(float screenWidth, float screenHeight)
            => LogicalRect(new Rect(36f, 38f, 176f, 102f),
                screenWidth, screenHeight);

        public static Rect CenterMessageRect(float screenWidth, float screenHeight)
            => LogicalRect(new Rect(28f, 72f, 200f, 30f),
                screenWidth, screenHeight);

        private static Color FromHex(byte r, byte g, byte b)
            => new Color32(r, g, b, 255);
    }
}
