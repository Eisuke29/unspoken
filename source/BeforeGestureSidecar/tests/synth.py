"""Synthetic 21-landmark hand generator for tests (no camera / no mediapipe)."""
from __future__ import annotations

import numpy as np

# Base open-hand layout in a local frame (wrist at origin, fingers up +y-ish).
# Screen convention y-down is applied later; here we just need a consistent 21.
_BASE_OPEN = np.array([
    [0.00, 0.00],           # 0 wrist
    [-0.30, 0.10], [-0.45, 0.30], [-0.55, 0.50], [-0.60, 0.70],  # thumb 1-4
    [-0.15, 0.20], [-0.18, 0.55], [-0.20, 0.80], [-0.21, 1.00],  # index 5-8
    [0.00, 0.22], [0.00, 0.60], [0.00, 0.88], [0.00, 1.10],      # middle 9-12
    [0.15, 0.20], [0.18, 0.55], [0.20, 0.80], [0.21, 1.00],      # ring 13-16
    [0.30, 0.15], [0.36, 0.45], [0.40, 0.66], [0.43, 0.84],      # pinky 17-20
], dtype=np.float64)

# Fist: fingertips curled back toward palm (short extension).
_BASE_FIST = _BASE_OPEN.copy()
for tip, mcp in ((4, 2), (8, 5), (12, 9), (16, 13), (20, 17)):
    _BASE_FIST[tip] = _BASE_FIST[mcp] + (_BASE_FIST[mcp] - _BASE_FIST[tip]) * 0.15
    _BASE_FIST[tip - 1] = _BASE_FIST[mcp] * 1.05


def _to_image(pts: np.ndarray, center=(0.5, 0.5), scale=0.18) -> np.ndarray:
    """Map local frame to a 21x3 image-normalized array (y down)."""
    out = pts.copy()
    out[:, 1] *= -1.0                      # local +y up -> image y down
    out = out * scale + np.array(center)
    z = np.zeros((out.shape[0], 1))
    return np.hstack([out, z])


def open_hand(center=(0.5, 0.5), scale=0.18, rot=0.0, jitter=0.0, seed=0):
    return _make(_BASE_OPEN, center, scale, rot, jitter, seed)


def fist(center=(0.5, 0.5), scale=0.18, rot=0.0, jitter=0.0, seed=0):
    return _make(_BASE_FIST, center, scale, rot, jitter, seed)


def point_hand(sign=1, center=(0.5, 0.5), scale=0.18):
    """Index extended laterally (sign>0 = right), other fingers curled."""
    pts = _BASE_FIST.copy()
    # extend index straight along +x*sign
    base = pts[5].copy()
    for k, i in enumerate((6, 7, 8)):
        pts[i] = base + np.array([sign * 0.25 * (k + 1) / 3.0, 0.02])
    return _make(pts, center, scale, 0.0, 0.0, 0)


def v_sign(center=(0.5, 0.5), scale=0.18, rot=0.0, spread=1.0, jitter=0.0,
           seed=0):
    """Index + middle extended (a 'V'), ring/pinky/thumb curled.

    `spread` scales the angle between index and middle so we can model natural
    finger-spacing variation across reproductions.
    """
    pts = _BASE_FIST.copy()
    # keep index (5-8) and middle (9-12) extended from the open layout
    for i in (5, 6, 7, 8, 9, 10, 11, 12):
        pts[i] = _BASE_OPEN[i].copy()
    # widen/narrow the V by pushing index left and middle right about their MCPs
    for tip, mcp, sgn in ((8, 5, -1.0), (12, 9, +1.0)):
        v = pts[tip] - pts[mcp]
        pts[tip] = pts[mcp] + v + np.array([sgn * 0.12 * (spread - 1.0), 0.0])
        pts[tip - 1] = pts[mcp] + (pts[tip] - pts[mcp]) * 0.6
    return _make(pts, center, scale, rot, jitter, seed)


def _make(pts, center, scale, rot, jitter, seed):
    p = pts.copy()
    if rot:
        c, s = np.cos(rot), np.sin(rot)
        p = p @ np.array([[c, -s], [s, c]]).T
    img = _to_image(p, center, scale)
    if jitter:
        rng = np.random.default_rng(seed)
        img[:, :2] += rng.normal(0, jitter, size=(21, 2))
    return img
