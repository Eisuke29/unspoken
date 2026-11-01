"""Static privacy scan (D-099): no camera-RGB display/save/network path.

Fails if the sidecar source ever writes an image, opens a network socket, or
displays the RGB frame. The bench's only cv2.imshow target is the generated
black diagnostic canvas `img`, never the camera `rgb`.
"""
import os
import re

SRC = os.path.join(os.path.dirname(__file__), "..", "src", "before_gesture")


def _src_files():
    for name in os.listdir(SRC):
        if name.endswith(".py"):
            with open(os.path.join(SRC, name), "r", encoding="utf-8") as fh:
                yield name, fh.read()


def test_no_image_write_anywhere():
    for name, txt in _src_files():
        assert "imwrite" not in txt, f"{name} writes an image"
        assert "imencode" not in txt, f"{name} encodes an image"


def test_only_transport_modules_use_sockets():
    # Networking is allowed ONLY for the local skeleton/command link, and only
    # in udp_sender.py (net_protocol.py just serializes). No cloud/HTTP anywhere.
    allowed = {"udp_sender.py"}
    for name, txt in _src_files():
        if "socket" in txt or "sendto(" in txt:
            assert name in allowed, f"{name} must not open a socket"
        for pat in ("urllib.request", "requests.", "http.client", "websocket",
                    "socket.AF_INET6"):
            assert pat not in txt, f"{name} contains a cloud/remote path {pat!r}"


def test_transport_is_local_only_and_carries_no_rgb():
    # the UDP link targets 127.0.0.1 and never touches rgb/image/frame pixels
    for name in ("udp_sender.py", "net_protocol.py"):
        txt = open(os.path.join(SRC, name), "r", encoding="utf-8").read()
        assert "127.0.0.1" in txt
        for bad in ("rgb", "imread", "imwrite", "read_mirrored", "cvtColor",
                    "WebCamTexture"):
            assert bad not in txt, f"{name} references pixels ({bad})"


def test_rgb_only_feeds_inference_never_display_or_disk():
    # `rgb` may only: be read from the camera, be checked for None, and be
    # passed to backend.process. It must never reach imshow/imwrite/save/send.
    for name, txt in _src_files():
        for m in re.finditer(r"^.*\brgb\b.*$", txt, re.MULTILINE):
            line = m.group(0)
            assert "imshow" not in line, f"{name}: rgb reaches imshow: {line.strip()}"
            assert "imwrite" not in line, f"{name}: rgb written: {line.strip()}"
            assert ".save(" not in line, f"{name}: rgb saved: {line.strip()}"
            assert "send" not in line, f"{name}: rgb sent: {line.strip()}"


def test_imshow_only_shows_generated_canvas():
    for name, txt in _src_files():
        for m in re.finditer(r"cv2\.imshow\(([^)]*)\)", txt):
            args = m.group(1)
            assert "rgb" not in args, f"{name}: imshow shows rgb: {args}"
            assert "img" in args, f"{name}: imshow arg unexpected: {args}"


def test_bench_reports_zero_rgb_counters():
    # the stats block hard-codes zero display/save/send counters
    bench = open(os.path.join(SRC, "bench_app.py"), "r", encoding="utf-8").read()
    assert '"rgb_frames_saved": 0' in bench
    assert '"rgb_frames_displayed": 0' in bench
    assert '"rgb_frames_sent": 0' in bench
