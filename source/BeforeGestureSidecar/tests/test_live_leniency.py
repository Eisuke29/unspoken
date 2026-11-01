"""D-130 live Motion leniency (Jump/Crouch only): live req = 0.85*model,
live cos = 0.85. Model required is unchanged; Stop/Direction unaffected."""
import numpy as np

from before_gesture import personal_pose as pp
from before_gesture import motion_primitive as mp
from before_gesture import command_engine as ce
import synth

PALM_IDS = (0, 5, 9, 13, 17)


def _palm(lm):
    a = np.asarray(lm)[:, :2]
    return tuple(a[list(PALM_IDS)].mean(axis=0))


def _pose(fn, name, seed0=0):
    takes = [pp.Recording([pp.FrameSample(fn(jitter=0.001, seed=seed0 + k * 100 + i).tolist(),
                                          0.9) for i in range(25)]) for k in range(3)]
    return pp.register_pose(name, takes)


def _jump_engine():
    m = _pose(synth.v_sign, "JUMP", 0)
    jm = mp.learn_jump_motion([[(0.5, 0.5), (0.5, 0.34)]] * 3, [0.2] * 3)  # up
    return ce.Engine(cfg=ce.EngineConfig(jump_pose=m, jump_motion=jm)), jm


def _crouch_engine():
    m = _pose(synth.open_hand, "CROUCH", 3000)
    cm = mp.learn_jump_motion([[(0.5, 0.5), (0.5, 0.66)]] * 3, [0.2] * 3)  # down
    return ce.Engine(cfg=ce.EngineConfig(crouch_pose=m, crouch_motion=cm)), cm


def _obs(fn, center, t):
    lm = fn(center=center)
    return ce.FrameObs(t=t, tracked=True, quality=0.9, landmarks=lm.tolist(),
                       palm=_palm(lm))


def _arm(eng, fn, t, n=8):
    for _ in range(n):
        eng.step(_obs(fn, (0.5, 0.5), t)); t += 0.1
    return t


def _drive(eng, fn, path, t, dt=0.05):
    """Feed a path of centers; return (t, jump_fires, crouch_fires)."""
    j = c = 0
    for cx, cy in path:
        for cmd in eng.step(_obs(fn, (cx, cy), t)):
            if cmd.type.value == "JUMP":
                j += 1
            elif cmd.type.value == "CROUCH":
                c += 1
        t += dt
    return t, j, c


# ---- diagnostics expose model + live thresholds ---------------------------
def test_diagnostics_show_model_and_live_thresholds():
    eng, jm = _jump_engine()
    d = eng.get_diagnostics()
    assert abs(d.required - jm.disp_threshold) < 1e-9
    assert abs(d.live_required - jm.disp_threshold * 0.85) < 1e-9
    assert d.cos_threshold == 0.85
    ce_, cm = _crouch_engine()
    cd = ce_.get_crouch_diagnostics()
    assert abs(cd.live_required - cm.disp_threshold * 0.85) < 1e-9
    assert cd.cos_threshold == 0.85
    # the model's own required is not mutated by live use
    assert jm.disp_threshold > 0 and cm.disp_threshold > 0


# ---- Jump ------------------------------------------------------------------
def test_jump_fires_at_85pct_and_cos_085():
    eng, jm = _jump_engine()
    req = jm.disp_threshold
    t = _arm(eng, synth.v_sign, 0.0)
    # straight up stroke reaching ~88% of req (below the old 100% gate, above 85%)
    up = 0.86 * req
    t, j, _ = _drive(eng, synth.v_sign, [(0.5, 0.5 - 0.5 * up), (0.5, 0.5 - up)], t)
    assert j == 1

    # a stroke whose direction cosine is ~0.87 (was rejected at 0.90) now fires
    eng2, jm2 = _jump_engine()
    t = _arm(eng2, synth.v_sign, 0.0)
    dy = -1.4 * jm2.disp_threshold
    dx = 0.567 * abs(dy)      # cos = |dy|/hypot ~= 0.87
    t, j2, _ = _drive(eng2, synth.v_sign,
                      [(0.5 + dx * 0.5, 0.5 + dy * 0.5), (0.5 + dx, 0.5 + dy)], t)
    assert j2 == 1


def test_jump_no_fire_below_70pct():
    eng, jm = _jump_engine()
    req = jm.disp_threshold
    t = _arm(eng, synth.v_sign, 0.0)
    up = 0.65 * req           # below live req (0.85) and below 70%
    t, j, _ = _drive(eng, synth.v_sign,
                     [(0.5, 0.5 - 0.5 * up), (0.5, 0.5 - up), (0.5, 0.5 - up)], t)
    assert j == 0


def test_jump_no_fire_wrong_direction_cos_075():
    eng, jm = _jump_engine()
    t = _arm(eng, synth.v_sign, 0.0)
    dy = -1.4 * jm.disp_threshold
    dx = 0.882 * abs(dy)      # cos ~= 0.75 (< 0.85 live gate)
    t, j, _ = _drive(eng, synth.v_sign,
                     [(0.5 + dx * 0.5, 0.5 + dy * 0.5), (0.5 + dx, 0.5 + dy)], t)
    assert j == 0


# ---- Crouch ----------------------------------------------------------------
def test_crouch_fires_at_85pct():
    eng, cm = _crouch_engine()
    req = cm.disp_threshold
    t = _arm(eng, synth.open_hand, 0.0)
    down = 0.86 * req         # down stroke ~86% of req
    t, _, c = _drive(eng, synth.open_hand,
                     [(0.5, 0.5 + 0.5 * down), (0.5, 0.5 + down)], t)
    assert c == 1


def test_crouch_no_fire_below_70pct_and_wrong_dir():
    eng, cm = _crouch_engine()
    req = cm.disp_threshold
    t = _arm(eng, synth.open_hand, 0.0)
    down = 0.65 * req
    t, _, c = _drive(eng, synth.open_hand,
                     [(0.5, 0.5 + 0.5 * down), (0.5, 0.5 + down), (0.5, 0.5 + down)], t)
    assert c == 0


# ---- Stop / Direction untouched by the live leniency -----------------------
def test_wrong_stroke_shows_retry_hint_without_recal():
    # D-130 Fix B: a deliberate wrong-direction stroke after READY shows a
    # non-destructive retry hint and NEVER enters recalibration.
    from before_gesture import teach_controller as tc

    def cap(ctl, key, fn, t0, dt=0.05):
        ctl.on_key(key, t0); t = t0
        for i in range(40):
            if ctl.capturing is None:
                break
            lm = fn(i); ctl.feed(lm.tolist(), 0.9, _palm(lm), t); t += dt
        return t

    ctl = tc.TeachController()
    t = 0.0
    for _ in range(3):
        t = cap(ctl, "j", lambda i: synth.v_sign(jitter=0.001, seed=i), t + 1.0)
    ctl.on_key("m", t + 0.2); t += 0.3
    for cy in (0.55, 0.50, 0.45, 0.40, 0.35):
        lm = synth.v_sign(center=(0.5, cy)); ctl.feed(lm.tolist(), 0.9, _palm(lm), t); t += 0.05
    ctl.on_key("e", t); t += 0.05
    ctl.on_key("f", t + 0.2); t += 0.5
    assert ctl.verified and ctl.proof_state == "ready"
    r0 = ctl.jump_model.radius
    for _ in range(8):
        lm = synth.v_sign(center=(0.5, 0.5)); ctl.feed(lm.tolist(), 0.9, _palm(lm), t); t += 0.1
    # diagonal stroke that starts forward-ish but drifts wrong (cos ~0.6 < 0.85)
    for cx, cy in ((0.55, 0.46), (0.60, 0.42), (0.66, 0.38), (0.72, 0.34)):
        lm = synth.v_sign(center=(cx, cy)); ctl.feed(lm.tolist(), 0.9, _palm(lm), t); t += 0.05
    assert "SIGN NOT MATCHED" in ctl.big_message(t)
    assert ctl.proof_state == "ready" and ctl.jump_model.radius == r0


def test_stop_unaffected_by_live_constants():
    # Stop has no cos/displacement gate; live constants must not appear there.
    stop = _pose(synth.fist, "STOP", 7000)
    eng = ce.Engine(cfg=ce.EngineConfig(stop_pose=stop))
    t = 0.0
    on = 0
    for _ in range(6):
        for cmd in eng.step(_obs(synth.fist, (0.5, 0.5), t)):
            if cmd.type.value == "STOP_ON":
                on += 1
        t += 0.05
    assert on == 1 and eng.stop_active
