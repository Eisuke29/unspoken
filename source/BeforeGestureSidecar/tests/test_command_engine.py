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


def _recordings(kind, seed0=0):
    recs = []
    for t in range(3):
        fr = []
        for i in range(25):
            lm = (synth.open_hand(jitter=0.001, seed=seed0 + t * 100 + i)
                  if kind == "open"
                  else synth.fist(jitter=0.001, seed=seed0 + t * 100 + i))
            fr.append(pp.FrameSample(lm.tolist(), 0.9))
        recs.append(pp.Recording(fr))
    return recs


def _engine():
    jp = pp.register_pose("JUMP", _recordings("open"))
    sp = pp.register_pose("STOP", _recordings("fist", seed0=7000))
    strokes = [[(0.5, 0.5), (0.5, 0.34)] for _ in range(3)]
    jm = mp.learn_jump_motion(strokes, [0.2, 0.2, 0.2])
    cfg = ce.EngineConfig(jump_pose=jp, jump_motion=jm, stop_pose=sp)
    return ce.Engine(cfg=cfg)


def _obs(kind, center, t):
    lm = (synth.open_hand(center=center) if kind == "open"
          else synth.fist(center=center))
    return ce.FrameObs(t=t, tracked=True, quality=0.9,
                       landmarks=lm.tolist(), palm=_palm(lm))


def test_jump_threshold_crossing_immediate_fire():
    protocol.reset_ids()
    eng = _engine()
    fired = []
    t = 0.0
    # hold open pose to arm+latch
    for _ in range(12):
        fired += eng.step(_obs("open", (0.5, 0.5), t))
        t += 0.1
    assert eng.jstate.name in ("POSE_HELD_IDLE", "STROKE_TRACKING")
    # fast upward move
    fired += eng.step(_obs("open", (0.5, 0.40), t)); t += 0.05
    fired += eng.step(_obs("open", (0.5, 0.30), t)); t += 0.05
    types = [c.type.value for c in fired]
    assert "JUMP" in types


def test_pose_only_no_jump():
    eng = _engine()
    fired = []
    t = 0.0
    for _ in range(20):
        fired += eng.step(_obs("open", (0.5, 0.5), t))  # never moves
        t += 0.1
    assert not any(c.type.value == "JUMP" for c in fired)


def test_wrong_pose_motion_no_jump():
    eng = _engine()
    fired = []
    t = 0.0
    # fist (stop pose) moving up should not fire JUMP
    for c in range(12):
        fired += eng.step(_obs("fist", (0.5, 0.5), t)); t += 0.1
    fired += eng.step(_obs("fist", (0.5, 0.30), t))
    assert not any(c.type.value == "JUMP" for c in fired)


def test_one_gesture_one_fire():
    eng = _engine()
    t = 0.0
    fires = 0
    for _ in range(12):
        eng.step(_obs("open", (0.5, 0.5), t)); t += 0.1
    for _ in range(6):  # keep moving/holding after crossing
        cs = eng.step(_obs("open", (0.5, 0.28), t)); t += 0.05
        fires += sum(1 for c in cs if c.type.value == "JUMP")
    assert fires == 1


def test_stop_hold_emits_stop_on():
    eng = _engine()
    t = 0.0
    cmds = []
    for _ in range(8):
        cmds += eng.step(_obs("fist", (0.5, 0.5), t)); t += 0.05
    assert any(c.type.value == "STOP_ON" for c in cmds)
    assert eng.stop_active


def test_stop_intentional_release():
    eng = _engine()
    t = 0.0
    for _ in range(8):
        eng.step(_obs("fist", (0.5, 0.5), t)); t += 0.05
    assert eng.stop_active
    rel = []
    for _ in range(8):  # switch to open (deliberate break), still tracked
        rel += eng.step(_obs("open", (0.5, 0.5), t)); t += 0.05
    assert any(c.type.value == "STOP_OFF" for c in rel)
    assert not eng.stop_active


def test_tracking_loss_does_not_release():
    eng = _engine()
    t = 0.0
    for _ in range(8):
        eng.step(_obs("fist", (0.5, 0.5), t)); t += 0.05
    assert eng.stop_active
    # tracking loss frames
    for _ in range(10):
        cs = eng.step(ce.FrameObs(t=t, tracked=False, quality=0.0,
                                  landmarks=None, palm=None))
        assert not any(c.type.value == "STOP_OFF" for c in cs)
        t += 0.05
    assert eng.stop_active


def test_stop_latch_ignores_translation_and_high_palm_speed_for_one_second():
    eng = _engine()
    dt = 1.0 / 30.0
    t = 0.0
    on = []
    for _ in range(4):
        on += eng.step(_obs("fist", (0.5, 0.5), t))
        t += dt
    assert sum(c.type.value == "STOP_ON" for c in on) == 1
    assert eng.stop_state == ce.StopState.HELD

    events = []
    centers = ((0.30, 0.35), (0.70, 0.65), (0.30, 0.65), (0.70, 0.35))
    for i in range(31):
        events += eng.step(_obs("fist", centers[i % len(centers)], t))
        t += dt
    assert not any(c.type.value == "STOP_OFF" for c in events)
    assert eng.stop_active and eng.stop_state == ce.StopState.HELD


def test_stop_latch_ignores_low_quality_and_invalid_geometry_mismatches():
    eng = _engine()
    t = 0.0
    for _ in range(4):
        eng.step(_obs("fist", (0.5, 0.5), t))
        t += 0.05
    assert eng.stop_active

    events = []
    for _ in range(10):
        lm = synth.open_hand()
        events += eng.step(ce.FrameObs(
            t=t, tracked=True, quality=0.1,
            landmarks=lm.tolist(), palm=_palm(lm)))
        t += 0.05
    assert not any(c.type.value == "STOP_OFF" for c in events)
    assert eng.stop_active and eng.stop_state == ce.StopState.HELD
    assert eng.get_stop_diagnostics(t).reject_reason == "low-quality"

    for _ in range(10):
        lm = synth.open_hand(center=(0.5, 0.98))
        events += eng.step(ce.FrameObs(
            t=t, tracked=True, quality=0.9,
            landmarks=lm.tolist(), palm=_palm(lm)))
        t += 0.05
    assert not any(c.type.value == "STOP_OFF" for c in events)
    assert eng.stop_active and eng.stop_state == ce.StopState.HELD
    assert eng.get_stop_diagnostics(t).reject_reason == "geometry-invalid"


def test_stop_release_candidate_requires_four_valid_mismatches_and_rematch_resets():
    eng = _engine()
    t = 0.0
    for _ in range(4):
        eng.step(_obs("fist", (0.5, 0.5), t))
        t += 0.05
    assert eng.stop_active

    for _ in range(3):
        assert not eng.step(_obs("open", (0.5, 0.5), t))
        t += 0.05
    assert eng.stop_state == ce.StopState.RELEASE_CANDIDATE

    assert not eng.step(_obs("fist", (0.65, 0.4), t))
    t += 0.05
    assert eng.stop_state == ce.StopState.HELD

    off = []
    for _ in range(4):
        off += eng.step(_obs("open", (0.5, 0.5), t))
        t += 0.05
    assert sum(c.type.value == "STOP_OFF" for c in off) == 1
    assert not eng.stop_active and eng.stop_state == ce.StopState.NEUTRAL


def test_invalid_stop_entry_never_fires():
    eng = _engine()
    t = 0.0
    events = []
    for _ in range(8):
        lm = synth.fist()
        events += eng.step(ce.FrameObs(
            t=t, tracked=True, quality=0.1,
            landmarks=lm.tolist(), palm=_palm(lm)))
        t += 0.05
    for _ in range(8):
        lm = synth.fist(center=(0.5, 0.98))
        events += eng.step(ce.FrameObs(
            t=t, tracked=True, quality=0.9,
            landmarks=lm.tolist(), palm=_palm(lm)))
        t += 0.05
    assert not any(c.type.value == "STOP_ON" for c in events)
    assert not eng.stop_active and eng.stop_state == ce.StopState.NEUTRAL


def test_stop_on_off_latency_is_within_200ms_at_30hz_and_no_duplicate_fire():
    eng = _engine()
    dt = 1.0 / 30.0
    t = 100.0
    on = []
    for _ in range(20):
        on += eng.step(_obs("fist", (0.5, 0.5), t))
        t += dt
    on_cmds = [c for c in on if c.type.value == "STOP_ON"]
    assert len(on_cmds) == 1
    assert (on_cmds[0].t_monotonic - 100.0) * 1000.0 <= 200.0

    off_started = t
    off = []
    for _ in range(20):
        off += eng.step(_obs("open", (0.5, 0.5), t))
        t += dt
    off_cmds = [c for c in off if c.type.value == "STOP_OFF"]
    assert len(off_cmds) == 1
    assert (off_cmds[0].t_monotonic - off_started) * 1000.0 <= 200.0
    diag = eng.get_stop_diagnostics(t)
    assert diag.off_issued_ms > diag.on_issued_ms
    assert diag.reject_reason in ("released", "other-class")


def test_direction_left_and_right():
    jp = pp.register_pose("JUMP", _recordings("open"))
    cfg = ce.EngineConfig(jump_pose=jp, mode_direction=True)
    eng = ce.Engine(cfg=cfg)
    t = 0.0
    right = []
    for _ in range(8):
        lm = synth.point_hand(sign=1)
        right += eng.step(ce.FrameObs(t=t, tracked=True, quality=0.9,
                                      landmarks=lm.tolist()))
        t += 0.1
    assert any(c.type.value == "DIRECTION" and c.direction == 1 for c in right)

    eng2 = ce.Engine(cfg=ce.EngineConfig(mode_direction=True))
    t = 0.0
    left = []
    for _ in range(8):
        lm = synth.point_hand(sign=-1)
        left += eng2.step(ce.FrameObs(t=t, tracked=True, quality=0.9,
                                      landmarks=lm.tolist()))
        t += 0.1
    assert any(c.type.value == "DIRECTION" and c.direction == -1 for c in left)


def test_direction_fake_input_ten_each_preview_commit_and_route_are_consistent():
    eng = ce.Engine(cfg=ce.EngineConfig(mode_direction=True))
    t = 10.0
    counts = {-1: 0, 1: 0}
    for sign in (-1, 1):
        for _ in range(10):
            emitted = []
            for frame in range(7):
                lm = synth.point_hand(sign=sign)
                emitted += eng.step(ce.FrameObs(
                    t=t, tracked=True, quality=0.9, landmarks=lm.tolist()))
                if frame < 5:
                    assert eng.direction_preview(None) == sign
                t += 0.1
            commits = [c for c in emitted
                       if c.type.value == "DIRECTION"]
            assert len(commits) == 1
            assert commits[0].direction == sign
            counts[sign] += 1
            neutral = synth.open_hand()
            assert eng.step(ce.FrameObs(
                t=t, tracked=True, quality=0.9,
                landmarks=neutral.tolist())) == []
            assert eng.direction_preview(None) == 0
            t += 0.1
    assert counts == {-1: 10, 1: 10}


def test_direction_open_vertical_and_short_inputs_never_commit():
    for invalid_kind in ("open", "vertical", "short"):
        eng = ce.Engine(cfg=ce.EngineConfig(mode_direction=True))
        t = 20.0
        emitted = []
        frames = 4 if invalid_kind == "short" else 10
        for _ in range(frames):
            if invalid_kind == "open":
                lm = synth.open_hand()
            else:
                lm = synth.point_hand(sign=1)
                if invalid_kind == "vertical":
                    base = lm[5, :2].copy()
                    for n, index in enumerate((6, 7, 8), start=1):
                        lm[index, 0] = base[0]
                        lm[index, 1] = base[1] - 0.015 * n
            emitted += eng.step(ce.FrameObs(
                t=t, tracked=True, quality=0.9, landmarks=lm.tolist()))
            t += 0.1
        assert not any(c.type.value == "DIRECTION" for c in emitted), invalid_kind


def test_neutral_false_positive_simulation():
    eng = _engine()
    t = 0.0
    fires = 0
    rng = np.random.default_rng(3)
    for _ in range(120):  # ~ jittery neutral open-ish hand, small random drift
        cx = 0.5 + float(rng.normal(0, 0.01))
        cy = 0.5 + float(rng.normal(0, 0.01))
        lm = synth.fist(center=(cx, cy))  # relaxed non-jump pose
        cs = eng.step(ce.FrameObs(t=t, tracked=True, quality=0.9,
                                  landmarks=lm.tolist(), palm=_palm(lm)))
        fires += sum(1 for c in cs if c.type.value == "JUMP")
        t += 0.033
    assert fires == 0


def test_command_ids_are_monotonic_and_unique():
    protocol.reset_ids()
    c1 = protocol.Command(protocol.CommandType.JUMP, 0.0)
    c2 = protocol.Command(protocol.CommandType.STOP_ON, 0.1)
    assert c2.id == c1.id + 1
