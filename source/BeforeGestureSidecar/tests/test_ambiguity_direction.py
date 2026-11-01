"""Direction exclusivity (D-103/§14) and ambiguity = no fire (D-112/§Ambiguity)."""
import numpy as np

from before_gesture import personal_pose as pp
from before_gesture import motion_primitive as mp
from before_gesture import command_engine as ce
import synth

PALM_IDS = (0, 5, 9, 13, 17)


def _palm(lm):
    a = np.asarray(lm)[:, :2]
    return tuple(a[list(PALM_IDS)].mean(axis=0))


def _model(fn, name, seed0=0):
    takes = [pp.Recording([pp.FrameSample(fn(jitter=0.001, seed=seed0 + k * 100 + i).tolist(),
                                          0.9) for i in range(25)]) for k in range(3)]
    return pp.register_pose(name, takes)


def _full_engine():
    jp = _model(synth.v_sign, "JUMP", 0)
    cp = _model(synth.open_hand, "CROUCH", 3000)
    sp = _model(synth.fist, "STOP", 7000)
    pp.constrain_separation(jp, cp)
    pp.constrain_separation(jp, sp)
    pp.constrain_separation(cp, sp)
    jm = mp.learn_jump_motion([[(0.5, 0.5), (0.5, 0.34)]] * 3, [0.2] * 3)
    cm = mp.learn_jump_motion([[(0.5, 0.5), (0.5, 0.66)]] * 3, [0.2] * 3)
    return ce.Engine(cfg=ce.EngineConfig(jump_pose=jp, jump_motion=jm,
                                         crouch_pose=cp, crouch_motion=cm,
                                         stop_pose=sp))


def test_14_direction_mode_suppresses_jump_crouch_stop():
    eng = _full_engine()
    eng.cfg.mode_direction = True
    t = 0.0
    banned = {"JUMP", "CROUCH", "STOP_ON", "STOP_OFF"}
    seen = 0
    for kind, fn in (("v", synth.v_sign), ("open", synth.open_hand),
                     ("fist", synth.fist)):
        for _ in range(10):
            lm = fn(center=(0.5, 0.5))
            for c in eng.step(ce.FrameObs(t=t, tracked=True, quality=0.9,
                                          landmarks=lm.tolist(), palm=_palm(lm))):
                if c.type.value in banned:
                    seen += 1
            t += 0.1
    assert seen == 0
    assert not eng.stop_active
    assert eng.crouch.cycle_count == 0   # Crouch never fired in Direction mode


def test_direction_left_right_still_works_in_mode():
    eng = _full_engine()
    eng.cfg.mode_direction = True
    t = 0.0
    got = []
    for _ in range(8):
        lm = synth.point_hand(sign=1)
        got += eng.step(ce.FrameObs(t=t, tracked=True, quality=0.9,
                                    landmarks=lm.tolist()))
        t += 0.1
    assert any(c.type.value == "DIRECTION" and c.direction == 1 for c in got)


def test_ambiguous_two_classes_no_fire():
    # two overlapping (NOT separation-constrained) classes -> ambiguous -> no fire
    a = _model(synth.v_sign, "JUMP", 0)
    b = _model(synth.v_sign, "CROUCH", 50)   # same shape, different name
    jm = mp.learn_jump_motion([[(0.5, 0.5), (0.5, 0.34)]] * 3, [0.2] * 3)
    eng = ce.Engine(cfg=ce.EngineConfig(jump_pose=a, jump_motion=jm,
                                        crouch_pose=b, crouch_motion=jm))
    feat = pp.feature_vector(synth.v_sign().tolist())
    m = pp.match_pose(feat, [a, b])
    assert not m.armed and m.reason == pp.R_AMBIG
    assert m.best_dist < m.second_dist  # candidate distances are separated in the snapshot
    # engine: hold + stroke -> neither JUMP nor CROUCH fires (ambiguous)
    t = 0.0
    fired = 0
    for _ in range(12):
        lm = synth.v_sign(center=(0.5, 0.5))
        for c in eng.step(ce.FrameObs(t=t, tracked=True, quality=0.9,
                                      landmarks=lm.tolist(), palm=_palm(lm))):
            fired += 1
        t += 0.1
    for cy in (0.42, 0.30, 0.28):
        lm = synth.v_sign(center=(0.5, cy))
        for c in eng.step(ce.FrameObs(t=t, tracked=True, quality=0.9,
                                      landmarks=lm.tolist(), palm=_palm(lm))):
            fired += 1
        t += 0.05
    assert fired == 0
