"""Bench visual-feedback state (UI only).

Pure logic (no cv2) so it is unit-testable. Holds command-event feedback for the
diagnostic bench: per-type counts, a recent-event list, and the transient
fire-banner / border-flash timing. It performs NO recognition, changes NO
thresholds/models/QA — it only records events the bench already emitted so the
renderer can show them clearly.
"""
from __future__ import annotations

from collections import defaultdict, deque
from dataclasses import dataclass, field
from typing import Deque, Dict, List, Tuple

BANNER_S = 0.70    # big centered "…FIRED #n" banner hold
FLASH_S = 0.25     # border flash hold

# Colors are BGR (OpenCV order).
CYAN = (255, 255, 0)
AMBER = (0, 191, 255)
DIRTY_WHITE = (200, 200, 210)
VIOLET = (220, 80, 185)

# D-115: Jump cyan, Crouch amber, Stop magenta.
MAGENTA = (216, 74, 120)  # BGR-ish for stop (magenta)

COLORS: Dict[str, Tuple[int, int, int]] = {
    "JUMP": CYAN,
    "CROUCH": AMBER,           # D-128 discrete event
    "STOP_ON": MAGENTA,
    "STOP_OFF": DIRTY_WHITE,
    "LEFT": VIOLET,
    "RIGHT": VIOLET,
}

_BANNER_LABEL = {
    "JUMP": "JUMP FIRED",
    "CROUCH": "CROUCH FIRED",
    "STOP_ON": "STOP ON",
    "STOP_OFF": "STOP OFF",
    "LEFT": "LEFT",
    "RIGHT": "RIGHT",
}


def event_color(ctype: str) -> Tuple[int, int, int]:
    return COLORS.get(ctype, (255, 255, 255))


@dataclass
class OverlayState:
    counts: Dict[str, int] = field(default_factory=lambda: defaultdict(int))
    events: Deque[Tuple[str, str]] = field(
        default_factory=lambda: deque(maxlen=5))  # (wall_str, ctype), newest-first
    last_fire_t: float = -1e9
    last_type: str = ""

    def on_command(self, ctype: str, t_mono: float, wall: str) -> None:
        """Record one emitted command for display only."""
        if ctype not in COLORS:
            return
        self.counts[ctype] += 1
        self.events.appendleft((wall, ctype))
        self.last_fire_t = t_mono
        self.last_type = ctype

    def banner_active(self, now: float) -> bool:
        return self.last_type != "" and (now - self.last_fire_t) <= BANNER_S

    def flash_active(self, now: float) -> bool:
        return self.last_type != "" and (now - self.last_fire_t) <= FLASH_S

    def banner_text(self) -> str:
        if not self.last_type:
            return ""
        return f"{_BANNER_LABEL[self.last_type]}  #{self.counts[self.last_type]}"

    def banner_color(self) -> Tuple[int, int, int]:
        return event_color(self.last_type)

    def recent(self) -> List[Tuple[str, str]]:
        return list(self.events)  # newest-first
