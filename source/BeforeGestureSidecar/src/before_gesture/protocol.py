"""Command protocol (D-101/D-102/D-103, D-105).

Defines the command messages the bench emits. This pass defines the protocol
and can log/print commands, but per D-105 does NOT open a Unity UDP receiver or
integrate with Unity. A future integration pass (after D-104 passes) may send
these as JSON datagrams; the schema is fixed here so it is stable.

Each command carries a monotonically increasing integer id so the receiving
side (later) can execute each command exactly once (D-106 acceptance).
"""
from __future__ import annotations

import json
from dataclasses import dataclass, field, asdict
from enum import Enum
from itertools import count
from typing import Optional


class CommandType(str, Enum):
    JUMP = "JUMP"
    CROUCH = "CROUCH"        # D-128 discrete Pose B + Motion B stroke event
    STOP_ON = "STOP_ON"
    STOP_OFF = "STOP_OFF"
    DIRECTION = "DIRECTION"  # payload.dir in {-1 left, +1 right}


_ids = count(1)


@dataclass(frozen=True)
class Command:
    """One recognized command. Never gated by any game-side judgment (D-108)."""
    type: CommandType
    t_monotonic: float
    id: int = field(default_factory=lambda: next(_ids))
    # Optional payloads
    direction: Optional[int] = None          # DIRECTION: -1 / +1
    motion_dir8: Optional[int] = None         # JUMP: 0..7 quantized primitive
    latency_ms: Optional[float] = None        # decision latency at emit time

    def to_json(self) -> str:
        d = asdict(self)
        d["type"] = self.type.value
        return json.dumps(d, separators=(",", ":"))


def reset_ids() -> None:
    """Test helper: restart the command id counter."""
    global _ids
    _ids = count(1)
