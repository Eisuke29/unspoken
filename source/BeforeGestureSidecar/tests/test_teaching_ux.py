import numpy as np

from before_gesture import personal_pose as pp
from before_gesture import teach_controller as tc
import synth

PALM_IDS = (0, 5, 9, 13, 17)


def _palm(lm):
    a = np.asarray(lm)[:, :2]
    return tuple(a[list(PALM_IDS)].mean(axis=0))


def _capture_pose(ctl, key, hand_fn, t0, quality=0.9, dt=0.05):
    """Press key then feed frames across one TAKE_SECONDS window."""
    ctl.on_key(key, t0)
    t = t0
    # feed enough frames to pass TAKE_SECONDS while capturing
    for i in range(40):
        if ctl.capturing is None:
            break
        lm = hand_fn(seed=i)
        ctl.feed(lm.tolist(), quality, _palm(lm), t)
        t += dt
    return t


def _open(seed=0):
    return synth.open_hand(jitter=0.001, seed=seed)


def _fist(seed=0):
    return synth.fist(jitter=0.001, seed=seed)


# ---- geometry -----------------------------------------------------------
def _bounded_hand(x_min=0.25, x_max=0.75, y_min=0.25, y_max=0.75):
    """Synthetic 21-point hand with exact image-space extrema."""
    lm = np.full((21, 3), 0.5, dtype=np.float64)
    lm[0, :2] = (x_min, y_min)
    lm[1, :2] = (x_max, y_max)
    return lm


def test_geometry_ok_centered():
    ok, reason = pp.teaching_geometry(synth.open_hand())
    assert ok and reason == "OK"


def test_geometry_accepts_new_safe_frame_boundaries():
    assert pp.TEACH_MARGIN == 0.05
    ok, reason = pp.teaching_geometry(
        _bounded_hand(x_min=0.05, x_max=0.95, y_min=0.05, y_max=0.95))
    assert ok and reason == "OK"


def test_geometry_rejects_just_outside_new_safe_frame():
    cases = (
        (_bounded_hand(x_min=0.049999), "MOVE HAND RIGHT"),
        (_bounded_hand(x_max=0.950001), "MOVE HAND LEFT"),
        (_bounded_hand(y_min=0.049999), "MOVE HAND DOWN"),
        (_bounded_hand(y_max=0.950001), "MOVE HAND UP"),
    )
    for landmarks, expected in cases:
        ok, reason = pp.teaching_geometry(landmarks)
        assert not ok and reason == expected


def test_geometry_accepts_bbox_boundary_and_rejects_smaller():
    assert pp.TEACH_BBOX_MIN == 0.09
    valid = _bounded_hand(0.455, 0.545, 0.5, 0.5)
    invalid = _bounded_hand(0.455, 0.544999, 0.5, 0.5)
    assert pp.teaching_geometry(valid) == (True, "OK")
    assert pp.teaching_geometry(invalid) == (False, "MOVE CLOSER")


def test_geometry_change_does_not_modify_recognition_thresholds():
    assert pp.TRACK_QUALITY_MIN == 0.55
    assert pp.HAND_SIZE_MIN == 0.06
    assert pp.MIN_ACCEPT_RADIUS == 0.12
    assert pp.MAX_ACCEPT_RADIUS == 0.45
    assert pp.CLASS_GAP == 0.06


def test_geometry_wrist_out_bottom_rejected():
    # push whole hand down so the wrist exits the safe frame
    lm = synth.open_hand(center=(0.5, 0.98))
    ok, reason = pp.teaching_geometry(lm)
    assert not ok and reason == "MOVE HAND UP"


def test_geometry_too_small_move_closer():
    lm = synth.open_hand(scale=0.05)
    ok, reason = pp.teaching_geometry(lm)
    assert not ok and reason == "MOVE CLOSER"


# ---- caps ---------------------------------------------------------------
def test_jump_fourth_take_not_accepted():
    ctl = tc.TeachController()
    t = 0.0
    for _ in range(5):  # try FIVE takes
        t = _capture_pose(ctl, "j", _open, t + 1.0)
    assert len(ctl.jump_takes) == 3


def test_stop_fourth_take_not_accepted():
    ctl = tc.TeachController()
    t = 0.0
    for _ in range(5):
        t = _capture_pose(ctl, "s", _fist, t + 1.0)
    assert len(ctl.stop_takes) == 3


def test_key_repeat_no_duplicate_capture():
    ctl = tc.TeachController()
    ctl.on_key("j", 1.0)
    assert ctl.capturing == tc.KIND_JUMP
    # immediate repeats while capturing must not start another / stack
    ctl.on_key("j", 1.01)
    ctl.on_key("j", 1.02)
    assert ctl.capturing == tc.KIND_JUMP


def test_input_ignored_mid_capture():
    ctl = tc.TeachController()
    ctl.on_key("j", 1.0)
    ctl.on_key("s", 1.05)  # different start key mid-capture: ignored
    assert ctl.capturing == tc.KIND_JUMP


def test_pose_ready_message_after_three():
    ctl = tc.TeachController()
    t = 0.0
    for _ in range(3):
        t = _capture_pose(ctl, "j", _open, t + 1.0)
    assert "POSE READY" in ctl.big_message(t + 2.0)


# ---- geometry gating in capture ----------------------------------------
def test_out_of_frame_capture_rejected_not_counted():
    ctl = tc.TeachController()
    t = _capture_pose(ctl, "j", lambda seed=0: synth.open_hand(center=(0.5, 0.98)),
                      1.0)
    assert len(ctl.jump_takes) == 0
    assert ctl.jump_rejected == 1


def test_high_confidence_but_geometry_invalid_rejected():
    ctl = tc.TeachController()
    # quality 1.0 (perfect confidence) but hand clipped by the frame
    t = _capture_pose(ctl, "j",
                      lambda seed=0: synth.open_hand(center=(0.5, 0.98)),
                      1.0, quality=1.0)
    assert len(ctl.jump_takes) == 0
    assert ctl.jump_model is None


def test_rejected_take_leaves_model_unchanged():
    ctl = tc.TeachController()
    _capture_pose(ctl, "j",
                  lambda seed=0: synth.open_hand(center=(0.5, 0.98)), 1.0)
    assert ctl.jump_model is None and ctl.jump_motion is None
    assert len(ctl.jump_takes) == 0


# ---- reset --------------------------------------------------------------
def test_reset_clears_all_teaching_state():
    ctl = tc.TeachController()
    t = 0.0
    for _ in range(3):
        t = _capture_pose(ctl, "j", _open, t + 1.0)
    assert len(ctl.jump_takes) == 3
    ctl.on_key("r", t + 1.0)
    assert len(ctl.jump_takes) == 0
    assert len(ctl.stop_takes) == 0
    assert ctl.jump_model is None and ctl.jump_motion is None
    assert ctl.qa_reset_requested


def test_reregister_after_reset():
    ctl = tc.TeachController()
    t = 0.0
    for _ in range(3):
        t = _capture_pose(ctl, "j", _open, t + 1.0)
    ctl.on_key("r", t + 1.0)
    t += 2.0
    assert "READY" in ctl.big_message(t + 1.0)  # back to 0/3 ready
    for _ in range(3):
        t = _capture_pose(ctl, "j", _open, t + 1.0)
    assert len(ctl.jump_takes) == 3
