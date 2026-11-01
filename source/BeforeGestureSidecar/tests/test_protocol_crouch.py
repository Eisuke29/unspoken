"""D-128 protocol: CROUCH exists, CROUCH_OFF removed; QA/overlay aligned."""
from before_gesture import protocol
from before_gesture import bench_app
from before_gesture import bench_overlay as bo


def test_16_protocol_has_crouch():
    names = [c.value for c in protocol.CommandType]
    assert "CROUCH" in names


def test_17_protocol_has_no_crouch_off_or_on():
    names = [c.value for c in protocol.CommandType]
    assert "CROUCH_OFF" not in names
    assert "CROUCH_ON" not in names
    assert not hasattr(protocol.CommandType, "CROUCH_OFF")
    assert not hasattr(protocol.CommandType, "CROUCH_ON")


def test_18_qa_and_events_use_crouch():
    assert "CROUCH" in bench_app._QA_KEYS
    assert "CROUCH_ON" not in bench_app._QA_KEYS
    assert "CROUCH_OFF" not in bench_app._QA_KEYS
    assert bo.event_color("CROUCH") == bo.AMBER
    ov = bo.OverlayState()
    ov.on_command("CROUCH", 1.0, "12:00:00.000")
    assert ov.counts["CROUCH"] == 1
    assert ov.recent()[0] == ("12:00:00.000", "CROUCH")


def test_crouch_command_serializes():
    c = protocol.Command(protocol.CommandType.CROUCH, 1.23, motion_dir8=2)
    assert '"type":"CROUCH"' in c.to_json()
