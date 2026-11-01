"""Camera ownership (D-098/D-099, §5).

The sidecar alone owns the webcam. Exact preferred name "Integrated Camera";
if not found, STOP (no automatic fallback to another camera). RGB frames are
used only as MediaPipe input and for computing mirrored landmarks — they are
never displayed, saved, or transmitted (D-099).

Windows device enumeration uses cv2 with the MSMF/DShow backend. Because
OpenCV cannot always name devices, we enumerate via the Windows registry /
pygrabber if available; otherwise we honor an explicit index override and log
which name the user must confirm.
"""
from __future__ import annotations

from dataclasses import dataclass
from typing import List, Optional, Tuple

import cv2  # type: ignore

PREFERRED_NAME = "Integrated Camera"
WIDTH = 640
HEIGHT = 480
FPS = 30


def list_device_names() -> List[str]:
    """Best-effort device-name enumeration on Windows."""
    try:
        from pygrabber.dshow_graph import FilterGraph  # type: ignore
        return list(FilterGraph().get_input_devices())
    except Exception:
        return []


class CameraError(Exception):
    pass


def resolve_exact_index(name: str, devices: List[str]) -> int:
    """Pure resolver (testable without hardware).

    Returns the index of the single exact-name match, else STOPS by raising
    CameraError. Never falls back to another camera and never guesses an index.
    """
    if not devices:
        raise CameraError(
            "Cannot enumerate camera names (pygrabber unavailable). "
            "Re-run with an explicit --camera-index and CONFIRM it is the "
            "Integrated Camera; do not auto-fallback.")
    matches = [i for i, n in enumerate(devices) if n == name]
    if len(matches) == 0:
        raise CameraError(
            f"Exact camera '{name}' not found. Available: {devices}. "
            "STOP: no automatic fallback to another camera (D-098/§5).")
    if len(matches) > 1:
        raise CameraError(
            f"Multiple exact '{name}' devices at indices {matches}. "
            f"Available: {devices}. STOP: ambiguous — report the list, do not "
            "guess (D-098/§5).")
    return matches[0]


@dataclass
class Camera:
    index: int
    name: str
    _cap: "cv2.VideoCapture"

    @classmethod
    def open_preferred(cls, name: str = PREFERRED_NAME,
                       index_override: Optional[int] = None,
                       confirm_override: bool = False) -> "Camera":
        names = list_device_names()
        if index_override is None:
            # Normal path: enumerate every launch and resolve EXACT name.
            idx = resolve_exact_index(name, names)
            chosen = names[idx]
        else:
            # Diagnostic override: show the real device name at that index and
            # refuse to start without explicit confirmation (D-098 §7).
            idx = index_override
            real = names[idx] if (names and 0 <= idx < len(names)) else "<unknown>"
            if not confirm_override:
                raise CameraError(
                    f"--camera-index {idx} maps to device {real!r}. "
                    "STOP: re-run with --confirm ONLY if this is the Integrated "
                    "Camera; the bench will not start an unconfirmed override.")
            chosen = real
            print(f"override: index {idx} -> device {chosen!r} (confirmed)")
        cap = cv2.VideoCapture(idx, cv2.CAP_DSHOW)
        if not cap.isOpened():
            cap = cv2.VideoCapture(idx)
        if not cap.isOpened():
            raise CameraError(f"Failed to open camera index {idx}")
        cap.set(cv2.CAP_PROP_FRAME_WIDTH, WIDTH)
        cap.set(cv2.CAP_PROP_FRAME_HEIGHT, HEIGHT)
        cap.set(cv2.CAP_PROP_FPS, FPS)
        return cls(index=int(idx), name=chosen, _cap=cap)

    def read_mirrored(self):
        """Return an RGB, horizontally-mirrored frame (kept in memory only)."""
        ok, bgr = self._cap.read()
        if not ok:
            return None
        bgr = cv2.flip(bgr, 1)  # mirror
        return cv2.cvtColor(bgr, cv2.COLOR_BGR2RGB)

    def release(self) -> None:
        try:
            self._cap.release()
        except Exception:
            pass
