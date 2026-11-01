"""UDP command/landmark sender for the CommandLab link (Goal 2).

Local `127.0.0.1` only. One session per process run; each commandId is sent
CMD_REPEAT (3) times with seq 1..3 so Unity can dedupe. Never sends RGB.
"""
from __future__ import annotations

import socket
from typing import Optional, Sequence

from . import net_protocol as np_


class UdpSender:
    def __init__(self, host: str = np_.DEFAULT_HOST, port: int = np_.DEFAULT_PORT,
                 session: Optional[str] = None):
        self.host = host
        self.port = port
        self.session = session or np_.new_session()
        self.session_start_us = np_.now_us()
        self._cmd_id = 0
        self._sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)

    def send_command(self, ctype: str, *, direction: Optional[int] = None,
                     confidence: float = 1.0, pose_distance: float = 0.0,
                     motion_progress: float = 1.0,
                     tracking_quality: float = 1.0) -> int:
        """Send one command 3x (seq 1..3). Returns the assigned commandId."""
        self._cmd_id += 1
        cid = self._cmd_id
        t = np_.now_us()
        for seq in range(1, np_.CMD_REPEAT + 1):
            data = np_.encode_command(
                self.session, self.session_start_us, cid, seq, ctype, t_us=t,
                confidence=confidence, pose_distance=pose_distance,
                motion_progress=motion_progress, tracking_quality=tracking_quality,
                direction=direction)
            self._sock.sendto(data, (self.host, self.port))
        return cid

    def send_landmarks(self, pts: Optional[Sequence[Sequence[float]]], *,
                       readiness: str = "", signal: str = "ok",
                       **diagnostics) -> None:
        data = np_.encode_landmarks(self.session, self.session_start_us, pts,
                                    readiness=readiness, signal=signal,
                                    **diagnostics)
        self._sock.sendto(data, (self.host, self.port))

    def close(self) -> None:
        try:
            self._sock.close()
        except Exception:
            pass
