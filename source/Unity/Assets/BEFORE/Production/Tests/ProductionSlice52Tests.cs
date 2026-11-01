using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Before.Production.Tests
{
    public class ProductionSlice52Tests
    {
        private GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null) UnityEngine.Object.DestroyImmediate(_root);
        }

        [Test]
        public void DebugLifeKeyRequiresNumpadOrShiftedMainPlus()
        {
            Assert.IsTrue(ProductionGameLoop.IsDebugLifeKeyPressed(
                true, false, false, false));
            Assert.IsTrue(ProductionGameLoop.IsDebugLifeKeyPressed(
                false, true, true, false));
            Assert.IsTrue(ProductionGameLoop.IsDebugLifeKeyPressed(
                false, true, false, true));
            Assert.IsFalse(ProductionGameLoop.IsDebugLifeKeyPressed(
                false, false, true, false), "unshifted equals is not plus");
            Assert.IsFalse(ProductionGameLoop.IsDebugLifeKeyPressed(
                false, false, false, true), "unshifted semicolon is not plus");
        }

        [Test]
        public void DebugLifeAddsOnlyToExistingThreeLifeCapBeforeGameOver()
        {
            _root = new GameObject("slice52-lives");
            var loop = _root.AddComponent<ProductionGameLoop>();
            SetAutoProperty(loop, "Lives", 2);
            int notification = -1;
            loop.OnLivesChanged += value => notification = value;

            Assert.IsTrue(loop.TryAddDebugLife());
            Assert.AreEqual(ProductionGameLoop.StartingLives, loop.Lives);
            Assert.AreEqual(3, notification);
            Assert.IsFalse(loop.TryAddDebugLife(), "three stays at the existing cap");

            SetAutoProperty(loop, "Lives", 2);
            SetAutoProperty(loop, "IsGameOver", true);
            Assert.IsFalse(loop.TryAddDebugLife(), "GAME OVER cannot revive");
            Assert.AreEqual(2, loop.Lives);
        }

        [Test]
        public void LandmarkStatusCarriesReadinessWithoutBecomingCommand()
        {
            const string json = "{\"v\":1,\"kind\":\"lm\",\"tUs\":42," +
                "\"pts\":[],\"ready\":\"STOP\",\"signal\":\"ok\"}";
            Assert.IsTrue(LandmarkPacket.TryParseStatus(json, out var status));
            Assert.AreEqual(42, status.TimestampUs);
            Assert.AreEqual(ProductionReadinessKind.Stop, status.Readiness);
            Assert.AreEqual(ProductionHandSignal.Ok, status.Signal);
            Assert.IsFalse(CommandPacket.TryParseCommand(json, out _));
            StringAssert.DoesNotContain("rgb", json.ToLowerInvariant());
        }

        [Test]
        public void HandBorderUsesOneResolvedStateAndSafetyPriority()
        {
            Color jumpReadiness = ProductionDiagnosticsHud.HandBorderColor(false,
                ProductionHandSignal.Ok, false, ProductionReadinessKind.Jump);
            Color crouchReadiness = ProductionDiagnosticsHud.HandBorderColor(false,
                ProductionHandSignal.Ok, false, ProductionReadinessKind.Crouch);
            Color stopReadiness = ProductionDiagnosticsHud.HandBorderColor(false,
                ProductionHandSignal.Ok, false, ProductionReadinessKind.Stop);
            Assert.AreEqual(ProductionHudLayout.DirtyWhite, jumpReadiness);
            Assert.AreEqual(ProductionHudLayout.DirtyWhite, crouchReadiness);
            Assert.AreEqual(ProductionHudLayout.DirtyWhite, stopReadiness);

            Color jump = ProductionDiagnosticsHud.HandBorderColor(false,
                ProductionHandSignal.Ok, false, ProductionReadinessKind.None,
                CommandKind.Jump);
            Color crouch = ProductionDiagnosticsHud.HandBorderColor(false,
                ProductionHandSignal.Ok, false, ProductionReadinessKind.None,
                CommandKind.Crouch);
            Color stop = ProductionDiagnosticsHud.HandBorderColor(false,
                ProductionHandSignal.Ok, false, ProductionReadinessKind.None,
                CommandKind.StopOn);
            Assert.AreEqual(ProductionFeedbackHud.CommandColorFor(
                ProductionObstacleKind.Jump), jump);
            Assert.AreEqual(ProductionFeedbackHud.CommandColorFor(
                ProductionObstacleKind.Crouch), crouch);
            Assert.AreEqual(ProductionFeedbackHud.CommandColorFor(
                ProductionObstacleKind.Stop), stop);

            Assert.AreEqual(ProductionHudLayout.DirtyWhite,
                ProductionDiagnosticsHud.HandBorderColor(false,
                    ProductionHandSignal.Ok, false, ProductionReadinessKind.None));
            Assert.AreEqual(ProductionHudLayout.DirtyWhite,
                ProductionDiagnosticsHud.HandBorderColor(true,
                    ProductionHandSignal.Ok, false, ProductionReadinessKind.Jump));
            Assert.AreEqual(ProductionHudLayout.Violet,
                ProductionDiagnosticsHud.HandBorderColor(false,
                    ProductionHandSignal.Ok, true, ProductionReadinessKind.Jump));
            Assert.AreEqual(ProductionHudLayout.Fault,
                ProductionDiagnosticsHud.HandBorderColor(false,
                    ProductionHandSignal.TrackingLost, true,
                    ProductionReadinessKind.Stop, CommandKind.Crouch));
            Assert.AreEqual(ProductionHudLayout.Amber,
                ProductionDiagnosticsHud.HandBorderColor(false,
                    ProductionHandSignal.OutOfFrame, false,
                    ProductionReadinessKind.Jump, CommandKind.Crouch));
        }

        [Test]
        public void CrouchFlashRequiresRealEventExpiresAndResetClearsIt()
        {
            _root = new GameObject("slice52-hand-flash");
            var diagnostics = _root.AddComponent<ProductionDiagnosticsHud>();
            MethodInfo handle = typeof(ProductionDiagnosticsHud).GetMethod(
                "HandleCommandFlash", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo reset = typeof(ProductionDiagnosticsHud).GetMethod(
                "ResetCommandFlash", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(handle);
            Assert.IsNotNull(reset);

            float now = Time.unscaledTime;
            Assert.AreEqual(CommandKind.Unknown, diagnostics.CommandFlashKindAt(now));
            handle.Invoke(diagnostics, new object[]
            {
                new CommandPacket { Kind = CommandKind.Crouch }
            });
            Assert.AreEqual(CommandKind.Crouch,
                diagnostics.CommandFlashKindAt(Time.unscaledTime));
            Assert.AreEqual(CommandKind.Unknown,
                diagnostics.CommandFlashKindAt(
                    Time.unscaledTime + ProductionDiagnosticsHud.CommandFlashSeconds));

            handle.Invoke(diagnostics, new object[]
            {
                new CommandPacket { Kind = CommandKind.Crouch }
            });
            reset.Invoke(diagnostics, null);
            Assert.AreEqual(CommandKind.Unknown,
                diagnostics.CommandFlashKindAt(Time.unscaledTime));
        }

        [Test]
        public void PoseLockRequiresValidReadinessAndHonorsDisplayPriority()
        {
            Assert.AreEqual(ProductionReadinessKind.None,
                ProductionDiagnosticsHud.ResolvePoseLockKind(
                    false, ProductionHandSignal.Ok, false,
                    ProductionReadinessKind.None, false));
            Assert.AreEqual(ProductionReadinessKind.None,
                ProductionDiagnosticsHud.ResolvePoseLockKind(
                    true, ProductionHandSignal.Ok, false,
                    ProductionReadinessKind.Jump, false));
            Assert.AreEqual(ProductionReadinessKind.None,
                ProductionDiagnosticsHud.ResolvePoseLockKind(
                    false, ProductionHandSignal.LowQuality, false,
                    ProductionReadinessKind.Crouch, false));
            Assert.AreEqual(ProductionReadinessKind.None,
                ProductionDiagnosticsHud.ResolvePoseLockKind(
                    false, ProductionHandSignal.OutOfFrame, false,
                    ProductionReadinessKind.Stop, true));

            Assert.AreEqual(ProductionReadinessKind.Jump,
                ProductionDiagnosticsHud.ResolvePoseLockKind(
                    false, ProductionHandSignal.Ok, false,
                    ProductionReadinessKind.Jump, false));
            Assert.AreEqual(ProductionReadinessKind.Crouch,
                ProductionDiagnosticsHud.ResolvePoseLockKind(
                    false, ProductionHandSignal.Ok, false,
                    ProductionReadinessKind.Crouch, false));
            Assert.AreEqual(ProductionReadinessKind.Stop,
                ProductionDiagnosticsHud.ResolvePoseLockKind(
                    false, ProductionHandSignal.Ok, false,
                    ProductionReadinessKind.Stop, false));
            Assert.AreEqual(ProductionReadinessKind.Stop,
                ProductionDiagnosticsHud.ResolvePoseLockKind(
                    false, ProductionHandSignal.Ok, false,
                    ProductionReadinessKind.None, true),
                "latched STOP remains visible without readiness inference");

            Assert.IsFalse(ProductionDiagnosticsHud.ShouldDrawPoseLock(
                ProductionReadinessKind.Crouch, ProductionHandSignal.Ok,
                false, false, CommandKind.Crouch),
                "the existing command flash has higher priority");
            Assert.IsTrue(ProductionDiagnosticsHud.ShouldDrawPoseLock(
                ProductionReadinessKind.Crouch, ProductionHandSignal.Ok,
                false, false, CommandKind.Unknown));
        }

        [Test]
        public void PoseLockUsesFourIntegerCornerBracketsAndNoOldProgressEdge()
        {
            Assert.AreEqual(4f, ProductionDiagnosticsHud.PoseLockInset);
            Assert.AreEqual(8f, ProductionDiagnosticsHud.PoseLockArmLength);
            Assert.AreEqual(2f, ProductionDiagnosticsHud.PoseLockThickness);
            Assert.AreEqual(2f, ProductionDiagnosticsHud.PoseLockSnapDistance);
            Assert.AreEqual(0.12f, ProductionDiagnosticsHud.PoseLockAppearSeconds);

            Rect panel = new Rect(0f, 0f, 48f, 64f);
            Rect[] rects = ProductionDiagnosticsHud.PoseLockRects(panel, 1f, 0f);
            Assert.AreEqual(8, rects.Length, "two arms per corner");
            foreach (Rect rect in rects)
            {
                Assert.AreEqual(Mathf.Round(rect.x), rect.x);
                Assert.AreEqual(Mathf.Round(rect.y), rect.y);
                Assert.AreEqual(Mathf.Round(rect.width), rect.width);
                Assert.AreEqual(Mathf.Round(rect.height), rect.height);
                Assert.IsTrue(rect.width == 8f && rect.height == 2f ||
                              rect.width == 2f && rect.height == 8f);
                Assert.IsTrue(panel.Contains(rect.min));
                Assert.IsTrue(panel.Contains(new Vector2(
                    rect.xMax - 0.01f, rect.yMax - 0.01f)));
            }
            Assert.IsNull(typeof(ProductionDiagnosticsHud).GetMethod(
                "CandidateProgress", BindingFlags.Public | BindingFlags.Static),
                "the old bottom-edge readiness protrusion must not remain");
        }

        [Test]
        public void PoseLockSnapRunsOncePerNewCandidateAndResetStateIsEmpty()
        {
            Assert.IsTrue(ProductionDiagnosticsHud.ShouldRestartPoseLock(
                ProductionReadinessKind.None, ProductionReadinessKind.Jump));
            Assert.IsFalse(ProductionDiagnosticsHud.ShouldRestartPoseLock(
                ProductionReadinessKind.Jump, ProductionReadinessKind.Jump));
            Assert.IsTrue(ProductionDiagnosticsHud.ShouldRestartPoseLock(
                ProductionReadinessKind.Jump, ProductionReadinessKind.Crouch));
            Assert.IsFalse(ProductionDiagnosticsHud.ShouldRestartPoseLock(
                ProductionReadinessKind.Jump, ProductionReadinessKind.None));

            Assert.AreEqual(2f, ProductionDiagnosticsHud.PoseLockSnapOffset(0f));
            Assert.AreEqual(1f, ProductionDiagnosticsHud.PoseLockSnapOffset(0.05f));
            Assert.AreEqual(0f, ProductionDiagnosticsHud.PoseLockSnapOffset(0.10f));
            Assert.AreEqual(0f, ProductionDiagnosticsHud.PoseLockSnapOffset(1f));
        }

        [Test]
        public void StopStateIgnoresNoneAndChangesOnlyOnStopOffOrReset()
        {
            var nul = new NulState();
            nul.Apply(new CommandPacket { Kind = CommandKind.StopOn }, 0.0);
            Assert.IsTrue(nul.StopActive);
            nul.Apply(new CommandPacket { Kind = CommandKind.Unknown }, 0.1);
            Assert.IsTrue(nul.StopActive);
            nul.Apply(new CommandPacket { Kind = CommandKind.Jump }, 0.2);
            Assert.IsTrue(nul.StopActive);
            nul.Apply(new CommandPacket { Kind = CommandKind.StopOff }, 0.3);
            Assert.IsFalse(nul.StopActive);

            nul.Apply(new CommandPacket { Kind = CommandKind.StopOn }, 0.4);
            Assert.IsTrue(nul.StopActive);
            nul.StopActive = false;
            Assert.IsFalse(nul.StopActive, "explicit reset clears the latch");
        }

        [Test]
        public void StopGateUsesAuthoritativeOpenStateAsColorlessSafeState()
        {
            foreach (float time in new[] { 0f, 1.5f, 3.1f, 4.8f })
            {
                bool safe = ProductionCourseLoop.EnergyWallIsOpen(time);
                Color beam = ProductionCoursePresenter.ObstacleColorFor(
                    ProductionObstacleKind.Stop, safe);
                Assert.AreEqual(safe, beam.a == 0f,
                    "the same authoritative state owns damage and visibility");
            }
        }

        private static void SetAutoProperty(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField("<" + name + ">k__BackingField",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, name);
            field.SetValue(target, value);
        }
    }
}
