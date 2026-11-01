"""User-defined hand-shape model (D-100).

Pure numpy: extracts normalized features from 21 MediaPipe landmarks and builds
a per-user pose model from 3 recordings of >=20 valid frames each, validating
per recording (not by mixing takes and self-scoring).

Landmark topology (MediaPipe hand, 21 points):
    0 wrist; 1-4 thumb; 5-8 index; 9-12 middle; 13-16 ring; 17-20 pinky.
Palm center = mean of {0,5,9,13,17}; palm width = dist(5,17).

Normalizations (D-098/D-100):
    - wrist origin
    - palm-width scale
    - palm-axis rotation (align wrist->middle-MCP to +Y)
Features: normalized 21-point shape (42), finger joint angles, fingertip
distances from palm center, finger extension ratios.
"""
from __future__ import annotations

from dataclasses import dataclass
from typing import List, Optional, Sequence

import numpy as np

WRIST = 0
INDEX_MCP = 5
MIDDLE_MCP = 9
RING_MCP = 13
PINKY_MCP = 17
FINGER_TIPS = (4, 8, 12, 16, 20)
FINGER_MCPS = (2, 5, 9, 13, 17)
FINGER_PIPS = (3, 6, 10, 14, 18)
PALM_IDS = (0, 5, 9, 13, 17)

# Registration constants (D-100)
TAKE_SECONDS = 1.20
TAKES_REQUIRED = 3
MIN_VALID_FRAMES = 20
# Quality / safety gates
TRACK_QUALITY_MIN = 0.55
HAND_SIZE_MIN = 0.06          # palm width in normalized image units

# Teaching geometry gates (separate from MediaPipe confidence).
# A frame is only teachable if the WHOLE hand sits inside the safe frame and is
# large enough — a confident landmark set that is clipped by the frame edge is
# NOT acceptable teaching data.
TEACH_MARGIN = 0.05          # required inside [margin, 1-margin] on both axes
TEACH_BBOX_MIN = 0.09        # min hand bounding-box extent (normalized)


def teaching_geometry(landmarks) -> "tuple[bool, str]":
    """Return (ok, reason) for teaching placement, independent of confidence.

    Coordinates are mirrored image-normalized (x,y in [0,1], y down). Reasons
    are the participant-facing correction messages.
    """
    pts = np.asarray(landmarks, dtype=np.float64)[:, :2]
    xs, ys = pts[:, 0], pts[:, 1]
    lo, hi = TEACH_MARGIN, 1.0 - TEACH_MARGIN
    violations = []
    if xs.min() < lo:
        violations.append("MOVE HAND RIGHT")
    if xs.max() > hi:
        violations.append("MOVE HAND LEFT")
    if ys.min() < lo:
        violations.append("MOVE HAND DOWN")
    if ys.max() > hi:
        violations.append("MOVE HAND UP")
    if len(violations) >= 2:
        return False, "HAND OUT OF FRAME"
    if violations:
        return False, violations[0]
    w = float(xs.max() - xs.min())
    h = float(ys.max() - ys.min())
    if max(w, h) < TEACH_BBOX_MIN:
        return False, "MOVE CLOSER"
    return True, "OK"
SAFE_MARGIN = 0.05           # keep landmarks inside [margin, 1-margin]
# A single steady hold's own p90 frame-to-medoid distance was measured at ~0.05
# and max ~0.06, so the old 0.055 cap flagged legitimate holds as "unstable".
# This cap only rejects a take whose internal jitter is grossly larger than a
# real hold (the corrupted-take regression uses ~0.2+); it is NOT the accept
# threshold (see below).
WITHIN_CLASS_DISP_MAX = 0.14
TAKE_INCONSISTENT_MAX = 0.16   # spread of take-medoids for held-out consistency
SEPARATION_MARGIN_MIN = 0.045

# --- Personal accept radius (individual reproduction tolerance) --------------
# The accept RADIUS is the pose model's acceptance threshold: a live frame with
# pose_distance <= radius is "within" the personal pose. It is derived from the
# person's own teaching frame distribution (a robust high quantile) times a
# reproduction margin, then clamped and (once a second class exists) bounded so
# the two classes never overlap. It is NOT a flat global loosening.
REPRO_MARGIN_K = 3.0          # natural re-creation is materially wider than hold
MIN_ACCEPT_RADIUS = 0.12
MAX_ACCEPT_RADIUS = 0.45
CLASS_GAP = 0.06              # required empty gap between the two accept regions


def _palm_center(pts: np.ndarray) -> np.ndarray:
    return pts[list(PALM_IDS)].mean(axis=0)


def _palm_width(pts: np.ndarray) -> float:
    return float(np.linalg.norm(pts[INDEX_MCP] - pts[PINKY_MCP]))


def normalize_landmarks(landmarks: Sequence[Sequence[float]]) -> np.ndarray:
    """Return 21x2 normalized (translation/scale/rotation invariant) points."""
    pts = np.asarray(landmarks, dtype=np.float64)[:, :2].copy()
    origin = pts[WRIST].copy()
    pts -= origin
    scale = _palm_width(pts + origin - origin) or 1.0
    # palm width computed on translated pts:
    scale = float(np.linalg.norm(pts[INDEX_MCP] - pts[PINKY_MCP])) or 1.0
    pts /= scale
    # rotation: align wrist->middle MCP axis to +Y
    axis = pts[MIDDLE_MCP] - pts[WRIST]
    theta = np.arctan2(axis[0], axis[1])  # angle from +Y
    c, s = np.cos(theta), np.sin(theta)
    rot = np.array([[c, -s], [s, c]])
    pts = pts @ rot.T
    return pts


def _angle(a: np.ndarray, b: np.ndarray, c: np.ndarray) -> float:
    v1 = a - b
    v2 = c - b
    n1 = np.linalg.norm(v1)
    n2 = np.linalg.norm(v2)
    if n1 == 0 or n2 == 0:
        return 0.0
    cosv = float(np.clip(np.dot(v1, v2) / (n1 * n2), -1.0, 1.0))
    return float(np.arccos(cosv))


def feature_vector(landmarks: Sequence[Sequence[float]]) -> np.ndarray:
    """Full descriptor used for pose matching (invariant to T/S/R)."""
    pts = normalize_landmarks(landmarks)
    palm = _palm_center(pts)
    shape = pts.reshape(-1)                                   # 42
    tip_dist = np.array([np.linalg.norm(pts[t] - palm) for t in FINGER_TIPS])  # 5
    angles = np.array([_angle(pts[m], pts[p], pts[t])
                       for m, p, t in zip(FINGER_MCPS, FINGER_PIPS, FINGER_TIPS)])  # 5
    ext = np.array([np.linalg.norm(pts[t] - pts[WRIST]) /
                    (np.linalg.norm(pts[m] - pts[WRIST]) + 1e-6)
                    for t, m in zip(FINGER_TIPS, FINGER_MCPS)])  # 5
    return np.concatenate([shape, tip_dist, angles, ext])


# Weighting for distance: shape vs derived features.
_W_SHAPE = 0.55
_W_TIP = 0.20
_W_ANG = 0.15
_W_EXT = 0.10


def pose_distance(f1: np.ndarray, f2: np.ndarray) -> float:
    shape = np.linalg.norm(f1[:42] - f2[:42]) / np.sqrt(42)
    tip = np.linalg.norm(f1[42:47] - f2[42:47]) / np.sqrt(5)
    ang = np.linalg.norm(f1[47:52] - f2[47:52]) / (np.pi * np.sqrt(5))
    ext = np.linalg.norm(f1[52:57] - f2[52:57]) / np.sqrt(5)
    return _W_SHAPE * shape + _W_TIP * tip + _W_ANG * ang + _W_EXT * ext


@dataclass
class FrameSample:
    landmarks: Sequence[Sequence[float]]
    quality: float


@dataclass
class Recording:
    frames: List[FrameSample]

    def valid_frames(self) -> List[FrameSample]:
        out = []
        for fr in self.frames:
            if fr.quality < TRACK_QUALITY_MIN:
                continue
            pts = np.asarray(fr.landmarks, dtype=np.float64)[:, :2]
            if _palm_width(pts) < HAND_SIZE_MIN:
                continue
            if pts.min() < SAFE_MARGIN or pts.max() > 1.0 - SAFE_MARGIN:
                continue
            out.append(fr)
        return out


@dataclass
class PoseModel:
    name: str
    centroid: np.ndarray
    radius: float           # within-class dispersion (robust)
    take_count: int


class RegistrationError(Exception):
    pass


def _robust_centroid(feats: np.ndarray) -> np.ndarray:
    return np.median(feats, axis=0)


def take_stability(recording: Recording) -> "tuple[bool, float, int, str]":
    """THE single per-take validator (D-100).

    Returns (ok, disp, valid_frame_count, reason). It is applied IDENTICALLY at
    capture time (teach_controller) and at register time so a take accepted
    while recording is never rejected later. reason is one of
    OK / TOO_FEW_FRAMES / UNSTABLE.
    """
    vf = recording.valid_frames()
    if len(vf) < MIN_VALID_FRAMES:
        return False, 0.0, len(vf), "TOO_FEW_FRAMES"
    feats = np.array([feature_vector(f.landmarks) for f in vf])
    c = _robust_centroid(feats)
    # reject only a take whose internal jitter is grossly larger than a genuine
    # hold (does NOT reject natural hold wobble).
    disp = float(np.median([pose_distance(f, c) for f in feats]))
    if disp > WITHIN_CLASS_DISP_MAX:
        return False, disp, len(vf), "UNSTABLE"
    return True, disp, len(vf), "OK"


def register_pose(name: str, recordings: Sequence[Recording]) -> PoseModel:
    """Build the model from validated recordings (D-100).

    Uses the shared `take_stability` validator so its per-take verdict matches
    capture time exactly. Raises RegistrationError when a take is invalid — the
    caller must NOT keep or merge a rejected recording into any existing model.
    """
    if len(recordings) < TAKES_REQUIRED:
        raise RegistrationError(
            f"{name}: need {TAKES_REQUIRED} takes, got {len(recordings)}")

    take_medoids: List[np.ndarray] = []
    all_feats: List[np.ndarray] = []
    for i, rec in enumerate(recordings):
        ok, disp, n, reason = take_stability(rec)
        if not ok:
            if reason == "TOO_FEW_FRAMES":
                raise RegistrationError(
                    f"{name} take {i+1}: {n} valid frames < {MIN_VALID_FRAMES}")
            raise RegistrationError(
                f"{name} take {i+1}: unstable (disp {disp:.3f} > "
                f"{WITHIN_CLASS_DISP_MAX})")
        vf = rec.valid_frames()
        feats = np.array([feature_vector(f.landmarks) for f in vf])
        take_medoids.append(_robust_centroid(feats))
        all_feats.extend(feats)

    # held-out consistency across the 3 takes (all three must describe one pose)
    overall = _robust_centroid(np.array(take_medoids))
    spread = float(np.median([pose_distance(c, overall) for c in take_medoids]))
    if spread > TAKE_INCONSISTENT_MAX:
        raise RegistrationError(
            f"{name}: takes inconsistent (spread {spread:.3f})")

    # robust center over ALL accepted frames from ALL takes (every take
    # contributes), and a personal accept radius from the frame distribution.
    centroid = _robust_centroid(np.array(all_feats))
    dists = np.array([pose_distance(f, centroid) for f in all_feats])
    p90 = float(np.percentile(dists, 90))
    radius = float(np.clip(p90 * REPRO_MARGIN_K,
                           MIN_ACCEPT_RADIUS, MAX_ACCEPT_RADIUS))
    return PoseModel(name=name, centroid=centroid, radius=radius,
                     take_count=len(recordings))


def constrain_separation(a: PoseModel, b: PoseModel) -> None:
    """Shrink both accept radii in place so the two regions never overlap.

    Keeps a CLASS_GAP of empty space between the accept balls. Raises if the
    class centers are so close that even minimal radii would overlap (the poses
    are not separable and one must be re-registered).
    """
    d = pose_distance(a.centroid, b.centroid)
    budget = d - CLASS_GAP
    if budget < 2 * MIN_ACCEPT_RADIUS * 0.5:
        raise RegistrationError(
            f"classes too close (center dist {d:.3f}); re-register a more "
            "distinct pose")
    if a.radius + b.radius > budget:
        scale = budget / (a.radius + b.radius)
        a.radius = max(MIN_ACCEPT_RADIUS * 0.5, a.radius * scale)
        b.radius = max(MIN_ACCEPT_RADIUS * 0.5, b.radius * scale)


def check_separation(a: PoseModel, b: PoseModel) -> float:
    """Return the class-separation margin; caller rejects if below minimum."""
    return pose_distance(a.centroid, b.centroid) - (a.radius + b.radius)


def is_separable(a: PoseModel, b: PoseModel) -> bool:
    return pose_distance(a.centroid, b.centroid) >= (
        MIN_ACCEPT_RADIUS + CLASS_GAP)


# Pose match reasons (diagnostics).
R_ARMED = "Armed"
R_DIST = "DistanceTooLarge"
R_AMBIG = "ClassAmbiguous"
R_NOMODEL = "NoModel"


@dataclass
class MatchResult:
    best: Optional[str]
    best_dist: float
    second: Optional[str]
    second_dist: float
    margin: float
    armed: bool
    reason: str = ""


def match_pose(feat: np.ndarray, models: Sequence[PoseModel]) -> MatchResult:
    if not models:
        return MatchResult(None, float("inf"), None, float("inf"), 0.0, False,
                           R_NOMODEL)
    scored = sorted(((pose_distance(feat, m.centroid), m) for m in models),
                    key=lambda x: x[0])
    best_d, best_m = scored[0]
    second_d, second_name = (scored[1][0], scored[1][1].name) if len(scored) > 1 \
        else (float("inf"), None)
    margin = second_d - best_d
    within = best_d <= best_m.radius
    # ambiguous only if the runner-up is also inside ITS accept region
    ambiguous = (len(scored) > 1 and scored[1][0] <= scored[1][1].radius)
    if not within:
        reason = R_DIST
    elif ambiguous:
        reason = R_AMBIG
    else:
        reason = R_ARMED
    armed = within and not ambiguous
    return MatchResult(best_m.name, best_d, second_name, second_d, margin,
                       armed, reason)
