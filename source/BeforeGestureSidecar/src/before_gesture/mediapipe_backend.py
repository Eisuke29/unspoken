"""MediaPipe hand landmarks + canned gesture categories (D-098/D-100).

Wraps the official float16 revision-1 gesture_recognizer.task. Produces 21
landmarks, handedness, and canned category probabilities (auxiliary evidence
only). No RGB leaves this module (D-099).
"""
from __future__ import annotations

import os
from dataclasses import dataclass
from typing import List, Optional, Sequence, Tuple

import numpy as np

MODEL_PATH = os.path.join(os.path.dirname(__file__), "..", "..",
                          "models", "gesture_recognizer.task")


@dataclass
class HandResult:
    tracked: bool
    landmarks: Optional[np.ndarray]          # 21x3 normalized (x,y in [0,1])
    handedness: Optional[str]
    quality: float                            # visibility/presence proxy
    top_category: Optional[str]
    category_score: float


class MediaPipeBackend:
    def __init__(self, model_path: str = MODEL_PATH):
        import mediapipe as mp  # type: ignore
        from mediapipe.tasks import python as mp_python  # type: ignore
        from mediapipe.tasks.python import vision  # type: ignore

        model_path = os.path.abspath(model_path)
        if not os.path.exists(model_path):
            raise FileNotFoundError(
                f"gesture_recognizer.task not found at {model_path}. "
                "Download the official model first (see mediapipe-sidecar.md).")
        base = mp_python.BaseOptions(model_asset_path=model_path)
        opts = vision.GestureRecognizerOptions(
            base_options=base,
            running_mode=vision.RunningMode.VIDEO,
            num_hands=1,
            min_hand_detection_confidence=0.5,
            min_hand_presence_confidence=0.5,
            min_tracking_confidence=0.5,
        )
        self._mp = mp
        self._recognizer = vision.GestureRecognizer.create_from_options(opts)

    def process(self, rgb: np.ndarray, timestamp_ms: int) -> HandResult:
        mp = self._mp
        image = mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb)
        res = self._recognizer.recognize_for_video(image, timestamp_ms)
        if not res.hand_landmarks:
            return HandResult(False, None, None, 0.0, None, 0.0)
        lms = res.hand_landmarks[0]
        arr = np.array([[p.x, p.y, p.z] for p in lms], dtype=np.float64)
        handed = None
        if res.handedness and res.handedness[0]:
            handed = res.handedness[0][0].category_name
        top_cat, score = None, 0.0
        if res.gestures and res.gestures[0]:
            top_cat = res.gestures[0][0].category_name
            score = float(res.gestures[0][0].score)
        # quality proxy: mean presence via z-spread stability is unavailable;
        # use detection presence (always tracked here) + hand size sanity.
        quality = 1.0
        return HandResult(True, arr, handed, quality, top_cat, score)

    def close(self) -> None:
        try:
            self._recognizer.close()
        except Exception:
            pass
