import json
import urllib.request

import numpy as np

from before_gesture import production_control as pc
from before_gesture import teach_controller as tc
import synth

PALM_IDS = (0, 5, 9, 13, 17)


def _palm(lm):
    a = np.asarray(lm)[:, :2]
    return tuple(a[list(PALM_IDS)].mean(axis=0))


def _hold(runtime, hand_fn, t, center=(0.5, 0.5), frames=24):
    for i in range(frames):
        lm = hand_fn(center=center, jitter=0.001, seed=i)
        runtime.feed(lm.tolist(), 0.9, _palm(lm), t, tracked=True)
        t += 0.05
    return t


def _motion_take(runtime, hand_fn, t, upward=True):
    runtime.press(t)
    t = _hold(runtime, hand_fn, t)
    ys = (0.46, 0.42, 0.38, 0.34) if upward else (0.54, 0.58, 0.62, 0.66)
    for y in ys:
        lm = hand_fn(center=(0.5, y))
        runtime.feed(lm.tolist(), 0.9, _palm(lm), t, tracked=True)
        t += 0.05
    runtime.release(t)
    return t + 0.2


def _stop_take(runtime, hand_fn, t):
    runtime.press(t)
    t = _hold(runtime, hand_fn, t)
    runtime.release(t)
    return t + 0.2


def test_jump_space_samples_auto_finalize_after_three_releases():
    teach = tc.TeachController()
    runtime = pc.ProductionTeachingRuntime(teach)
    runtime.begin("jump", repair=False)
    t = 0.0
    for expected in (1, 2, 3):
        t = _motion_take(runtime, synth.v_sign, t)
        assert runtime.accepted == expected
    assert runtime.finalized
    assert teach.jump_model is not None and teach.jump_motion is not None
    assert len(teach.jump_takes) == len(teach.strokes) == 3


def test_stop_space_hold_release_auto_finalizes_three_samples():
    teach = tc.TeachController()
    runtime = pc.ProductionTeachingRuntime(teach)
    runtime.begin("stop", repair=False)
    t = 0.0
    for _ in range(3):
        t = _stop_take(runtime, synth.fist, t)
    assert runtime.accepted == 3 and runtime.finalized
    assert teach.stop_model is not None and len(teach.stop_takes) == 3


def test_early_release_and_tracking_loss_reject_only_current_attempt():
    teach = tc.TeachController()
    runtime = pc.ProductionTeachingRuntime(teach)
    runtime.begin("crouch", repair=False)
    runtime.press(0.0)
    runtime.release(0.1)
    assert runtime.accepted == 0 and runtime.reason == "early_release"

    runtime.press(1.0)
    runtime.feed(None, 0.0, None, 1.1, tracked=False)
    runtime.release(1.2)
    assert runtime.accepted == 0 and runtime.reason == "tracking_lost"
    assert teach.crouch_model is None and teach.crouch_takes == []


def test_inconsistent_third_sample_does_not_increment_or_change_model():
    teach = tc.TeachController()
    runtime = pc.ProductionTeachingRuntime(teach)
    runtime.begin("jump", repair=False)
    t = _motion_take(runtime, synth.v_sign, 0.0)
    t = _motion_take(runtime, synth.v_sign, t)
    assert runtime.accepted == 2 and teach.jump_model is None
    _motion_take(runtime, synth.fist, t)
    assert runtime.accepted == 2 and runtime.reason == "inconsistent"
    assert teach.jump_model is None and teach.jump_takes == []


def test_geometry_rejected_repair_preserves_existing_model_and_accepted_count():
    teach = tc.TeachController()
    runtime = pc.ProductionTeachingRuntime(teach)
    runtime.begin("stop", repair=False)
    t = 0.0
    for _ in range(3):
        t = _stop_take(runtime, synth.fist, t)
    old_model = teach.stop_model
    old_centroid = old_model.centroid.copy()
    old_takes = list(teach.stop_takes)

    runtime.begin("stop", repair=True)
    runtime.press(t)
    for i in range(24):
        lm = synth.fist(center=(0.5, 0.98), jitter=0.001, seed=i)
        runtime.feed(lm.tolist(), 1.0, _palm(lm), t, tracked=True)
        t += 0.05
    runtime.release(t)
    assert runtime.accepted == 0 and runtime.reason == "out_of_frame"
    assert teach.stop_model is old_model
    assert np.array_equal(teach.stop_model.centroid, old_centroid)
    assert teach.stop_takes == old_takes


def test_control_server_is_loopback_only_and_uses_semantic_actions():
    teach = tc.TeachController()
    runtime = pc.ProductionTeachingRuntime(teach)
    plane = pc.ProductionControlPlane(runtime)
    server = pc.LocalControlServer(plane, 0)
    server.start()
    try:
        host, port = server.address
        assert host == "127.0.0.1"
        body = json.dumps({"action": "begin", "kind": "stop",
                           "repair": False}).encode("utf-8")
        req = urllib.request.Request(
            f"http://127.0.0.1:{port}{pc.CONTROL_PATH}", body,
            {"Content-Type": "application/json"}, method="POST")
        with urllib.request.urlopen(req, timeout=2.0) as response:
            assert response.status == 200
        plane.process_pending(1.0)
        assert runtime.active and runtime.kind == "stop"
        assert "key" not in runtime.snapshot()
    finally:
        server.close()


def test_direction_control_is_explicit_and_exclusive_without_key_injection():
    teach = tc.TeachController()
    runtime = pc.ProductionTeachingRuntime(teach)
    plane = pc.ProductionControlPlane(runtime)
    plane.enqueue({"action": "direction", "enabled": True})
    plane.process_pending(0.0)
    assert runtime.direction_mode and teach.engine.cfg.mode_direction
    plane.enqueue({"action": "direction", "enabled": False})
    plane.process_pending(0.1)
    assert not runtime.direction_mode and not teach.engine.cfg.mode_direction


def test_reset_command_is_target_atomic_and_acknowledged():
    teach = tc.TeachController()
    engine = teach._engine()
    jump_model, crouch_model, stop_model = object(), object(), object()
    jump_motion, crouch_motion = object(), object()
    jump_take, crouch_take, stop_take = object(), object(), object()
    teach.jump_model, teach.crouch_model, teach.stop_model = (
        jump_model, crouch_model, stop_model)
    teach.jump_motion, teach.crouch_motion = jump_motion, crouch_motion
    teach.jump_takes, teach.crouch_takes, teach.stop_takes = (
        [jump_take], [crouch_take], [stop_take])
    teach.strokes, teach.dts = [[(0.0, 0.0), (0.1, 0.1)]], [0.2]
    teach.crouch_strokes, teach.crouch_dts = (
        [[(0.0, 0.0), (0.0, 0.1)]], [0.3])
    engine.cfg.jump_pose, engine.cfg.crouch_pose, engine.cfg.stop_pose = (
        jump_model, crouch_model, stop_model)
    engine.cfg.jump_motion, engine.cfg.crouch_motion = jump_motion, crouch_motion
    engine.cfg.mode_direction = True
    engine.stop_active = True

    runtime = pc.ProductionTeachingRuntime(teach)
    runtime.direction_mode = True
    runtime.begin("jump", repair=True)
    runtime._session_takes = [object()]
    runtime._session_strokes = [[(0.0, 0.0), (0.2, 0.0)]]
    runtime._session_dts = [0.4]
    plane = pc.ProductionControlPlane(runtime)
    plane.enqueue({"action": "reset_command", "target": "jump",
                   "request_id": 41})
    plane.process_pending(2.0)

    snap = runtime.snapshot()
    assert snap["ack_action"] == "reset_command"
    assert snap["ack_target"] == "jump" and snap["ack_id"] == 41
    assert runtime.active and runtime.kind == "jump"
    assert not runtime.repair and runtime.required == 3 and runtime.accepted == 0
    assert runtime._session_takes == runtime._session_strokes == []
    assert runtime._session_dts == []
    assert teach.jump_model is None and teach.jump_motion is None
    assert teach.jump_takes == teach.strokes == teach.dts == []
    assert teach.crouch_model is crouch_model
    assert teach.stop_model is stop_model and teach.stop_takes == [stop_take]
    assert engine.cfg.crouch_pose is crouch_model
    assert engine.cfg.stop_pose is stop_model and engine.stop_active
    assert runtime.direction_mode and engine.cfg.mode_direction
    assert "key" not in snap and "command" not in snap


def test_reset_stop_clears_latch_without_touching_other_commands():
    teach = tc.TeachController()
    engine = teach._engine()
    jump_model, crouch_model, stop_model = object(), object(), object()
    teach.jump_model, teach.crouch_model, teach.stop_model = (
        jump_model, crouch_model, stop_model)
    teach.stop_takes = [object(), object(), object()]
    engine.cfg.jump_pose, engine.cfg.crouch_pose, engine.cfg.stop_pose = (
        jump_model, crouch_model, stop_model)
    engine.cfg.mode_direction = True
    engine.stop_active = True
    engine._on_evi, engine._off_evi = 3, 2

    runtime = pc.ProductionTeachingRuntime(teach)
    runtime.direction_mode = True
    runtime.begin("stop", repair=True)
    assert runtime.reset_command("stop", 7, 3.0)

    snap = runtime.snapshot()
    assert teach.stop_model is None and teach.stop_takes == []
    assert engine.cfg.stop_pose is None
    assert not engine.stop_active and engine._on_evi == engine._off_evi == 0
    assert teach.jump_model is jump_model and teach.crouch_model is crouch_model
    assert engine.cfg.jump_pose is jump_model and engine.cfg.crouch_pose is crouch_model
    assert runtime.direction_mode and engine.cfg.mode_direction
    assert runtime.active and not runtime.repair
    assert runtime.required == 3 and runtime.accepted == 0
    assert snap["ack_target"] == "stop" and snap["ack_id"] == 7
    assert "command" not in snap
