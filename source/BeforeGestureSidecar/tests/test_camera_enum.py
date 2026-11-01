import pytest

from before_gesture.camera import resolve_exact_index, CameraError, PREFERRED_NAME

# Real enumeration observed on the target machine (for reference):
REAL = ["Iriun Webcam", "Integrated Camera", "FMVＳﾏｰﾄｶﾀﾗ", "DemoCreator Virtual Camera"]


def test_single_exact_match_resolves():
    assert resolve_exact_index(PREFERRED_NAME, REAL) == 1


def test_zero_match_stops():
    with pytest.raises(CameraError):
        resolve_exact_index(PREFERRED_NAME, ["Iriun Webcam", "Other Cam"])


def test_multiple_match_stops():
    with pytest.raises(CameraError):
        resolve_exact_index(PREFERRED_NAME,
                            ["Integrated Camera", "Integrated Camera"])


def test_empty_enumeration_stops():
    with pytest.raises(CameraError):
        resolve_exact_index(PREFERRED_NAME, [])


def test_no_substring_fallback():
    # "Integrated Camera 2" must NOT satisfy an exact "Integrated Camera".
    with pytest.raises(CameraError):
        resolve_exact_index(PREFERRED_NAME, ["Integrated Camera 2", "Webcam"])
