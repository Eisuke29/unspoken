// BEFORE Goal 2 — BEFORE_CommandLab empty receive scene controller.
// Wires: Sidecar (opt) -> UDP -> CommandRouter (session+dedup) -> CommandBus ->
// NulState (immediate reaction, D-113). Skeleton-only hand panel + private
// tokens (Jump cyan / Crouch amber / Stop magenta, D-115). Never shows RGB.
// Keys: P pause (suppress, no replay), D toggle Direction-exclusive mode.
using System.Collections.Generic;
using UnityEngine;

namespace Before.Production
{
    public sealed class CommandLabController : MonoBehaviour
    {
        public int udpPort = 8765;
        public bool autoLaunchSidecar = false;   // off for hermetic Play; user enables

        // public state for tests / inspection
        public readonly CommandRouter Router = new CommandRouter();
        public readonly CommandBus Bus = new CommandBus();
        public readonly NulState Nul = new NulState();

        private UdpCommandReceiver _rx;
        private SidecarProcessManager _sidecar;

        // visuals
        private Camera _cam;
        private SpriteRenderer _nul;
        private readonly SpriteRenderer[] _tokens = new SpriteRenderer[3]; // J,C,S
        private readonly float[] _flash = new float[3];
        private float[] _landmarks;         // last skeleton (42 floats) or null
        private float _hopY, _hopVel;

        private static readonly Color Cyan = new Color(0f, 0.85f, 0.91f);
        private static readonly Color Amber = new Color(0.95f, 0.71f, 0.27f);
        private static readonly Color Magenta = new Color(0.85f, 0.29f, 0.47f);

        private void Awake()
        {
            _cam = Camera.main;
            if (_cam == null)
            {
                var go = new GameObject("DisplayCamera");
                _cam = go.AddComponent<Camera>();
                _cam.tag = "MainCamera";
            }
            _cam.orthographic = true;
            _cam.orthographicSize = 3f;
            _cam.backgroundColor = new Color(0.02f, 0.03f, 0.04f);
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.transform.position = new Vector3(0f, 0f, -10f);

            _nul = MakeSprite("NUL", Color.white, new Vector3(0f, -1.5f, 0f), 0.8f);
            _tokens[0] = MakeSprite("Token_Jump", Dim(Cyan), new Vector3(-1.2f, 2.2f, 0f), 0.5f);
            _tokens[1] = MakeSprite("Token_Crouch", Dim(Amber), new Vector3(0f, 2.2f, 0f), 0.5f);
            _tokens[2] = MakeSprite("Token_Stop", Dim(Magenta), new Vector3(1.2f, 2.2f, 0f), 0.5f);

            _rx = gameObject.AddComponent<UdpCommandReceiver>();
            _rx.port = udpPort;
            _sidecar = gameObject.AddComponent<SidecarProcessManager>();
            _sidecar.udpPort = udpPort;

            Bus.OnCommand += OnBusCommand;
        }

        private void Start()
        {
            // re-read the (possibly Inspector/test-overridden) port before binding
            _rx.port = udpPort;
            _sidecar.udpPort = udpPort;
            _rx.StartListening();
            if (autoLaunchSidecar) _sidecar.Launch();
        }

        private void OnBusCommand(CommandPacket p)
        {
            double now = Time.timeAsDouble;
            Nul.Apply(p, now);
            switch (p.Kind)
            {
                case CommandKind.Jump: _hopVel = 4.5f; _flash[0] = 1f; break;
                case CommandKind.Crouch: _flash[1] = 1f; break;
                case CommandKind.StopOn:
                case CommandKind.StopOff: _flash[2] = 1f; break;
            }
        }

        private void Update()
        {
            // The project uses the Input System package; read the keyboard through
            // it (never legacy UnityEngine.Input, which throws under that setting).
#if ENABLE_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null)
            {
                if (kb.pKey.wasPressedThisFrame) Nul.Paused = !Nul.Paused;
                if (kb.dKey.wasPressedThisFrame) Nul.DirectionMode = !Nul.DirectionMode;
            }
#endif
            IngestUdp();

            double now = Time.timeAsDouble;
            Nul.Tick(now);

            // NUL hop physics + crouch scale + stop tint (guarded: objects may be
            // torn down mid-frame during tests / scene unload).
            _hopVel -= 18f * Time.deltaTime;
            _hopY += _hopVel * Time.deltaTime;
            if (_hopY <= 0f) { _hopY = 0f; if (_hopVel < 0f) _hopVel = 0f; }
            if (_nul != null)
            {
                float sy = Nul.Crouching ? 0.4f : 1f;
                _nul.transform.localPosition = new Vector3(0f, -1.5f + _hopY, 0f);
                _nul.transform.localScale = new Vector3(0.8f, 0.8f * sy, 1f);
                _nul.color = Nul.StopActive ? new Color(1f, 0.35f, 0.38f) : Color.white;
            }
            for (int i = 0; i < 3; i++)
            {
                _flash[i] = Mathf.Max(0f, _flash[i] - Time.deltaTime * 3f);
                if (_tokens[i] == null) continue;
                Color baseC = i == 0 ? Cyan : (i == 1 ? Amber : Magenta);
                _tokens[i].color = Color.Lerp(Dim(baseC), baseC, _flash[i]);
            }
        }

        private void IngestUdp()
        {
            if (_rx == null) return;
            List<string> msgs = _rx.Poll();
            for (int i = 0; i < msgs.Count; i++)
            {
                string m = msgs[i];
                if (CommandPacket.TryParseCommand(m, out var pkt))
                {
                    // dedup + session gate happen here; publish only first-seen,
                    // active-session commands. Suppression while paused happens in
                    // NulState.Apply, but the command is still consumed (no replay).
                    if (Router.Ingest(pkt)) Bus.Publish(pkt);
                }
                else if (LandmarkPacket.TryParse(m, out var pts))
                {
                    _landmarks = pts;
                }
            }
        }

        // Test/inspection helpers ------------------------------------------------
        public int ReceiverPort => udpPort;
        public bool ReceiverListening => _rx != null && _rx.Listening;

        // Feed a raw datagram directly (used by tests without a socket).
        public void InjectDatagram(string json)
        {
            if (CommandPacket.TryParseCommand(json, out var pkt))
            {
                if (Router.Ingest(pkt)) Bus.Publish(pkt);
            }
            else if (LandmarkPacket.TryParse(json, out var pts))
            {
                _landmarks = pts;
            }
        }

        private void OnDisable()
        {
            Bus.OnCommand -= OnBusCommand;
        }

        // ---- visuals helpers ----
        private static Color Dim(Color c) => new Color(c.r * 0.25f, c.g * 0.25f, c.b * 0.25f);

        private SpriteRenderer MakeSprite(string name, Color c, Vector3 pos, float scale)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = pos;
            go.transform.localScale = new Vector3(scale, scale, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            var tex = new Texture2D(2, 2) { filterMode = FilterMode.Point };
            tex.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white });
            tex.Apply();
            sr.sprite = Sprite.Create(tex, new Rect(0, 0, 2, 2), new Vector2(0.5f, 0.5f), 2f);
            sr.color = c;
            return sr;
        }

        private void OnGUI()
        {
            // SYS / state (top-left) — no RGB anywhere on screen
            GUI.color = Color.white;
            GUI.Label(new Rect(12, 8, 900, 22),
                "SYS// CommandLab  udp:" + udpPort + "  session:" +
                (Router.Session.ActiveSession ?? "-") + "  stale-discarded:" +
                Router.Session.DiscardedStale);
            GUI.Label(new Rect(12, 30, 900, 22),
                "JUMP x" + Nul.JumpCount + "   CROUCH " +
                (Nul.Crouching ? "ON " + Nul.CrouchRemaining(Time.timeAsDouble).ToString("0.00") + "s" : "off") +
                "   STOP " + (Nul.StopActive ? "ON" : "off") +
                "   DIR-mode " + Nul.DirectionMode + " last:" +
                (Nul.LastDirection < 0 ? "LEFT" : (Nul.LastDirection > 0 ? "RIGHT" : "-")) +
                (Nul.Paused ? "   [PAUSED]" : ""));
            GUI.Label(new Rect(12, 52, 900, 22),
                "keys: P pause   D direction-mode   (recognition frozen; RGB never shown)");

            DrawSkeleton();
        }

        // Skeleton-only hand panel (mirrored 21 landmarks), never RGB.
        private static readonly int[,] Conn = {
            {0,1},{1,2},{2,3},{3,4}, {0,5},{5,6},{6,7},{7,8},
            {0,9},{9,10},{10,11},{11,12}, {0,13},{13,14},{14,15},{15,16},
            {0,17},{17,18},{18,19},{19,20}, {5,9},{9,13},{13,17} };

        private void DrawSkeleton()
        {
            var panel = new Rect(Screen.width - 300, 80, 288, 384);
            GUI.color = new Color(0f, 0f, 0f, 1f);
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = new Color(0.05f, 0.05f, 0.07f);
            GUI.DrawTexture(new Rect(panel.x + 2, panel.y + 2, panel.width - 4, panel.height - 4),
                            Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(panel.x + 6, panel.y + 4, 280, 20), "HAND (skeleton only)");
            if (_landmarks == null || _landmarks.Length != 42) return;
            Vector2 P(int idx)
            {
                float x = _landmarks[idx * 2], y = _landmarks[idx * 2 + 1];
                return new Vector2(panel.x + 6 + x * (panel.width - 12),
                                   panel.y + 22 + y * (panel.height - 30));
            }
            for (int i = 0; i < Conn.GetLength(0); i++)
                DrawLine(P(Conn[i, 0]), P(Conn[i, 1]), new Color(0.24f, 0.78f, 1f), 2f);
            for (int i = 0; i < 21; i++)
            {
                var p = P(i);
                GUI.color = new Color(0.85f, 0.85f, 0.85f);
                GUI.DrawTexture(new Rect(p.x - 2, p.y - 2, 4, 4), Texture2D.whiteTexture);
            }
        }

        private static void DrawLine(Vector2 a, Vector2 b, Color c, float w)
        {
            Matrix4x4 m = GUI.matrix;
            Color prev = GUI.color;
            GUI.color = c;
            float ang = Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg;
            float len = Vector2.Distance(a, b);
            GUIUtility.RotateAroundPivot(ang, a);
            GUI.DrawTexture(new Rect(a.x, a.y - w * 0.5f, len, w), Texture2D.whiteTexture);
            GUI.matrix = m;
            GUI.color = prev;
        }
    }
}
