// BEFORE Goal 2 — EditMode logic tests for the CommandLab receive path.
using NUnit.Framework;
using Before.Production;

namespace Before.Production.Tests
{
    public class CommandCoreTests
    {
        private static string Cmd(string session, long sstart, int cmdId, int seq,
                                  string type, int dir = 0)
        {
            string d = dir != 0 ? (",\"dir\":" + dir) : "";
            return "{\"v\":1,\"kind\":\"cmd\",\"session\":\"" + session +
                   "\",\"sstart\":" + sstart + ",\"cmdId\":" + cmdId + ",\"seq\":" +
                   seq + ",\"tUs\":0,\"type\":\"" + type + "\",\"phase\":\"fire\"" + d + "}";
        }

        [Test]
        public void Parse_valid_command()
        {
            Assert.IsTrue(CommandPacket.TryParseCommand(Cmd("s", 1, 3, 2, "JUMP"), out var p));
            Assert.AreEqual(CommandKind.Jump, p.Kind);
            Assert.AreEqual(3, p.CommandId);
            Assert.AreEqual("s", p.Session);
        }

        [Test]
        public void Parse_rejects_landmark_and_garbage_as_command()
        {
            Assert.IsFalse(CommandPacket.TryParseCommand("{\"kind\":\"lm\",\"pts\":[]}", out _));
            Assert.IsFalse(CommandPacket.TryParseCommand("not json", out _));
            Assert.IsFalse(CommandPacket.TryParseCommand(Cmd("s", 1, 0, 1, "JUMP"), out _));
        }

        [Test]
        public void Triple_send_executes_once()
        {
            var r = new CommandRouter();
            int exec = 0;
            for (int seq = 1; seq <= 3; seq++)
            {
                CommandPacket.TryParseCommand(Cmd("s", 1, 5, seq, "JUMP"), out var p);
                if (r.Ingest(p)) exec++;
            }
            Assert.AreEqual(1, exec);
        }

        [Test]
        public void Five_distinct_jumps_execute_five()
        {
            var r = new CommandRouter();
            var nul = new NulState();
            for (int id = 1; id <= 5; id++)
                for (int seq = 1; seq <= 3; seq++)   // each id sent 3x
                {
                    CommandPacket.TryParseCommand(Cmd("s", 1, id, seq, "JUMP"), out var p);
                    if (r.Ingest(p)) nul.Apply(p, 0);
                }
            Assert.AreEqual(5, nul.JumpCount);
        }

        [Test]
        public void Stale_session_is_discarded()
        {
            var r = new CommandRouter();
            var nul = new NulState();
            // active session start = 100
            CommandPacket.TryParseCommand(Cmd("new", 100, 1, 1, "JUMP"), out var a);
            Assert.IsTrue(r.Ingest(a)); nul.Apply(a, 0);
            // older session start = 50 -> discarded, no effect
            CommandPacket.TryParseCommand(Cmd("old", 50, 1, 1, "JUMP"), out var b);
            Assert.IsFalse(r.Ingest(b));
            Assert.AreEqual(1, nul.JumpCount);
            Assert.AreEqual(1, r.Session.DiscardedStale);
        }

        [Test]
        public void Newer_session_switch_clears_dedup()
        {
            var r = new CommandRouter();
            CommandPacket.TryParseCommand(Cmd("s1", 100, 1, 1, "JUMP"), out var a);
            Assert.IsTrue(r.Ingest(a));
            // a NEW session (later start) reusing cmdId 1 must still execute
            CommandPacket.TryParseCommand(Cmd("s2", 200, 1, 1, "JUMP"), out var b);
            Assert.IsTrue(r.Ingest(b));
            Assert.AreEqual("s2", r.Session.ActiveSession);
        }

        [Test]
        public void Crouch_turns_on_then_off_by_075s_timer_and_refreshes()
        {
            var nul = new NulState();
            CommandPacket.TryParseCommand(Cmd("s", 1, 1, 1, "CROUCH"), out var c);
            nul.Apply(c, 0.0);
            Assert.IsTrue(nul.Crouching);
            nul.Tick(0.5); Assert.IsTrue(nul.Crouching);      // still within 0.75
            // a second CROUCH at t=0.5 refreshes the remaining time to 0.75
            CommandPacket.TryParseCommand(Cmd("s", 1, 2, 1, "CROUCH"), out var c2);
            nul.Apply(c2, 0.5);
            nul.Tick(0.8); Assert.IsTrue(nul.Crouching);      // refreshed -> still on
            nul.Tick(1.3); Assert.IsFalse(nul.Crouching);     // 0.5+0.75 elapsed
        }

        [Test]
        public void Stop_on_off_toggles()
        {
            var nul = new NulState();
            CommandPacket.TryParseCommand(Cmd("s", 1, 1, 1, "STOP_ON"), out var on);
            nul.Apply(on, 0); Assert.IsTrue(nul.StopActive);
            CommandPacket.TryParseCommand(Cmd("s", 1, 2, 1, "STOP_OFF"), out var off);
            nul.Apply(off, 0); Assert.IsFalse(nul.StopActive);
        }

        [Test]
        public void Direction_mode_is_exclusive()
        {
            var nul = new NulState { DirectionMode = true };
            CommandPacket.TryParseCommand(Cmd("s", 1, 1, 1, "JUMP"), out var j);
            CommandPacket.TryParseCommand(Cmd("s", 1, 2, 1, "STOP_ON"), out var st);
            CommandPacket.TryParseCommand(Cmd("s", 1, 3, 1, "RIGHT", 1), out var rg);
            nul.Apply(j, 0); nul.Apply(st, 0); nul.Apply(rg, 0);
            Assert.AreEqual(0, nul.JumpCount);      // suppressed
            Assert.IsFalse(nul.StopActive);         // suppressed
            Assert.AreEqual(1, nul.LastDirection);  // Right acted
        }

        [Test]
        public void Pause_suppresses_and_never_replays()
        {
            var r = new CommandRouter();
            var nul = new NulState { Paused = true };
            // command arrives while paused: consumed (deduped) but no effect
            for (int seq = 1; seq <= 3; seq++)
            {
                CommandPacket.TryParseCommand(Cmd("s", 1, 7, seq, "JUMP"), out var p);
                if (r.Ingest(p)) nul.Apply(p, 0);
            }
            Assert.AreEqual(0, nul.JumpCount);
            // unpause: the SAME command id must NOT re-execute (already deduped)
            nul.Paused = false;
            for (int seq = 1; seq <= 3; seq++)
            {
                CommandPacket.TryParseCommand(Cmd("s", 1, 7, seq, "JUMP"), out var p);
                if (r.Ingest(p)) nul.Apply(p, 0);
            }
            Assert.AreEqual(0, nul.JumpCount);      // no replay
        }

        [Test]
        public void Bus_publishes_executed_commands()
        {
            var bus = new CommandBus();
            int got = 0;
            bus.OnCommand += _ => got++;
            CommandPacket.TryParseCommand(Cmd("s", 1, 1, 1, "JUMP"), out var p);
            bus.Publish(p);
            Assert.AreEqual(1, got);
        }

        [Test]
        public void Landmark_parse_flat_42()
        {
            var sb = new System.Text.StringBuilder("{\"v\":1,\"kind\":\"lm\",\"session\":\"s\",\"sstart\":0,\"tUs\":0,\"pts\":[");
            for (int i = 0; i < 42; i++) { if (i > 0) sb.Append(','); sb.Append("0.5"); }
            sb.Append("]}");
            Assert.IsTrue(LandmarkPacket.TryParse(sb.ToString(), out var pts));
            Assert.AreEqual(42, pts.Length);
        }
    }
}
