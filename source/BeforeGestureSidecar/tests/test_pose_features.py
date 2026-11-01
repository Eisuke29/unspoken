import numpy as np
import pytest

from before_gesture import personal_pose as pp
import synth


def _feat(lm):
    return pp.feature_vector(lm)


def test_translation_invariance():
    a = _feat(synth.open_hand(center=(0.4, 0.4)))
    b = _feat(synth.open_hand(center=(0.7, 0.6)))
    assert pp.pose_distance(a, b) < 0.02


def test_scale_invariance():
    a = _feat(synth.open_hand(scale=0.14))
    b = _feat(synth.open_hand(scale=0.24))
    assert pp.pose_distance(a, b) < 0.02


def test_moderate_rotation_invariance():
    a = _feat(synth.open_hand(rot=0.0))
    b = _feat(synth.open_hand(rot=np.deg2rad(15)))
    assert pp.pose_distance(a, b) < 0.05


def test_open_vs_fist_separated_in_feature_space():
    a = _feat(synth.open_hand())
    b = _feat(synth.fist())
    assert pp.pose_distance(a, b) > 0.08


def _recordings(kind, takes=3, frames=25, seed0=0):
    recs = []
    for t in range(takes):
        fr = []
        for i in range(frames):
            lm = (synth.open_hand(jitter=0.001, seed=seed0 + t * 100 + i)
                  if kind == "open"
                  else synth.fist(jitter=0.001, seed=seed0 + t * 100 + i))
            fr.append(pp.FrameSample(lm.tolist(), 0.9))
        recs.append(pp.Recording(fr))
    return recs


def test_recording_group_validation_passes():
    model = pp.register_pose("JUMP", _recordings("open"))
    assert model.take_count == 3
    assert model.radius > 0


def test_low_quality_rejection():
    recs = _recordings("open")
    for fr in recs[0].frames:
        fr.quality = 0.2  # below TRACK_QUALITY_MIN
    with pytest.raises(pp.RegistrationError):
        pp.register_pose("JUMP", recs)


def test_too_few_takes_rejected():
    with pytest.raises(pp.RegistrationError):
        pp.register_pose("JUMP", _recordings("open", takes=2))


def test_unstable_teaching_rejection():
    recs = _recordings("open")
    # corrupt one take with huge jitter -> unstable dispersion
    bad = []
    for i in range(25):
        bad.append(pp.FrameSample(
            synth.open_hand(jitter=0.05, seed=999 + i).tolist(), 0.9))
    recs[1] = pp.Recording(bad)
    with pytest.raises(pp.RegistrationError):
        pp.register_pose("JUMP", recs)


def test_class_overlap_rejection():
    a = pp.register_pose("JUMP", _recordings("open", seed0=0))
    b = pp.register_pose("STOP", _recordings("open", seed0=5000))
    # same shape -> not separable
    assert not pp.is_separable(a, b)


def test_class_separation_accepts_distinct():
    a = pp.register_pose("JUMP", _recordings("open"))
    b = pp.register_pose("STOP", _recordings("fist"))
    assert pp.is_separable(a, b)
