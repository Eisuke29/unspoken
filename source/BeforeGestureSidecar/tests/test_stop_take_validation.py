"""Stop take validation parity (Human Gate A fix): capture and finalize use the
SAME validator, an unstable take is rejected at RECORD time, and a defensive
finalize drops only the bad take without dead-ending the model or touching
Jump/Crouch."""
import numpy as np

from before_gesture import personal_pose as pp
from before_gesture import command_engine as ce
from before_gesture import teach_controller as tc
import synth

PALM_IDS = (0, 5, 9, 13, 17)
UNSTABLE_J = 0.009   # -> disp ~0.155 > 0.14
STABLE_J = 0.002


def _palm(lm):
    a = np.asarray(lm)[:, :2]
    return tuple(a[list(PALM_IDS)].mean(axis=0))


def _rec(fn, jitter):
    return pp.Recording([pp.FrameSample(fn(jitter=jitter, seed=i).tolist(), 0.9)
                         for i in range(25)])


# ---- shared validator -----------------------------------------------------
def test_unstable_take_disp_over_threshold_is_invalid():
    ok, disp, n, reason = pp.take_stability(_rec(synth.fist, UNSTABLE_J))
    assert not ok and reason == "UNSTABLE" and disp > pp.WITHIN_CLASS_DISP_MAX


def test_stable_take_under_threshold_is_valid():
    ok, disp, n, reason = pp.take_stability(_rec(synth.fist, STABLE_J))
    assert ok and disp <= pp.WITHIN_CLASS_DISP_MAX


def test_capture_and_finalize_use_same_validator():
    # register_pose must reject exactly what take_stability rejects
    good = [_rec(synth.fist, STABLE_J) for _ in range(2)]
    bad = _rec(synth.fist, UNSTABLE_J)
    ok, disp, n, reason = pp.take_stability(bad)
    assert not ok
    try:
        pp.register_pose("STOP", good + [bad])
        raised = False
    except pp.RegistrationError as exc:
        raised = "unstable" in str(exc)
    assert raised


# ---- teach controller: record-time rejection ------------------------------
def _capture(ctl, key, fn, jitter, t0, dt=0.05):
    ctl.on_key(key, t0)
    t = t0
    for i in range(40):
        if ctl.capturing is None:
            break
        lm = fn(jitter=jitter, seed=i)
        ctl.feed(lm.tolist(), 0.9, _palm(lm), t)
        t += dt
    return t


def test_unstable_stop_take_rejected_at_record_time():
    ctl = tc.TeachController()
    t = _capture(ctl, "s", synth.fist, UNSTABLE_J, 1.0)
    assert len(ctl.stop_takes) == 0        # not accepted
    assert ctl.stop_rejected == 1
    assert "UNSTABLE" in ctl.big_message(t)


def _teach_stop_ok(ctl, t):
    for _ in range(3):
        t = _capture(ctl, "s", synth.fist, STABLE_J, t + 1.0)
    return t


def test_defensive_finalize_drops_only_bad_take_rolls_to_two():
    ctl = tc.TeachController()
    # inject one unstable + two stable takes directly (simulate a legacy bad take
    # that slipped in before validation), then finalize.
    ctl.stop_takes = [_rec(synth.fist, UNSTABLE_J), _rec(synth.fist, STABLE_J),
                      _rec(synth.fist, STABLE_J)]
    ctl.finalize_stop(2.0)
    assert len(ctl.stop_takes) == 2                # rolled 3/3 -> 2/3
    assert ctl.stop_model is None                  # not dead-ended, just waiting
    assert "INVALID" in ctl.big_message(2.0) and "RETAKE" in ctl.big_message(2.0)


def test_reretake_then_finalize_succeeds():
    ctl = tc.TeachController()
    ctl.stop_takes = [_rec(synth.fist, UNSTABLE_J), _rec(synth.fist, STABLE_J),
                      _rec(synth.fist, STABLE_J)]
    ctl.finalize_stop(2.0)
    assert len(ctl.stop_takes) == 2
    # re-record the missing sample with S, then finalize
    t = _capture(ctl, "s", synth.fist, STABLE_J, 3.0)
    assert len(ctl.stop_takes) == 3
    ctl.finalize_stop(t + 0.2)
    assert ctl.stop_model is not None


def test_stop_repair_leaves_jump_crouch_and_stats_unchanged():
    ctl = tc.TeachController()
    # give Jump + Crouch finalized models and some QA-like counters
    jp = pp.register_pose("JUMP", [_rec(synth.v_sign, STABLE_J) for _ in range(3)])
    cp = pp.register_pose("CROUCH", [_rec(synth.open_hand, STABLE_J) for _ in range(3)])
    ctl.jump_model, ctl.crouch_model = jp, cp
    ctl.jump_rejected, ctl.crouch_rejected = 0, 0
    j_id, c_id = id(ctl.jump_model), id(ctl.crouch_model)
    j_r = jp.radius
    # a bad stop finalize must not touch them
    ctl.stop_takes = [_rec(synth.fist, UNSTABLE_J), _rec(synth.fist, STABLE_J),
                      _rec(synth.fist, STABLE_J)]
    ctl.finalize_stop(2.0)
    assert id(ctl.jump_model) == j_id and id(ctl.crouch_model) == c_id
    assert ctl.jump_model.radius == j_r
    assert ctl.jump_rejected == 0 and ctl.crouch_rejected == 0


# ---- ON/OFF + tracking-loss regression after repair -----------------------
def test_stop_on_off_and_tracking_loss_after_finalize():
    ctl = tc.TeachController()
    t = _teach_stop_ok(ctl, 0.0)
    ctl.finalize_stop(t + 0.2); t += 0.5
    assert ctl.stop_model is not None
    eng = ctl.engine

    def feed(kind, t, tracked=True):
        if not tracked:
            return ctl.feed(None, 0.0, None, t, tracked=False)
        lm = (synth.fist if kind == "fist" else synth.v_sign)(center=(0.5, 0.5))
        return ctl.feed(lm.tolist(), 0.9, _palm(lm), t)

    on = 0
    for _ in range(8):
        on += sum(1 for c in feed("fist", t) if c.type.value == "STOP_ON"); t += 0.1
    assert on == 1 and eng.stop_active
    # tracking loss must NOT release
    for _ in range(10):
        assert not any(c.type.value == "STOP_OFF" for c in feed("fist", t, tracked=False))
        t += 0.1
    assert eng.stop_active
    # a real mismatch releases
    off = 0
    for _ in range(8):
        off += sum(1 for c in feed("v", t) if c.type.value == "STOP_OFF"); t += 0.1
    assert off == 1 and not eng.stop_active
