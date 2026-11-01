"""Local UDP wire protocol for the sidecar -> Unity CommandLab link (Goal 2).

Additive transport ONLY. It never changes recognition (D-129 freeze); it just
serializes the frozen recognizers' output. Local `127.0.0.1` only. Two datagram
kinds share a session so Unity can dedupe and discard stale sessions:

  command datagram (sent 3x per commandId, Unity dedups by session+cmdId):
    {"v":1,"kind":"cmd","session":"<hex>","sstart":<us>,"cmdId":<int>,
     "seq":<1..3>,"tUs":<us>,"type":"JUMP|CROUCH|STOP_ON|STOP_OFF|LEFT|RIGHT",
     "phase":"fire","conf":<f>,"poseD":<f>,"motion":<f>,"track":<f>,"dir":<int?>}

  landmark datagram (~30 Hz, mirrored normalized points; NEVER any RGB):
    {"v":1,"kind":"lm","session":"<hex>","sstart":<us>,"tUs":<us>,
     "pts":[[x,y]*21]}
"""
from __future__ import annotations

import json
import math
import os
import time
import uuid
from typing import List, Optional, Sequence

PROTOCOL_VERSION = 1
DEFAULT_HOST = "127.0.0.1"
DEFAULT_PORT = 8765
CMD_REPEAT = 3  # send each commandId three times


def new_session() -> str:
    return uuid.uuid4().hex[:12]


def now_us() -> int:
    return int(time.time() * 1_000_000)


def encode_command(session: str, session_start_us: int, cmd_id: int, seq: int,
                   ctype: str, *, t_us: Optional[int] = None,
                   confidence: float = 1.0, pose_distance: float = 0.0,
                   motion_progress: float = 1.0, tracking_quality: float = 1.0,
                   direction: Optional[int] = None, phase: str = "fire") -> bytes:
    payload = {
        "v": PROTOCOL_VERSION, "kind": "cmd", "session": session,
        "sstart": session_start_us, "cmdId": cmd_id, "seq": seq,
        "tUs": t_us if t_us is not None else now_us(), "type": ctype,
        "phase": phase, "conf": round(confidence, 4),
        "poseD": round(pose_distance, 4), "motion": round(motion_progress, 4),
        "track": round(tracking_quality, 4),
    }
    if direction is not None:
        payload["dir"] = int(direction)
    return json.dumps(payload, separators=(",", ":")).encode("utf-8")


def encode_landmarks(session: str, session_start_us: int,
                     pts: Optional[Sequence[Sequence[float]]], *,
                     t_us: Optional[int] = None, readiness: str = "",
                     signal: str = "ok", quality: float = 0.0,
                     direction_preview: int = 0,
                     direction_hold: float = 0.0,
                     recognition_owner: str = "live",
                     stop_distance: float = -1.0,
                     stop_threshold: float = 0.0,
                     stop_candidate_started_ms: float = 0.0,
                     stop_stable_frames: int = 0,
                     stop_hold_ms: float = 0.0,
                     stop_on_issued_ms: float = 0.0,
                     stop_off_issued_ms: float = 0.0,
                     stop_reject: str = "",
                     stop_active: bool = False) -> bytes:
    # pts are mirrored normalized (x,y) in [0,1]; no depth, no RGB, ever.
    # Flat [x0,y0,x1,y1,...] so a Unity JsonUtility float[] can parse it.
    flat: List[float] = []
    if pts is not None:
        for p in pts:
            flat.append(round(float(p[0]), 4))
            flat.append(round(float(p[1]), 4))
    payload = {"v": PROTOCOL_VERSION, "kind": "lm", "session": session,
               "sstart": session_start_us,
               "tUs": t_us if t_us is not None else now_us(), "pts": flat}
    payload["ready"] = readiness if readiness in ("JUMP", "CROUCH", "STOP") else ""
    payload["signal"] = signal if signal in (
        "ok", "tracking_lost", "low_quality", "out_of_frame") else "tracking_lost"
    payload["trackQ"] = round(float(quality), 4)
    payload["dir"] = int(direction_preview) if direction_preview in (-1, 1) else 0
    payload["dirHold"] = round(max(0.0, min(1.0, float(direction_hold))), 4)
    payload["owner"] = recognition_owner if recognition_owner in (
        "live", "direction", "teaching") else "live"

    def finite(value: float, fallback: float = 0.0) -> float:
        value = float(value)
        return value if math.isfinite(value) else fallback

    payload["stopD"] = round(finite(stop_distance, -1.0), 5)
    payload["stopTh"] = round(finite(stop_threshold), 5)
    payload["stopCandidateMs"] = round(finite(stop_candidate_started_ms), 3)
    payload["stopStable"] = max(0, int(stop_stable_frames))
    payload["stopHoldMs"] = round(max(0.0, finite(stop_hold_ms)), 3)
    payload["stopOnMs"] = round(max(0.0, finite(stop_on_issued_ms)), 3)
    payload["stopOffMs"] = round(max(0.0, finite(stop_off_issued_ms)), 3)
    payload["stopReject"] = str(stop_reject)[:48]
    payload["stopActive"] = bool(stop_active)
    return json.dumps(payload, separators=(",", ":")).encode("utf-8")


def decode(data: bytes) -> dict:
    return json.loads(data.decode("utf-8"))
