using System.Reflection;
using Before.Capture;
using Before.Config;
using Before.Presentation;
using NUnit.Framework;
using UnityEngine;

namespace Before.Tests
{
    /// <summary>
    /// Regression tests for the D-088 / supervised-fix contract: integer-scale Fit (Fix B),
    /// exact camera-name match (Fix C), the landmark-only hand panel (mirroring, palm center,
    /// in-camera position preservation, safe border), and the absence of any camera-RGB surface.
    /// </summary>
    public sealed class BeforeViewportTests
    {
        [Test]
        public void IntegerScale_At1920x1080_IsExactlySix()
        {
            Assert.AreEqual(6, LogicalViewport.IntegerScale(1920, 1080));
            var v = LogicalViewport.Viewport(1920, 1080);
            Assert.AreEqual(1920f, v.width, 0.001f);
            Assert.AreEqual(1080f, v.height, 0.001f);
            Assert.AreEqual(0f, v.x, 0.001f);
            Assert.AreEqual(0f, v.y, 0.001f);
        }

        [Test]
        public void Fit_NonSixteenNine_1918x946_UsesLargestContainingIntegerScale()
        {
            // min(1918/320=5.99, 946/180=5.25) -> floor 5
            Assert.AreEqual(5, LogicalViewport.IntegerScale(1918, 946));
            Assert.IsTrue(LogicalViewport.FullyContained(1918, 946), "320x180 must fit fully at 1918x946");
            var v = LogicalViewport.Viewport(1918, 946);
            Assert.AreEqual(1600f, v.width, 0.001f);  // 320*5
            Assert.AreEqual(900f, v.height, 0.001f);  // 180*5
            Assert.GreaterOrEqual(v.x, 0f);
            Assert.GreaterOrEqual(v.y, 0f);
        }

        [Test]
        public void Fit_ManyResolutions_NeverClipAndNeverFractional()
        {
            int[,] cases = { {1920,1080}, {1918,946}, {1280,720}, {1600,900}, {1024,768}, {800,600}, {2560,1440}, {640,480} };
            for (int i = 0; i < cases.GetLength(0); i++)
            {
                int w = cases[i, 0], h = cases[i, 1];
                int s = LogicalViewport.IntegerScale(w, h);
                Assert.GreaterOrEqual(s, 1, $"scale must be >=1 at {w}x{h}");
                Assert.AreEqual(s, Mathf.Round(s), $"scale must be integer at {w}x{h}");
                Assert.IsTrue(LogicalViewport.FullyContained(w, h), $"frame must fully fit at {w}x{h}");
            }
        }

        [Test]
        public void Mirror_InvertsXOnly()
        {
            var m = LogicalViewport.Mirror(new Vector2(0.2f, 0.7f));
            Assert.AreEqual(0.8f, m.x, 1e-5f);
            Assert.AreEqual(0.7f, m.y, 1e-5f);
        }

        [Test]
        public void PalmCenter_IsMeanOfLandmarks_0_5_9_13_17()
        {
            var lms = new Vector2[21];
            for (int i = 0; i < 21; i++) lms[i] = new Vector2(0.1f * i, 0.05f * i);
            var expected = (lms[0] + lms[5] + lms[9] + lms[13] + lms[17]) / 5f;
            var actual = LogicalViewport.PalmCenter(lms);
            Assert.AreEqual(expected.x, actual.x, 1e-5f);
            Assert.AreEqual(expected.y, actual.y, 1e-5f);
        }

        [Test]
        public void ToPanel_PreservesInCameraPosition_NoBoundingBoxFit()
        {
            // a mirrored point at the left camera edge maps to the panel left edge; center to center.
            Assert.AreEqual(0f, LogicalViewport.ToPanel(new Vector2(0f, 0.5f), 72, 96).x, 1e-4f);
            Assert.AreEqual(72f, LogicalViewport.ToPanel(new Vector2(1f, 0.5f), 72, 96).x, 1e-4f);
            var center = LogicalViewport.ToPanel(new Vector2(0.5f, 0.5f), 72, 96);
            Assert.AreEqual(36f, center.x, 1e-4f);
            Assert.AreEqual(48f, center.y, 1e-4f); // y flipped: (1-0.5)*96
        }

        [Test]
        public void SafeBorder_DetectsInCameraEdgeProximity()
        {
            Assert.IsTrue(LogicalViewport.NearSafeBorder(new Vector2(0.01f, 0.5f), 72, 6));
            Assert.IsTrue(LogicalViewport.NearSafeBorder(new Vector2(0.99f, 0.5f), 72, 6));
            Assert.IsFalse(LogicalViewport.NearSafeBorder(new Vector2(0.5f, 0.5f), 72, 6));
        }

        [Test]
        public void MatchesDevice_IsExactMatchOnly_NoFallback()
        {
            Assert.IsTrue(LogicalViewport.MatchesDevice("Integrated Camera", "Integrated Camera"));
            Assert.IsFalse(LogicalViewport.MatchesDevice("Integrated", "Integrated Camera"));
            Assert.IsFalse(LogicalViewport.MatchesDevice("Integrated Camera (2)", "Integrated Camera"));
            Assert.IsFalse(LogicalViewport.MatchesDevice("USB Camera", "Integrated Camera"));
        }

        [Test]
        public void RuntimeConfig_DefaultPreferredDevice_IsIntegratedCamera()
        {
            var cfg = ScriptableObject.CreateInstance<BeforeRuntimeConfig>();
            Assert.AreEqual("Integrated Camera", cfg.preferredDeviceName);
            Object.DestroyImmediate(cfg);
        }

        [Test]
        public void Layout_AllHudRects_FitInsideLogicalFrameAndDoNotOverlap()
        {
            // every HUD rect must sit inside 320x180
            AssertInsideFrame(BeforeLayout.SystemText, "systemText");
            AssertInsideFrame(BeforeLayout.Panel, "panel");
            AssertInsideFrame(BeforeLayout.DebugPanel, "debugPanel");
            // system text right edge and panel right margin
            Assert.LessOrEqual(BeforeLayout.SystemText.xMax, BeforeLayout.SystemTextMaxRightX, "system text exceeds x=232");
            Assert.AreEqual(8, LogicalViewport.LogicalWidth - (int)BeforeLayout.Panel.xMax, "panel right margin must be 8px");
            // panel is 48x64
            Assert.AreEqual(48, (int)BeforeLayout.Panel.width);
            Assert.AreEqual(64, (int)BeforeLayout.Panel.height);
            // queue sits left of the panel and right of the system text, no horizontal overlap
            float queueRight = BeforeLayout.QueueX + BeforeLayout.QueueSlotSize;
            Assert.LessOrEqual(queueRight, BeforeLayout.PanelX, "queue overlaps hand panel");
            Assert.LessOrEqual(BeforeLayout.SystemText.xMax, BeforeLayout.QueueX, "system text overlaps queue");
            // queue slots within frame
            foreach (int cy in BeforeLayout.QueueSlotCenterY)
            {
                Assert.GreaterOrEqual(cy - BeforeLayout.QueueNextMaxSize / 2f, 0f);
                Assert.LessOrEqual(cy + BeforeLayout.QueueNextMaxSize / 2f, LogicalViewport.LogicalHeight);
            }
        }

        [Test]
        public void Layout_SystemTextLines_FitWithinBlockHeight()
        {
            // two lines at row1/row2 with lineStep must fit inside the text block height
            int lastLineBottom = BeforeLayout.SystemTextRow2Y + BeforeLayout.SystemTextLineStep;
            Assert.LessOrEqual(lastLineBottom, (int)BeforeLayout.SystemText.yMax, "second line exceeds block height");
            Assert.AreEqual(BeforeLayout.SystemTextRow1Y + BeforeLayout.SystemTextLineStep, BeforeLayout.SystemTextRow2Y);
        }

        [Test]
        public void Layout_DebugPanel_AllLinesFitWithinBounds()
        {
            const int lineCount = 12;
            int lastY = (int)BeforeLayout.DebugPanel.y + 2 + (lineCount - 1) * BeforeLayout.DebugLineStep + BeforeLayout.DebugLineStep;
            Assert.LessOrEqual(lastY, BeforeLayout.DebugMaxBottomY, "debug lines exceed y=170");
            Assert.LessOrEqual(BeforeLayout.DebugPanel.xMax, BeforeLayout.DebugMaxRightX, "debug panel exceeds x=240");
        }

        [Test]
        public void Energy_CurveEndpoints_MatchPalmAndNulCore()
        {
            var start = new Vector2(300, 60);
            var end = BeforeLayout.NulCore;
            var ctrl = BeforeLayout.EnergyControl(start, end);
            var p0 = BeforeLayout.EnergyPoint(start, ctrl, end, 0f);
            var p1 = BeforeLayout.EnergyPoint(start, ctrl, end, 1f);
            Assert.AreEqual(start.x, p0.x, 1e-4f, "t=0 must be the palm start");
            Assert.AreEqual(start.y, p0.y, 1e-4f);
            Assert.AreEqual(end.x, p1.x, 1e-4f, "t=1 must be the NUL core");
            Assert.AreEqual(end.y, p1.y, 1e-4f);
        }

        [Test]
        public void Energy_ParticleReachesCore_AtEndOfTravel()
        {
            var start = BeforeLayout.PalmLogical(new Vector2(0.5f, 0.5f));
            var end = BeforeLayout.NulCore;
            var ctrl = BeforeLayout.EnergyControl(start, end);
            var arrival = BeforeLayout.EnergyPoint(start, ctrl, end, 1f);
            Assert.Less(Vector2.Distance(arrival, end), 0.01f, "particle must reach the NUL core at t=1");
        }

        [Test]
        public void Energy_Curve_IsContinuous_MinTwelveSegments()
        {
            Assert.GreaterOrEqual(BeforeLayout.EnergyCurveSegments, 12);
            var start = new Vector2(300, 60);
            var end = BeforeLayout.NulCore;
            var ctrl = BeforeLayout.EnergyControl(start, end);
            Vector2 prev = BeforeLayout.EnergyPoint(start, ctrl, end, 0f);
            float total = Vector2.Distance(start, end);
            for (int i = 1; i <= BeforeLayout.EnergyCurveSegments; i++)
            {
                Vector2 cur = BeforeLayout.EnergyPoint(start, ctrl, end, i / (float)BeforeLayout.EnergyCurveSegments);
                // no segment jump larger than the whole straight-line distance (curve stays connected)
                Assert.Less(Vector2.Distance(prev, cur), total, "curve segment gap too large (discontinuous)");
                prev = cur;
            }
        }

        [Test]
        public void MedianHz_TrackedHand_DoesNotReadZeroAfterAFewSeconds()
        {
            var rate = new Before.Diagnostics.RateMonitor();
            double now = 0;
            // 5 seconds of steady 15 Hz events
            for (int s = 0; s < 5; s++)
                for (int k = 0; k < 15; k++)
                    rate.AddEvent(s + k / 15.0);
            now = 5.0;
            double hz = rate.MedianHz(now, 60.0);
            Assert.Greater(hz, 0.0, "median Hz must not read 0 for a steadily tracked hand");
            Assert.GreaterOrEqual(hz, 14.0, "steady 15 Hz should median >= 14");
        }

        [Test]
        public void Energy_D093_Constants()
        {
            Assert.AreEqual(2.2f, BeforeLayout.EnergyTravelSeconds, 1e-4f);
            Assert.AreEqual(12f, BeforeLayout.EnergyNormalRate, 1e-4f);
            Assert.AreEqual(20f, BeforeLayout.EnergyActiveRate, 1e-4f);
            Assert.AreEqual(28, BeforeLayout.EnergyNormalCap);
            Assert.AreEqual(44, BeforeLayout.EnergyActiveCap);
            Assert.AreEqual(0.38f, BeforeLayout.EnergyNormalAlpha, 1e-4f);
            Assert.AreEqual(0.50f, BeforeLayout.EnergyActiveAlpha, 1e-4f);
            Assert.AreEqual(0.06f, BeforeLayout.EnergyNormalBaseAlpha, 1e-4f);
            Assert.AreEqual(0.14f, BeforeLayout.EnergyActiveBaseAlpha, 1e-4f);
            Assert.AreEqual(0.6f, BeforeLayout.EnergyFadeSeconds, 1e-4f);
            Assert.AreEqual(0.5f, BeforeLayout.EnergyReconnectRampSeconds, 1e-4f);
            Assert.LessOrEqual(BeforeLayout.EnergySuccessPulseScale, 1.35f + 1e-4f);
            Assert.GreaterOrEqual(BeforeLayout.EnergyPoolSize, BeforeLayout.EnergyActiveCap);
        }

        private static void RunField(EnergyField f, double start, double seconds, bool connected, bool active)
        {
            float dt = 1f / 60f;
            for (double t = start; t < start + seconds; t += dt) f.Update(t, dt, connected, active);
        }

        [Test]
        public void Energy_NormalRate_IsTwelvePerSecond()
        {
            var f = new EnergyField();
            RunField(f, 0, 2.0, true, false); // ramp completes in 0.5 s
            long before = f.TotalSpawned;
            RunField(f, 2.0, 1.0, true, false);
            long inOneSecond = f.TotalSpawned - before;
            Assert.That(inOneSecond, Is.InRange(11, 13), "normal ~12 particles/s");
        }

        [Test]
        public void Energy_ActiveRate_IsTwentyPerSecond()
        {
            var f = new EnergyField();
            RunField(f, 0, 2.0, true, true);
            long before = f.TotalSpawned;
            RunField(f, 2.0, 1.0, true, true);
            long inOneSecond = f.TotalSpawned - before;
            Assert.That(inOneSecond, Is.InRange(19, 21), "active ~20 particles/s");
        }

        [Test]
        public void Energy_ConcurrentCaps_NormalTwentyEight_ActiveFortyFour()
        {
            var fn = new EnergyField();
            RunField(fn, 0, 6.0, true, false);
            Assert.LessOrEqual(fn.AliveCount, BeforeLayout.EnergyNormalCap);
            var fa = new EnergyField();
            RunField(fa, 0, 6.0, true, true);
            Assert.LessOrEqual(fa.AliveCount, BeforeLayout.EnergyActiveCap);
            Assert.LessOrEqual(fa.AliveCount, BeforeLayout.EnergyPoolSize, "never exceeds the fixed pool");
        }

        [Test]
        public void Energy_HandLoss_FadesToZeroOverSixTenths()
        {
            var f = new EnergyField();
            RunField(f, 0, 3.0, true, false);
            Assert.Greater(f.AliveCount, 0);
            Assert.AreEqual(1f, f.Fade(3.0), 1e-4f);
            // lose the hand at t=3.0
            f.Update(3.0, 1f / 60f, false, false);
            Assert.Greater(f.Fade(3.2), 0f, "still fading at 0.2 s");
            Assert.AreEqual(0f, f.Fade(3.7), 1e-4f, "fully faded by 0.6 s");
            RunField(f, 3.7, 0.1, false, false);
            Assert.AreEqual(0, f.AliveCount, "no particles after the fade completes");
        }

        [Test]
        public void Energy_Reconnect_RampsRateOverHalfSecond()
        {
            var f = new EnergyField();
            // fresh connection at t=0 ramps: first 0.25 s should spawn clearly fewer than the full rate
            long earlyStart = f.TotalSpawned;
            RunField(f, 0, 0.25, true, false);
            long early = f.TotalSpawned - earlyStart;   // ramp ~0..0.5 -> ~ average 0.25*12*0.25 ~ under full
            Assert.Less(early, 3, "reconnect must ramp, not burst the full rate immediately");
        }

        private static void AssertInsideFrame(Rect r, string name)
        {
            Assert.GreaterOrEqual(r.x, 0f, $"{name}.x < 0");
            Assert.GreaterOrEqual(r.y, 0f, $"{name}.y < 0");
            Assert.LessOrEqual(r.xMax, LogicalViewport.LogicalWidth, $"{name} exceeds right edge");
            Assert.LessOrEqual(r.yMax, LogicalViewport.LogicalHeight, $"{name} exceeds bottom edge");
        }

        [Test]
        public void HandSignalSource_ExposesNoCameraRgbSurface()
        {
            // D-088: no member may hand out a WebCamTexture / Texture (which would allow drawing camera RGB).
            foreach (var p in typeof(IHandSignalSource).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                Assert.IsFalse(typeof(Texture).IsAssignableFrom(p.PropertyType),
                    $"IHandSignalSource.{p.Name} exposes a Texture; the hand panel must never receive camera RGB.");
            }
            // The concrete webcam source must likewise expose no public Texture surface.
            foreach (var p in typeof(WebcamHandSource).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                Assert.IsFalse(typeof(Texture).IsAssignableFrom(p.PropertyType),
                    $"WebcamHandSource.{p.Name} exposes a Texture; camera RGB must stay internal (Sentis input only).");
            }
        }
    }
}
