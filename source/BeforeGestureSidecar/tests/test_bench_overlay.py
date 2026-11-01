from before_gesture import bench_overlay as bo


def test_jump_banner_text_and_count():
    ov = bo.OverlayState()
    ov.on_command("JUMP", 10.0, "12:31:05.240")
    ov.on_command("JUMP", 12.0, "12:31:08.510")
    assert ov.counts["JUMP"] == 2
    assert ov.banner_text() == "JUMP FIRED  #2"
    assert ov.banner_color() == bo.CYAN


def test_banner_and_flash_timing_windows():
    ov = bo.OverlayState()
    ov.on_command("JUMP", 100.0, "t")
    assert ov.banner_active(100.0) and ov.flash_active(100.0)
    assert ov.flash_active(100.24) and not ov.flash_active(100.26)
    assert ov.banner_active(100.69) and not ov.banner_active(100.71)


def test_recent_events_newest_first_and_trimmed():
    ov = bo.OverlayState()
    for i in range(7):
        ov.on_command("JUMP", float(i), f"t{i}")
    rec = ov.recent()
    assert len(rec) == 5              # trimmed to 5
    assert rec[0] == ("t6", "JUMP")   # newest first
    assert rec[-1] == ("t2", "JUMP")


def test_all_event_colors():
    # D-115/D-128: Jump cyan, Crouch amber (discrete), Stop magenta.
    assert bo.event_color("JUMP") == bo.CYAN
    assert bo.event_color("CROUCH") == bo.AMBER
    assert bo.event_color("STOP_ON") == bo.MAGENTA
    assert bo.event_color("STOP_OFF") == bo.DIRTY_WHITE
    assert bo.event_color("LEFT") == bo.VIOLET
    assert bo.event_color("RIGHT") == bo.VIOLET


def test_stop_crouch_and_direction_banners():
    ov = bo.OverlayState()
    ov.on_command("STOP_ON", 1.0, "t")
    assert ov.banner_text() == "STOP ON  #1" and ov.banner_color() == bo.MAGENTA
    ov.on_command("CROUCH", 1.5, "t")
    assert ov.banner_text() == "CROUCH FIRED  #1" and ov.banner_color() == bo.AMBER
    ov.on_command("STOP_OFF", 2.0, "t")
    assert ov.banner_text() == "STOP OFF  #1"
    assert ov.banner_color() == bo.DIRTY_WHITE
    ov.on_command("LEFT", 3.0, "t")
    assert ov.banner_text() == "LEFT  #1" and ov.banner_color() == bo.VIOLET


def test_unknown_type_ignored():
    ov = bo.OverlayState()
    ov.on_command("DIRECTION", 1.0, "t")  # must be mapped to LEFT/RIGHT by caller
    assert ov.counts.get("DIRECTION", 0) == 0
    assert ov.recent() == []
