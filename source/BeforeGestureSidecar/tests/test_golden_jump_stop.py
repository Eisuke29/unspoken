"""GOLDEN freeze for Jump + Stop (D-128 forbids changing them).

If the Crouch rework (or any future change) alters a Jump/Stop constant, its
required-displacement formula, its state machine, or its representative I/O,
these tests fail. Jump/Stop behaviour must be byte-for-byte stable.
"""
import numpy as np

from before_gesture import personal_pose as pp
from before_gesture import motion_primitive as mp
from before_gesture import command_engine as ce
from before_gesture import protocol

PALM_IDS = (0, 5, 9, 13, 17)


def _palm(lm):
    a = np.asarray(lm)[:, :2]
    return tuple(a[list(PALM_IDS)].mean(axis=0))


# ---- frozen constants ------------------------------------------------------
def test_golden_jump_constants():
    assert ce.STABLE_WINDOW == 7
    assert ce.STABLE_NEEDED == 5
    assert ce.COS_PASS == 0.90
    assert ce.FIRE_LOCKOUT_S == 0.15
    assert ce.START_DISP_FRAC == 0.25
    assert ce.START_SPEED_FRAC == 0.30
    assert ce.START_FORWARD_COS == 0.30
    assert ce.REJECT_DISP_FRAC == 0.25
    assert ce.REARM_REVERSE_FRAC == 0.15
    assert ce.REARM_REVERSE_FRAMES == 2
    assert ce.REARM_SETTLE_SPEED_FRAC == 0.25
    assert ce.REARM_SETTLE_FRAMES == 2
    assert ce.SETTLE_SPEED_FRAC == 0.15
    assert ce.SETTLE_FRAMES == 4


def test_golden_stop_constants():
    assert ce.STOP_ON_EVIDENCE == 4
    assert ce.STOP_OFF_EVIDENCE == 4
    assert ce.STOP_MATCH_RADIUS_K == 1.0


def test_golden_required_displacement_formula():
    # required displacement = median stroke displacement * 0.55 (unchanged)
    m = mp.learn_jump_motion([[(0.5, 0.5), (0.5, 0.34)]] * 3, [0.2, 0.2, 0.2])
    assert abs(m.disp_threshold - 0.16 * 0.55) < 1e-9
    assert abs(m.vel_threshold - (0.16 / 0.2) * 0.45) < 1e-9
    assert m.dir8 == 6


# ---- frozen representative I/O --------------------------------------------
import synth  # noqa: E402


def _jump_engine():
    takes = [pp.Recording([pp.FrameSample(synth.v_sign(jitter=0.001, seed=k * 100 + i).tolist(),
                                          0.9) for i in range(25)]) for k in range(3)]
    m = pp.register_pose("JUMP", takes)
    jm = mp.learn_jump_motion([[(0.5, 0.5), (0.5, 0.34)]] * 3, [0.2, 0.2, 0.2])
    return ce.Engine(cfg=ce.EngineConfig(jump_pose=m, jump_motion=jm))


def _obs(kind, y, t, tracked=True):
    if not tracked:
        return ce.FrameObs(t=t, tracked=False, quality=0.0, landmarks=None, palm=None)
    lm = (synth.v_sign if kind == "v" else synth.fist)(center=(0.5, y))
    return ce.FrameObs(t=t, tracked=True, quality=0.9, landmarks=lm.tolist(),
                       palm=_palm(lm))


def test_golden_jump_five_rhythmic_reps():
    protocol.reset_ids()
    eng = _jump_engine()
    t = 0.0
    for _ in range(6):
        eng.step(_obs("v", 0.5, t)); t += 0.05
    ys, y = [], 0.50
    for _ in range(5):
        for _ in range(4):
            y -= 0.06; ys.append(round(y, 3))
        for _ in range(4):
            y += 0.05; ys.append(round(y, 3))
    ids = []
    for yy in ys:
        for c in eng.step(_obs("v", yy, t)):
            if c.type.value == "JUMP":
                ids.append(c.id)
        assert eng.jstate != ce.JumpState.POSE_SEARCHING
        t += 0.05
    assert ids == [1, 2, 3, 4, 5]


def test_golden_jump_long_stroke_one_and_pose_only_zero():
    eng = _jump_engine()
    t = 0.0
    for _ in range(6):
        eng.step(_obs("v", 0.5, t)); t += 0.05
    fires = 0
    for i in range(14):
        for c in eng.step(_obs("v", round(0.50 - 0.03 * i, 3), t)):
            fires += 1
        t += 0.05
    assert fires == 1
    eng2 = _jump_engine()
    t = 0.0
    for _ in range(20):
        assert eng2.step(_obs("v", 0.5, t)) == []   # pose held, no motion
        t += 0.1


def _stop_engine():
    stop = pp.register_pose("STOP", [pp.Recording([pp.FrameSample(
        synth.fist(jitter=0.001, seed=i).tolist(), 0.9) for i in range(25)])
        for _ in range(3)])
    return ce.Engine(cfg=ce.EngineConfig(stop_pose=stop))


def test_golden_stop_on_off_and_tracking_loss():
    eng = _stop_engine()
    t = 0.0
    on = 0
    for _ in range(6):
        on += sum(1 for c in eng.step(_obs("fist", 0.5, t)) if c.type.value == "STOP_ON")
        t += 0.05
    assert on == 1 and eng.stop_active
    # tracking loss must NOT release
    for _ in range(10):
        cs = eng.step(_obs("fist", 0.5, t, tracked=False))
        assert not any(c.type.value == "STOP_OFF" for c in cs)
        t += 0.05
    assert eng.stop_active
    # a tracked mismatch releases after 4 frames
    off = 0
    for _ in range(6):
        off += sum(1 for c in eng.step(_obs("v", 0.5, t)) if c.type.value == "STOP_OFF")
        t += 0.05
    assert off == 1 and not eng.stop_active
