// BEFORE Goal 4A — PlayMode behaviour of the NUL production slice.
// Covers the seven checks listed in 04_goal_unity_nul.md §テスト PlayMode.
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Before.Production.Tests
{
    public class NulProductionPlayTests
    {
        private GameObject _root;

        private static string Cmd(int cmdId, string type, string session = "p1", long sstart = 900)
            => "{\"v\":1,\"kind\":\"cmd\",\"session\":\"" + session + "\",\"sstart\":" + sstart +
               ",\"cmdId\":" + cmdId + ",\"seq\":1,\"tUs\":0,\"type\":\"" + type +
               "\",\"phase\":\"fire\"}";

        private static void SetPrivate(object target, string field, object value)
        {
            var f = target.GetType().GetField(field,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(f, "field not found: " + field);
            f.SetValue(target, value);
        }

        // Builds the same rig the scene holds, on a free UDP port so the tests are
        // hermetic and never touch the saved scene.
        private NulProductionController MakeRig(int port)
        {
            float standH = NulProductionController.StandColliderH;
            float standW = NulProductionController.StandColliderW;
            float crouchH = standH * NulProductionController.CrouchColliderRatio;
            float k = NulProductionController.VisualScale;

            _root = new GameObject("NUL_Production_Test");
            _root.SetActive(false);            // configure before Awake runs

            var rb = _root.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.gravityScale = 0f;
            rb.freezeRotation = true;

            var visual = new GameObject("VisualRoot");
            visual.transform.SetParent(_root.transform, false);
            visual.transform.localScale = new Vector3(k, k, 1f);
            visual.AddComponent<SpriteRenderer>();
            var animator = visual.AddComponent<Animator>();

            var standGO = new GameObject("StandingCollider");
            standGO.transform.SetParent(_root.transform, false);
            var standC = standGO.AddComponent<BoxCollider2D>();
            standC.size = new Vector2(standW, standH);
            standC.offset = new Vector2(0f, standH * 0.5f);

            var crouchGO = new GameObject("CrouchCollider");
            crouchGO.transform.SetParent(_root.transform, false);
            var crouchC = crouchGO.AddComponent<BoxCollider2D>();
            crouchC.size = new Vector2(standW, crouchH);
            crouchC.offset = new Vector2(0f, crouchH * 0.5f);
            crouchC.enabled = false;

            var ground = new GameObject("GroundCheck");
            ground.transform.SetParent(_root.transform, false);
            var fx = new GameObject("EffectsAnchor");
            fx.transform.SetParent(_root.transform, false);

            var ctrl = _root.AddComponent<NulProductionController>();
            ctrl.udpPort = port;
            ctrl.autoLaunchSidecar = false;
            ctrl.autoRun = true;
            SetPrivate(ctrl, "visualRoot", visual.transform);
            SetPrivate(ctrl, "animator", animator);
            SetPrivate(ctrl, "standingCollider", standC);
            SetPrivate(ctrl, "crouchCollider", crouchC);
            SetPrivate(ctrl, "groundCheck", ground.transform);
            SetPrivate(ctrl, "effectsAnchor", fx.transform);

            _root.SetActive(true);
            return ctrl;
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.Destroy(_root);
            _root = null;
        }

        // 1 — five distinct JUMP commands are processed five times.
        [UnityTest]
        public IEnumerator Five_distinct_jumps_processed()
        {
            var ctl = MakeRig(8801);
            yield return null;
            for (int id = 1; id <= 5; id++) ctl.InjectDatagram(Cmd(id, "JUMP"));
            yield return null;
            Assert.AreEqual(5, ctl.Nul.JumpCount);
        }

        // 2 — CROUCH lasts 0.75 s and swaps the collider for its duration.
        [UnityTest]
        public IEnumerator Crouch_holds_075_and_swaps_collider()
        {
            var ctl = MakeRig(8802);
            yield return null;
            Assert.IsTrue(ctl.StandingColliderActive);
            Assert.IsFalse(ctl.CrouchColliderActive);

            ctl.InjectDatagram(Cmd(1, "CROUCH"));
            yield return null;
            Assert.IsTrue(ctl.CrouchColliderActive, "crouch collider on");
            Assert.IsFalse(ctl.StandingColliderActive, "standing collider off");

            yield return new WaitForSeconds(0.40f);
            Assert.IsTrue(ctl.CrouchColliderActive, "still crouched at 0.40 s");

            yield return new WaitForSeconds(0.50f);   // past 0.75 s total
            Assert.IsFalse(ctl.CrouchColliderActive, "crouch released after 0.75 s");
            Assert.IsTrue(ctl.StandingColliderActive, "standing collider restored");
        }

        // 3 — a CROUCH received while crouching refreshes the remaining time.
        [UnityTest]
        public IEnumerator Crouch_resend_refreshes_time()
        {
            var ctl = MakeRig(8803);
            yield return null;
            ctl.InjectDatagram(Cmd(1, "CROUCH"));
            yield return new WaitForSeconds(0.50f);
            Assert.IsTrue(ctl.CrouchColliderActive);

            ctl.InjectDatagram(Cmd(2, "CROUCH"));      // refresh to a full 0.75 s
            yield return new WaitForSeconds(0.50f);    // 1.00 s after the first
            Assert.IsTrue(ctl.CrouchColliderActive, "refresh must extend the crouch");

            yield return new WaitForSeconds(0.40f);    // 0.90 s after the refresh
            Assert.IsFalse(ctl.CrouchColliderActive, "crouch ends 0.75 s after refresh");
        }

        // 4 — STOP_ON halts horizontal motion at once; STOP_OFF resumes it.
        [UnityTest]
        public IEnumerator Stop_on_halts_and_off_resumes()
        {
            var ctl = MakeRig(8804);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Assert.AreEqual(NulProductionController.RunSpeed, ctl.HorizontalSpeed, 0.01f,
                            "auto-run before stop");

            ctl.InjectDatagram(Cmd(1, "STOP_ON"));
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Assert.AreEqual(0f, ctl.HorizontalSpeed, 0.01f, "STOP_ON halts immediately");
            Assert.AreEqual(NulProductionController.NulAnimState.Stop, ctl.State);

            ctl.InjectDatagram(Cmd(2, "STOP_OFF"));
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Assert.AreEqual(NulProductionController.RunSpeed, ctl.HorizontalSpeed, 0.01f,
                            "STOP_OFF resumes");
        }

        // 5 — Jump -> Landing -> Run, and the foot line returns to the ground.
        [UnityTest]
        public IEnumerator Jump_then_landing_then_run()
        {
            var ctl = MakeRig(8805);
            yield return null;
            float groundY = ctl.transform.position.y;

            ctl.InjectDatagram(Cmd(1, "JUMP"));
            yield return null;
            Assert.IsTrue(ctl.Airborne, "airborne right after JUMP");
            Assert.AreEqual(NulProductionController.NulAnimState.Jump, ctl.State);

            // an extra airborne JUMP must not double-jump
            int jumps = ctl.Nul.JumpCount;
            ctl.InjectDatagram(Cmd(2, "JUMP"));
            yield return null;
            Assert.IsTrue(ctl.Airborne);
            Assert.Greater(ctl.Nul.JumpCount, jumps, "the command is still counted");

            yield return new WaitForSeconds(0.85f);       // past the 0.80 s airtime
            Assert.IsFalse(ctl.Airborne, "back on the ground");
            Assert.AreEqual(NulProductionController.NulAnimState.Landing, ctl.State);
            Assert.AreEqual(groundY, ctl.transform.position.y, 0.5f,
                            "foot line returns to the ground with no teleport");

            yield return new WaitForSeconds(0.30f);       // past the 0.267 s landing clip
            Assert.AreEqual(NulProductionController.NulAnimState.Run, ctl.State);
        }

        // 6 — Direction mode suppresses Jump/Crouch/Stop.
        [UnityTest]
        public IEnumerator Direction_mode_is_exclusive()
        {
            var ctl = MakeRig(8806);
            yield return null;
            ctl.Nul.DirectionMode = true;

            ctl.InjectDatagram(Cmd(1, "JUMP"));
            ctl.InjectDatagram(Cmd(2, "CROUCH"));
            ctl.InjectDatagram(Cmd(3, "STOP_ON"));
            yield return null;
            Assert.AreEqual(0, ctl.Nul.JumpCount);
            Assert.IsFalse(ctl.CrouchColliderActive);
            Assert.IsFalse(ctl.Nul.StopActive);
            Assert.IsFalse(ctl.Airborne);

            ctl.InjectDatagram(Cmd(4, "LEFT"));
            yield return null;
            Assert.AreEqual(-1, ctl.Nul.LastDirection);
        }

        // 7 — input received while paused is never replayed after resuming.
        [UnityTest]
        public IEnumerator Paused_input_is_not_replayed()
        {
            var ctl = MakeRig(8807);
            yield return null;
            ctl.Nul.Paused = true;

            ctl.InjectDatagram(Cmd(1, "JUMP"));
            ctl.InjectDatagram(Cmd(2, "STOP_ON"));
            yield return null;
            Assert.AreEqual(0, ctl.Nul.JumpCount);
            Assert.IsFalse(ctl.Nul.StopActive);
            Assert.IsFalse(ctl.Airborne);

            ctl.Nul.Paused = false;
            yield return null;
            yield return null;
            Assert.AreEqual(0, ctl.Nul.JumpCount, "paused input must not replay");
            Assert.IsFalse(ctl.Nul.StopActive, "paused input must not replay");
            Assert.IsFalse(ctl.Airborne, "paused input must not replay");

            ctl.InjectDatagram(Cmd(3, "JUMP"));
            yield return null;
            Assert.AreEqual(1, ctl.Nul.JumpCount, "commands work again after resume");
        }
    }
}
