"""Environment verification (D-098).

Prints Python version, mediapipe version, cv2 import + version, numpy import.
Exits non-zero and reports clearly if cv2 cannot be imported, WITHOUT
attempting to install any additional OpenCV package.
"""
from __future__ import annotations

import sys


def main() -> int:
    print(f"python           : {sys.version.split()[0]}")
    ok = True

    try:
        import mediapipe as mp  # type: ignore
        print(f"mediapipe        : {mp.__version__}")
    except Exception as exc:  # pragma: no cover - environment probe
        print(f"mediapipe        : IMPORT FAILED: {exc!r}")
        ok = False

    try:
        import cv2  # type: ignore
        print(f"cv2              : {cv2.__version__}")
    except Exception as exc:  # pragma: no cover - environment probe
        print(f"cv2              : IMPORT FAILED: {exc!r}")
        print("STOP: OpenCV (cv2) is not importable. Do NOT stack multiple "
              "OpenCV packages. Report this and stop.")
        ok = False

    try:
        import numpy as np  # type: ignore
        print(f"numpy            : {np.__version__}")
    except Exception as exc:  # pragma: no cover - environment probe
        print(f"numpy            : IMPORT FAILED: {exc!r}")
        ok = False

    print("verify_env       :", "OK" if ok else "FAILED")
    return 0 if ok else 1


if __name__ == "__main__":
    raise SystemExit(main())
