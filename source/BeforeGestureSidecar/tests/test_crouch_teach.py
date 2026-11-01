"""D-112 Crouch teaching through TeachController (3 takes + motion, separation)."""
import numpy as np

from before_gesture import personal_pose as pp
from before_gesture import command_engine as ce
from before_gesture import teach_controller as tc
import synth

PALM_IDS = (0, 5, 9, 13, 17)


def _palm(lm):
    a = np.asarray(lm)[:, :2]
    return tuple(a[list(PALM_IDS)].mean(axis=0))


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


def _teach_crouch(ctl, t, pose_fn=synth.open_hand, ys=(0.55, 0.60, 0.66)):
    for _ in range(3):
        t = _capture(ctl, "c", lambda i: pose_fn(jitter=0.001, seed=i), t + 1.0)
    ctl.on_key("v", t + 0.2); t += 0.3
    for cy in ys:                       # crouch motion (down)
        lm = pose_fn(center=(0.5, cy)); ctl.feed(lm.tolist(), 0.9, _palm(lm), t); t += 0.05
    ctl.on_key("b", t); t += 0.05
    ctl.on_key("g", t + 0.2); t += 0.3
    return t


def _teach_jump(ctl, t):
    for _ in range(3):
        t = _capture(ctl, "j", lambda i: synth.v_sign(jitter=0.001, seed=i), t + 1.0)
    ctl.on_key("m", t + 0.2); t += 0.3
    for cy in (0.55, 0.50, 0.45, 0.40, 0.35):
        lm = synth.v_sign(center=(0.5, cy)); ctl.feed(lm.tolist(), 0.9, _palm(lm), t); t += 0.05
    ctl.on_key("e", t); t += 0.05
    ctl.on_key("f", t + 0.2); t += 0.5
    for _ in range(12):
        lm = synth.v_sign(center=(0.5, 0.5)); ctl.feed(lm.tolist(), 0.9, _palm(lm), t); t += 0.1
    return t


def test_crouch_teach_three_takes_and_fire():
    ctl = tc.TeachController()
    t = _teach_crouch(ctl, 0.0)
    assert len(ctl.crouch_takes) == 3
    assert ctl.crouch_model is not None and ctl.crouch_motion is not None
    # live: hold Crouch pose (5/7 stable) then a down stroke -> one CROUCH (D-128)
    fired = 0
    for _ in range(8):
        lm = synth.open_hand(center=(0.5, 0.5))
        for c in ctl.feed(lm.tolist(), 0.9, _palm(lm), t):
            if c.type.value == "CROUCH":
                fired += 1
        t += 0.1
    for cy in (0.58, 0.66, 0.70):
        lm = synth.open_hand(center=(0.5, cy))
        for c in ctl.feed(lm.tolist(), 0.9, _palm(lm), t):
            if c.type.value == "CROUCH":
                fired += 1
        t += 0.05
    assert fired == 1 and ctl.engine.crouch.cycle_count == 1


def test_crouch_fourth_take_not_accepted():
    ctl = tc.TeachController()
    t = 0.0
    for _ in range(5):
        t = _capture(ctl, "c", lambda i: synth.open_hand(jitter=0.001, seed=i), t + 1.0)
    assert len(ctl.crouch_takes) == 3


def test_13_similar_crouch_rejected_leaves_models_unchanged():
    ctl = tc.TeachController()
    t = _teach_jump(ctl, 0.0)
    assert ctl.jump_model is not None
    jump_before = ctl.jump_model
    radius_before = ctl.jump_model.radius
    # teach Crouch using the SAME sign as Jump -> must reject crouch only
    t = _teach_crouch(ctl, t, pose_fn=synth.v_sign, ys=(0.55, 0.60, 0.66))
    assert ctl.crouch_model is None                      # crouch rejected
    assert ctl.jump_model is jump_before                 # jump object unchanged
    assert ctl.jump_model.radius == radius_before        # jump radius unchanged
    assert "REJECTED" in ctl.big_message(t)


def test_reset_clears_crouch():
    ctl = tc.TeachController()
    t = _teach_crouch(ctl, 0.0)
    assert ctl.crouch_model is not None
    ctl.on_key("r", t + 0.2)
    assert ctl.crouch_model is None and ctl.crouch_takes == []
    assert ctl.crouch_motion is None and ctl.crouch_rejected == 0
