import numpy as np

from before_gesture import motion_primitive as mp


def test_dir8_quantization_cardinals():
    assert mp.quantize_dir8(1, 0) == 0     # right
    assert mp.quantize_dir8(0, 1) == 2     # down
    assert mp.quantize_dir8(-1, 0) == 4    # left
    assert mp.quantize_dir8(0, -1) == 6    # up


def test_dir8_quantization_diagonals():
    assert mp.quantize_dir8(1, 1) == 1     # down-right
    assert mp.quantize_dir8(-1, -1) == 5   # up-left
    assert mp.quantize_dir8(1, -1) == 7    # up-right


def test_learn_jump_motion_up():
    strokes = [[(0.5, 0.6), (0.5, 0.35)] for _ in range(3)]  # upward
    dts = [0.2, 0.22, 0.19]
    m = mp.learn_jump_motion(strokes, dts)
    assert m.dir8 == 6
    assert m.disp_threshold < 0.25
    assert m.vel_threshold > 0


def test_threshold_crossing():
    strokes = [[(0.5, 0.6), (0.5, 0.35)] for _ in range(3)]
    m = mp.learn_jump_motion(strokes, [0.2, 0.2, 0.2])
    tr = mp.MotionTracker()
    tr.arm((0.5, 0.6), 0.0)
    # not yet moved
    disp, d8, vel = tr.update((0.5, 0.58), 0.05)
    assert not mp.crosses_threshold(disp, d8, vel, m)
    # big fast upward move
    disp, d8, vel = tr.update((0.5, 0.30), 0.10)
    assert mp.crosses_threshold(disp, d8, vel, m)
