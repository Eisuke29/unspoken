import numpy as np

from before_gesture import personal_pose as pp
from before_gesture import motion_primitive as mpr
from before_gesture import command_engine as ce
from before_gesture import teach_controller as tc
from before_gesture import protocol
import synth

PALM_IDS = (0, 5, 9, 13, 17)


def _palm(lm):
    a = np.asarray(lm)[:, :2]
    return tuple(a[list(PALM_IDS)].mean(axis=0))


def _v_take(seed0, rot=0.0, spread=1.0, center=(0.5, 0.5)):
    return pp.Recording([pp.FrameSample(
        synth.v_sign(center=center, rot=rot, spread=spread, jitter=0.001,
                     seed=seed0 + i).tolist(), 0.9) for i in range(25)])


def _v_model():
    takes = [_v_take(0), _v_take(100, rot=np.deg2rad(3), spread=1.05),
             _v_take(200, rot=np.deg2rad(-2), spread=0.97)]
    return pp.register_pose("JUMP", takes)


# ---- 1. natural variation armed / distinct not ------------------------
def test_natural_variation_is_armed():
    m = _v_model()
    for rot, spread, center in ((12, 1.35, (0.56, 0.47)),
                                (20, 1.6, (0.60, 0.44)),
                                (-14, 0.8, (0.44, 0.55))):
        rep = synth.v_sign(center=center, rot=np.deg2rad(rot), spread=spread,
                           jitter=0.002, seed=7)
        r = pp.match_pose(pp.feature_vector(rep.tolist()), [m])
        assert r.armed, (rot, spread, r.best_dist, r.reason)


def test_distinct_poses_not_armed():
    m = _v_model()
    for fn in (synth.fist, synth.open_hand, lambda: synth.point_hand(1)):
        r = pp.match_pose(pp.feature_vector(fn().tolist()), [m])
        assert not r.armed and r.reason == pp.R_DIST


# ---- 2. all three takes contribute; outlier does not destroy ----------
def test_all_three_takes_contribute():
    m = _v_model()
    assert m.take_count == 3
    # a model whose 3rd take is a different spread yields a different centroid,
    # proving every take's frames feed the robust center (not just take 1).
    alt = pp.register_pose("JUMP", [_v_take(0), _v_take(100, spread=1.05),
                                    _v_take(400, spread=1.9)])
    assert not np.allclose(m.centroid, alt.centroid)


def test_outlier_take_does_not_destroy_model():
    # one noticeably rotated take (still the same V) must not break acceptance
    takes = [_v_take(0), _v_take(100, spread=1.05),
             _v_take(300, rot=np.deg2rad(14), spread=1.2)]
    m = pp.register_pose("JUMP", takes)
    rep = synth.v_sign(jitter=0.002, seed=5)  # close to take 1
    r = pp.match_pose(pp.feature_vector(rep.tolist()), [m])
    assert r.armed


# ---- 3. 5-of-7 stability ----------------------------------------------
def _engine_with_v():
    m = _v_model()
    strokes = [[(0.5, 0.5), (0.5, 0.34)] for _ in range(3)]
    jm = mpr.learn_jump_motion(strokes, [0.2, 0.2, 0.2])
    return ce.Engine(cfg=ce.EngineConfig(jump_pose=m, jump_motion=jm))


def _obs_v(center, t, kind="v"):
    lm = synth.v_sign(center=center) if kind == "v" else synth.fist(center=center)
    return ce.FrameObs(t=t, tracked=True, quality=0.9,
                       landmarks=lm.tolist(), palm=_palm(lm))


def test_five_of_seven_stability_gate():
    eng = _engine_with_v()
    t = 0.0
    pattern = ["v", "v", "v", "fist", "v", "v", "fist"]  # 5 good in last 7
    for k in pattern:
        eng.step(_obs_v((0.5, 0.5), t, k)); t += 0.05
    assert eng.stable_count == 5


def test_below_five_of_seven_not_stable():
    eng = _engine_with_v()
    t = 0.0
    for k in ["v", "fist", "v", "fist", "v", "fist", "fist"]:  # 3 good
        eng.step(_obs_v((0.5, 0.5), t, k)); t += 0.05
    assert eng.stable_count < ce.STABLE_NEEDED


# ---- 4. jump/stop never both armed ------------------------------------
def test_jump_stop_not_both_armed():
    jm = _v_model()
    stop = pp.register_pose("STOP", [
        pp.Recording([pp.FrameSample(synth.fist(jitter=0.001, seed=i).tolist(),
                                     0.9) for i in range(25)])
        for _ in range(3)])
    pp.constrain_separation(jm, stop)
    models = [jm, stop]
    rv = pp.match_pose(pp.feature_vector(synth.v_sign().tolist()), models)
    rf = pp.match_pose(pp.feature_vector(synth.fist().tolist()), models)
    assert rv.best == "JUMP" and rv.armed
    assert rf.best == "STOP" and rf.armed
    assert rv.best != rf.best  # never the same class armed for both inputs


# ---- 5/6/7. live proof: pose verify -> motion fires once --------------
def _capture(ctl, key, hand_fn, t0, quality=0.9, dt=0.05):
    ctl.on_key(key, t0)
    t = t0
    for i in range(40):
        if ctl.capturing is None:
            break
        lm = hand_fn(i)
        ctl.feed(lm.tolist(), quality, _palm(lm), t)
        t += dt
    return t


def _teach_jump(ctl, t):
    for _ in range(3):
        t = _capture(ctl, "j", lambda i: synth.v_sign(jitter=0.001, seed=i),
                     t + 1.0)
    # motion: press m, move palm up, press e
    ctl.on_key("m", t + 0.2)
    t += 0.3
    for cy in (0.55, 0.50, 0.45, 0.40, 0.35):
        lm = synth.v_sign(center=(0.5, cy))
        ctl.feed(lm.tolist(), 0.9, _palm(lm), t); t += 0.05
    ctl.on_key("e", t)
    return t + 0.05


# D-130: the old D-111 live-proof / auto-recalibration / radius-expansion tests
# are obsolete (that flow is removed). Replaced with D-130 behavior: F finalizes
# the model as READY immediately (no proof), live use fires, and NOTHING in live
# use ever discards/shrinks/recalibrates the model — only explicit R re-registers.
def test_finalize_ready_immediately_then_stroke_fires():
    protocol.reset_ids()
    ctl = tc.TeachController()
    t = _teach_jump(ctl, 0.0)
    ctl.on_key("f", t + 0.2)          # finalize -> READY at once (no proof)
    assert ctl.verified and ctl.proof_state == "ready"
    t += 0.5
    for _ in range(8):                # arm the held pose (5/7 stable)
        lm = synth.v_sign(center=(0.5, 0.5)); ctl.feed(lm.tolist(), 0.9, _palm(lm), t); t += 0.1
    fires = 0
    for cy in (0.5, 0.42, 0.30, 0.28):   # up stroke
        lm = synth.v_sign(center=(0.5, cy))
        cmds = ctl.feed(lm.tolist(), 0.9, _palm(lm), t); t += 0.05
        fires += sum(1 for c in cmds if c.type == ce.CommandType.JUMP)
    assert fires == 1


def test_finalize_does_not_change_radius_and_never_recalibrates():
    ctl = tc.TeachController()
    t = _teach_jump(ctl, 0.0)
    ctl.on_key("f", t + 0.2)
    r0 = ctl.jump_model.radius
    centroid0 = ctl.jump_model.centroid.copy()
    t += 0.5
    # live misses: wrong-direction strokes, pose loss, tracking loss for a while
    for cx in (0.5, 0.58, 0.66, 0.74):          # sideways = wrong direction
        lm = synth.v_sign(center=(cx, 0.5))
        ctl.feed(lm.tolist(), 0.9, _palm(lm), t); t += 0.05
    for _ in range(10):                          # tracking loss
        ctl.feed(None, 0.0, None, t, tracked=False); t += 0.1
    # model is completely unchanged, and NEVER entered a recalibration state
    assert ctl.jump_model.radius == r0
    assert np.allclose(ctl.jump_model.centroid, centroid0)
    assert ctl.proof_state != "recal" and ctl.verified
    # a correct pose+motion still fires WITHOUT any re-registration
    for _ in range(8):                # re-arm the held pose
        lm = synth.v_sign(center=(0.5, 0.5)); ctl.feed(lm.tolist(), 0.9, _palm(lm), t); t += 0.1
    fires = 0
    for cy in (0.5, 0.42, 0.30, 0.28):
        lm = synth.v_sign(center=(0.5, cy))
        cmds = ctl.feed(lm.tolist(), 0.9, _palm(lm), t); t += 0.05
        fires += sum(1 for c in cmds if c.type == ce.CommandType.JUMP)
    assert fires == 1


def test_pose_only_no_fire_after_ready():
    ctl = tc.TeachController()
    t = _teach_jump(ctl, 0.0)
    ctl.on_key("f", t + 0.2)
    t += 0.5
    fires = 0
    for _ in range(20):  # hold pose, never move
        lm = synth.v_sign(center=(0.5, 0.5))
        cmds = ctl.feed(lm.tolist(), 0.9, _palm(lm), t); t += 0.1
        fires += sum(1 for c in cmds if c.type == ce.CommandType.JUMP)
    assert fires == 0
    assert ctl.verified            # model stays ready (D-130: no proof gating)


def test_wrong_motion_no_fire():
    ctl = tc.TeachController()
    t = _teach_jump(ctl, 0.0)
    ctl.on_key("f", t + 0.2)
    t += 0.5
    for _ in range(12):  # verify pose
        lm = synth.v_sign(center=(0.5, 0.5))
        ctl.feed(lm.tolist(), 0.9, _palm(lm), t); t += 0.1
    fires = 0
    for cx in (0.5, 0.58, 0.66, 0.74):  # move sideways (taught dir is up)
        lm = synth.v_sign(center=(cx, 0.5))
        cmds = ctl.feed(lm.tolist(), 0.9, _palm(lm), t); t += 0.05
        fires += sum(1 for c in cmds if c.type == ce.CommandType.JUMP)
    assert fires == 0
