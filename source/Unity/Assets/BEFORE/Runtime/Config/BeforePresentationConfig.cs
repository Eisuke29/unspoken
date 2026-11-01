using UnityEngine;

namespace Before.Config
{
    /// <summary>Presentation timings/layout. Initial values fixed by narrative-presentation.md and visual-design.md.</summary>
    public sealed class BeforePresentationConfig : ScriptableObject
    {
        public Color voidBlack = FromHex(0x050608);
        public Color charcoal = FromHex(0x15181C);
        public Color midGray = FromHex(0x555B63);
        public Color ashGray = FromHex(0xAEB4BA);
        public Color dirtyWhite = FromHex(0xE5E7E8);
        public Color signalCyan = FromHex(0x00E5FF);
        public Color waitAmber = FromHex(0xFFB000);
        public Color directionViolet = FromHex(0xB26CFF);
        public Color faultRed = FromHex(0xFF3B4E);
        public float textScanWipeSeconds = 0.12f;
        public float oneShotHold = 0.8f;
        public float oneShotFade = 0.25f;
        public float mapExpandSeconds = 0.8f;
        public float mapCloseSeconds = 0.6f;
        public float routeTraceSeconds = 0.55f;
        public float planReviewSeconds = 2.0f;
        public float resetHoldSeconds = 2.0f;
        public int energyParticlesPerSecond = 14;
        public int energyParticlesTeaching = 28;
        public int panelWidth = 72;
        public int panelHeight = 96;
        public int panelSafeMargin = 6;

        public static Color FromHex(int rgb)
        {
            return new Color(
                ((rgb >> 16) & 0xFF) / 255f,
                ((rgb >> 8) & 0xFF) / 255f,
                (rgb & 0xFF) / 255f,
                1f);
        }
    }
}
