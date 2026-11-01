"""Localhost-only production teaching control-plane.

Unity sends semantic control events (context / SPACE press / SPACE release /
reset / Direction exclusivity).  No key is injected or synthesized.  Camera
frames stay inside the Sidecar and only status JSON crosses this plane.
"""
from __future__ import annotations

import copy
import json
import queue
import threading
from dataclasses import dataclass
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from typing import Optional, Tuple

import numpy as np

from . import motion_primitive as mpr
from . import personal_pose as pp

KINDS = ("jump", "crouch", "stop")
MIN_STROKE_DISPLACEMENT = 0.03
CONTROL_PATH = "/v1/teaching"


@dataclass
class _Frame:
    landmarks: list
    quality: float


class ProductionTeachingRuntime:
    """Atomic SPACE-driven teaching layered over the frozen recognizers."""

    def __init__(self, teach):
        self.teach = teach
        self.active = False
        self.kind = ""
        self.repair = False
        self.required = 0
        self.accepted = 0
        self.phase = "idle"
        self.reason = ""
        self.event_id = 0
        self.finalized = False
        self.direction_mode = False
        self.ack_action = ""
        self.ack_target = ""
        self.ack_id = 0
        self._pressed = False
        self._lost = False
        self._invalid_reason = ""
        self._frames = []
        self._pose = None
        self._track = []
        self._track_started = 0.0
        self._session_takes = []
        self._session_strokes = []
        self._session_dts = []
        self._lock = threading.RLock()

    def snapshot(self) -> dict:
        with self._lock:
            return {
                "active": self.active,
                "kind": self.kind,
                "repair": self.repair,
                "required": self.required,
                "accepted": self.accepted,
                "phase": self.phase,
                "reason": self.reason,
                "event_id": self.event_id,
                "finalized": self.finalized,
                "direction_mode": self.direction_mode,
                "ack_action": self.ack_action,
                "ack_target": self.ack_target,
                "ack_id": self.ack_id,
                "rgb_frames_sent": 0,
            }

    def begin(self, kind: str, repair: bool) -> None:
        if kind not in KINDS:
            return
        with self._lock:
            self._clear_capture()
            self.active = True
            self.kind = kind
            self.repair = bool(repair)
            self.required = 1 if self.repair else pp.TAKES_REQUIRED
            self.accepted = 0
            self.phase = "ready"
            self.reason = ""
            self.finalized = False
            self._session_takes = []
            self._session_strokes = []
            self._session_dts = []

    def cancel(self) -> None:
        with self._lock:
            self._clear_capture()
            self.active = False
            self.kind = ""
            self.repair = False
            self.required = 0
            self.accepted = 0
            self.phase = "idle"
            self.reason = ""
            self.finalized = False
            self._session_takes = []
            self._session_strokes = []
            self._session_dts = []

    def reset(self, t: float) -> None:
        with self._lock:
            self.cancel()
            self.direction_mode = False
            self.ack_action = ""
            self.ack_target = ""
            self.ack_id = 0
            self.teach.reset(t)

    def reset_command(self, target: str, request_id: int, t: float) -> bool:
        """Atomically reset one command and begin a fresh three-sample session."""
        target = str(target).lower()
        if target not in KINDS or int(request_id) <= 0:
            return False
        with self._lock:
            self._clear_capture()
            if not self.teach.reset_command(target, t):
                return False
            self.active = True
            self.kind = target
            self.repair = False
            self.required = pp.TAKES_REQUIRED
            self.accepted = 0
            self.phase = "ready"
            self.reason = ""
            self.finalized = False
            self._session_takes = []
            self._session_strokes = []
            self._session_dts = []
            self.ack_action = "reset_command"
            self.ack_target = target
            self.ack_id = int(request_id)
            return True

    def set_direction_mode(self, enabled: bool) -> None:
        with self._lock:
            self.direction_mode = bool(enabled)
            self.teach._engine().cfg.mode_direction = self.direction_mode

    def press(self, t: float) -> None:
        with self._lock:
            if not self.active or self._pressed or self.finalized:
                return
            self._clear_capture()
            self._pressed = True
            self.phase = "stabilizing"
            self.reason = ""
            self._track_started = t

    def release(self, t: float) -> None:
        with self._lock:
            if not self.active or not self._pressed:
                return
            self._pressed = False
            if self._lost:
                self._reject("tracking_lost")
                return
            if self._pose is None:
                if len(self._frames) >= pp.MIN_VALID_FRAMES:
                    rec = self._recording()
                    ok, _, _, reason = pp.take_stability(rec)
                    self._reject("unstable" if reason == "UNSTABLE" else
                                 "early_release")
                elif self._invalid_reason:
                    self._reject(self._geometry_reason(self._invalid_reason))
                else:
                    self._reject("early_release")
                return
            if self.kind != "stop":
                if len(self._track) < 2 or self._stroke_displacement() < MIN_STROKE_DISPLACEMENT:
                    self._reject("early_release")
                    return
            self._accept_candidate(t)

    def feed(self, landmarks, quality: float, palm: Optional[Tuple[float, float]],
             t: float, tracked: bool = True) -> None:
        """Consume one camera observation only while production teaching is active."""
        with self._lock:
            if landmarks is not None:
                ok, why = pp.teaching_geometry(landmarks)
                self.teach.geom_valid, self.teach.geom_reason = ok, why
            else:
                self.teach.geom_valid, self.teach.geom_reason = False, "HAND OUT OF FRAME"
            self.teach.conf = quality
            if not self.active or not self._pressed:
                return
            if not tracked or landmarks is None or palm is None:
                self._lost = True
                return

            geom_ok, geom_reason = pp.teaching_geometry(landmarks)
            if not geom_ok or quality < pp.TRACK_QUALITY_MIN:
                self._invalid_reason = geom_reason if not geom_ok else "TRACKING LOST"
                return

            if self._pose is None:
                self._frames.append(_Frame(list(landmarks), quality))
                if len(self._frames) < pp.MIN_VALID_FRAMES:
                    return
                rec = self._recording()
                stable, _, _, _ = pp.take_stability(rec)
                if not stable:
                    return
                self._pose = rec
                self._track = [(float(palm[0]), float(palm[1]))]
                self._track_started = t
                self.phase = "release" if self.kind == "stop" else "moving"
                return

            if self.kind != "stop":
                self._track.append((float(palm[0]), float(palm[1])))
                if self._stroke_displacement() >= MIN_STROKE_DISPLACEMENT:
                    self.phase = "release"

    def _recording(self):
        return pp.Recording([pp.FrameSample(f.landmarks, f.quality)
                             for f in self._frames])

    def _stroke_displacement(self) -> float:
        if len(self._track) < 2:
            return 0.0
        a, b = self._track[0], self._track[-1]
        return float(np.hypot(b[0] - a[0], b[1] - a[1]))

    def _accept_candidate(self, t: float) -> None:
        take = self._pose
        stroke = list(self._track)
        dt = max(1e-3, t - self._track_started)
        reference_takes = self._session_takes
        reference_strokes = self._session_strokes
        if self.repair:
            reference_takes, reference_strokes, _ = self._stored_samples()
        if reference_takes and not self._pose_consistent(take, reference_takes):
            self._reject("inconsistent")
            return
        if (self.kind != "stop" and reference_strokes and
                not self._motion_consistent(stroke, reference_strokes)):
            self._reject("inconsistent")
            return
        prospective_takes = self._session_takes + [take]
        prospective_strokes = self._session_strokes + ([stroke] if self.kind != "stop" else [])
        prospective_dts = self._session_dts + ([dt] if self.kind != "stop" else [])

        completing = len(prospective_takes) >= self.required
        if completing:
            if self.repair:
                old_takes, old_strokes, old_dts = self._stored_samples()
                if len(old_takes) < 2 or (self.kind != "stop" and len(old_strokes) < 2):
                    self._reject("inconsistent")
                    return
                prospective_takes = old_takes[-2:] + [take]
                if self.kind != "stop":
                    prospective_strokes = old_strokes[-2:] + [stroke]
                    prospective_dts = old_dts[-2:] + [dt]
            try:
                candidate = self._build_candidate(prospective_takes,
                                                  prospective_strokes,
                                                  prospective_dts)
            except Exception:
                self._reject("inconsistent")
                return
            self._commit(prospective_takes, prospective_strokes,
                         prospective_dts, candidate)

        self._session_takes.append(take)
        if self.kind != "stop":
            self._session_strokes.append(stroke)
            self._session_dts.append(dt)
        self.accepted += 1
        self.finalized = completing
        self.phase = "accepted"
        self.reason = ""
        self.event_id += 1
        self._clear_capture(keep_phase=True)

    def _stored_samples(self):
        if self.kind == "jump":
            return list(self.teach.jump_takes), list(self.teach.strokes), list(self.teach.dts)
        if self.kind == "crouch":
            return (list(self.teach.crouch_takes), list(self.teach.crouch_strokes),
                    list(self.teach.crouch_dts))
        return list(self.teach.stop_takes), [], []

    @staticmethod
    def _pose_consistent(candidate, references) -> bool:
        def medoid(rec):
            feats = np.array([pp.feature_vector(f.landmarks)
                              for f in rec.valid_frames()])
            return np.median(feats, axis=0)
        target = medoid(candidate)
        reference = np.median(np.array([medoid(r) for r in references]), axis=0)
        return pp.pose_distance(target, reference) <= pp.TAKE_INCONSISTENT_MAX

    @staticmethod
    def _motion_consistent(candidate, references) -> bool:
        def direction(track):
            a, b = track[0], track[-1]
            return mpr.quantize_dir8(b[0] - a[0], b[1] - a[1])
        expected = int(np.bincount([direction(s) for s in references]).argmax())
        return direction(candidate) == expected

    def _build_candidate(self, takes, strokes, dts):
        jump = copy.deepcopy(self.teach.jump_model)
        crouch = copy.deepcopy(self.teach.crouch_model)
        stop = copy.deepcopy(self.teach.stop_model)
        motion = None
        name = self.kind.upper()
        pose = pp.register_pose(name, takes)
        if self.kind == "jump":
            motion = mpr.learn_jump_motion(strokes, dts)
            if stop and not pp.is_separable(pose, stop):
                raise pp.RegistrationError("JUMP too close to STOP")
            if stop:
                pp.constrain_separation(pose, stop)
            jump = pose
        elif self.kind == "crouch":
            motion = mpr.learn_jump_motion(strokes, dts)
            if jump and not pp.is_separable(pose, jump):
                raise pp.RegistrationError("CROUCH too close to JUMP")
            if stop and not pp.is_separable(pose, stop):
                raise pp.RegistrationError("CROUCH too close to STOP")
            if jump:
                pp.constrain_separation(jump, pose)
            if stop:
                pp.constrain_separation(stop, pose)
            crouch = pose
        else:
            if jump and not pp.is_separable(pose, jump):
                raise pp.RegistrationError("STOP too close to JUMP")
            if jump:
                pp.constrain_separation(jump, pose)
            stop = pose
        return jump, crouch, stop, motion

    def _commit(self, takes, strokes, dts, candidate) -> None:
        jump, crouch, stop, motion = candidate
        self.teach.jump_model = jump
        self.teach.crouch_model = crouch
        self.teach.stop_model = stop
        if self.kind == "jump":
            self.teach.jump_takes = list(takes)
            self.teach.strokes = list(strokes)
            self.teach.dts = list(dts)
            self.teach.jump_motion = motion
            self.teach.verified = True
            self.teach.proof_state = "ready"
        elif self.kind == "crouch":
            self.teach.crouch_takes = list(takes)
            self.teach.crouch_strokes = list(strokes)
            self.teach.crouch_dts = list(dts)
            self.teach.crouch_motion = motion
        else:
            self.teach.stop_takes = list(takes)

    def _reject(self, reason: str) -> None:
        self.reason = reason
        self.phase = "rejected"
        self.finalized = False
        self.event_id += 1
        if self.kind == "jump":
            self.teach.jump_rejected += 1
        elif self.kind == "crouch":
            self.teach.crouch_rejected += 1
        elif self.kind == "stop":
            self.teach.stop_rejected += 1
        self.teach.last_reason = reason
        self._clear_capture(keep_phase=True)

    def _clear_capture(self, keep_phase: bool = False) -> None:
        self._pressed = False
        self._lost = False
        self._invalid_reason = ""
        self._frames = []
        self._pose = None
        self._track = []
        self._track_started = 0.0
        if not keep_phase and self.active:
            self.phase = "ready"

    @staticmethod
    def _geometry_reason(reason: str) -> str:
        if reason in ("HAND OUT OF FRAME", "TRACKING LOST"):
            return "tracking_lost"
        if reason == "MOVE CLOSER":
            return "too_far"
        return "out_of_frame"


class ProductionControlPlane:
    def __init__(self, runtime: ProductionTeachingRuntime, bench_cfg=None):
        self.runtime = runtime
        self.bench_cfg = bench_cfg
        self.actions = queue.Queue()

    def enqueue(self, payload: dict) -> None:
        self.actions.put(dict(payload))

    def process_pending(self, t: float) -> None:
        while True:
            try:
                msg = self.actions.get_nowait()
            except queue.Empty:
                return
            action = str(msg.get("action", ""))
            if action == "begin":
                self.runtime.begin(str(msg.get("kind", "")).lower(),
                                   bool(msg.get("repair", False)))
            elif action == "press":
                self.runtime.press(t)
            elif action == "release":
                self.runtime.release(t)
            elif action == "cancel":
                self.runtime.cancel()
            elif action == "reset":
                self.runtime.reset(t)
            elif action == "reset_command":
                self.runtime.reset_command(str(msg.get("target", "")),
                                           int(msg.get("request_id", 0)), t)
            elif action == "direction":
                enabled = bool(msg.get("enabled", False))
                self.runtime.set_direction_mode(enabled)
                if self.bench_cfg is not None:
                    self.bench_cfg.mode_direction = enabled


class _Server(ThreadingHTTPServer):
    daemon_threads = True
    allow_reuse_address = True

    def __init__(self, address, plane):
        self.plane = plane
        super().__init__(address, _Handler)


class _Handler(BaseHTTPRequestHandler):
    server: _Server

    def do_GET(self):  # noqa: N802
        if self.path != CONTROL_PATH:
            self.send_error(404)
            return
        self._write(self.server.plane.runtime.snapshot())

    def do_POST(self):  # noqa: N802
        if self.path != CONTROL_PATH:
            self.send_error(404)
            return
        try:
            size = int(self.headers.get("Content-Length", "0"))
            if size <= 0 or size > 4096:
                raise ValueError("invalid body size")
            payload = json.loads(self.rfile.read(size).decode("utf-8"))
            if not isinstance(payload, dict):
                raise ValueError("object required")
            self.server.plane.enqueue(payload)
            self._write(self.server.plane.runtime.snapshot())
        except (ValueError, UnicodeDecodeError, json.JSONDecodeError):
            self._write({"error": "bad_request"}, status=400)

    def _write(self, payload: dict, status: int = 200) -> None:
        data = json.dumps(payload, separators=(",", ":")).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(data)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(data)

    def log_message(self, _format, *args):
        return


class LocalControlServer:
    """Small lifecycle wrapper; it can only bind to IPv4 loopback."""

    def __init__(self, plane: ProductionControlPlane, port: int):
        self._server = _Server(("127.0.0.1", int(port)), plane)
        self._thread = None

    @property
    def address(self):
        return self._server.server_address

    def start(self) -> None:
        self._thread = threading.Thread(target=self._server.serve_forever,
                                        name="before-control", daemon=True)
        self._thread.start()

    def close(self) -> None:
        self._server.shutdown()
        self._server.server_close()
        if self._thread is not None:
            self._thread.join(timeout=1.0)
            self._thread = None
