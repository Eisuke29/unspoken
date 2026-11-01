"""Deterministic restart (D-106/§20): fresh engine/controller start identically."""
import numpy as np

from before_gesture import command_engine as ce
from before_gesture import teach_controller as tc
from before_gesture import protocol
import synth

PALM_IDS = (0, 5, 9, 13, 17)


def _palm(lm):
    a = np.asarray(lm)[:, :2]
    return tuple(a[list(PALM_IDS)].mean(axis=0))


def _fresh_engine_state():
    eng = ce.Engine(cfg=ce.EngineConfig())
    return (eng.jstate, eng.cycle_count, eng.stop_active,
            eng.crouch.state, eng.crouch.cycle_count, eng.reset_reason)


def test_fresh_engine_is_deterministic_initial_state():
    assert _fresh_engine_state() == _fresh_engine_state()
    eng = ce.Engine(cfg=ce.EngineConfig())
    assert eng.jstate == ce.JumpState.POSE_SEARCHING
    assert eng.crouch.state == ce.CrouchState.POSE_SEARCHING
    assert eng.cycle_count == 0 and not eng.stop_active
    assert eng.crouch.cycle_count == 0


def test_two_engines_same_input_same_state():
    # feeding an identical neutral stream yields identical final state
    def run():
        eng = ce.Engine(cfg=ce.EngineConfig())
        t = 0.0
        for _ in range(50):
            lm = synth.open_hand(center=(0.5, 0.5))
            eng.step(ce.FrameObs(t=t, tracked=True, quality=0.9,
                                 landmarks=lm.tolist(), palm=_palm(lm)))
            t += 0.05
        return (eng.jstate, eng.crouch.state, eng.stop_active)
    assert run() == run()


def test_reset_returns_controller_to_initial_message():
    ctl = tc.TeachController()
    first = ctl.big_message(0.0)
    # mutate then reset
    ctl.jump_takes.append("x")
    ctl.on_key("r", 5.0)
    # after the RESET COMPLETE hold, default message returns to initial ready
    assert ctl.big_message(10.0) == first
    assert first.startswith("READY")


def test_command_ids_reset_deterministically():
    protocol.reset_ids()
    a = protocol.Command(protocol.CommandType.JUMP, 0.0).id
    protocol.reset_ids()
    b = protocol.Command(protocol.CommandType.JUMP, 0.0).id
    assert a == b == 1
