"""Teaching UX controller (D-100, teaching UX fix).

Pure logic (no cv2 / no camera) so it is unit-testable. Owns:
- accepted-take caps (exactly max 3 for Jump and for Stop),
- key-edge debounce and ignoring input while a capture is running,
- teaching geometry validation (separate from MediaPipe confidence),
- accepted/rejected counters, capture progress,
- a large teaching status message with a minimum hold time.

The bench feeds it edged keys and per-frame observations; recognition itself
still runs in command_engine (the bench applies finalized models to the engine).
"""
from __future__ import annotations

from dataclasses import dataclass, field
from typing import List, Optional, Tuple

from . import personal_pose as pp
from . import motion_primitive as mpr
from . import command_engine as ce

KIND_JUMP = "jump"
KIND_CROUCH = "crouch"
KIND_STOP = "stop"
KIND_MOTION = "motion"                # jump motion capture
KIND_CROUCH_MOTION = "crouch_motion"  # crouch motion capture

DEBOUNCE_S = 0.40
MSG_HOLD_S = 0.80
MAX_TAKES = pp.TAKES_REQUIRED  # 3

# Live-proof (post-finalize verification) constants.
PROOF_POSE_TIMEOUT = 5.0       # s to reproduce the pose before RECALIBRATION
PROOF_MOTION_TIMEOUT = 8.0     # s to perform the move before RECALIBRATION
PROOF_CONSISTENCY = 0.06       # max spread of recent proof distances to expand
PROOF_EXPAND_PAD = 1.12        # radius pulled just past the reproduced distance


@dataclass
class _Frame:
    landmarks: list
    quality: float
    ok: bool
    reason: str


@dataclass
class TeachController:
    jump_takes: List[pp.Recording] = field(default_factory=list)
    crouch_takes: List[pp.Recording] = field(default_factory=list)
    stop_takes: List[pp.Recording] = field(default_factory=list)
    jump_rejected: int = 0
    crouch_rejected: int = 0
    stop_rejected: int = 0
    strokes: List[list] = field(default_factory=list)
    dts: List[float] = field(default_factory=list)
    crouch_strokes: List[list] = field(default_factory=list)
    crouch_dts: List[float] = field(default_factory=list)

    jump_model: Optional[pp.PoseModel] = None
    crouch_model: Optional[pp.PoseModel] = None
    stop_model: Optional[pp.PoseModel] = None
    jump_motion: Optional[mpr.JumpMotionModel] = None
    crouch_motion: Optional[mpr.JumpMotionModel] = None

    capturing: Optional[str] = None
    _buf: List[_Frame] = field(default_factory=list)
    _track: List[Tuple[float, float]] = field(default_factory=list)
    _cap_start: float = 0.0

    _last_key: str = ""
    _last_key_t: float = -999.0
    _msg: str = ""
    _msg_until: float = -999.0

    # live per-frame diagnostics
    conf: float = 0.0
    geom_valid: bool = False
    geom_reason: str = ""
    last_reason: str = ""
    qa_reset_requested: bool = False

    # live-proof verification (post-finalize)
    verified: bool = False
    proof_state: str = ""          # "" | ready  (D-130: no pose/motion/recal proof)
    _proof_since: float = 0.0
    _proof_ds: List[float] = field(default_factory=list)
    proof_d: float = float("inf")
    proof_th: float = 0.0
    _last_jreset: str = "None"     # D-130 retry-hint edge detection
    _last_creset: str = "None"
    engine: object = None

    def _engine(self):
        if self.engine is None:
            self.engine = ce.Engine(cfg=ce.EngineConfig())
        return self.engine

    # ---- messaging -----------------------------------------------------
    def _set_msg(self, text: str, t: float) -> None:
        self._msg = text
        self._msg_until = t + MSG_HOLD_S

    def big_message(self, t: float) -> str:
        if t < self._msg_until and self._msg:
            return self._msg
        # state-derived default
        if self.capturing in (KIND_JUMP, KIND_CROUCH, KIND_STOP):
            n = len(self._takes_for(self.capturing))
            label = {KIND_JUMP: "JUMP", KIND_CROUCH: "CROUCH",
                     KIND_STOP: "STOP"}[self.capturing]
            return f"CAPTURING {label} {n + 1}/3 — HOLD STILL"
        if self.capturing == KIND_MOTION:
            return "RECORDING JUMP MOTION — PRESS E TO END"
        if self.capturing == KIND_CROUCH_MOTION:
            return "RECORDING CROUCH MOTION — PRESS B TO END"
        # D-130: no live proof / recalibration states.
        if self.verified and self.jump_model and self.jump_motion:
            return "JUMP READY"
        if len(self.jump_takes) >= MAX_TAKES and self.jump_motion is None:
            return "POSE READY 3/3 — PRESS M AND MOVE"
        if len(self.jump_takes) == 0 and not self.stop_takes:
            return "READY — MAKE YOUR JUMP SIGN"
        return f"JUMP TAKES {len(self.jump_takes)}/3"

    def capture_remaining(self, t: float) -> float:
        if self.capturing in (KIND_JUMP, KIND_CROUCH, KIND_STOP):
            return max(0.0, pp.TAKE_SECONDS - (t - self._cap_start))
        return 0.0

    # ---- key handling (edged + debounced) ------------------------------
    def on_key(self, key: str, t: float) -> None:
        if not key:
            return
        if key == self._last_key and (t - self._last_key_t) < DEBOUNCE_S:
            return
        self._last_key = key
        self._last_key_t = t

        if key == "r":
            self.reset(t)
            return
        if self.capturing is not None:
            # ignore all start keys mid-capture; only the matching end key ends
            if key == "e" and self.capturing == KIND_MOTION:
                self._end_motion(KIND_JUMP, t)
            elif key == "b" and self.capturing == KIND_CROUCH_MOTION:
                self._end_motion(KIND_CROUCH, t)
            return
        if key == "j":
            self._start_pose(KIND_JUMP, t)
        elif key == "c":
            self._start_pose(KIND_CROUCH, t)
        elif key == "s":
            self._start_pose(KIND_STOP, t)
        elif key == "m":
            self._start_motion(KIND_JUMP, t)
        elif key == "v":
            self._start_motion(KIND_CROUCH, t)
        elif key in ("e", "b"):
            pass
        elif key == "f":
            self.finalize_jump(t)
        elif key == "g":
            self.finalize_crouch(t)
        elif key == "S":
            self.finalize_stop(t)

    # ---- capture start -------------------------------------------------
    def _takes_for(self, kind: str) -> List[pp.Recording]:
        return {KIND_JUMP: self.jump_takes, KIND_CROUCH: self.crouch_takes,
                KIND_STOP: self.stop_takes}[kind]

    def _start_pose(self, kind: str, t: float) -> None:
        takes = self._takes_for(kind)
        if len(takes) >= MAX_TAKES:
            label = {KIND_JUMP: "POSE READY 3/3 — PRESS M AND MOVE",
                     KIND_CROUCH: "CROUCH POSE READY 3/3 — PRESS V AND MOVE",
                     KIND_STOP: "STOP READY 3/3"}[kind]
            self._set_msg(label, t)
            return
        self.capturing = kind
        self._buf = []
        self._cap_start = t

    def _start_motion(self, kind: str, t: float) -> None:
        takes = self.jump_takes if kind == KIND_JUMP else self.crouch_takes
        if len(takes) < MAX_TAKES:
            self._set_msg(f"RECORD 3 {kind.upper()} POSES FIRST", t)
            return
        self.capturing = KIND_MOTION if kind == KIND_JUMP else KIND_CROUCH_MOTION
        self._track = []
        self._cap_start = t

    # ---- per-frame feed ------------------------------------------------
    def feed(self, landmarks, quality: float, palm, t: float,
             tracked: bool = True) -> list:
        """Advance teaching + live recognition. Returns emitted Commands."""
        self.conf = quality
        if landmarks is not None:
            ok, reason = pp.teaching_geometry(landmarks)
            self.geom_valid, self.geom_reason = ok, reason
        else:
            self.geom_valid, self.geom_reason = False, "HAND OUT OF FRAME"

        if self.capturing in (KIND_JUMP, KIND_CROUCH, KIND_STOP):
            if landmarks is not None:
                ok = (quality >= pp.TRACK_QUALITY_MIN and self.geom_valid)
                self._buf.append(_Frame(list(landmarks), quality, ok,
                                        self.geom_reason))
            if (t - self._cap_start) >= pp.TAKE_SECONDS:
                self._complete_pose(t)
        elif self.capturing in (KIND_MOTION, KIND_CROUCH_MOTION) and palm is not None:
            self._track.append((float(palm[0]), float(palm[1])))

        # live recognition (runs once models are finalized; drives proof)
        cmds = []
        eng = self._engine()
        eng.cfg.jump_pose = self.jump_model
        eng.cfg.jump_motion = self.jump_motion
        eng.cfg.crouch_pose = self.crouch_model
        eng.cfg.crouch_motion = self.crouch_motion
        eng.cfg.stop_pose = self.stop_model
        if (self.jump_model is not None or self.stop_model is not None
                or self.crouch_model is not None):
            obs = ce.FrameObs(t=t, tracked=tracked, quality=quality,
                              landmarks=landmarks, palm=palm)
            cmds = eng.step(obs)
        self._proof_step(t, landmarks, tracked, cmds)
        return cmds

    # ---- live retry hint (D-130: NO auto-recalibration) ----------------
    def _proof_step(self, t, landmarks, tracked, cmds) -> None:
        # D-130: once a model is finalized (F/G), live PoseNoMatch / TrackingLost
        # / MotionTimeout / WrongDirection NEVER discard, shrink, or recalibrate
        # the model. A single deliberate wrong-direction stroke just shows a
        # non-destructive retry hint; re-registration happens ONLY on explicit R.
        eng = self.engine
        if eng is None:
            return
        jreset = getattr(eng, "reset_reason", "None")
        creset = getattr(eng.crouch, "reset_reason", "None") if eng.crouch else "None"
        newly_wrong = ((jreset == "WrongDirection" and self._last_jreset != "WrongDirection")
                       or (creset == "WrongDirection" and self._last_creset != "WrongDirection"))
        if newly_wrong:
            self._set_msg("SIGN NOT MATCHED — TRY AGAIN", t)
        self._last_jreset, self._last_creset = jreset, creset

    def _complete_pose(self, t: float) -> None:
        kind = self.capturing
        self.capturing = None
        valid = [f for f in self._buf if f.ok]
        takes = self._takes_for(kind)
        rec = pp.Recording([pp.FrameSample(f.landmarks, f.quality)
                            for f in valid])
        # SAME validator as finalize (take_stability): a take accepted here is
        # never rejected later, and an unstable hold is rejected at record time.
        ok, disp, n_valid, reason = pp.take_stability(rec)
        if ok:
            takes.append(rec)
            n = len(takes)
            if n >= MAX_TAKES:
                self._set_msg({
                    KIND_JUMP: "POSE READY 3/3 — PRESS M AND MOVE",
                    KIND_CROUCH: "CROUCH POSE READY 3/3 — PRESS V AND MOVE",
                    KIND_STOP: "STOP READY 3/3"}[kind], t)
            else:
                self._set_msg(f"ACCEPTED {n}/3", t)
        else:
            if reason == "UNSTABLE":
                rmsg = f"UNSTABLE disp {disp:.3f} > {pp.WITHIN_CLASS_DISP_MAX:.2f}"
            else:
                # too few valid frames: use the dominant geometry reason
                greasons = [f.reason for f in self._buf if not f.ok]
                rmsg = (max(set(greasons), key=greasons.count) if greasons
                        else "TOO FEW FRAMES")
            if kind == KIND_JUMP:
                self.jump_rejected += 1
            elif kind == KIND_CROUCH:
                self.crouch_rejected += 1
            else:
                self.stop_rejected += 1
            self.last_reason = rmsg
            self._set_msg(f"REJECTED — {rmsg}", t)
        self._buf = []

    def _ensure_valid_takes(self, takes: List[pp.Recording], label: str,
                            t: float) -> bool:
        """Defensive finalize check with the SAME validator. If a stored take is
        invalid, drop ONLY that take (rolling 3/3 -> 2/3), name it, and return
        False so the user re-records just that sample — never dead-ends the model
        or touches the other vocabularies (D-112)."""
        for i, rec in enumerate(takes):
            ok, disp, n, reason = pp.take_stability(rec)
            if not ok:
                del takes[i]
                if reason == "UNSTABLE":
                    self._set_msg(
                        f"{label} TAKE {i + 1} INVALID — disp {disp:.3f} > "
                        f"{pp.WITHIN_CLASS_DISP_MAX:.2f} — RETAKE 1 SAMPLE", t)
                else:
                    self._set_msg(
                        f"{label} TAKE {i + 1} INVALID — {reason} — "
                        "RETAKE 1 SAMPLE", t)
                self.last_reason = f"{label} take {i + 1} {reason}"
                return False
        if len(takes) < MAX_TAKES:
            key = {"JUMP": "J", "CROUCH": "C", "STOP": "S"}[label]
            self._set_msg(
                f"{label} NEEDS {MAX_TAKES - len(takes)} MORE — PRESS {key}", t)
            return False
        return True

    def _end_motion(self, kind: str, t: float) -> None:
        self.capturing = None
        strokes = self.strokes if kind == KIND_JUMP else self.crouch_strokes
        dts = self.dts if kind == KIND_JUMP else self.crouch_dts
        if len(self._track) >= 2:
            strokes.append(list(self._track))
            dts.append(max(1e-3, t - self._cap_start))
            key = "F" if kind == KIND_JUMP else "G"
            self._set_msg(f"MOTION READY — PRESS {key} TO FINALIZE", t)
        else:
            self._set_msg("REJECTED — NO MOTION", t)
        self._track = []

    # ---- finalize ------------------------------------------------------
    def finalize_jump(self, t: float) -> None:
        if not self._ensure_valid_takes(self.jump_takes, "JUMP", t):
            return
        try:
            pose = pp.register_pose("JUMP", self.jump_takes)
            motion = mpr.learn_jump_motion(self.strokes, self.dts)
        except Exception as exc:
            self._set_msg(f"REJECTED — {exc}", t)
            return
        if self.stop_model and not pp.is_separable(pose, self.stop_model):
            self._set_msg("REJECTED — JUMP TOO CLOSE TO STOP", t)
            return
        if self.stop_model:
            try:
                pp.constrain_separation(pose, self.stop_model)
            except pp.RegistrationError as exc:
                self._set_msg(f"REJECTED — {exc}", t)
                return
        self.jump_model, self.jump_motion = pose, motion
        # D-130: the model is CONFIRMED at F. No live reproduction proof, no
        # auto-recalibration, no radius change. Live use fires immediately; only
        # an explicit R re-registers.
        self.verified = True
        self.proof_state = "ready"
        self._proof_ds = []
        if self.engine is not None:
            self.engine.jstate = ce.JumpState.POSE_SEARCHING
        self._set_msg("JUMP READY", t)

    def finalize_crouch(self, t: float) -> None:
        if not self._ensure_valid_takes(self.crouch_takes, "CROUCH", t):
            return
        try:
            pose = pp.register_pose("CROUCH", self.crouch_takes)
            motion = mpr.learn_jump_motion(self.crouch_strokes, self.crouch_dts)
        except Exception as exc:
            self._set_msg(f"REJECTED — {exc}", t)
            return
        # Reject ONLY the new Crouch class if it can't separate from an existing
        # class; never destroy the already-taught Jump/Stop (D-112).
        if self.jump_model and not pp.is_separable(pose, self.jump_model):
            self._set_msg("REJECTED — CROUCH TOO CLOSE TO JUMP", t)
            return
        if self.stop_model and not pp.is_separable(pose, self.stop_model):
            self._set_msg("REJECTED — CROUCH TOO CLOSE TO STOP", t)
            return
        try:
            if self.jump_model:
                pp.constrain_separation(self.jump_model, pose)
            if self.stop_model:
                pp.constrain_separation(self.stop_model, pose)
        except pp.RegistrationError as exc:
            self._set_msg(f"REJECTED — {exc}", t)
            return
        self.crouch_model, self.crouch_motion = pose, motion
        self._set_msg("CROUCH READY 3/3", t)

    def finalize_stop(self, t: float) -> None:
        if not self._ensure_valid_takes(self.stop_takes, "STOP", t):
            return
        try:
            pose = pp.register_pose("STOP", self.stop_takes)
        except Exception as exc:
            self._set_msg(f"REJECTED — {exc}", t)
            return
        if self.jump_model and not pp.is_separable(pose, self.jump_model):
            self._set_msg("REJECTED — STOP TOO CLOSE TO JUMP", t)
            return
        if self.jump_model:
            try:
                pp.constrain_separation(self.jump_model, pose)
            except pp.RegistrationError as exc:
                self._set_msg(f"REJECTED — {exc}", t)
                return
        self.stop_model = pose
        self._set_msg("STOP READY 3/3", t)

    # ---- reset ---------------------------------------------------------
    def reset_command(self, kind: str, t: float) -> bool:
        """Delete exactly one learned command without emitting game commands."""
        if kind not in (KIND_JUMP, KIND_CROUCH, KIND_STOP):
            return False

        self.capturing = None
        self._buf = []
        self._track = []
        self.last_reason = ""

        if kind == KIND_JUMP:
            self.jump_takes = []
            self.jump_rejected = 0
            self.strokes = []
            self.dts = []
            self.jump_model = None
            self.jump_motion = None
            self.verified = False
            self.proof_state = ""
            self._proof_ds = []
            self.proof_d = float("inf")
            self.proof_th = 0.0
            self._last_jreset = "None"
        elif kind == KIND_CROUCH:
            self.crouch_takes = []
            self.crouch_rejected = 0
            self.crouch_strokes = []
            self.crouch_dts = []
            self.crouch_model = None
            self.crouch_motion = None
            self._last_creset = "None"
        else:
            self.stop_takes = []
            self.stop_rejected = 0
            self.stop_model = None

        if self.engine is not None:
            if kind == KIND_JUMP:
                self.engine.cfg.jump_pose = None
                self.engine.cfg.jump_motion = None
                self.engine.jstate = ce.JumpState.POSE_SEARCHING
                self.engine._origin = None
                self.engine._last_palm = None
                self.engine._last_t = None
                self.engine._stable.clear()
                self.engine.reset_reason = "None"
            elif kind == KIND_CROUCH:
                self.engine.cfg.crouch_pose = None
                self.engine.cfg.crouch_motion = None
                self.engine.crouch = ce.CrouchRecognizer()
            else:
                self.engine.cfg.stop_pose = None
                self.engine.stop_active = False
                self.engine._on_evi = 0
                self.engine._off_evi = 0

        self._set_msg(f"{kind.upper()} RESET COMPLETE", t)
        return True

    def reset(self, t: float) -> None:
        self.jump_takes = []
        self.crouch_takes = []
        self.stop_takes = []
        self.jump_rejected = 0
        self.crouch_rejected = 0
        self.stop_rejected = 0
        self.strokes = []
        self.dts = []
        self.crouch_strokes = []
        self.crouch_dts = []
        self.jump_model = None
        self.crouch_model = None
        self.stop_model = None
        self.jump_motion = None
        self.crouch_motion = None
        self.capturing = None
        self._buf = []
        self._track = []
        self.last_reason = ""
        self.qa_reset_requested = True
        self.verified = False
        self.proof_state = ""
        self._proof_ds = []
        self.proof_d = float("inf")
        self.proof_th = 0.0
        self._last_jreset = "None"
        self._last_creset = "None"
        self.engine = ce.Engine(cfg=ce.EngineConfig())
        self._set_msg("RESET COMPLETE", t)
