"""Skeleton-only diagnostic recognition bench (D-099, §9).

Draws ONLY a black background with the mirrored 21-landmark skeleton, hand
connection lines, wrist/palm-center/fingertips, and diagnostic text. No RGB is
ever shown or written. No game or story is created here.

Controls (printed at start):
    j : record a Jump pose take (1.20 s)      -> after 3 takes, teach motion
    m : record a Jump motion stroke (hold and move)
    f : finalize Jump (build pose+motion model from the recorded takes)
    s : record a Stop pose take (1.20 s)       -> after 3 takes, build Stop
    d : toggle Direction bench mode (exclusive)
    1..5 : mark expected label for QA counting (Jump/StopON/StopOFF/Left/Right)
    q : quit

QA counts are display/tally only; the expected label never enters recognition.
Live measurement statistics (latency, false fires) are exported to a JSON file
on quit for D-104 evidence.
"""
from __future__ import annotations

import argparse
import json
import os
import sys
import time
from collections import deque
from dataclasses import dataclass, field
from typing import Dict, List, Optional, Tuple

import numpy as np

HAND_CONNECTIONS = [
    (0, 1), (1, 2), (2, 3), (3, 4),
    (0, 5), (5, 6), (6, 7), (7, 8),
    (0, 9), (9, 10), (10, 11), (11, 12),
    (0, 13), (13, 14), (14, 15), (15, 16),
    (0, 17), (17, 18), (18, 19), (19, 20),
    (5, 9), (9, 13), (13, 17),
]
FINGER_TIPS = (4, 8, 12, 16, 20)
PALM_IDS = (0, 5, 9, 13, 17)
W, H = 960, 720


def jump_diag_lines(diag):
    """Build the three D-111 diagnostic strings from a JumpDiagnostics snapshot.

    Pure text; uses only the public snapshot (no Engine private fields). Shared
    by the bench renderer and the regression tests so the exact draw-path data
    assembly is covered.
    """
    pose = (f"POSE channel:JUMP held:{diag.pose_held} stable:{diag.stable}/7 "
            f"d:{diag.distance:.3f} th:{diag.threshold:.3f} reason:{diag.reason}")
    stroke = (f"STROKE state:{diag.stroke_state} disp:{diag.displacement:.3f} "
              f"model-req:{diag.required:.3f} live-req:{diag.live_required:.3f} "
              f"dir:{diag.cur_dir} expected:{diag.expected_dir} "
              f"cos:{diag.cosine:.2f} cos-th:{diag.cos_threshold:.2f}")
    speed = (f"fwd-disp:{diag.displacement:.3f} "
             f"reverse:{diag.reverse:.3f}/{diag.reverse_req:.3f} "
             f"rev-frames:{diag.reverse_frames}/{diag.reverse_needed} "
             f"settle:{diag.settle}/{diag.settle_needed} "
             f"speed:{diag.speed:.3f}/{diag.settle_speed:.3f} "
             f"lockout:{diag.lockout_ms:.0f}ms cycle:{diag.cycle} "
             f"lastReset:{diag.last_reset}")
    return pose, stroke, speed


def crouch_diag_line(diag):
    """Build the D-128 discrete-Crouch diagnostic string (Jump-shaped snapshot)."""
    return (f"CROUCH held:{diag.pose_held} stable:{diag.stable}/7 "
            f"state:{diag.stroke_state} fwd-disp:{diag.displacement:.3f} "
            f"model-req:{diag.required:.3f} live-req:{diag.live_required:.3f} "
            f"cos:{diag.cosine:.2f} cos-th:{diag.cos_threshold:.2f} "
            f"reverse:{diag.reverse:.3f}/{diag.reverse_req:.3f} "
            f"rev:{diag.reverse_frames}/{diag.reverse_needed} "
            f"settle:{diag.settle}/{diag.settle_needed} "
            f"lockout:{diag.lockout_ms:.0f}ms cycle:{diag.cycle} "
            f"reason:{diag.reason}")


def _lazy_imports():
    import cv2  # type: ignore
    from . import camera as cam
    from . import mediapipe_backend as mb
    from . import personal_pose as pp
    from . import motion_primitive as mpr
    from . import command_engine as ce
    return cv2, cam, mb, pp, mpr, ce


_QA_KEYS = ("JUMP", "CROUCH", "STOP_ON", "STOP_OFF", "LEFT", "RIGHT")


@dataclass
class QaCounts:
    trials: Dict[str, int] = field(default_factory=lambda: {k: 0 for k in _QA_KEYS})
    fires: Dict[str, int] = field(default_factory=lambda: {k: 0 for k in _QA_KEYS})


def run(camera_index: Optional[int], confirm_override: bool = False,
         camera=None, backend=None, key_script=None, max_frames=None,
         headless: bool = False, clock=None, probe=None,
         udp_port: Optional[int] = None,
         control_port: Optional[int] = None) -> int:
    cv2, cam, mb, pp, mpr, ce = _lazy_imports()
    now_fn = clock or time.monotonic
    key_script = key_script or {}

    sender = None
    if udp_port:
        from .udp_sender import UdpSender
        sender = UdpSender(port=udp_port)
        print(f"UDP link: 127.0.0.1:{udp_port} session={sender.session}")

    print(__doc__)
    if camera is None:
        try:
            camera = cam.Camera.open_preferred(index_override=camera_index,
                                               confirm_override=confirm_override)
        except cam.CameraError as exc:
            print(f"CAMERA STOP: {exc}")
            return 2
    print(f"camera: name={getattr(camera, 'name', '<injected>')!r}")

    if backend is None:
        try:
            backend = mb.MediaPipeBackend()
        except Exception as exc:
            print(f"MODEL STOP: {exc}")
            camera.release()
            return 3

    from . import teach_controller as tcm
    from . import bench_overlay as bo

    cfg = ce.EngineConfig()
    engine = ce.Engine(cfg=cfg)
    teach = tcm.TeachController()
    production = None
    control_server = None
    if control_port:
        from .production_control import (LocalControlServer,
                                         ProductionControlPlane,
                                         ProductionTeachingRuntime)
        production = ProductionTeachingRuntime(teach)
        control_plane = ProductionControlPlane(production, cfg)
        control_server = LocalControlServer(control_plane, control_port)
        control_server.start()
        print(f"Control plane: http://127.0.0.1:{control_port}/v1/teaching")
    overlay = bo.OverlayState()
    if probe is not None:
        probe["teach"] = teach
        probe["overlay"] = overlay
        probe["engine_ref"] = lambda: teach.engine
        probe["production_teaching"] = production

    qa = QaCounts()
    expected: Optional[str] = None
    latencies: deque = deque(maxlen=400)
    hz = deque(maxlen=30)
    last_cmd = ""
    t0 = now_fn()
    win = "BEFORE gesture bench (skeleton-only)"
    if not headless:
        cv2.namedWindow(win)

    # keys handled as down-edges: only act when the key changes from "none".
    ACTION_KEYS = set("jcsmevbfgSdrq123456")
    prev_key = 255

    def canvas():
        return np.zeros((H, W, 3), dtype=np.uint8)

    def draw_skeleton(img, lm, geom_ok):
        col = (60, 200, 255) if geom_ok else (60, 90, 200)
        pxy = [(int(x * W), int(y * H)) for x, y, *_ in lm]
        for a, b in HAND_CONNECTIONS:
            cv2.line(img, pxy[a], pxy[b], col, 2)
        for p in pxy:
            cv2.circle(img, p, 3, (200, 200, 200), -1)
        for t in FINGER_TIPS:
            cv2.circle(img, pxy[t], 5, (80, 255, 160), -1)
        cv2.circle(img, pxy[0], 6, (255, 120, 120), -1)  # wrist
        palm = np.mean([lm[i][:2] for i in PALM_IDS], axis=0)
        cv2.circle(img, (int(palm[0] * W), int(palm[1] * H)), 5,
                   (255, 220, 120), -1)
        return (float(palm[0]), float(palm[1]))

    FONT = cv2.FONT_HERSHEY_SIMPLEX
    # Fixed diagnostics grid: left column, one line per row, no overlap.
    DIAG_X, DIAG_Y0, ROW_H = 12, 96, 22
    DIAG_MAXW = int(W * 0.60)   # leave the right side free for the event log

    def put_row(img, row, text, col=(210, 210, 210)):
        # auto-shrink to fit DIAG_MAXW so lines never run into each other/edge
        scale = 0.48
        while scale > 0.30:
            (tw, _), _ = cv2.getTextSize(text, FONT, scale, 1)
            if tw <= DIAG_MAXW:
                break
            scale -= 0.04
        y = DIAG_Y0 + row * ROW_H
        cv2.putText(img, text, (DIAG_X, y), FONT, scale, col, 1, cv2.LINE_AA)

    def draw_safe_box(img):
        m = int(pp.TEACH_MARGIN * W), int(pp.TEACH_MARGIN * H)
        cv2.rectangle(img, m, (W - m[0], H - m[1]), (50, 50, 60), 1)

    def big_status(img, text):
        # top model/teaching status only (separate from fire events)
        reject = text.startswith("REJECTED") or "OUT OF FRAME" in text or \
            text.startswith("MOVE") or "RECALIBRATION" in text
        col = (90, 120, 255) if reject else (120, 240, 160)
        (tw, _), _ = cv2.getTextSize(text, FONT, 1.0, 2)
        cv2.putText(img, text, (max(10, (W - tw) // 2), 52), FONT, 1.0, col, 2,
                    cv2.LINE_AA)

    def fire_banner(img, now):
        # large centered transient "<CMD> FIRED #n" on any recognized command
        if not overlay.banner_active(now):
            return
        text = overlay.banner_text()
        col = overlay.banner_color()
        (tw, th), _ = cv2.getTextSize(text, FONT, 1.7, 4)
        x, y = max(10, (W - tw) // 2), H // 2
        cv2.putText(img, text, (x, y), FONT, 1.7, col, 4, cv2.LINE_AA)

    def draw_flash(img, now):
        # border flash in the command color; NEVER shows RGB camera frames
        if overlay.flash_active(now):
            cv2.rectangle(img, (0, 0), (W - 1, H - 1), overlay.banner_color(), 8)

    def draw_events(img):
        # recent 5 events bottom-right, newest first, each in its type color
        x = W - 300
        y0 = H - 110
        cv2.putText(img, "recent events", (x, y0), FONT, 0.45, (140, 140, 150),
                    1, cv2.LINE_AA)
        for i, (wall, ctype) in enumerate(overlay.recent()):
            cv2.putText(img, f"{wall}  {ctype}", (x, y0 + 20 * (i + 1)), FONT,
                        0.5, bo.event_color(ctype), 1, cv2.LINE_AA)

    frame_i = 0
    try:
        while True:
            if max_frames is not None and frame_i >= max_frames:
                break
            rgb = camera.read_mirrored()
            now = now_fn()
            if rgb is None:
                continue
            frame_i += 1
            res = backend.process(rgb, int((now - t0) * 1000))
            hz.append(now)
            lm_list = res.landmarks.tolist() if res.landmarks is not None else None

            img = canvas()
            draw_safe_box(img)
            palm = None
            if res.tracked and res.landmarks is not None:
                palm = draw_skeleton(img, res.landmarks, teach.geom_valid)

            # teaching + live recognition (teach owns the engine; drives proof)
            palm_pre = palm
            if palm_pre is None and res.landmarks is not None:
                palm_pre = tuple(np.mean([res.landmarks[i][:2]
                                          for i in PALM_IDS], axis=0))
            t_dec = time.monotonic()
            if production is not None:
                control_plane.process_pending(now)
            if production is not None and production.active:
                production.feed(lm_list, res.quality if res.tracked else 0.0,
                                palm_pre, now, tracked=res.tracked)
                cmds = []
            else:
                cmds = teach.feed(lm_list, res.quality if res.tracked else 0.0,
                                  palm_pre, now, tracked=res.tracked)
            dec_ms = (time.monotonic() - t_dec) * 1000.0
            engine = teach.engine
            if cmds:
                latencies.append(dec_ms)
            for c in cmds:
                last_cmd = f"{c.type.value} id={c.id}"
                key = c.type.value
                if key == "DIRECTION":
                    key = "LEFT" if c.direction < 0 else "RIGHT"
                if key in qa.fires:            # QA counting unchanged
                    qa.fires[key] += 1
                # visual feedback only (no recognition/threshold/QA change)
                wall = time.strftime("%H:%M:%S") + f".{int((time.time() % 1) * 1000):03d}"
                overlay.on_command(key, now, wall)
                # transport only (Goal 2): stream the frozen command over UDP
                if sender is not None:
                    d = c.direction if c.type.value == "DIRECTION" else None
                    sender.send_command(key, direction=d)
                print(f"CMD {c.to_json()}")
            # Stream skeleton + read-only recognizer readiness (never RGB). The
            # readiness value is projected from the Engine state advanced above;
            # no Unity-side pose approximation and no command packet is involved.
            signal = "ok"
            if not res.tracked or lm_list is None:
                signal = "tracking_lost"
            elif res.quality < pp.TRACK_QUALITY_MIN:
                signal = "low_quality"
            elif not teach.geom_valid:
                signal = "out_of_frame"
            signal_valid = signal == "ok"
            readiness = (engine.readiness(
                signal_valid=signal_valid,
                teaching_active=production is not None and production.active)
                if engine is not None else "")
            if sender is not None:
                points = ([(p[0], p[1]) for p in lm_list]
                          if lm_list is not None else None)
                owner = ("teaching" if production is not None and production.active
                         else "direction" if engine is not None and
                         engine.cfg.mode_direction else "live")
                stop_diag = engine.get_stop_diagnostics(now) if engine is not None else None
                sender.send_landmarks(
                    points, readiness=readiness, signal=signal,
                    quality=res.quality if res.tracked else 0.0,
                    direction_preview=(engine.direction_preview(None)
                                       if engine is not None else 0),
                    direction_hold=(engine.direction_hold_progress(now)
                                    if engine is not None else 0.0),
                    recognition_owner=owner,
                    stop_distance=(stop_diag.distance if stop_diag else -1.0),
                    stop_threshold=(stop_diag.threshold if stop_diag else 0.0),
                    stop_candidate_started_ms=(
                        stop_diag.candidate_started_ms if stop_diag else 0.0),
                    stop_stable_frames=(
                        stop_diag.stable_frames if stop_diag else 0),
                    stop_hold_ms=(stop_diag.hold_ms if stop_diag else 0.0),
                    stop_on_issued_ms=(
                        stop_diag.on_issued_ms if stop_diag else 0.0),
                    stop_off_issued_ms=(
                        stop_diag.off_issued_ms if stop_diag else 0.0),
                    stop_reject=(stop_diag.reject_reason if stop_diag else ""),
                    stop_active=(stop_diag.active if stop_diag else False))

            # ---- top model/teaching status (JUMP READY lives here) ----
            big_status(img, teach.big_message(now))

            # ---- separated fixed-row diagnostics (no overlap) ----
            fps = (len(hz) - 1) / (hz[-1] - hz[0]) if len(hz) > 1 else 0.0
            gcol = (120, 240, 160) if teach.geom_valid else (90, 120, 255)
            put_row(img, 0, f"cam {camera.name}  cam-fps {fps:4.1f}  mp-Hz {fps:4.1f}")
            put_row(img, 1, f"mediapipe-confidence {res.quality:.2f}  "
                            f"tracked {res.tracked}")
            put_row(img, 2, f"teaching-geometry-valid {teach.geom_valid} "
                            f"({teach.geom_reason})", gcol)
            put_row(img, 3, f"jump {len(teach.jump_takes)}/3 rej {teach.jump_rejected}  "
                            f"crouch {len(teach.crouch_takes)}/3 rej {teach.crouch_rejected}  "
                            f"stop {len(teach.stop_takes)}/3 rej {teach.stop_rejected}")
            put_row(img, 4, f"capture {teach.capturing or 'none'}  "
                            f"remaining {teach.capture_remaining(now):.2f}s   "
                            f"last-rejection {teach.last_reason}",
                    (160, 160, 220))
            # D-111 pose-held / stroke-segmented diagnostics — via the public
            # read-only snapshot API only (no Engine private-field access).
            if engine is not None and (teach.jump_model or teach.stop_model):
                pose_l, stroke_l, speed_l = jump_diag_lines(
                    engine.get_diagnostics(now))
                acol = (120, 240, 160) if "reason:Armed" in pose_l \
                    else (120, 220, 240)
                put_row(img, 5, pose_l, acol)
                put_row(img, 6, stroke_l, (120, 220, 240))
                put_row(img, 11, speed_l, (120, 220, 240))
            else:
                put_row(img, 5, "POSE — no model yet")
                put_row(img, 6, "STROKE — no model yet")
            med = float(np.median(latencies)) if latencies else 0.0
            p95 = float(np.percentile(latencies, 95)) if latencies else 0.0
            put_row(img, 7, f"decide-ms median {med:.1f} p95 {p95:.1f}  "
                            f"last {last_cmd}")
            put_row(img, 8, f"canned {res.top_category} {res.category_score:.2f} "
                            f"(auxiliary)  dir-mode {cfg.mode_direction}")
            # QA line (own row)
            put_row(img, 9, f"QA expected={expected} trials={qa.trials}",
                    (160, 160, 220))
            put_row(img, 10, f"QA fires={qa.fires}", (160, 160, 220))
            # D-112 Crouch diagnostics (own row) via public snapshot API
            if engine is not None and teach.crouch_model is not None:
                put_row(img, 12,
                        crouch_diag_line(engine.get_crouch_diagnostics(now)),
                        (120, 220, 240))
            if teach.proof_state == "recal":
                put_row(img, 13, f"PROOF FAILED d={teach.proof_d:.3f} "
                                 f"th={teach.proof_th:.3f} — press R and re-teach",
                        (90, 120, 255))

            # ---- command event feedback (visual only) ----
            fire_banner(img, now)
            draw_events(img)
            draw_flash(img, now)

            cv2.putText(img, "keys: J jump-take M/E motion F finalize | "
                        "C crouch-take V/B motion G finalize | S stop-take "
                        "Shift+S finalize | D dir  R reset  1-6 QA  Q quit",
                        (12, H - 14), FONT, 0.42, (120, 120, 130), 1, cv2.LINE_AA)

            if headless:
                k = key_script.get(frame_i, 255)
            else:
                cv2.imshow(win, img)
                k = cv2.waitKey(1) & 0xFF

            # down-edge detection: act only when the key first changes
            ch = chr(k) if 32 <= k < 127 else ""
            edge = (k != prev_key) and (ch in ACTION_KEYS)
            prev_key = k
            if edge:
                if ch == "q":
                    break
                elif ch in "jcsmevbfgSr":
                    teach.on_key(ch, now)
                    if teach.qa_reset_requested:
                        qa = QaCounts()
                        expected = None
                        teach.qa_reset_requested = False
                elif ch == "d":
                    e = teach._engine()
                    e.cfg.mode_direction = not e.cfg.mode_direction
                    cfg.mode_direction = e.cfg.mode_direction
                elif ch in "123456":
                    expected = ["JUMP", "CROUCH", "STOP_ON", "STOP_OFF",
                                "LEFT", "RIGHT"][int(ch) - 1]
                    qa.trials[expected] += 1
    finally:
        camera.release()
        backend.close()
        if sender is not None:
            sender.close()
        if control_server is not None:
            control_server.close()
        if not headless:
            cv2.destroyAllWindows()
        if probe is not None:
            probe["frames"] = frame_i

    stats = {
        "camera": getattr(camera, "name", "<injected>"),
        "frames": frame_i,
        "jump_takes_accepted": len(teach.jump_takes),
        "jump_takes_rejected": teach.jump_rejected,
        "crouch_takes_accepted": len(teach.crouch_takes),
        "crouch_takes_rejected": teach.crouch_rejected,
        "stop_takes_accepted": len(teach.stop_takes),
        "stop_takes_rejected": teach.stop_rejected,
        "qa_trials": qa.trials,
        "qa_fires": qa.fires,
        "latency_median_ms": float(np.median(latencies)) if latencies else None,
        "latency_p95_ms": float(np.percentile(latencies, 95)) if latencies else None,
        "rgb_frames_saved": 0,
        "rgb_frames_displayed": 0,
        "rgb_frames_sent": 0,
    }
    out = os.path.join(os.path.dirname(__file__), "..", "..",
                       "bench_stats.json")
    with open(os.path.abspath(out), "w", encoding="utf-8") as fh:
        json.dump(stats, fh, ensure_ascii=False, indent=2)
    print(f"stats written: {os.path.abspath(out)}")
    return 0


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description="BEFORE gesture bench")
    ap.add_argument("--camera-index", type=int, default=None,
                    help="diagnostic override device index; requires --confirm")
    ap.add_argument("--confirm", action="store_true",
                    help="confirm the overridden --camera-index is the Integrated Camera")
    ap.add_argument("--udp", type=int, default=None, metavar="PORT",
                    help="also stream frozen commands + skeleton to 127.0.0.1:PORT "
                         "(Unity CommandLab link, Goal 2)")
    ap.add_argument("--control", type=int, default=None, metavar="PORT",
                    help="localhost-only Unity production teaching control-plane")
    args = ap.parse_args(argv)
    return run(args.camera_index, confirm_override=args.confirm,
               udp_port=args.udp, control_port=args.control)


if __name__ == "__main__":
    raise SystemExit(main())
