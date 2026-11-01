"""8-direction motion primitives + Jump threshold learning (D-101).

Pure numpy. Screen-space palm-center displacement is quantized to one of 8
primitives; Jump teaching records the dominant direction plus displacement /
velocity thresholds derived from the taught strokes.

Direction index (screen space, y DOWN as in image coords):
    0 = right, 1 = down-right, 2 = down, 3 = down-left,
    4 = left, 5 = up-left, 6 = up, 7 = up-right
Bench/game "up" jump therefore maps to index 6.
"""
from __future__ import annotations

from dataclasses import dataclass
from typing import List, Optional, Sequence, Tuple

import numpy as np

DIR8_NAMES = ("right", "down-right", "down", "down-left",
              "left", "up-left", "up", "up-right")


def quantize_dir8(dx: float, dy: float) -> int:
    """Quantize a displacement vector (screen space, y down) to 0..7."""
    ang = np.arctan2(dy, dx)              # -pi..pi, 0 = +x
    idx = int(round(ang / (np.pi / 4.0))) % 8
    return idx


@dataclass
class JumpMotionModel:
    dir8: int
    disp_threshold: float        # displacement (normalized image units)
    vel_threshold: float         # units / second
    teaching_quality: float
    peak_speed: float = 0.0      # learned peak stroke speed (aux; not a gate)

    @property
    def unit(self) -> Tuple[float, float]:
        return dir8_to_unit(self.dir8)


def dir8_to_unit(dir8: int) -> Tuple[float, float]:
    """Unit vector (screen space, y down) for a quantized 8-direction."""
    a = dir8 * (np.pi / 4.0)
    return float(np.cos(a)), float(np.sin(a))


def _stroke_vector(track: Sequence[Tuple[float, float]]) -> Tuple[float, float]:
    a = np.asarray(track[0], dtype=np.float64)
    b = np.asarray(track[-1], dtype=np.float64)
    return float(b[0] - a[0]), float(b[1] - a[1])


def learn_jump_motion(strokes: Sequence[Sequence[Tuple[float, float]]],
                      dts: Sequence[float]) -> JumpMotionModel:
    """Derive the 8-dir primitive and thresholds from taught strokes.

    strokes: list of palm-center tracks (each a list of (x,y)).
    dts: per-stroke total elapsed seconds.
    """
    if not strokes:
        raise ValueError("no strokes")
    dirs = []
    disps = []
    vels = []
    for track, dt in zip(strokes, dts):
        if len(track) < 2 or dt <= 0:
            continue
        dx, dy = _stroke_vector(track)
        disp = float(np.hypot(dx, dy))
        dirs.append(quantize_dir8(dx, dy))
        disps.append(disp)
        vels.append(disp / dt)
    if not disps:
        raise ValueError("no usable strokes")
    dir8 = int(np.bincount(dirs).argmax())
    disp_med = float(np.median(disps))
    vel_med = float(np.median(vels))
    # trigger earlier than the taught full displacement (fire on prefix)
    disp_thr = disp_med * 0.55
    vel_thr = vel_med * 0.45
    # quality: direction agreement fraction
    quality = float(np.mean([1.0 if d == dir8 else 0.0 for d in dirs]))
    peak = float(max(vels))     # learned peak stroke speed (aux for settle)
    return JumpMotionModel(dir8=dir8, disp_threshold=disp_thr,
                           vel_threshold=vel_thr, teaching_quality=quality,
                           peak_speed=peak)


@dataclass
class MotionTracker:
    """Live displacement/velocity accumulator from an armed origin."""
    origin: Optional[Tuple[float, float]] = None
    last_p: Optional[Tuple[float, float]] = None
    last_t: Optional[float] = None
    peak_vel: float = 0.0

    def arm(self, p: Tuple[float, float], t: float) -> None:
        self.origin = p
        self.last_p = p
        self.last_t = t
        self.peak_vel = 0.0

    def reset(self) -> None:
        self.origin = None
        self.last_p = None
        self.last_t = None
        self.peak_vel = 0.0

    def update(self, p: Tuple[float, float], t: float) -> Tuple[float, int, float]:
        """Return (displacement, dir8, instantaneous velocity)."""
        if self.origin is None:
            self.arm(p, t)
            return 0.0, 0, 0.0
        dx = p[0] - self.origin[0]
        dy = p[1] - self.origin[1]
        disp = float(np.hypot(dx, dy))
        d8 = quantize_dir8(dx, dy)
        vel = 0.0
        if self.last_p is not None and self.last_t is not None and t > self.last_t:
            step = float(np.hypot(p[0] - self.last_p[0], p[1] - self.last_p[1]))
            vel = step / (t - self.last_t)
            self.peak_vel = max(self.peak_vel, vel)
        self.last_p = p
        self.last_t = t
        return disp, d8, vel


def crosses_threshold(disp: float, d8: int, vel: float,
                      model: JumpMotionModel) -> bool:
    return (d8 == model.dir8 and disp >= model.disp_threshold
            and vel >= model.vel_threshold)
