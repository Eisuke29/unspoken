using UnityEngine;

namespace Before.Production
{
    /// <summary>
    /// Production adaptation of the previously approved pooled EnergyField:
    /// fixed particles, 2x2 logical pixels with sparse 3x3 accents, and a
    /// palm-to-live-NUL-core curve. No RGB and no per-frame GameObject creation.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProductionEnergyLinkPresenter : MonoBehaviour
    {
        public const float NormalRate = 14f;
        public const float ActiveRate = 28f;
        public const float TravelSeconds = 2.2f;
        public const float FadeSeconds = 0.6f;
        public const int PoolSize = 52;
        public const int BigEvery = 9;
        private static readonly int[] PalmIndices = { 0, 5, 9, 13, 17 };

        private struct Particle
        {
            public bool Alive;
            public float SpawnedAt;
            public float Offset;
            public bool Big;
        }

        [SerializeField] private NulProductionController controller;
        [SerializeField] private ProductionEnergyRecoveryFlow recovery;
        [SerializeField] private ProductionShellFlow shell;
        [SerializeField] private ProductionCoreFlow core;
        [SerializeField] private ProductionTeachingFlow teaching;
        [SerializeField] private ProductionGameLoop gameLoop;
        [SerializeField] private Camera worldCamera;
        [SerializeField] private Font publicFont;

        private readonly Particle[] _pool = new Particle[PoolSize];
        private float _spawnAccum;
        private int _spawnIndex;
        private float _disconnectAt = float.NegativeInfinity;
        private bool _wasEmitting;
        private Vector2 _lastPalmNormalized = new Vector2(0.5f, 0.5f);
        private int _lastPublished;
        private float _acceptedPulseAt = float.NegativeInfinity;
        private GUIStyle _message;
        private GUIStyle _instruction;

        public NulProductionController Controller
        {
            get => controller;
            set => controller = value;
        }
        public ProductionEnergyRecoveryFlow Recovery
        {
            get => recovery;
            set => recovery = value;
        }
        public ProductionShellFlow Shell { get => shell; set => shell = value; }
        public ProductionCoreFlow Core { get => core; set => core = value; }
        public Camera WorldCamera { get => worldCamera; set => worldCamera = value; }
        public Font PublicFont
        {
            get => publicFont;
            set
            {
                publicFont = value;
                _message = null;
                _instruction = null;
            }
        }
        public int AliveParticleCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < _pool.Length; i++)
                    if (_pool[i].Alive) count++;
                return count;
            }
        }
        public string EmissionState { get; private set; } = "off";
        public Vector2 LastPalmScreenPoint { get; private set; }
        public Vector2 LastCoreScreenPoint { get; private set; }
        public bool ComposedExternally { get; set; }

        private void Awake()
        {
            if (controller == null) controller = GetComponent<NulProductionController>();
            if (recovery == null) recovery = GetComponent<ProductionEnergyRecoveryFlow>();
            if (shell == null) shell = GetComponent<ProductionShellFlow>();
            if (core == null) core = GetComponent<ProductionCoreFlow>();
            if (teaching == null) teaching = GetComponent<ProductionTeachingFlow>();
            if (gameLoop == null) gameLoop = GetComponent<ProductionGameLoop>();
            if (worldCamera == null) worldCamera = Camera.main;
        }

        private void OnEnable()
        {
            ResetPresentation();
            if (gameLoop == null) gameLoop = GetComponent<ProductionGameLoop>();
            if (gameLoop != null) gameLoop.OnLoopReset += ResetPresentation;
        }

        private void OnDisable()
        {
            if (gameLoop != null) gameLoop.OnLoopReset -= ResetPresentation;
        }

        private void Update()
        {
            if (controller == null) return;
            if (TryGetPalmNormalized(controller, out Vector2 palm))
                _lastPalmNormalized = palm;

            if (controller.Bus.Published > _lastPublished)
            {
                _acceptedPulseAt = Time.unscaledTime;
                _lastPublished = controller.Bus.Published;
            }

            bool independent = recovery != null &&
                               recovery.State == ProductionEnergyState.Independent;
            bool entryLink = shell != null &&
                (shell.EntryStage == ProductionEntryStage.ConnectionDetected ||
                 shell.EntryStage == ProductionEntryStage.LinkEstablished ||
                 shell.EntryStage == ProductionEntryStage.Opening);
            bool connected = !independent &&
                (entryLink || recovery == null ||
                 recovery.State == ProductionEnergyState.Connected ||
                 recovery.State == ProductionEnergyState.Recharging);
            bool active = connected &&
                ((teaching != null && teaching.IsActive) ||
                 controller.Readiness != ProductionReadinessKind.None ||
                 controller.DirectionPreview != 0);
            AdvancePool(Time.unscaledTime, Time.unscaledDeltaTime, connected, active);

            if (independent) EmissionState = "independent-off";
            else if (recovery != null &&
                     recovery.State == ProductionEnergyState.Fragmenting)
                EmissionState = "fragmenting";
            else if (recovery != null &&
                     (recovery.State == ProductionEnergyState.Depleted ||
                      recovery.State == ProductionEnergyState.Collapsed))
                EmissionState = "off";
            else if (recovery != null &&
                     recovery.State == ProductionEnergyState.Recharging)
                EmissionState = "recharging";
            else EmissionState = active ? "active" : connected ? "normal" : "off";
        }

        private void OnGUI()
        {
            if (ComposedExternally) return;
            DrawOverlay();
        }

        public void DrawOverlay()
        {
            if (Event.current.type != EventType.Repaint || controller == null) return;
            if (shell != null && shell.EntryStage == ProductionEntryStage.Title)
                return;
            bool independent = recovery != null &&
                               recovery.State == ProductionEnergyState.Independent;
            if (independent) return;
            int previousDepth = GUI.depth;
            GUI.depth = shell != null &&
                shell.EntryStage != ProductionEntryStage.Running ? -520 : -70;

            Rect content = ProductionHudLayout.ContentRect(Screen.width, Screen.height);
            Rect panel = ProductionHudLayout.HandPanelRect(Screen.width, Screen.height);
            Vector2 start = PalmScreenPoint(panel, _lastPalmNormalized);
            Vector2 end = NulCoreScreenPoint(worldCamera, controller, content);
            LastPalmScreenPoint = start;
            LastCoreScreenPoint = end;

            float linkProgress = EntryLinkProgress();
            Vector2 visibleEnd = Vector2.Lerp(start, end, linkProgress);
            bool fragmenting = recovery != null &&
                               recovery.State == ProductionEnergyState.Fragmenting;
            bool depleted = recovery != null &&
                (recovery.State == ProductionEnergyState.Depleted ||
                 recovery.State == ProductionEnergyState.Collapsed);
            float fade = _disconnectAt > float.NegativeInfinity
                ? 1f - Mathf.Clamp01((Time.unscaledTime - _disconnectAt) / FadeSeconds)
                : 1f;
            if (!depleted && (linkProgress > 0f || fade > 0f))
                DrawLink(content, start, visibleEnd, fragmenting, fade);

            DrawCoreState(end);
            DrawRecoveryMessage(content);
            GUI.depth = previousDepth;
        }

        public static bool TryGetPalmNormalized(NulProductionController source,
            out Vector2 palm)
        {
            palm = Vector2.zero;
            if (source == null) return false;
            for (int i = 0; i < PalmIndices.Length; i++)
            {
                if (!source.TryGetLandmark(PalmIndices[i], out Vector2 point))
                    return false;
                palm += point;
            }
            palm /= PalmIndices.Length;
            return true;
        }

        public static Vector2 PalmScreenPoint(Rect panel, Vector2 normalized)
            => new Vector2(panel.x + Mathf.Clamp01(normalized.x) * panel.width,
                panel.y + Mathf.Clamp01(normalized.y) * panel.height);

        public static Vector2 NulCoreScreenPoint(Camera camera,
            NulProductionController source, Rect content)
        {
            if (camera == null || source == null) return content.center;
            Vector3 world = source.transform.position +
                            Vector3.up * (15f * NulProductionController.VisualScale);
            Vector3 viewport = camera.WorldToViewportPoint(world);
            return ScreenPointForViewport(content,
                new Vector2(viewport.x, viewport.y));
        }

        public static Vector2 ScreenPointForViewport(Rect content, Vector2 viewport)
            => new Vector2(
                Mathf.Clamp(content.x + viewport.x * content.width,
                    content.xMin, content.xMax),
                Mathf.Clamp(content.y + (1f - viewport.y) * content.height,
                    content.yMin, content.yMax));

        public static Vector2 CurvePoint(Vector2 start, Vector2 control,
            Vector2 end, float t)
        {
            Vector2 a = Vector2.Lerp(start, control, t);
            Vector2 b = Vector2.Lerp(control, end, t);
            return Vector2.Lerp(a, b, t);
        }

        public void ResetPresentation()
        {
            for (int i = 0; i < _pool.Length; i++) _pool[i].Alive = false;
            _spawnAccum = 0f;
            _spawnIndex = 0;
            _disconnectAt = float.NegativeInfinity;
            _wasEmitting = false;
            _lastPublished = controller != null ? controller.Bus.Published : 0;
            _acceptedPulseAt = float.NegativeInfinity;
            EmissionState = "off";
        }

        private float EntryLinkProgress()
        {
            if (shell == null || shell.EntryStage == ProductionEntryStage.Running)
                return 1f;
            if (shell.EntryStage == ProductionEntryStage.ConnectionDetected)
            {
                float phase = Mathf.Clamp01(shell.StageElapsed /
                    ProductionShellFlow.ConnectionDetectedSeconds);
                return Mathf.Clamp01((phase - 0.35f) / 0.65f);
            }
            if (shell.EntryStage == ProductionEntryStage.LinkEstablished ||
                shell.EntryStage == ProductionEntryStage.Opening) return 1f;
            return 0f;
        }

        private void AdvancePool(float now, float delta, bool emitting, bool active)
        {
            for (int i = 0; i < _pool.Length; i++)
                if (_pool[i].Alive &&
                    now - _pool[i].SpawnedAt >= TravelSeconds)
                    _pool[i].Alive = false;

            if (emitting && !_wasEmitting)
                _disconnectAt = float.NegativeInfinity;
            else if (!emitting && _wasEmitting)
                _disconnectAt = now;
            _wasEmitting = emitting;
            if (!emitting)
            {
                _spawnAccum = 0f;
                return;
            }

            _spawnAccum += Mathf.Max(0f, delta) * (active ? ActiveRate : NormalRate);
            int cap = active ? 44 : 32;
            while (_spawnAccum >= 1f && AliveParticleCount < cap)
            {
                _spawnAccum -= 1f;
                Spawn(now);
            }
        }

        private void Spawn(float now)
        {
            for (int i = 0; i < _pool.Length; i++)
            {
                if (_pool[i].Alive) continue;
                int lane = _spawnIndex % 5;
                _pool[i] = new Particle
                {
                    Alive = true,
                    SpawnedAt = now,
                    Offset = Mathf.Lerp(-2f, 2f, lane / 4f),
                    Big = _spawnIndex % BigEvery == 0
                };
                _spawnIndex++;
                return;
            }
        }

        private void DrawLink(Rect content, Vector2 start, Vector2 end,
            bool fragmenting, float fade)
        {
            float scale = ProductionHudLayout.Scale(Screen.width, Screen.height);
            Vector2 control = Vector2.Lerp(start, end, 0.5f) +
                              Vector2.up * (-8f * scale);
            if (teaching != null && teaching.IsActive)
            {
                Rect teachingRect = ProductionHudLayout.TeachingRect(
                    Screen.width, Screen.height);
                control.y = Mathf.Min(content.yMax - 10f * scale,
                    Mathf.Max(teachingRect.yMax + 8f * scale,
                        Mathf.Max(start.y, end.y) + 18f * scale));
            }
            Vector2 direction = end - start;
            Vector2 perpendicular = direction.sqrMagnitude > 0.001f
                ? new Vector2(-direction.y, direction.x).normalized
                : Vector2.up;
            Color linkColor = fragmenting
                ? ProductionHudLayout.Amber : ProductionHudLayout.Cyan;
            float baseAlpha = fragmenting ? 0.11f : 0.16f;
            DrawCurve(content, start, control, end,
                new Color(linkColor.r, linkColor.g, linkColor.b,
                    baseAlpha * fade), Mathf.Max(1f, scale));

            for (int i = 0; i < _pool.Length; i++)
            {
                if (!_pool[i].Alive) continue;
                float phase = Mathf.Clamp01(
                    (Time.unscaledTime - _pool[i].SpawnedAt) / TravelSeconds);
                Vector2 point = CurvePoint(start, control, end, phase);
                point += perpendicular * _pool[i].Offset * scale *
                         Mathf.Sin(phase * Mathf.PI);
                point.x = Mathf.Clamp(point.x, content.xMin, content.xMax);
                point.y = Mathf.Clamp(point.y, content.yMin, content.yMax);
                float alpha = Mathf.Sin(phase * Mathf.PI) *
                              (fragmenting ? 0.48f : 0.82f) * fade;
                float logicalSize = _pool[i].Big ? 3f : 2f;
                float size = logicalSize * scale;
                DrawRect(new Rect(point.x - size * 0.5f,
                    point.y - size * 0.5f, size, size),
                    new Color(linkColor.r, linkColor.g, linkColor.b, alpha));
            }
        }

        private void DrawCoreState(Vector2 corePoint)
        {
            float scale = ProductionHudLayout.Scale(Screen.width, Screen.height);
            float pulse = _acceptedPulseAt > float.NegativeInfinity
                ? Mathf.Clamp01(1f -
                    (Time.unscaledTime - _acceptedPulseAt) / 0.5f) : 0f;
            ProductionEnergyState state = recovery != null
                ? recovery.State : ProductionEnergyState.Connected;
            Color color = state == ProductionEnergyState.Fragmenting
                ? ProductionHudLayout.Amber
                : state == ProductionEnergyState.Depleted ||
                  state == ProductionEnergyState.Collapsed
                    ? ProductionHudLayout.Fault : ProductionHudLayout.Cyan;
            float alpha = state == ProductionEnergyState.Collapsed ? 0.08f
                : state == ProductionEnergyState.Depleted ? 0.18f
                : 0.28f + 0.42f * pulse;
            float size = (4f + 3f * pulse) * scale;
            DrawRect(new Rect(corePoint.x - size * 0.5f,
                corePoint.y - size * 0.5f, size, size),
                new Color(color.r, color.g, color.b, alpha));
        }

        private void DrawRecoveryMessage(Rect content)
        {
            if (recovery == null) return;
            string line = null;
            string instruction = null;
            if (recovery.State == ProductionEnergyState.Depleted ||
                recovery.State == ProductionEnergyState.Collapsed)
            {
                line = ProductionTextCatalog.EnergyLost;
                instruction = ProductionTextCatalog.EnergyLostAction;
            }
            else if (recovery.State == ProductionEnergyState.Recharging)
            {
                line = ProductionTextCatalog.EnergyRecharging;
                instruction = ProductionTextCatalog.EnergyRechargingAction;
            }
            if (line == null) return;

            EnsureStyles();
            float scale = ProductionHudLayout.Scale(Screen.width, Screen.height);
            Rect box = ProductionHudLayout.LogicalRect(
                new Rect(12f, 9f, 220f, 19f), Screen.width, Screen.height);
            Color previous = GUI.color;
            GUI.color = new Color(ProductionHudLayout.Structural.r,
                ProductionHudLayout.Structural.g,
                ProductionHudLayout.Structural.b, 0.90f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(box.x + 2f * scale, box.y,
                box.width - 4f * scale, 8f * scale), line, _message);
            GUI.Label(new Rect(box.x + 2f * scale, box.y + 8f * scale,
                box.width - 4f * scale, 8f * scale), instruction, _instruction);
            GUI.color = previous;
        }

        private void EnsureStyles()
        {
            float scale = ProductionHudLayout.Scale(Screen.width, Screen.height);
            int fontSize = Mathf.Max(12, Mathf.RoundToInt(4.5f * scale));
            if (_message != null && _message.fontSize == fontSize &&
                _message.font == publicFont) return;
            _message = new GUIStyle(GUI.skin.label)
            {
                font = publicFont,
                fontSize = fontSize,
                normal = { textColor = ProductionHudLayout.DirtyWhite }
            };
            _instruction = new GUIStyle(_message)
            {
                normal = { textColor = ProductionHudLayout.Cyan }
            };
        }

        private static void DrawCurve(Rect content, Vector2 start,
            Vector2 control, Vector2 end, Color color, float size)
        {
            const int segments = 24;
            Vector2 previous = start;
            for (int i = 1; i <= segments; i++)
            {
                Vector2 current = CurvePoint(start, control, end,
                    i / (float)segments);
                float length = Vector2.Distance(previous, current);
                int steps = Mathf.Max(1, Mathf.CeilToInt(length / size));
                for (int j = 0; j <= steps; j++)
                {
                    Vector2 point = Vector2.Lerp(previous, current,
                        j / (float)steps);
                    if (content.Contains(point))
                        DrawRect(new Rect(point.x - size * 0.5f,
                            point.y - size * 0.5f, size, size), color);
                }
                previous = current;
            }
        }

        private static void DrawRect(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }
    }
}
