"""Goal 2 UDP transport (additive; recognition frozen). Local 127.0.0.1 only."""
import socket

from before_gesture import net_protocol as npc
from before_gesture import udp_sender as us


def test_command_roundtrip_fields():
    b = npc.encode_command("abc123", 111, 7, 2, "JUMP", t_us=222,
                           confidence=0.9, pose_distance=0.05,
                           motion_progress=1.0, tracking_quality=1.0)
    d = npc.decode(b)
    assert d["kind"] == "cmd" and d["v"] == 1
    assert d["session"] == "abc123" and d["sstart"] == 111
    assert d["cmdId"] == 7 and d["seq"] == 2 and d["type"] == "JUMP"
    assert d["conf"] == 0.9 and "poseD" in d and "motion" in d and "track" in d


def test_direction_payload():
    d = npc.decode(npc.encode_command("s", 0, 1, 1, "LEFT", direction=-1))
    assert d["type"] == "LEFT" and d["dir"] == -1


def test_landmarks_carry_no_rgb():
    pts = [[0.1 * i, 0.2 * i] for i in range(21)]
    d = npc.decode(npc.encode_landmarks("s", 0, pts))
    assert d["kind"] == "lm" and len(d["pts"]) == 42   # flat [x,y]*21
    # only 2-D normalized points; no image/rgb/depth keys anywhere
    for k in d:
        assert k not in ("rgb", "image", "frame", "z", "depth")


def test_landmarks_add_direction_stop_and_owner_diagnostics_without_rgb():
    pts = [[0.1, 0.2] for _ in range(21)]
    d = npc.decode(npc.encode_landmarks(
        "s", 0, pts, quality=0.82, direction_preview=-1,
        direction_hold=0.6, recognition_owner="direction",
        stop_distance=0.21, stop_threshold=0.3,
        stop_candidate_started_ms=1000.0, stop_stable_frames=3,
        stop_hold_ms=66.7, stop_on_issued_ms=1066.7,
        stop_reject="candidate", stop_active=False))
    assert d["trackQ"] == 0.82
    assert d["dir"] == -1 and d["dirHold"] == 0.6
    assert d["owner"] == "direction"
    assert d["stopStable"] == 3
    assert d["stopReject"] == "candidate"
    assert "rgb" not in d


def test_sender_sends_each_command_three_times_same_id():
    rx = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    rx.bind(("127.0.0.1", 0))
    rx.settimeout(1.0)
    port = rx.getsockname()[1]
    s = us.UdpSender(port=port)
    cid = s.send_command("JUMP")
    seqs = []
    for _ in range(3):
        data, _ = rx.recvfrom(4096)
        d = npc.decode(data)
        assert d["cmdId"] == cid and d["session"] == s.session
        seqs.append(d["seq"])
    assert sorted(seqs) == [1, 2, 3]
    s.close(); rx.close()


def test_sender_command_ids_are_monotonic():
    s = us.UdpSender(port=9)  # not actually delivered; ids still increment
    a = s.send_command("JUMP")
    b = s.send_command("CROUCH")
    assert b == a + 1
    s.close()


def test_two_senders_have_distinct_sessions():
    a = us.UdpSender(port=9)
    b = us.UdpSender(port=9)
    assert a.session != b.session
    a.close(); b.close()
