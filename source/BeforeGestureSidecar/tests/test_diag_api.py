"""Regression for the bench <-> Engine diagnostics API (D-111 crash fix).

Covers the exact draw-path that raised AttributeError('_tracker') right after F:
the bench must build UI diagnostics only from Engine.get_diagnostics().
"""
import numpy as np

from before_gesture import personal_pose as pp
from before_gesture import command_engine as ce
from before_gesture import teach_controller as tc
from before_gesture import bench_app
from before_gesture.mediapipe_backend import HandResult
import synth

PALM_IDS = (0, 5, 9, 13, 17)


def _palm(lm):
    a = np.asarray(lm)[:, :2]
    return tuple(a[list(PALM_IDS)].mean(axis=0))


# ---------- item 5: integration through teach -> READY -> UI data ----------
def _capture(ctl, key, fn, t0, dt=0.05):
    ctl.on_key(key, t0)
    t = t0
    for i in range(40):
        if ctl.capturing is None:
            break
        lm = fn(i)
        ctl.feed(lm.tolist(), 0.9, _palm(lm), t)
        t += dt
    return t


def _teach_to_ready(ctl, t):
    for _ in range(3):
        t = _capture(ctl, "j", lambda i: synth.v_sign(jitter=0.001, seed=i),
                     t + 1.0)
    ctl.on_key("m", t + 0.2); t += 0.3
    for cy in (0.55, 0.50, 0.45, 0.40, 0.35):
        lm = synth.v_sign(center=(0.5, cy)); ctl.feed(lm.tolist(), 0.9, _palm(lm), t); t += 0.05
    ctl.on_key("e", t); t += 0.05
    ctl.on_key("f", t + 0.2); t += 0.5
    for _ in range(12):          # proof: hold pose -> verify
        lm = synth.v_sign(center=(0.5, 0.5)); ctl.feed(lm.tolist(), 0.9, _palm(lm), t); t += 0.1
    return t


def test_finalize_then_ui_diagnostics_no_attributeerror():
    ctl = tc.TeachController()
    t = _teach_to_ready(ctl, 0.0)
    eng = ctl.engine
    # the exact post-F UI data assembly must not raise
    diag = eng.get_diagnostics(t)
    pose_l, stroke_l, speed_l = bench_app.jump_diag_lines(diag)
    assert "POSE channel:JUMP" in pose_l
    assert "STROKE state:" in stroke_l
    assert "cycle:" in speed_l
    # engine has NO _tracker attribute anymore
    assert not hasattr(eng, "_tracker")


def test_ui_diagnostics_update_on_stroke_fire():
    ctl = tc.TeachController()
    t = _teach_to_ready(ctl, 0.0)
    before = ctl.engine.get_diagnostics(t).cycle
    fired = 0
    for cy in (0.5, 0.5, 0.42, 0.30, 0.28):
        lm = synth.v_sign(center=(0.5, cy))
        cmds = ctl.feed(lm.tolist(), 0.9, _palm(lm), t, tracked=True)
        fired += sum(1 for c in cmds if c.type.value == "JUMP")
        # rebuild UI lines every frame — must never raise
        bench_app.jump_diag_lines(ctl.engine.get_diagnostics(t))
        t += 0.05
    after = ctl.engine.get_diagnostics(t)
    assert fired == 1
    assert after.cycle == before + 1
    assert after.stroke_state in ("FiredLockout", "Idle", "Tracking")


# ---------- item 6: headless bench smoke test through F (no camera) ----------
class _FakeCamera:
    name = "Integrated Camera (fake)"

    def read_mirrored(self):
        return np.zeros((480, 640, 3), dtype=np.uint8)

    def release(self):
        pass


class _FakeBackend:
    """Scripted hand stream: v-sign held, with upward strokes in set windows."""
    def __init__(self):
        self.i = -1

    def _center(self, f):
        if 86 <= f <= 93:                 # motion teaching stroke (up)
            return (0.5, 0.55 - 0.03 * (f - 86))
        if 118 <= f <= 140:               # proof stroke + repeat (up)
            return (0.5, 0.52 - 0.02 * (f - 118))
        return (0.5, 0.5)

    def process(self, rgb, ts):
        self.i += 1
        lm = synth.v_sign(center=self._center(self.i))
        return HandResult(True, lm, "Right", 1.0, None, 0.0)

    def close(self):
        pass


def test_headless_bench_smoke_through_finalize():
    # deterministic fake clock so teaching TAKE_SECONDS elapses in fixed frames
    state = {"t": 0.0}

    def clock():
        state["t"] += 0.05
        return state["t"]

    keys = {2: ord("j"), 30: ord("j"), 58: ord("j"),
            86: ord("m"), 94: ord("e"), 100: ord("f")}
    probe = {}
    rc = bench_app.run(camera_index=None, camera=_FakeCamera(),
                       backend=_FakeBackend(), key_script=keys,
                       max_frames=160, headless=True, clock=clock, probe=probe)
    assert rc == 0
    teach = probe["teach"]
    # reached at least the proof stage after F (model built, no crash on draw)
    assert teach.jump_model is not None
    assert probe["frames"] >= 155
    assert teach.proof_state in ("pose", "motion", "ready")
