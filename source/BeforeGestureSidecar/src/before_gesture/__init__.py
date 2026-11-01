"""BEFORE gesture sidecar.

Project-local MediaPipe perception bench for the BEFORE gesture co-op.

Decision references (project-local decision-log.md):
- D-097 freeze current in-Unity recognition (recoverable prototype).
- D-098 MediaPipe Python sidecar owns the camera; local inference only.
- D-099 privacy: skeleton-only, never display/save/transmit RGB.
- D-100 user-defined hand-shapes (3 takes x 1.20 s, per-recording validation).
- D-101 Jump = pose + taught 8-direction motion, threshold-crossing fire.
- D-102 Stop = static hand-shape, hold=ON / intentional break=OFF, loss != OFF.
- D-103 Direction = fixed index-finger deictic pointing (not learned).
- D-104 user real-camera acceptance thresholds gate Unity integration.
- D-105 Unity-integration prohibition until D-104 passes.

This package contains NO Unity code and performs NO Unity integration.
"""

__all__ = [
    "protocol",
    "personal_pose",
    "motion_primitive",
    "command_engine",
]

__version__ = "0.1.0"
