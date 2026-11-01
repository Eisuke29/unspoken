// BEFORE Goal 4A — EditMode acceptance tests for the NUL production slice.
// Covers the ten checks listed in 04_goal_unity_nul.md §テスト EditMode.
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Before.Production.Tests
{
    public class Goal4ATests
    {
        private const string ArtDir = "Assets/BEFORE/Production/Art/NUL";
        private const string AnimDir = "Assets/BEFORE/Production/Animation/NUL";
        private const string ScenePath = "Assets/BEFORE/Scenes/BEFORE_ProductionV2.unity";

        private static readonly string[] BForbidden =
            { "Confused", "CrouchEnter", "CrouchLoop", "CrouchExit", "Fail_", "Transfer_" };

        private static List<string> ASetNames()
        {
            var n = new List<string>();
            for (int i = 0; i < 4; i++) n.Add("Idle_0" + i);
            for (int i = 0; i < 8; i++) n.Add("Run_0" + i);
            for (int i = 0; i < 6; i++) n.Add("Jump_0" + i);
            for (int i = 0; i < 4; i++) n.Add("Landing_0" + i);
            for (int i = 0; i < 4; i++) n.Add("Stop_0" + i);
            return n;
        }

        private static CommandPacket Cmd(CommandKind kind, int id, string session = "s1",
                                         long start = 1000, int dir = 0)
            => new CommandPacket { Session = session, SessionStartUs = start, CommandId = id,
                                   Kind = kind, Direction = dir };

        // 1 — all 26 A-set sprites are present and are 32x32.
        [Test]
        public void ASet_TwentySix_SpritesExist()
        {
            var names = ASetNames();
            Assert.AreEqual(26, names.Count);
            foreach (var n in names)
            {
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(ArtDir + "/" + n + ".png");
                Assert.IsNotNull(sprite, "missing sprite: " + n);
                Assert.AreEqual(32, (int)sprite.rect.width, n + " width");
                Assert.AreEqual(32, (int)sprite.rect.height, n + " height");
            }
        }

        // 2 — no worker-b / worker-c artwork reached the production art folder.
        [Test]
        public void NoWorkerBC_AssetsReferenced()
        {
            var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { ArtDir });
            Assert.AreEqual(26, guids.Length, "Art/NUL must hold exactly the 26 A-set frames");
            foreach (var g in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                foreach (var bad in BForbidden)
                    Assert.IsFalse(path.Contains(bad), "worker-b/c asset leaked in: " + path);
            }
        }

        // 3 — every one of the 26 shares the same import settings (PPU 1, D-117).
        [Test]
        public void ImportSettings_IdenticalAcrossAllFrames()
        {
            foreach (var n in ASetNames())
            {
                string path = ArtDir + "/" + n + ".png";
                var ti = (TextureImporter)AssetImporter.GetAtPath(path);
                Assert.IsNotNull(ti, "no importer: " + n);
                Assert.AreEqual(TextureImporterType.Sprite, ti.textureType, n);
                Assert.AreEqual(SpriteImportMode.Single, ti.spriteImportMode, n);
                Assert.AreEqual(1f, ti.spritePixelsPerUnit, 0.0001f, n + " PPU must be 1 (D-117)");
                Assert.AreEqual(FilterMode.Point, ti.filterMode, n);
                Assert.AreEqual(TextureImporterCompression.Uncompressed, ti.textureCompression, n);
                Assert.IsFalse(ti.mipmapEnabled, n);
                Assert.IsTrue(ti.alphaIsTransparency, n);
                Assert.AreEqual(TextureWrapMode.Clamp, ti.wrapMode, n);

                var s = new TextureImporterSettings();
                ti.ReadTextureSettings(s);
                Assert.AreEqual((int)SpriteAlignment.BottomCenter, s.spriteAlignment, n + " pivot");
                Assert.AreEqual(SpriteMeshType.FullRect, s.spriteMeshType, n + " mesh");
            }
        }

        // 4 — the controller exposes exactly the six required states.
        [Test]
        public void Animator_HasSixRequiredStates()
        {
            var ac = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(
                AnimDir + "/NUL_Production.controller");
            Assert.IsNotNull(ac, "controller missing");
            var sm = ac.layers[0].stateMachine;

            var found = new List<string>();
            foreach (var s in sm.states)
            {
                found.Add(s.state.name);
                Assert.IsNotNull(s.state.motion, s.state.name + " has no clip");
            }
            foreach (var required in new[] { "Idle", "Run", "Jump", "Landing", "Stop", "Crouch" })
                Assert.Contains(required, found, "missing state " + required);
            Assert.AreEqual(6, sm.states.Length);
            Assert.AreEqual(6, sm.anyStateTransitions.Length, "one AnyState transition per state");
        }

        // 5 — the crouch collider is 55 % tall and shares the standing bottom edge.
        [Test]
        public void Colliders_ShareBottomEdge()
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                ScenePath, UnityEditor.SceneManagement.OpenSceneMode.Single);
            NulProductionController ctrl = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                ctrl = root.GetComponentInChildren<NulProductionController>();
                if (ctrl != null) break;
            }
            Assert.IsNotNull(ctrl, "NulProductionController not found in " + ScenePath);
            Assert.AreEqual(ctrl.StandingBottomY, ctrl.CrouchBottomY, 0.0001f,
                            "standing and crouch colliders must share a bottom edge");
        }

        // 6 — a CROUCH received while crouching refreshes the remaining time to 0.75 s.
        [Test]
        public void Crouch_Refreshes_To_075()
        {
            var nul = new NulState();
            nul.Apply(Cmd(CommandKind.Crouch, 1), 10.0);
            Assert.IsTrue(nul.Crouching);
            Assert.AreEqual(0.75, nul.CrouchRemaining(10.0), 1e-6);

            nul.Tick(10.5);                                  // 0.25 s left
            Assert.AreEqual(0.25, nul.CrouchRemaining(10.5), 1e-6);
            nul.Apply(Cmd(CommandKind.Crouch, 2), 10.5);      // refresh
            Assert.AreEqual(0.75, nul.CrouchRemaining(10.5), 1e-6);

            nul.Tick(11.30);
            Assert.IsFalse(nul.Crouching, "crouch must expire 0.75 s after the refresh");
        }

        // 7 — STOP_ON / STOP_OFF drive the stop latch.
        [Test]
        public void Stop_On_Off_Transitions()
        {
            var nul = new NulState();
            Assert.IsFalse(nul.StopActive);
            nul.Apply(Cmd(CommandKind.StopOn, 1), 0.0);
            Assert.IsTrue(nul.StopActive);
            nul.Apply(Cmd(CommandKind.StopOff, 2), 0.1);
            Assert.IsFalse(nul.StopActive);
        }

        // 8 — commands arriving while paused are consumed, never replayed on resume.
        [Test]
        public void Paused_Input_Is_Not_Replayed()
        {
            var nul = new NulState { Paused = true };
            nul.Apply(Cmd(CommandKind.Jump, 1), 0.0);
            nul.Apply(Cmd(CommandKind.Jump, 2), 0.1);
            nul.Apply(Cmd(CommandKind.StopOn, 3), 0.2);
            Assert.AreEqual(0, nul.JumpCount);
            Assert.IsFalse(nul.StopActive);

            nul.Paused = false;                 // resume: nothing queued
            Assert.AreEqual(0, nul.JumpCount, "paused commands must not replay");
            Assert.IsFalse(nul.StopActive, "paused commands must not replay");

            nul.Apply(Cmd(CommandKind.Jump, 4), 0.3);
            Assert.AreEqual(1, nul.JumpCount);
        }

        // 9 — Direction mode is exclusive: Jump/Crouch/Stop are suppressed.
        [Test]
        public void DirectionMode_Is_Exclusive()
        {
            var nul = new NulState { DirectionMode = true };
            nul.Apply(Cmd(CommandKind.Jump, 1), 0.0);
            nul.Apply(Cmd(CommandKind.Crouch, 2), 0.1);
            nul.Apply(Cmd(CommandKind.StopOn, 3), 0.2);
            Assert.AreEqual(0, nul.JumpCount);
            Assert.IsFalse(nul.Crouching);
            Assert.IsFalse(nul.StopActive);

            nul.Apply(Cmd(CommandKind.Right, 4), 0.3);
            Assert.AreEqual(1, nul.LastDirection);
            nul.Apply(Cmd(CommandKind.Left, 5), 0.4);
            Assert.AreEqual(-1, nul.LastDirection);
        }

        // 10 — the sidecar sends every commandId three times; only the first runs.
        [Test]
        public void TripleSend_Executes_Once()
        {
            var router = new CommandRouter();
            var nul = new NulState();
            var p = Cmd(CommandKind.Jump, 42);
            for (int i = 0; i < 3; i++)
                if (router.Ingest(p)) nul.Apply(p, 0.0);
            Assert.AreEqual(1, nul.JumpCount, "triple-sent commandId must execute once");

            var q = Cmd(CommandKind.Jump, 43);
            for (int i = 0; i < 3; i++)
                if (router.Ingest(q)) nul.Apply(q, 0.1);
            Assert.AreEqual(2, nul.JumpCount);
        }
    }
}
