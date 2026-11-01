import numpy as np

from before_gesture import command_engine as ce
from before_gesture import motion_primitive as mp
from before_gesture import net_protocol as npc
from before_gesture import personal_pose as pp
import synth

PALM_IDS = (0, 5, 9, 13, 17)


def _palm(lm):
    pts = np.asarray(lm)[:, :2]
    return tuple(pts[list(PALM_IDS)].mean(axis=0))


def _model(fn, name, seed0):
    takes = []
    for take in range(3):
        frames = [pp.FrameSample(
            fn(jitter=0.001, seed=seed0 + take * 100 + frame).tolist(), 0.9)
            for frame in range(25)]
        takes.append(pp.Recording(frames))
    return pp.register_pose(name, takes)


def _engine():
    jump = _model(synth.v_sign, "JUMP", 0)
    crouch = _model(synth.open_hand, "CROUCH", 3000)
    stop = _model(synth.fist, "STOP", 7000)
    pp.constrain_separation(jump, crouch)
    pp.constrain_separation(jump, stop)
    pp.constrain_separation(crouch, stop)
    jump_motion = mp.learn_jump_motion(
        [[(0.5, 0.5), (0.5, 0.34)]] * 3, [0.2] * 3)
    crouch_motion = mp.learn_jump_motion(
        [[(0.5, 0.5), (0.5, 0.66)]] * 3, [0.2] * 3)
    return ce.Engine(ce.EngineConfig(
        jump_pose=jump, jump_motion=jump_motion,
        crouch_pose=crouch, crouch_motion=crouch_motion,
        stop_pose=stop))


def _obs(fn, y, t):
    lm = fn(center=(0.5, y))
    return ce.FrameObs(t=t, tracked=True, quality=0.9,
                       landmarks=lm.tolist(), palm=_palm(lm))


def test_readiness_is_exact_engine_arm_state_without_display_hold():
    eng = _engine()
    t = 0.0
    for _ in range(8):
        eng.step(_obs(synth.v_sign, 0.5, t)); t += 0.05
    assert eng.readiness() == "JUMP"
    assert eng.readiness(signal_valid=False) == ""
    assert eng.readiness(teaching_active=True) == ""
    eng.cfg.mode_direction = True
    assert eng.readiness() == ""
    eng.cfg.mode_direction = False

    # Crossing the real motion threshold enters the existing fire lockout, which
    # must remove readiness until the recognizer itself rearms.
    fired = []
    for y in (0.44, 0.36, 0.28):
        fired += eng.step(_obs(synth.v_sign, y, t)); t += 0.05
    assert any(c.type.value == "JUMP" for c in fired)
    assert eng.jstate == ce.JumpState.FIRED_LOCKOUT
    assert eng.readiness() == ""

    crouch = _engine()
    t = 0.0
    for _ in range(8):
        crouch.step(_obs(synth.open_hand, 0.5, t)); t += 0.05
    assert crouch.readiness() == "CROUCH"

    stop = _engine()
    first = stop.step(_obs(synth.fist, 0.5, 0.0))
    assert first == [] and not stop.stop_active
    assert stop.readiness() == "STOP"  # real candidate, before 4-frame fire
    for i in range(1, 4):
        stop.step(_obs(synth.fist, 0.5, i * 0.05))
    assert stop.stop_active and stop.readiness() == "STOP"
    for i in range(3):
        stop.step(_obs(synth.v_sign, 0.5, 0.25 + i * 0.05))
        assert stop.readiness() == "STOP"  # held until real STOP_OFF evidence
    off = stop.step(_obs(synth.v_sign, 0.5, 0.40))
    assert any(c.type.value == "STOP_OFF" for c in off)
    assert stop.readiness() == ""


def test_stop_readiness_latch_survives_invalid_release_evidence():
    stop = _engine()
    t = 0.0
    for _ in range(4):
        stop.step(_obs(synth.fist, 0.5, t))
        t += 0.05
    assert stop.stop_active and stop.readiness() == "STOP"

    for _ in range(8):
        lm = synth.open_hand()
        stop.step(ce.FrameObs(t=t, tracked=True, quality=0.1,
                              landmarks=lm.tolist(), palm=_palm(lm)))
        t += 0.05
        assert stop.readiness(signal_valid=False) == ""
    assert stop.stop_active

    stop.step(_obs(synth.fist, 0.65, t))
    assert stop.readiness() == "STOP"


def test_ambiguous_pose_never_reports_readiness():
    recs = []
    for take in range(3):
        recs.append(pp.Recording([pp.FrameSample(
            synth.open_hand(jitter=0.001, seed=take * 100 + i).tolist(), 0.9)
            for i in range(25)]))
    jump = pp.register_pose("JUMP", recs)
    crouch = pp.register_pose("CROUCH", recs)
    motion = mp.learn_jump_motion(
        [[(0.5, 0.5), (0.5, 0.34)]] * 3, [0.2] * 3)
    eng = ce.Engine(ce.EngineConfig(
        jump_pose=jump, jump_motion=motion,
        crouch_pose=crouch, crouch_motion=motion))
    for i in range(10):
        eng.step(_obs(synth.open_hand, 0.5, i * 0.05))
    assert eng.jstate == ce.JumpState.POSE_SEARCHING
    assert eng.crouch.state == ce.CrouchState.POSE_SEARCHING
    assert eng.readiness() == ""


def test_readiness_landmark_payload_is_noncommand_and_rgb_free():
    pts = [[0.01 * i, 0.02 * i] for i in range(21)]
    payload = npc.decode(npc.encode_landmarks(
        "session", 10, pts, t_us=20, readiness="CROUCH", signal="ok"))
    assert payload["kind"] == "lm" and len(payload["pts"]) == 42
    assert payload["ready"] == "CROUCH" and payload["signal"] == "ok"
    assert "cmdId" not in payload and "seq" not in payload and "type" not in payload
    assert not any(k in payload for k in ("rgb", "image", "frame", "depth", "z"))

    lost = npc.decode(npc.encode_landmarks(
        "session", 10, None, t_us=21, readiness="", signal="tracking_lost"))
    assert lost["pts"] == [] and lost["ready"] == ""
