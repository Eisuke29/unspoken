"""D-128 Crouch = discrete Pose B + Motion B stroke channel (Jump-shaped).

Replaces the obsolete D-112 hold/ON-OFF Crouch tests: Crouch no longer holds a
state, emits no CROUCH_OFF, and never releases on time/pose-drift. It fires a
single `CROUCH` per learned forward stroke, repeatable while Pose B is held.
"""
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


def _model(fn, name, seed0=0):
    takes = [pp.Recording([pp.FrameSample(fn(jitter=0.001, seed=seed0 + k * 100 + i).tolist(),
                                          0.9) for i in range(25)]) for k in range(3)]
    return pp.register_pose(name, takes)


def _engine(with_jump_stop=False):
    cp = _model(synth.open_hand, "CROUCH", 3000)
    cm = mp.learn_jump_motion([[(0.5, 0.5), (0.5, 0.66)]] * 3, [0.2] * 3)  # DOWN
    cfg = ce.EngineConfig(crouch_pose=cp, crouch_motion=cm)
    if with_jump_stop:
        cfg.jump_pose = _model(synth.v_sign, "JUMP", 0)
        cfg.jump_motion = mp.learn_jump_motion([[(0.5, 0.5), (0.5, 0.34)]] * 3, [0.2] * 3)
        cfg.stop_pose = _model(synth.fist, "STOP", 7000)
        pp.constrain_separation(cfg.jump_pose, cp)
        pp.constrain_separation(cfg.jump_pose, cfg.stop_pose)
        pp.constrain_separation(cp, cfg.stop_pose)
    return ce.Engine(cfg=cfg)


def _obs(kind, y, t, tracked=True):
    if not tracked:
        return ce.FrameObs(t=t, tracked=False, quality=0.0, landmarks=None, palm=None)
    fn = {"open": synth.open_hand, "v": synth.v_sign, "fist": synth.fist}[kind]
    lm = fn(center=(0.5, y))
    return ce.FrameObs(t=t, tracked=True, quality=0.9, landmarks=lm.tolist(),
                       palm=_palm(lm))


def _drive(eng, kind, ys, t, dt=0.05):
    ids, broke = [], False
    for y in ys:
        for c in eng.step(_obs(kind, y, t)):
            if c.type.value == "CROUCH":
                ids.append(c.id)
        if eng.crouch.state == ce.CrouchState.POSE_SEARCHING:
            broke = True
        t += dt
    return t, ids, broke


def _arm(eng, t):
    return _drive(eng, "open", [0.5] * 8, t)[0]      # hold Pose B (5/7 stable)


def test_1_pose_plus_motion_fires_once():
    eng = _engine()
    t = _arm(eng, 0.0)
    t, ids, _ = _drive(eng, "open", [0.50, 0.58, 0.66, 0.70], t)  # down stroke
    assert len(ids) == 1 and eng.crouch.cycle_count == 1


def test_2_3_17_five_reps_same_pose_ids_consecutive():
    protocol.reset_ids()
    eng = _engine()
    t = _arm(eng, 0.0)
    ys, y = [], 0.50
    for _ in range(5):
        for _ in range(4):
            y += 0.06; ys.append(round(y, 3))    # forward = DOWN
        for _ in range(4):
            y -= 0.05; ys.append(round(y, 3))    # return = UP (no fire)
    t, ids, broke = _drive(eng, "open", ys, t)
    assert ids == [1, 2, 3, 4, 5]
    assert not broke and eng.crouch.cycle_count == 5   # never needed another pose


def test_4_rhythmic_return_15pct_no_full_stop():
    eng = _engine()
    t = _arm(eng, 0.0)
    t, ids1, _ = _drive(eng, "open", [0.56, 0.62, 0.68, 0.72], t)   # down -> fire
    # partial return (up, 4 frames) then down again, never fully stopping
    t, ids2, _ = _drive(eng, "open", [0.66, 0.60, 0.54, 0.48, 0.54, 0.62, 0.70], t)
    assert len(ids1) == 1 and len(ids2) == 1


def test_5_two_frame_settle_rearm():
    eng = _engine()
    t = _arm(eng, 0.0)
    t, ids1, _ = _drive(eng, "open", [0.56, 0.62, 0.68, 0.74], t)
    t, ids2, _ = _drive(eng, "open", [0.74, 0.74, 0.80, 0.86, 0.92], t)  # 2-frame near-still
    assert len(ids1) == 1 and len(ids2) == 1


def test_6_one_long_stroke_one_fire():
    eng = _engine()
    t = _arm(eng, 0.0)
    ys = [round(0.50 + 0.03 * i, 3) for i in range(14)]  # continuous down
    t, ids, _ = _drive(eng, "open", ys, t)
    assert len(ids) == 1


def test_7_return_only_no_fire():
    eng = _engine()
    t = _arm(eng, 0.0)
    t, ids, _ = _drive(eng, "open", [0.44, 0.38, 0.32, 0.26, 0.20], t)  # UP only
    assert ids == []


def test_8_jitter_ten_seconds_no_fire():
    eng = _engine()
    t = _arm(eng, 0.0)
    fires = 0
    rng = np.random.default_rng(5)
    for _ in range(300):
        y = 0.5 + float(rng.normal(0, 0.006))
        for c in eng.step(_obs("open", y, t)):
            fires += 1
        t += 0.033
    assert fires == 0


def test_9_small_oscillation_below_required_no_fire():
    eng = _engine()
    t = _arm(eng, 0.0)
    ys = []
    for _ in range(10):
        ys += [0.51, 0.52, 0.51, 0.50]
    t, ids, _ = _drive(eng, "open", ys, t)
    assert ids == []


def test_10_fast_reversal_under_150ms_no_double():
    eng = _engine()
    t = _arm(eng, 0.0)
    t, ids, _ = _drive(eng, "open", [0.56, 0.62, 0.70], t, dt=0.02)  # fire
    for y in (0.64, 0.70, 0.64, 0.70):
        for c in eng.step(_obs("open", y, t)):
            if c.type.value == "CROUCH":
                ids.append(c.id)
        t += 0.02
    assert len(ids) == 1


def test_11_pose_only_no_fire():
    eng = _engine()
    t = _arm(eng, 0.0)
    fires = 0
    for _ in range(20):
        for c in eng.step(_obs("open", 0.5, t)):
            fires += 1
        t += 0.1
    assert fires == 0


def test_12_motion_only_wrong_pose_no_fire():
    eng = _engine()
    t = 0.0
    fires = 0
    for y in [0.50, 0.58, 0.66, 0.72]:   # downward MOTION but pose = v (not open)
        for c in eng.step(_obs("v", y, t)):
            fires += 1
        t += 0.05
    assert fires == 0


def test_13_wrong_direction_no_fire():
    eng = _engine()
    t = _arm(eng, 0.0)
    t, ids, _ = _drive(eng, "open", [0.50, 0.42, 0.34, 0.28], t)  # UP = wrong dir
    assert ids == []


def test_14_no_crouch_off_after_pose_break():
    eng = _engine()
    t = _arm(eng, 0.0)
    t, ids, _ = _drive(eng, "open", [0.56, 0.62, 0.70], t)
    assert len(ids) == 1
    # break the pose (switch to a different tracked pose) -> no output at all
    extra = 0
    for _ in range(15):
        for c in eng.step(_obs("v", 0.5, t)):
            extra += 1
        t += 0.1
    assert extra == 0


def test_15_no_output_over_time_while_held():
    eng = _engine()
    t = _arm(eng, 0.0)
    t, ids, _ = _drive(eng, "open", [0.56, 0.62, 0.70], t)
    assert len(ids) == 1
    extra = 0
    for _ in range(60):   # hold the pose ~3 s with tiny jitter
        lm = synth.open_hand(center=(0.5, 0.70), jitter=0.0015, seed=1)
        for c in eng.step(ce.FrameObs(t=t, tracked=True, quality=0.9,
                                      landmarks=lm.tolist(), palm=_palm(lm))):
            extra += 1
        t += 0.05
    assert extra == 0


def test_tracking_loss_no_output_and_resets():
    eng = _engine()
    t = _arm(eng, 0.0)
    t, ids, _ = _drive(eng, "open", [0.56, 0.62, 0.70], t)
    for _ in range(12):
        cs = eng.step(_obs("open", 0.0, t, tracked=False))
        assert cs == []
        t += 0.1
