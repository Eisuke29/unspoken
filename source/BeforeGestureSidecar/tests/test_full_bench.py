"""Headless full bench: teach Jump + Crouch + Stop, no camera, no crash (D-112)."""
import numpy as np

from before_gesture import bench_app
from before_gesture.mediapipe_backend import HandResult
import synth


class _FakeCamera:
    name = "Integrated Camera (fake)"

    def read_mirrored(self):
        return np.zeros((480, 640, 3), dtype=np.uint8)

    def release(self):
        pass


class _FakeBackend:
    def __init__(self):
        self.i = -1

    def _pose(self, f):
        if f < 86:
            return "v", (0.5, 0.5)
        if f < 95:
            return "v", (0.5, 0.55 - 0.03 * (f - 86))       # jump motion up
        if f < 122:
            return "v", (0.5, 0.5)
        if f < 132:
            return "v", (0.5, 0.50 - 0.03 * (f - 122))      # proof up
        if f < 216:
            return "open", (0.5, 0.5)                       # crouch takes
        if f < 226:
            return "open", (0.5, 0.50 + 0.03 * (f - 216))   # crouch motion down
        if f < 232:
            return "open", (0.5, 0.5)
        return "fist", (0.5, 0.5)                           # stop takes

    def process(self, rgb, ts):
        self.i += 1
        kind, center = self._pose(self.i)
        fn = {"v": synth.v_sign, "open": synth.open_hand, "fist": synth.fist}[kind]
        return HandResult(True, fn(center=center), "Right", 1.0, None, 0.0)

    def close(self):
        pass


def test_headless_full_bench_all_three_commands():
    state = {"t": 0.0}

    def clock():
        state["t"] += 0.05
        return state["t"]

    keys = {2: ord("j"), 30: ord("j"), 58: ord("j"),
            86: ord("m"), 94: ord("e"), 98: ord("f"),
            132: ord("c"), 160: ord("c"), 188: ord("c"),
            216: ord("v"), 224: ord("b"), 228: ord("g"),
            232: ord("s"), 260: ord("s"), 288: ord("s"), 316: ord("S")}
    probe = {}
    rc = bench_app.run(camera_index=None, camera=_FakeCamera(),
                       backend=_FakeBackend(), key_script=keys,
                       max_frames=340, headless=True, clock=clock, probe=probe)
    assert rc == 0
    teach = probe["teach"]
    # all three vocabularies were taught without a crash on the draw path
    assert teach.jump_model is not None
    assert teach.crouch_model is not None
    assert teach.stop_model is not None
    assert len(teach.jump_takes) == 3
    assert len(teach.crouch_takes) == 3
    assert len(teach.stop_takes) == 3
    assert probe["frames"] >= 335


class _StopFailBackend:
    """First Stop take is unstable (rejected at record time); later takes stable;
    then a stable fist (STOP_ON) followed by a different pose (STOP_OFF)."""
    def __init__(self):
        self.i = -1

    def process(self, rgb, ts):
        self.i += 1
        f = self.i
        if f < 30:
            lm = synth.fist(center=(0.5, 0.5), jitter=0.009, seed=f)   # unstable
        elif f < 140:
            lm = synth.fist(center=(0.5, 0.5), jitter=0.002, seed=f)   # stable
        else:
            lm = synth.v_sign(center=(0.5, 0.5))                        # mismatch
        return HandResult(True, lm, "Right", 1.0, None, 0.0)

    def close(self):
        pass


class _CrouchDirBackend:
    """Teach Crouch, fire a CROUCH stroke, then point for Direction."""
    def __init__(self):
        self.i = -1

    def process(self, rgb, ts):
        self.i += 1
        f = self.i
        if f < 84:
            lm = synth.open_hand(center=(0.5, 0.5))                 # crouch takes
        elif f < 98:
            lm = synth.open_hand(center=(0.5, 0.50 + 0.02 * (f - 84)))  # motion down
        elif f < 110:
            lm = synth.open_hand(center=(0.5, 0.5))                 # hold / stabilize
        elif f < 120:
            lm = synth.open_hand(center=(0.5, 0.50 + 0.03 * (f - 110)))  # fire stroke
        elif f < 140:
            lm = synth.open_hand(center=(0.5, 0.5))
        else:
            lm = synth.point_hand(sign=1)                          # Direction (right)
        return HandResult(True, lm, "Right", 1.0, None, 0.0)

    def close(self):
        pass


def test_headless_crouch_fire_and_direction():
    state = {"t": 0.0}

    def clock():
        state["t"] += 0.05
        return state["t"]

    keys = {2: ord("c"), 30: ord("c"), 58: ord("c"),
            88: ord("v"), 96: ord("b"), 100: ord("g"), 140: ord("d")}
    probe = {}
    rc = bench_app.run(camera_index=None, camera=_FakeCamera(),
                       backend=_CrouchDirBackend(), key_script=keys,
                       max_frames=175, headless=True, clock=clock, probe=probe)
    assert rc == 0
    teach = probe["teach"]
    overlay = probe["overlay"]
    assert teach.crouch_model is not None
    assert overlay.counts.get("CROUCH", 0) >= 1                    # discrete CROUCH fired
    assert overlay.counts.get("RIGHT", 0) + overlay.counts.get("LEFT", 0) >= 1


def test_headless_stop_fail_retake_finalize_onoff():
    state = {"t": 0.0}

    def clock():
        state["t"] += 0.05
        return state["t"]

    # s@2 (unstable -> rejected), s@30/58/86 (3 stable), Shift+S@114 finalize
    keys = {2: ord("s"), 30: ord("s"), 58: ord("s"), 86: ord("s"),
            114: ord("S")}
    probe = {}
    rc = bench_app.run(camera_index=None, camera=_FakeCamera(),
                       backend=_StopFailBackend(), key_script=keys,
                       max_frames=175, headless=True, clock=clock, probe=probe)
    assert rc == 0
    teach = probe["teach"]
    overlay = probe["overlay"]
    assert teach.stop_rejected >= 1                 # unstable take was rejected
    assert len(teach.stop_takes) == 3               # re-recorded to a full set
    assert teach.stop_model is not None             # finalized after retake
    assert overlay.counts.get("STOP_ON", 0) >= 1    # ON fired on stable fist
    assert overlay.counts.get("STOP_OFF", 0) >= 1   # OFF fired on mismatch
