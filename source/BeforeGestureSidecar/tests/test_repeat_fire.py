"""D-111 pose-held, stroke-segmented repeated JUMP. Pose is NEVER released."""
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
    takes = [pp.Recording([pp.FrameSample(
        synth.v_sign(jitter=0.001, seed=k * 100 + i).tolist(), 0.9)
        for i in range(25)]) for k in range(3)]
    m = pp.register_pose("JUMP", takes)
    jm = mp.learn_jump_motion([[(0.5, 0.5), (0.5, 0.34)]] * 3, [0.2, 0.2, 0.2])
    return ce.Engine(cfg=ce.EngineConfig(jump_pose=m, jump_motion=jm)), jm


def _obs(center, t, tracked=True):
    if not tracked:
        return ce.FrameObs(t=t, tracked=False, quality=0.0, landmarks=None,
                           palm=None)
    # V-sign shape held constant; only the palm CENTER moves (pose never breaks)
    lm = synth.v_sign(center=center)
    return ce.FrameObs(t=t, tracked=True, quality=0.9, landmarks=lm.tolist(),
                       palm=_palm(lm))


def _feed(eng, center, t, dt=0.05):
    c = eng.step(_obs(center, t))
    return t + dt, sum(1 for x in c if x.type.value == "JUMP")


def _hold(eng, t, center=(0.5, 0.5), n=8, dt=0.05):
    fires = 0
    for _ in range(n):
        t, f = _feed(eng, center, t, dt); fires += f
    return t, fires


def _up_stroke(eng, t, x=0.5, ys=(0.50, 0.44, 0.36, 0.30), dt=0.05):
    fires = 0
    for y in ys:
        t, f = _feed(eng, (x, y), t, dt); fires += f
    return t, fires


def _down_return(eng, t, x=0.5, ys=(0.36, 0.44, 0.50), dt=0.05):
    fires = 0
    for y in ys:
        t, f = _feed(eng, (x, y), t, dt); fires += f
    return t, fires


def _pose_never_broke(eng):
    # channel stayed on a matched pose the whole time
    return eng.jstate != ce.JumpState.POSE_SEARCHING


def test_1_pose_hold_plus_stroke_fires():
    eng, _ = _engine()
    t, _ = _hold(eng, 0.0)
    t, f = _up_stroke(eng, t)
    assert f == 1


def test_2_continuous_motion_no_extra_fire():
    eng, _ = _engine()
    t, _ = _hold(eng, 0.0)
    t, f = _up_stroke(eng, t, ys=(0.50, 0.44, 0.36, 0.30, 0.24, 0.18, 0.12))
    assert f == 1


def test_3_4_hold_still_ends_stroke_then_refire():
    eng, _ = _engine()
    t, _ = _hold(eng, 0.0)
    t, f1 = _up_stroke(eng, t)
    t, _ = _hold(eng, t, center=(0.5, 0.30), n=8)   # settle at top (still V)
    t, f2 = _up_stroke(eng, t, ys=(0.30, 0.24, 0.16, 0.10))
    assert f1 == 1 and f2 == 1


def test_5_6_five_cycles_same_pose_never_broken():
    protocol.reset_ids()
    eng, _ = _engine()
    t, _ = _hold(eng, 0.0)
    ids = []
    y_top = 0.30
    for cyc in range(5):
        # up stroke from resting -> fire
        fired = []
        for y in (0.50, 0.44, 0.36, y_top):
            for c in eng.step(_obs((0.5, y), t)):
                if c.type.value == "JUMP":
                    fired.append(c.id)
            t += 0.05
            assert _pose_never_broke(eng)
        assert len(fired) == 1
        ids.append(fired[0])
        # return down (reverse) to rest -> ends stroke, still holding V
        for y in (0.38, 0.46, 0.50):
            eng.step(_obs((0.5, y), t)); t += 0.05
            assert _pose_never_broke(eng)
    assert ids == [1, 2, 3, 4, 5]
    assert eng.cycle_count == 5


def test_7_reverse_return_does_not_fire():
    eng, _ = _engine()
    t, _ = _hold(eng, 0.0)
    t, f1 = _up_stroke(eng, t)
    # the downward return itself must not fire
    t, fr = _down_return(eng, t)
    assert f1 == 1 and fr == 0


def test_8_wrong_direction_then_correct_fires():
    eng, _ = _engine()
    t, _ = _hold(eng, 0.0)
    # sideways (wrong) stroke -> no fire
    t, fw = 0.0, 0
    for x in (0.50, 0.58, 0.66, 0.74):
        t2, f = _feed(eng, (x, 0.5), t); t = t2; fw += f
    assert fw == 0
    # settle, then correct up stroke fires
    t, _ = _hold(eng, t, center=(0.74, 0.5), n=6)
    t, f = _up_stroke(eng, t, x=0.74)
    assert f == 1


def test_9_small_tremor_no_fire():
    eng, _ = _engine()
    t = 0.0
    fires = 0
    rng = np.random.default_rng(1)
    for _ in range(300):  # ~10s at 33ms
        cx = 0.5 + float(rng.normal(0, 0.004))
        cy = 0.5 + float(rng.normal(0, 0.004))
        t, f = _feed(eng, (cx, cy), t, dt=0.033); fires += f
    assert fires == 0


def test_10_one_long_stroke_exactly_one_fire():
    eng, _ = _engine()
    t, _ = _hold(eng, 0.0)
    t, f = _up_stroke(eng, t, ys=tuple(0.50 - 0.03 * i for i in range(14)))
    assert f == 1


def test_11_fast_reversal_under_lockout_no_double():
    eng, _ = _engine()
    t, _ = _hold(eng, 0.0)
    # fire, then a very fast tiny up-down-up all within ~150ms
    t, f = _up_stroke(eng, t, ys=(0.50, 0.42, 0.34), dt=0.02)
    for y in (0.40, 0.34, 0.40, 0.34):   # jitter within lockout
        t, ff = _feed(eng, (0.5, y), t, dt=0.02); f += ff
    assert f == 1


def test_12_separated_repeats_each_fire():
    eng, _ = _engine()
    t, _ = _hold(eng, 0.0)
    total = 0
    for _ in range(3):
        t, f = _up_stroke(eng, t); total += f
        t, _ = _hold(eng, t, center=(0.5, 0.30), n=6)   # clear settle
        t, _ = _down_return(eng, t)
        t, _ = _hold(eng, t, center=(0.5, 0.50), n=4)
    assert total == 3


def test_13_pose_break_then_restore_reusable():
    eng, _ = _engine()
    t, _ = _hold(eng, 0.0)
    t, f1 = _up_stroke(eng, t)
    # break the pose (open hand) briefly
    for _ in range(6):
        lm = synth.open_hand(center=(0.5, 0.5))
        eng.step(ce.FrameObs(t=t, tracked=True, quality=0.9,
                             landmarks=lm.tolist(), palm=_palm(lm))); t += 0.05
    assert eng.jstate == ce.JumpState.POSE_SEARCHING
    # restore V and stroke -> fires again
    t, _ = _hold(eng, t)
    t, f2 = _up_stroke(eng, t)
    assert f1 == 1 and f2 == 1


def test_14_tracking_loss_alone_no_fire():
    eng, _ = _engine()
    fires = 0
    t = 0.0
    for _ in range(40):
        for c in eng.step(_obs((0, 0), t, tracked=False)):
            if c.type.value == "JUMP":
                fires += 1
        t += 0.05
    assert fires == 0


def test_16_stop_tracking_loss_hold_regression():
    stop = pp.register_pose("STOP", [pp.Recording([pp.FrameSample(
        synth.fist(jitter=0.001, seed=i).tolist(), 0.9) for i in range(25)])
        for _ in range(3)])
    eng = ce.Engine(cfg=ce.EngineConfig(stop_pose=stop))
    t = 0.0
    for _ in range(8):
        lm = synth.fist(center=(0.5, 0.5))
        eng.step(ce.FrameObs(t=t, tracked=True, quality=0.9,
                             landmarks=lm.tolist(), palm=_palm(lm))); t += 0.05
    assert eng.stop_active
    for _ in range(10):
        cs = eng.step(ce.FrameObs(t=t, tracked=False, quality=0.0,
                                  landmarks=None, palm=None))
        assert not any(c.type.value == "STOP_OFF" for c in cs)
        t += 0.05
    assert eng.stop_active
