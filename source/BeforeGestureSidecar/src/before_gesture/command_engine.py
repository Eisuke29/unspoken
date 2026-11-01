"""Live command engine (D-101/D-102/D-103, always-on per D-108).

Combines the user pose model, learned Jump motion, static Stop, and fixed
index-finger Direction pointing into per-frame command emission. Emission is
NEVER gated by any game-side judgment; consequence of bad timing lives in the
game's physics, not here.

Pure logic: fed already-extracted per-frame observations, returns Commands.
"""
from __future__ import annotations

from collections import deque
from dataclasses import dataclass, field
from enum import Enum
from typing import List, Optional, Sequence, Tuple

import numpy as np

from . import personal_pose as pp
from .motion_primitive import (JumpMotionModel, MotionTracker,
                               crosses_threshold, quantize_dir8)
from .protocol import Command, CommandType

INDEX_MCP = 5
INDEX_TIP = 8


@dataclass
class FrameObs:
    """Per-frame observation handed to the engine."""
    t: float
    tracked: bool
    quality: float
    landmarks: Optional[Sequence[Sequence[float]]]  # 21x2/3, None if not tracked
    palm: Optional[Tuple[float, float]] = None       # palm center (screen)


class JumpState(Enum):
    # D-111 Pose-held, stroke-segmented repeated command.
    POSE_SEARCHING = "pose_searching"    # pose not yet 5/7 stable
    POSE_HELD_IDLE = "pose_held_idle"    # channel held, awaiting a stroke
    STROKE_TRACKING = "stroke_tracking"  # a stroke is in progress
    FIRED_LOCKOUT = "fired_lockout"      # fired this stroke; awaiting stroke end
    STROKE_REJECTED = "stroke_rejected"  # wrong-direction stroke, no fire


class StopState(Enum):
    NEUTRAL = "neutral"
    CANDIDATE = "candidate"
    HELD = "held"
    RELEASE_CANDIDATE = "release_candidate"


# UI labels for the STROKE line.
STROKE_LABEL = {
    JumpState.POSE_SEARCHING: "Idle",
    JumpState.POSE_HELD_IDLE: "Idle",
    JumpState.STROKE_TRACKING: "Tracking",
    JumpState.FIRED_LOCKOUT: "FiredLockout",
    JumpState.STROKE_REJECTED: "Rejected",
}

# D-111 stroke segmentation parameters (pose thresholds/models unchanged).
STABLE_WINDOW = 7             # 5-of-7 stability gate for holding the channel
STABLE_NEEDED = 5
COS_PASS = 0.90               # stroke direction vs learned direction (~25 deg)
# D-130 live Motion leniency (Jump/Crouch ONLY; Stop/Direction never use these).
# The learned model's required displacement is NOT modified/overwritten; live
# firing uses LIVE_REQ_FRAC of it, and the live direction cosine gate is looser.
LIVE_COS_PASS = 0.85          # live Jump/Crouch fire + wrong-dir gate (was 0.90)
LIVE_REQ_FRAC = 0.85          # live required displacement = 0.85 * learned req
START_DISP_FRAC = 0.25        # stroke start: disp fraction of required
START_SPEED_FRAC = 0.30       # stroke start: speed fraction of learned peak
START_FORWARD_COS = 0.30      # a stroke only STARTS when moving toward learned dir
REJECT_DISP_FRAC = 0.25       # wrong-dir reject once disp >= 25% of required
SETTLE_SPEED_FRAC = 0.15      # wrong-dir reject clears when speed <= 15% peak ...
SETTLE_FRAMES = 4             # ... for 4 consecutive frames (reject -> idle only)
# Rhythmic re-arm after a fire (fire conditions themselves are unchanged).
FIRE_LOCKOUT_S = 0.15         # min lockout after a fire (was 0.25)
REARM_REVERSE_FRAC = 0.15     # re-arm A: reverse >= 15% of required ...
REARM_REVERSE_FRAMES = 2      # ... with backward velocity 2 consecutive frames
REARM_SETTLE_SPEED_FRAC = 0.25  # re-arm B: speed <= 25% of learned peak ...
REARM_SETTLE_FRAMES = 2       # ... for 2 consecutive frames (no full stop)

# Stop timing / hysteresis (D-102)
STOP_ON_EVIDENCE = 4          # consecutive matched frames
STOP_OFF_EVIDENCE = 4         # consecutive intentional-mismatch frames (tracked)
# D-128 Crouch is a DISCRETE stroke channel (same shape as Jump). It reuses the
# shared stroke constants above (STABLE_NEEDED, COS_PASS, FIRE_LOCKOUT_S,
# REARM_*, START_*, REJECT_DISP_FRAC, SETTLE_*) so its feel matches Jump. It has
# NO held ON/OFF, NO grace, NO exit hysteresis, NO CROUCH_OFF (D-128 supersedes
# the D-112 held design for Crouch only; Jump/Stop/Direction are unchanged).
# Accept region is the model radius itself (the personal accept threshold);
# no extra multiplier (radius already encodes reproduction tolerance).
STOP_MATCH_RADIUS_K = 1.0

# Direction (D-103)
DIR_INDEX_RATIO_MIN = 1.5     # index length vs mean of other fingers (only index out)
DIR_INDEX_LEN_MIN = 0.40      # index length in palm-width units
DIR_LATERAL_DOMINANCE = 1.6   # |dx| vs |dy|
DIR_HOLD_S = 0.45

_OTHER_FINGERS = ((12, 9), (16, 13), (20, 17))  # (tip, mcp) middle/ring/pinky


@dataclass(frozen=True)
class JumpDiagnostics:
    """Read-only UI snapshot of the D-111 jump state (see Engine.get_diagnostics)."""
    pose_held: bool
    stable: int
    distance: float
    threshold: float
    reason: str
    stroke_state: str
    displacement: float
    required: float           # learned model required displacement (unchanged)
    live_required: float      # D-130 live fire threshold = 0.85 * required
    cur_dir: int
    expected_dir: int
    cosine: float
    cos_threshold: float      # D-130 live cosine gate (0.85)
    speed: float
    settle: int
    settle_needed: int
    reverse: float
    reverse_req: float
    reverse_frames: int
    reverse_needed: int
    settle_speed: float
    lockout_ms: float
    cycle: int
    last_reset: str


@dataclass(frozen=True)
class StopDiagnostics:
    """Read-only timing/evidence snapshot; it never changes recognition."""
    active: bool
    candidate: bool
    distance: float
    threshold: float
    stable_frames: int
    candidate_started_ms: float
    hold_ms: float
    on_issued_ms: float
    off_issued_ms: float
    reject_reason: str


@dataclass
class EngineConfig:
    jump_pose: Optional[pp.PoseModel] = None
    jump_motion: Optional[JumpMotionModel] = None
    crouch_pose: Optional[pp.PoseModel] = None       # D-112 Pose B
    crouch_motion: Optional[JumpMotionModel] = None  # D-112 Motion B
    stop_pose: Optional[pp.PoseModel] = None
    mode_direction: bool = False   # exclusive Direction bench mode (D-103)


class CrouchState(Enum):
    # D-128 discrete stroke channel (same structure as Jump; NO held ON/OFF).
    POSE_SEARCHING = "pose_searching"    # pose not yet 5/7 stable
    POSE_HELD_IDLE = "pose_held_idle"    # channel held, awaiting a stroke
    STROKE_TRACKING = "stroke_tracking"  # a stroke is in progress
    FIRED_LOCKOUT = "fired_lockout"      # fired this stroke; awaiting re-arm
    STROKE_REJECTED = "stroke_rejected"  # wrong-direction stroke, no fire


_CROUCH_LABEL = {
    CrouchState.POSE_SEARCHING: "Idle",
    CrouchState.POSE_HELD_IDLE: "Idle",
    CrouchState.STROKE_TRACKING: "Tracking",
    CrouchState.FIRED_LOCKOUT: "FiredLockout",
    CrouchState.STROKE_REJECTED: "Rejected",
}


@dataclass(frozen=True)
class CrouchDiagnostics:
    """Read-only UI snapshot of the D-128 discrete Crouch channel (Jump-shaped)."""
    pose_held: bool
    stable: int
    distance: float
    threshold: float
    reason: str
    stroke_state: str
    displacement: float
    required: float           # learned model required displacement (unchanged)
    live_required: float      # D-130 live fire threshold = 0.85 * required
    cur_dir: int
    expected_dir: int
    cosine: float
    cos_threshold: float      # D-130 live cosine gate (0.85)
    speed: float
    settle: int
    settle_needed: int
    reverse: float
    reverse_req: float
    reverse_frames: int
    reverse_needed: int
    settle_speed: float
    lockout_ms: float
    cycle: int
    last_reset: str


@dataclass
class CrouchRecognizer:
    """D-128 Crouch = a DISCRETE Pose B + Motion B stroke channel.

    Same pose-held / stroke-segmented structure and constants as the Jump
    machine (that passed the participant test), but built from Crouch's own
    model + motion. It emits `CROUCH` once per learned forward stroke, supports
    the same no-stop rhythmic re-arm, and NEVER emits a release/OFF event, never
    holds through time, and never treats tracking loss as a state to preserve.

    Self-contained (a Crouch-only copy of the Jump logic) so it cannot change
    Jump/Stop behaviour; it only READS shared module-level constants.
    """
    state: CrouchState = CrouchState.POSE_SEARCHING
    _origin: Optional[Tuple[float, float]] = None
    _last_palm: Optional[Tuple[float, float]] = None
    _last_t: Optional[float] = None
    _peak_live: float = 0.0
    _fire_point: Optional[Tuple[float, float]] = None
    _fire_t: float = 0.0
    _settle_count: int = 0
    _rev_frames: int = 0
    cycle_count: int = 0
    reset_reason: str = "None"
    last_match: Optional[pp.MatchResult] = None
    pose_reason: str = "TrackingLost"
    _stable: deque = field(default_factory=lambda: deque(maxlen=STABLE_WINDOW))
    d_disp: float = 0.0
    d_cos: float = 0.0
    d_dir8: int = -1
    d_speed: float = 0.0
    d_reverse: float = 0.0

    @property
    def stable_count(self) -> int:
        return sum(1 for x in self._stable if x)

    @property
    def pose_held(self) -> bool:
        return self.state in (CrouchState.POSE_HELD_IDLE,
                              CrouchState.STROKE_TRACKING,
                              CrouchState.FIRED_LOCKOUT,
                              CrouchState.STROKE_REJECTED)

    @property
    def stroke_state(self) -> str:
        return _CROUCH_LABEL.get(self.state, "Idle")

    def lockout_remaining_ms(self, now: float) -> float:
        if self.state == CrouchState.FIRED_LOCKOUT:
            return max(0.0, (self._fire_t + FIRE_LOCKOUT_S - now) * 1000.0)
        return 0.0

    def _reset_stroke(self, palm, t) -> None:
        # full re-init at the current position (origin/buffer/disp/history)
        self._origin = palm
        self._last_palm = palm
        self._last_t = t
        self._peak_live = 0.0
        self._fire_point = None
        self._settle_count = 0
        self._rev_frames = 0
        self.d_disp = 0.0
        self.d_cos = 0.0
        self.d_dir8 = -1
        self.d_reverse = 0.0

    def _fire(self, t: float, d8: int) -> "Command":
        self.state = CrouchState.FIRED_LOCKOUT
        self.cycle_count += 1
        self._fire_t = t
        self._fire_point = self._last_palm
        self._settle_count = 0
        self._rev_frames = 0
        return Command(CommandType.CROUCH, t, motion_dir8=d8)

    def step(self, obs, models, cfg) -> List[Command]:
        pose = cfg.crouch_pose
        motion = cfg.crouch_motion
        if pose is None or motion is None:
            return []
        tracked = not (not obs.tracked or obs.landmarks is None
                       or obs.palm is None)
        if not tracked:
            # tracking loss never fires and never emits any event; it discards
            # the current stroke and returns the channel to searching (D-128).
            self._stable.append(False)
            self.pose_reason = "OutOfFrame" if obs.tracked else "TrackingLost"
            if self.state != CrouchState.POSE_SEARCHING:
                self.state = CrouchState.POSE_SEARCHING
                self._origin = None
                self.reset_reason = "TrackingLost"
            return []

        feat = pp.feature_vector(obs.landmarks)
        m = pp.match_pose(feat, models)
        self.last_match = m
        frame_ok = (m.best == pose.name and
                    m.best_dist <= pose.radius * STOP_MATCH_RADIUS_K and m.armed)
        self._stable.append(frame_ok)
        stable_ok = self.stable_count >= STABLE_NEEDED
        if m.best != pose.name:
            self.pose_reason = pp.R_AMBIG if m.reason == pp.R_AMBIG else pp.R_DIST
        elif not frame_ok:
            self.pose_reason = m.reason if m.reason else pp.R_DIST
        elif not stable_ok:
            self.pose_reason = "Unstable"
        else:
            self.pose_reason = pp.R_ARMED

        return self._stroke_step(obs, motion, stable_ok)

    def _stroke_step(self, obs, motion, stable_ok) -> List[Command]:
        palm, t = obs.palm, obs.t
        unit = motion.unit
        req = motion.disp_threshold
        peak = motion.peak_speed if motion.peak_speed > 0 else max(motion.vel_threshold, 1e-6)

        speed = 0.0
        fwd_vel = 0.0
        if self._last_palm is not None and self._last_t is not None and t > self._last_t:
            dvx = palm[0] - self._last_palm[0]
            dvy = palm[1] - self._last_palm[1]
            dt = t - self._last_t
            speed = float(np.hypot(dvx, dvy) / dt)
            fwd_vel = float((dvx * unit[0] + dvy * unit[1]) / dt)
        self._last_palm, self._last_t = palm, t
        self.d_speed = speed

        # Pose lost -> cancel any stroke, go to searching.
        if not stable_ok:
            if self.state != CrouchState.POSE_SEARCHING:
                self.state = CrouchState.POSE_SEARCHING
                self._origin = None
                self.reset_reason = "PoseLost"
            return []

        if self.state == CrouchState.POSE_SEARCHING:
            self.state = CrouchState.POSE_HELD_IDLE
            self._reset_stroke(palm, t)

        def metrics(origin):
            dx, dy = palm[0] - origin[0], palm[1] - origin[1]
            mag = float(np.hypot(dx, dy))
            proj = float(dx * unit[0] + dy * unit[1])
            cos = (proj / mag) if mag > 1e-6 else 0.0
            d8 = quantize_dir8(dx, dy) if mag > 1e-6 else -1
            return mag, proj, cos, d8

        if self.state == CrouchState.POSE_HELD_IDLE:
            mag, proj, cos, d8 = metrics(self._origin)
            forward = cos >= START_FORWARD_COS
            started = forward and (speed >= START_SPEED_FRAC * peak or
                                   mag >= START_DISP_FRAC * req)
            if not started:
                self._origin = palm
                self.d_disp, self.d_cos, self.d_dir8, self.d_reverse = 0.0, 0.0, -1, 0.0
                return []
            self.state = CrouchState.STROKE_TRACKING
            self._peak_live = speed
            self.reset_reason = "None"

        if self.state == CrouchState.STROKE_TRACKING:
            self._peak_live = max(self._peak_live, speed)
            mag, proj, cos, d8 = metrics(self._origin)
            self.d_disp, self.d_cos, self.d_dir8, self.d_reverse = proj, cos, d8, 0.0
            live_req = req * LIVE_REQ_FRAC        # D-130 live leniency (model req kept)
            if cos >= LIVE_COS_PASS and proj >= live_req:   # FIRE (once per stroke)
                return [self._fire(t, d8)]
            # wrong-direction gate uses the SAME live cosine so a valid-direction
            # stroke is never rejected before it reaches the live displacement.
            if mag >= REJECT_DISP_FRAC * req and cos < LIVE_COS_PASS:
                self.state = CrouchState.STROKE_REJECTED
                self.reset_reason = "WrongDirection"
                self._settle_count = 0
            return []

        if self.state == CrouchState.FIRED_LOCKOUT:
            self._peak_live = max(self._peak_live, speed)
            reverse = 0.0
            if self._fire_point is not None:
                bx = palm[0] - self._fire_point[0]
                by = palm[1] - self._fire_point[1]
                reverse = -(bx * unit[0] + by * unit[1])
            self.d_reverse = float(reverse)
            self._rev_frames = self._rev_frames + 1 if fwd_vel < -1e-4 else 0
            rearm_a = (reverse >= REARM_REVERSE_FRAC * req and
                       self._rev_frames >= REARM_REVERSE_FRAMES)
            self._settle_count = (self._settle_count + 1
                                  if speed <= REARM_SETTLE_SPEED_FRAC * peak else 0)
            rearm_b = self._settle_count >= REARM_SETTLE_FRAMES
            if (rearm_a or rearm_b) and t >= self._fire_t + FIRE_LOCKOUT_S:
                self.reset_reason = "Reversed" if rearm_a else "Settled"
                self.state = CrouchState.POSE_HELD_IDLE
                self._reset_stroke(palm, t)
            return []

        if self.state == CrouchState.STROKE_REJECTED:
            self._settle_count = (self._settle_count + 1
                                  if speed <= SETTLE_SPEED_FRAC * peak else 0)
            if self._settle_count >= SETTLE_FRAMES:
                self.reset_reason = "Settled"
                self.state = CrouchState.POSE_HELD_IDLE
                self._reset_stroke(palm, t)
            return []
        return []

    def get_diagnostics(self, cfg, now: float = 0.0) -> "CrouchDiagnostics":
        mot = cfg.crouch_motion
        pose = cfg.crouch_pose
        req = mot.disp_threshold if mot is not None else 0.0
        peak = (mot.peak_speed if (mot is not None and mot.peak_speed > 0)
                else (max(mot.vel_threshold, 1e-6) if mot is not None else 0.0))
        return CrouchDiagnostics(
            pose_held=self.pose_held,
            stable=self.stable_count,
            distance=(self.last_match.best_dist if self.last_match else float("inf")),
            threshold=(pose.radius if pose is not None else 0.0),
            reason=self.pose_reason,
            stroke_state=self.stroke_state,
            displacement=self.d_disp,
            required=req,
            live_required=req * LIVE_REQ_FRAC,
            cur_dir=self.d_dir8,
            expected_dir=(mot.dir8 if mot is not None else -1),
            cosine=self.d_cos,
            cos_threshold=LIVE_COS_PASS,
            speed=self.d_speed,
            settle=self._settle_count,
            settle_needed=REARM_SETTLE_FRAMES,
            reverse=self.d_reverse,
            reverse_req=req * REARM_REVERSE_FRAC,
            reverse_frames=self._rev_frames,
            reverse_needed=REARM_REVERSE_FRAMES,
            settle_speed=peak * REARM_SETTLE_SPEED_FRAC,
            lockout_ms=self.lockout_remaining_ms(now),
            cycle=self.cycle_count,
            last_reset=self.reset_reason,
        )


@dataclass
class Engine:
    cfg: EngineConfig
    # jump — D-111 pose-held, stroke-segmented
    jstate: JumpState = JumpState.POSE_SEARCHING
    _origin: Optional[Tuple[float, float]] = None   # current stroke origin
    _last_palm: Optional[Tuple[float, float]] = None
    _last_t: Optional[float] = None
    _peak_live: float = 0.0
    _fire_point: Optional[Tuple[float, float]] = None
    _fire_t: float = 0.0
    _settle_count: int = 0
    _rev_frames: int = 0            # consecutive backward-velocity frames (re-arm A)
    cycle_count: int = 0
    reset_reason: str = "None"  # Settled/Reversed/WrongDirection/PoseLost/TrackingLost/None
    # stop
    stop_active: bool = False
    _on_evi: int = 0
    _off_evi: int = 0
    _stop_distance: float = float("inf")
    _stop_candidate_since: float = 0.0
    _stop_off_candidate_since: float = 0.0
    _stop_on_issued: float = 0.0
    _stop_off_issued: float = 0.0
    _stop_reject: str = "no-model"
    # direction
    _dir_sign: int = 0
    _dir_since: float = 0.0
    _dir_committed: bool = False
    # diagnostics snapshot (updated each tracked jump frame)
    last_reject: str = ""
    last_match: Optional[pp.MatchResult] = None
    _stable: deque = field(default_factory=lambda: deque(maxlen=STABLE_WINDOW))
    pose_reason: str = "TrackingLost"
    stop_candidate: bool = False
    d_disp: float = 0.0        # projected displacement along learned dir
    d_cos: float = 0.0
    d_dir8: int = -1
    d_speed: float = 0.0
    d_reverse: float = 0.0
    # crouch (D-112) — isolated recognizer
    crouch: "CrouchRecognizer" = field(default_factory=lambda: CrouchRecognizer())

    @property
    def stable_count(self) -> int:
        return sum(1 for x in self._stable if x)

    @property
    def pose_held(self) -> bool:
        return self.jstate in (JumpState.POSE_HELD_IDLE,
                               JumpState.STROKE_TRACKING,
                               JumpState.FIRED_LOCKOUT,
                               JumpState.STROKE_REJECTED)

    @property
    def stroke_state(self) -> str:
        return STROKE_LABEL.get(self.jstate, "Idle")

    @property
    def stop_state(self) -> StopState:
        if self.stop_active:
            return (StopState.RELEASE_CANDIDATE
                    if self._off_evi > 0 else StopState.HELD)
        return StopState.CANDIDATE if self._on_evi > 0 else StopState.NEUTRAL

    @property
    def settle_count(self) -> int:
        return self._settle_count

    def lockout_remaining_ms(self, now: float) -> float:
        if self.jstate == JumpState.FIRED_LOCKOUT:
            return max(0.0, (self._fire_t + FIRE_LOCKOUT_S - now) * 1000.0)
        return 0.0

    def get_diagnostics(self, now: float = 0.0) -> "JumpDiagnostics":
        """Read-only diagnostics snapshot for the UI (D-111).

        The bench/overlay use ONLY this — they must not read Engine private
        fields. All values are copies; calling this never mutates state.
        """
        mot = self.cfg.jump_motion
        pose = self.cfg.jump_pose
        req = mot.disp_threshold if mot is not None else 0.0
        peak = (mot.peak_speed if (mot is not None and mot.peak_speed > 0)
                else (max(mot.vel_threshold, 1e-6) if mot is not None else 0.0))
        return JumpDiagnostics(
            pose_held=self.pose_held,
            stable=self.stable_count,
            distance=(self.last_match.best_dist if self.last_match else float("inf")),
            threshold=(pose.radius if pose is not None else 0.0),
            reason=self.pose_reason,
            stroke_state=self.stroke_state,
            displacement=self.d_disp,
            required=req,
            live_required=req * LIVE_REQ_FRAC,
            cur_dir=self.d_dir8,
            expected_dir=(mot.dir8 if mot is not None else -1),
            cosine=self.d_cos,
            cos_threshold=LIVE_COS_PASS,
            speed=self.d_speed,
            settle=self._settle_count,
            settle_needed=REARM_SETTLE_FRAMES,
            reverse=self.d_reverse,
            reverse_req=req * REARM_REVERSE_FRAC,
            reverse_frames=self._rev_frames,
            reverse_needed=REARM_REVERSE_FRAMES,
            settle_speed=peak * REARM_SETTLE_SPEED_FRAC,
            lockout_ms=self.lockout_remaining_ms(now),
            cycle=self.cycle_count,
            last_reset=self.reset_reason,
        )

    def _reset_stroke(self, palm, t) -> None:
        # full re-init at the current position: origin, buffer, accumulated
        # displacement, and direction history (D-111 re-arm requirement).
        self._origin = palm
        self._last_palm = palm
        self._last_t = t
        self._peak_live = 0.0
        self._fire_point = None
        self._settle_count = 0
        self._rev_frames = 0
        self.d_disp = 0.0
        self.d_cos = 0.0
        self.d_dir8 = -1
        self.d_reverse = 0.0

    # ---- helpers -------------------------------------------------------
    def _models_all(self) -> List[pp.PoseModel]:
        m = []
        if self.cfg.jump_pose:
            m.append(self.cfg.jump_pose)
        if self.cfg.crouch_pose:
            m.append(self.cfg.crouch_pose)
        if self.cfg.stop_pose:
            m.append(self.cfg.stop_pose)
        return m

    # ---- main step -----------------------------------------------------
    def step(self, obs: FrameObs) -> List[Command]:
        if self.cfg.mode_direction:
            return self._step_direction(obs)
        cmds: List[Command] = []
        cmds += self._step_stop(obs)
        cmds += self._step_jump(obs)
        cmds += self.crouch.step(obs, self._models_all(), self.cfg)
        return cmds

    def readiness(self, *, signal_valid: bool = True,
                  teaching_active: bool = False) -> str:
        """Return the command whose existing recognizer can currently fire.

        This is a read-only projection of the same match/evidence/state already
        advanced by ``step``.  It deliberately performs no pose matching and
        owns no threshold, hysteresis, or display hold.
        """
        if not signal_valid or teaching_active or self.cfg.mode_direction:
            return ""
        if self.stop_active or self.stop_candidate:
            return "STOP"
        if (self.jstate in (JumpState.POSE_HELD_IDLE,
                            JumpState.STROKE_TRACKING)
                and self.pose_reason == pp.R_ARMED):
            return "JUMP"
        if (self.crouch.state in (CrouchState.POSE_HELD_IDLE,
                                  CrouchState.STROKE_TRACKING)
                and self.crouch.pose_reason == pp.R_ARMED):
            return "CROUCH"
        return ""

    def get_stop_diagnostics(self, now: float) -> StopDiagnostics:
        pose = self.cfg.stop_pose
        started = (self._stop_off_candidate_since
                   if self.stop_active and self._stop_off_candidate_since > 0.0
                   else self._stop_candidate_since)
        return StopDiagnostics(
            active=self.stop_active,
            candidate=self.stop_candidate,
            distance=self._stop_distance,
            threshold=pose.radius if pose is not None else 0.0,
            stable_frames=self._off_evi if self.stop_active else self._on_evi,
            candidate_started_ms=started * 1000.0 if started > 0.0 else 0.0,
            hold_ms=max(0.0, (now - started) * 1000.0)
            if started > 0.0 else 0.0,
            on_issued_ms=self._stop_on_issued * 1000.0,
            off_issued_ms=self._stop_off_issued * 1000.0,
            reject_reason=self._stop_reject,
        )

    def get_crouch_diagnostics(self, now: float = 0.0) -> "CrouchDiagnostics":
        return self.crouch.get_diagnostics(self.cfg, now)

    # ---- Jump (D-101) --------------------------------------------------
    def _step_jump(self, obs: FrameObs) -> List[Command]:
        cfg = self.cfg
        if cfg.jump_pose is None or cfg.jump_motion is None:
            return []
        tracked = not (not obs.tracked or obs.landmarks is None
                       or obs.palm is None)
        if not tracked:
            # tracking loss never fires; it discards the current stroke and,
            # if the channel is lost, drops to PoseSearching (D-111).
            self._stable.append(False)
            self.pose_reason = "OutOfFrame" if obs.tracked else "TrackingLost"
            if self.jstate != JumpState.POSE_SEARCHING:
                self.jstate = JumpState.POSE_SEARCHING
                self._origin = None
                self.reset_reason = "TrackingLost"
            return []

        feat = pp.feature_vector(obs.landmarks)
        m = pp.match_pose(feat, self._models_all())
        self.last_match = m
        frame_ok = (m.best == cfg.jump_pose.name and
                    m.best_dist <= cfg.jump_pose.radius * STOP_MATCH_RADIUS_K
                    and m.armed)
        self._stable.append(frame_ok)
        stable_ok = self.stable_count >= STABLE_NEEDED
        if m.best != cfg.jump_pose.name:
            self.pose_reason = pp.R_AMBIG if m.reason == pp.R_AMBIG else pp.R_DIST
        elif not frame_ok:
            self.pose_reason = m.reason if m.reason else pp.R_DIST
        elif not stable_ok:
            self.pose_reason = "Unstable"
        else:
            self.pose_reason = pp.R_ARMED

        return self._jump_stroke_step(obs, stable_ok)

    # ---- D-111 pose-held, stroke-segmented jump ------------------------
    def _jump_stroke_step(self, obs: FrameObs, stable_ok: bool) -> List[Command]:
        mot = self.cfg.jump_motion
        palm, t = obs.palm, obs.t
        unit = mot.unit
        req = mot.disp_threshold
        peak = mot.peak_speed if mot.peak_speed > 0 else max(mot.vel_threshold, 1e-6)

        # instantaneous speed + signed forward velocity along the learned dir
        speed = 0.0
        fwd_vel = 0.0
        if self._last_palm is not None and self._last_t is not None and t > self._last_t:
            dvx = palm[0] - self._last_palm[0]
            dvy = palm[1] - self._last_palm[1]
            dt = t - self._last_t
            speed = float(np.hypot(dvx, dvy) / dt)
            fwd_vel = float((dvx * unit[0] + dvy * unit[1]) / dt)  # + forward
        self._last_palm, self._last_t = palm, t
        self.d_speed = speed

        # Pose lost -> cancel any stroke, go to PoseSearching.
        if not stable_ok:
            if self.jstate != JumpState.POSE_SEARCHING:
                self.jstate = JumpState.POSE_SEARCHING
                self._origin = None
                self.reset_reason = "PoseLost"
            return []

        # Channel held: enter idle when we were searching.
        if self.jstate == JumpState.POSE_SEARCHING:
            self.jstate = JumpState.POSE_HELD_IDLE
            self._reset_stroke(palm, t)

        def stroke_metrics(origin):
            dx, dy = palm[0] - origin[0], palm[1] - origin[1]
            mag = float(np.hypot(dx, dy))
            proj = float(dx * unit[0] + dy * unit[1])     # along learned dir
            cos = (proj / mag) if mag > 1e-6 else 0.0
            d8 = quantize_dir8(dx, dy) if mag > 1e-6 else -1
            return mag, proj, cos, d8

        if self.jstate == JumpState.POSE_HELD_IDLE:
            mag, proj, cos, d8 = stroke_metrics(self._origin)
            # a stroke only STARTS while moving toward the learned direction, so
            # a return (backward) motion never begins a stroke and the origin
            # tracks the hand to the turnaround (enables no-stop rhythmic repeat).
            forward = cos >= START_FORWARD_COS
            started = forward and (speed >= START_SPEED_FRAC * peak or
                                   mag >= START_DISP_FRAC * req)
            if not started:
                self._origin = palm          # follow the hand (incl. the return)
                self.d_disp, self.d_cos, self.d_dir8, self.d_reverse = 0.0, 0.0, -1, 0.0
                return []
            self.jstate = JumpState.STROKE_TRACKING
            self._peak_live = speed
            self.reset_reason = "None"
            # fall through and evaluate this frame as tracking

        if self.jstate == JumpState.STROKE_TRACKING:
            self._peak_live = max(self._peak_live, speed)
            mag, proj, cos, d8 = stroke_metrics(self._origin)
            self.d_disp, self.d_cos, self.d_dir8, self.d_reverse = proj, cos, d8, 0.0
            live_req = req * LIVE_REQ_FRAC        # D-130 live leniency (model req kept)
            if cos >= LIVE_COS_PASS and proj >= live_req:   # FIRE (once per stroke)
                return [self._fire(t, d8)]
            if mag >= REJECT_DISP_FRAC * req and cos < LIVE_COS_PASS:   # wrong dir
                self.jstate = JumpState.STROKE_REJECTED
                self.reset_reason = "WrongDirection"
                self._settle_count = 0
            return []

        if self.jstate == JumpState.FIRED_LOCKOUT:
            self._peak_live = max(self._peak_live, speed)
            # reverse displacement from the fire point (backward along learned dir)
            reverse = 0.0
            if self._fire_point is not None:
                bx = palm[0] - self._fire_point[0]
                by = palm[1] - self._fire_point[1]
                reverse = -(bx * unit[0] + by * unit[1])
            self.d_reverse = float(reverse)
            # A: backward-moving return (no full stop required)
            self._rev_frames = self._rev_frames + 1 if fwd_vel < -1e-4 else 0
            rearm_a = (reverse >= REARM_REVERSE_FRAC * req and
                       self._rev_frames >= REARM_REVERSE_FRAMES)
            # B: brief near-settle (2 frames, not a full stop)
            self._settle_count = (self._settle_count + 1
                                  if speed <= REARM_SETTLE_SPEED_FRAC * peak else 0)
            rearm_b = self._settle_count >= REARM_SETTLE_FRAMES
            if (rearm_a or rearm_b) and t >= self._fire_t + FIRE_LOCKOUT_S:
                self.reset_reason = "Reversed" if rearm_a else "Settled"
                self.jstate = JumpState.POSE_HELD_IDLE
                self._reset_stroke(palm, t)   # fresh origin/buffer/disp/history
            return []

        if self.jstate == JumpState.STROKE_REJECTED:
            # a wrong-direction stroke clears back to idle once the hand settles
            self._settle_count = (self._settle_count + 1
                                  if speed <= SETTLE_SPEED_FRAC * peak else 0)
            if self._settle_count >= SETTLE_FRAMES:
                self.reset_reason = "Settled"
                self.jstate = JumpState.POSE_HELD_IDLE
                self._reset_stroke(palm, t)
            return []
        return []

    def _fire(self, t: float, d8: int) -> "Command":
        """Emit JUMP once for the current stroke and enter FiredLockout."""
        self.jstate = JumpState.FIRED_LOCKOUT
        self.cycle_count += 1
        self._fire_t = t
        self._fire_point = self._last_palm
        self._settle_count = 0
        self._rev_frames = 0
        self.last_reject = ""
        return Command(CommandType.JUMP, t, motion_dir8=d8)

    # ---- Stop (D-102) --------------------------------------------------
    def _step_stop(self, obs: FrameObs) -> List[Command]:
        cfg = self.cfg
        if cfg.stop_pose is None:
            self.stop_candidate = False
            self._stop_reject = "no-model"
            return []
        if not obs.tracked or obs.landmarks is None:
            # tracking loss must NOT release (D-102)
            self._on_evi = 0
            self._off_evi = 0
            self._stop_candidate_since = 0.0
            self._stop_off_candidate_since = 0.0
            self.stop_candidate = False
            self._stop_reject = "tracking-recovery"
            return []
        if obs.quality < pp.TRACK_QUALITY_MIN:
            # A low-quality tracked frame is not deliberate release evidence.
            self._on_evi = 0
            self._off_evi = 0
            self._stop_candidate_since = 0.0
            self._stop_off_candidate_since = 0.0
            self.stop_candidate = False
            self._stop_reject = "low-quality"
            return []
        geometry_ok, _ = pp.teaching_geometry(obs.landmarks)
        if not geometry_ok:
            # A clipped/tiny hand is likewise recovery evidence, never STOP_OFF.
            self._on_evi = 0
            self._off_evi = 0
            self._stop_candidate_since = 0.0
            self._stop_off_candidate_since = 0.0
            self.stop_candidate = False
            self._stop_reject = "geometry-invalid"
            return []
        feat = pp.feature_vector(obs.landmarks)
        m = pp.match_pose(feat, self._models_all())
        stop_distance = pp.pose_distance(feat, cfg.stop_pose.centroid)
        entry_matched = (m.best == cfg.stop_pose.name and m.armed)
        # Entry keeps the existing unique-class match. Once latched, movement,
        # stability, and another class becoming marginally nearer are not release
        # conditions while the normalized Stop shape remains inside its existing
        # radius (STOP_MATCH_RADIUS_K stays 1.0).
        held_matched = (
            stop_distance <= cfg.stop_pose.radius * STOP_MATCH_RADIUS_K)
        matched = held_matched if self.stop_active else entry_matched
        self._stop_distance = stop_distance
        self.stop_candidate = matched
        if matched:
            self._stop_reject = "latched" if self.stop_active else "candidate"
        elif m.best != cfg.stop_pose.name:
            self._stop_reject = ("ambiguous" if m.reason == pp.R_AMBIG
                                 else "other-class")
        else:
            self._stop_reject = m.reason or "distance"

        cmds: List[Command] = []
        if not self.stop_active:
            if matched and self._on_evi == 0:
                self._stop_candidate_since = obs.t
            elif not matched:
                self._stop_candidate_since = 0.0
            self._on_evi = self._on_evi + 1 if matched else 0
            self._off_evi = 0
            self._stop_off_candidate_since = 0.0
            if self._on_evi >= STOP_ON_EVIDENCE:
                self.stop_active = True
                self._stop_on_issued = obs.t
                self._stop_reject = "latched"
                cmds.append(Command(CommandType.STOP_ON, obs.t))
        else:
            # ReleaseCandidate counts only consecutive valid shape mismatches.
            # Palm position/speed and entry stability never participate.
            if not matched and self._off_evi == 0:
                self._stop_off_candidate_since = obs.t
            elif matched:
                self._stop_off_candidate_since = 0.0
            self._off_evi = self._off_evi + 1 if not matched else 0
            self._on_evi = 0
            if self._off_evi >= STOP_OFF_EVIDENCE:
                self.stop_active = False
                self._stop_off_issued = obs.t
                self._stop_candidate_since = 0.0
                self._stop_off_candidate_since = 0.0
                self._stop_reject = "released"
                cmds.append(Command(CommandType.STOP_OFF, obs.t))
        return cmds

    # ---- Direction (D-103) ---------------------------------------------
    def _step_direction(self, obs: FrameObs) -> List[Command]:
        if not obs.tracked or obs.landmarks is None:
            self._dir_sign = 0
            self._dir_since = 0.0
            self._dir_committed = False
            return []
        pts = np.asarray(obs.landmarks, dtype=np.float64)[:, :2]
        vec = pts[INDEX_TIP] - pts[INDEX_MCP]
        dx, dy = float(vec[0]), float(vec[1])
        hand = pp.normalize_landmarks(obs.landmarks)
        idx_len = float(np.linalg.norm(hand[INDEX_TIP] - hand[INDEX_MCP]))
        others = float(np.mean([np.linalg.norm(hand[t] - hand[m])
                                for t, m in _OTHER_FINGERS]))
        ratio = idx_len / (others + 1e-6)
        lateral = abs(dx) >= DIR_LATERAL_DOMINANCE * abs(dy)
        extended = ratio >= DIR_INDEX_RATIO_MIN and idx_len >= DIR_INDEX_LEN_MIN
        if not (lateral and extended):
            self._dir_sign = 0
            self._dir_since = 0.0
            self.last_reject = "not-a-lateral-point"
            return []
        sign = 1 if dx > 0 else -1
        if sign != self._dir_sign:
            self._dir_sign = sign
            self._dir_since = obs.t
            self._dir_committed = False
            return []
        if (not self._dir_committed
                and obs.t - self._dir_since >= DIR_HOLD_S):
            self._dir_committed = True
            return [Command(CommandType.DIRECTION, obs.t, direction=sign)]
        return []

    def direction_preview(self, obs: FrameObs) -> int:
        """Non-committing live preview sign for the bench UI."""
        return self._dir_sign

    def direction_hold_progress(self, now: float) -> float:
        if self._dir_sign == 0 or self._dir_since <= 0.0:
            return 0.0
        if self._dir_committed:
            return 1.0
        return max(0.0, min(1.0, (now - self._dir_since) / DIR_HOLD_S))
