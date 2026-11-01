"""D-111 rhythmic Jump re-arm: up -> small return (no full stop) -> up ..."""
import numpy as np

from before_gesture import personal_pose as pp
from before_gesture import motion_primitive as mp
from before_gesture import command_engine as ce
from before_gesture import protocol
import synth

PALM_IDS = (0, 5, 9, 13, 17)


def _palm(lm):
    a = np.asarray(lm)[:, :2]
    return tuple(a[list(PALM_IDS)].mean(axis=0))


def _engine():
    takes = [pp.Recording([pp.FrameSample(synth.v_sign(jitter=0.001, seed=k * 100 + i).tolist(),
                                          0.9) for i in range(25)]) for k in range(3)]
    m = pp.register_pose("JUMP", takes)
    jm = mp.learn_jump_motion([[(0.5, 0.5), (0.5, 0.34)]] * 3, [0.2, 0.2, 0.2])  # up
    return ce.Engine(cfg=ce.EngineConfig(jump_pose=m, jump_motion=jm))


def _obs(y, t):
    lm = synth.v_sign(center=(0.5, y))
    return ce.FrameObs(t=t, tracked=True, quality=0.9, landmarks=lm.tolist(),
                       palm=_palm(lm))


def _drive(eng, ys, t, dt=0.05):
    ids = []
    broke = False
    for y in ys:
        for c in eng.step(_obs(y, t)):
            if c.type.value == "JUMP":
                ids.append(c.id)
        if eng.jstate == ce.JumpState.POSE_SEARCHING:
            broke = True
        t += dt
    return t, ids, broke


def test_9_10_17_rhythmic_five_reps_no_full_stop():
    protocol.reset_ids()
    eng = _engine()
    t, _, _ = _drive(eng, [0.5] * 6, 0.0)      # arm the pose (held, no stroke)
    ys, y = [], 0.50
    for _ in range(5):                          # up (fire) -> partial return
        for _ in range(4):
            y -= 0.06; ys.append(round(y, 3))   # forward, never stopping
        for _ in range(4):
            y += 0.05; ys.append(round(y, 3))   # backward return, never stopping
    t, ids, broke = _drive(eng, ys, t)
    assert ids == [1, 2, 3, 4, 5]               # each stroke fires exactly once
    assert not broke                            # pose never left SEARCHING
    assert eng.cycle_count == 5


def test_11_two_frame_settle_rearm():
    eng = _engine()
    t, _, _ = _drive(eng, [0.5] * 6, 0.0)
    # cycle 1 up -> fire
    t, ids1, _ = _drive(eng, [0.44, 0.38, 0.32, 0.26], t)
    # brief 2-frame near-settle (NOT a full 4-frame stop), then up again
    t, ids2, _ = _drive(eng, [0.26, 0.26, 0.20, 0.14, 0.08], t)
    assert len(ids1) == 1 and len(ids2) == 1


def test_12_one_long_stroke_one_fire():
    eng = _engine()
    t, _, _ = _drive(eng, [0.5] * 6, 0.0)
    ys = [round(0.50 - 0.03 * i, 3) for i in range(14)]  # one continuous up
    t, ids, _ = _drive(eng, ys, t)
    assert len(ids) == 1


def test_13_return_only_no_fire():
    eng = _engine()
    t, _, _ = _drive(eng, [0.5] * 6, 0.0)
    # purely downward (backward = the return direction) -> no stroke, no fire
    t, ids, _ = _drive(eng, [0.56, 0.62, 0.68, 0.74, 0.80], t)
    assert ids == []


def test_14_jitter_ten_seconds_no_fire():
    eng = _engine()
    t = 0.0
    fires = 0
    rng = np.random.default_rng(4)
    for _ in range(300):
        y = 0.5 + float(rng.normal(0, 0.006))
        for c in eng.step(_obs(y, t)):
            fires += 1
        t += 0.033
    assert fires == 0


def test_15_small_oscillation_below_required_no_fire():
    eng = _engine()
    t, _, _ = _drive(eng, [0.5] * 6, 0.0)
    ys = []
    for _ in range(10):                # small up/down, each well below required
        ys += [0.49, 0.48, 0.49, 0.50]
    t, ids, _ = _drive(eng, ys, t)
    assert ids == []


def test_16_fast_reversal_under_150ms_no_double():
    eng = _engine()
    t, _, _ = _drive(eng, [0.5] * 6, 0.0)
    # up -> fire, then a very fast down/up within the 150 ms lockout (dt 0.02)
    t, ids, _ = _drive(eng, [0.44, 0.38, 0.30], t, dt=0.02)
    for y in (0.36, 0.30, 0.36, 0.30):   # ~160 ms of jitter, still near lockout
        for c in eng.step(_obs(y, t)):
            if c.type.value == "JUMP":
                ids.append(c.id)
        t += 0.02
    assert len(ids) == 1
