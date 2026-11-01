using UnityEngine;

namespace Before.Production
{
    /// <summary>
    /// Final landmark-only public hand presentation for the production receive path.
    /// It can read normalized landmark points but has no camera RGB surface.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProductionDiagnosticsHud : MonoBehaviour
    {
        public const float CommandFlashSeconds = 0.25f;
        public const float PoseLockInset = 4f;
        public const float PoseLockArmLength = 8f;
        public const float PoseLockThickness = 2f;
        public const float PoseLockSnapDistance = 2f;
        public const float PoseLockAppearSeconds = 0.12f;

        private static readonly int[,] Connections =
        {
            {0,1},{1,2},{2,3},{3,4}, {0,5},{5,6},{6,7},{7,8},
            {0,9},{9,10},{10,11},{11,12}, {0,13},{13,14},{14,15},{15,16},
            {0,17},{17,18},{18,19},{19,20}, {5,9},{9,13},{13,17}
        };

        [SerializeField] private NulProductionController controller;
        [SerializeField] private ProductionTeachingFlow teaching;
        [SerializeField] private ProductionCourseLoop course;
        [SerializeField] private ProductionEnergyRecoveryFlow energyRecovery;
        [SerializeField] private ProductionEnergyLinkPresenter energyLink;
        [SerializeField] private ProductionEndingFlow ending;
        [SerializeField] private bool detailsVisible;
        [SerializeField] private Font publicFont;

        private GUIStyle _detailStyle;
        private ProductionGameLoop _gameLoop;
        private CommandKind _commandFlashKind = CommandKind.Unknown;
        private float _commandFlashUntil = float.NegativeInfinity;
        private ProductionReadinessKind _poseLockKind = ProductionReadinessKind.None;
        private float _poseLockAppearedAt = float.NegativeInfinity;

        public NulProductionController Controller
        {
            get => controller;
            set => controller = value;
        }

        public bool HasSkeleton => controller != null && controller.HasLandmarkFrame;
        public bool DetailsVisible => detailsVisible;
        public bool SuppressedForEnding => ending != null &&
            SuppressForEndingStage(ending.Stage);
        public Font PublicFont { get => publicFont; set { publicFont = value; _detailStyle = null; } }
        public int GuiRepaintCount { get; private set; }

        private void Awake()
        {
            if (controller == null) controller = GetComponent<NulProductionController>();
            if (teaching == null) teaching = GetComponent<ProductionTeachingFlow>();
            if (course == null) course = GetComponent<ProductionCourseLoop>();
            if (energyRecovery == null)
                energyRecovery = GetComponent<ProductionEnergyRecoveryFlow>();
            if (energyLink == null)
                energyLink = GetComponent<ProductionEnergyLinkPresenter>();
            if (ending == null) ending = GetComponent<ProductionEndingFlow>();
            _gameLoop = GetComponent<ProductionGameLoop>();
        }

        private void OnEnable()
        {
            ResetPresentationState();
            if (controller != null) controller.Bus.OnCommand += HandleCommandFlash;
            if (_gameLoop != null) _gameLoop.OnLoopReset += ResetPresentationState;
        }

        private void OnDisable()
        {
            if (controller != null) controller.Bus.OnCommand -= HandleCommandFlash;
            if (_gameLoop != null) _gameLoop.OnLoopReset -= ResetPresentationState;
        }

        private void Update()
        {
            UpdatePoseLockEvidence(Time.unscaledTime);
#if ENABLE_INPUT_SYSTEM
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null && keyboard.f1Key.wasPressedThisFrame)
                SetDetailsVisible(!detailsVisible);
#endif
        }

        public void SetDetailsVisible(bool value) => detailsVisible = value;

        public static bool SuppressForEndingStage(ProductionEndingStage stage)
            => stage >= ProductionEndingStage.WorldFade;

        public static Rect HandPanelRect(float screenWidth, float screenHeight)
            => ProductionHudLayout.HandPanelRect(screenWidth, screenHeight);

        private void OnGUI()
        {
            if (Event.current.type == EventType.Repaint) GuiRepaintCount++;
            // This component is earlier than the ending presenter in the saved
            // component list, so Unity's reverse OnGUI submission would otherwise
            // draw the always-on hand panel after the full-screen fade.
            if (controller == null || SuppressedForEnding) return;
            DrawSkeleton();
            if (!detailsVisible) return;
            EnsureDetailStyle();
            Rect content = ProductionHudLayout.ContentRect(Screen.width, Screen.height);
            float scale = ProductionHudLayout.Scale(Screen.width, Screen.height);
            GUI.color = Color.white;
            GUI.Label(new Rect(content.x + 2f * scale, content.y + 2f * scale,
                230f * scale, 7f * scale),
                "SYS// ProductionV2  udp:" + controller.udpPort + "  session:" +
                (controller.Router.Session.ActiveSession ?? "-") + "  stale-discarded:" +
                controller.Router.Session.DiscardedStale + "  sidecar:" + controller.SidecarStatus,
                _detailStyle);
            GUI.Label(new Rect(content.x + 2f * scale, content.y + 9f * scale,
                250f * scale, 7f * scale),
                "STATE " + controller.State + "   JUMP x" + controller.Nul.JumpCount + "   CROUCH " +
                (controller.Nul.Crouching
                    ? "ON " + controller.Nul.CrouchRemaining(controller.GameplayTime).ToString("0.00") + "s"
                    : "off") +
                "   STOP " + (controller.Nul.StopActive ? "ON" : "off") +
                "   DIR-mode " + controller.Nul.DirectionMode + " last:" +
                (controller.Nul.LastDirection < 0 ? "LEFT"
                    : controller.Nul.LastDirection > 0 ? "RIGHT" : "-") +
                (controller.Nul.Paused ? "   [PAUSED]" : ""), _detailStyle);
            GUI.Label(new Rect(content.x + 2f * scale, content.y + 16f * scale,
                315f * scale, 7f * scale),
                "TRACK tracked:" + controller.TrackedFresh +
                " q:" + controller.TrackingQuality.ToString("0.00") +
                " packet-age:" + FormatSeconds(controller.PacketAgeSeconds) +
                " stale:" + controller.LandmarkPacketStale +
                " loss:" + (energyRecovery != null
                    ? energyRecovery.LossSeconds.ToString("0.00") + "s" : "-") +
                " owner:" + controller.RecognitionOwner,
                _detailStyle);
            LandmarkPacket.StopStatus stop = controller.StopDiagnostics;
            GUI.Label(new Rect(content.x + 2f * scale, content.y + 23f * scale,
                315f * scale, 7f * scale),
                "STOP d:" + stop.PoseDistance.ToString("0.000") +
                " th:" + stop.Threshold.ToString("0.000") +
                " candidate:" + stop.CandidateStartedMs.ToString("0") + "ms" +
                " stable:" + stop.StableFrames + "/4 hold:" +
                stop.HoldMs.ToString("0") + "ms active:" + stop.Active +
                " reject:" + (string.IsNullOrEmpty(stop.RejectReason)
                    ? "-" : stop.RejectReason), _detailStyle);
            GUI.Label(new Rect(content.x + 2f * scale, content.y + 30f * scale,
                315f * scale, 7f * scale),
                "PIPE issue:" + controller.LastStopCommand.TimestampUs +
                " udp:" + controller.LastStopUdpReceivedUs +
                " state:" + controller.LastStopStateEnteredUs +
                " transport:" + FormatMilliseconds(controller.LastStopTransportMs) +
                " apply:" + FormatMilliseconds(controller.LastStopUnityApplyMs) +
                " off-issued:" + stop.StopOffIssuedMs.ToString("0") + "ms",
                _detailStyle);
            GUI.Label(new Rect(content.x + 2f * scale, content.y + 37f * scale,
                315f * scale, 7f * scale),
                "ENERGY " + (energyRecovery != null
                    ? energyRecovery.State.ToString() : "-") +
                " emission:" + (energyLink != null ? energyLink.EmissionState : "-") +
                " suspended:" + controller.EnergySuspended +
                " STOP-held:" + controller.Nul.StopActive +
                " recovery:" + (energyRecovery != null
                    ? energyRecovery.RecoveryTimer.ToString("0.00") + "s" : "-"),
                _detailStyle);
            GUI.Label(new Rect(content.x + 2f * scale, content.y + 44f * scale,
                315f * scale, 7f * scale),
                "WAIT safety:" + StopSafetyWait(controller, course) +
                "  keys: P pause  D direction-mode  F1 diagnostics  (RGB never shown)",
                _detailStyle);
        }

        private void DrawSkeleton()
        {
            Color previousColor = GUI.color;
            Rect panel = HandPanelRect(Screen.width, Screen.height);
            float scale = ProductionHudLayout.Scale(Screen.width, Screen.height);
            float border = Mathf.Max(1f, scale);
            Color panelStateColor = HandBorderColor(
                teaching != null && teaching.IsActive,
                controller.HandSignal,
                controller.Nul.DirectionMode,
                controller.Readiness,
                CommandFlashKindAt(Time.unscaledTime));
            GUI.color = panelStateColor;
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = ProductionHudLayout.VoidBlack;
            GUI.DrawTexture(new Rect(panel.x + border, panel.y + border,
                    panel.width - border * 2f, panel.height - border * 2f),
                Texture2D.whiteTexture);
            if (!HasSkeleton)
            {
                GUI.color = previousColor;
                return;
            }

            Vector2 PanelPoint(int index)
            {
                controller.TryGetLandmark(index, out Vector2 normalized);
                return new Vector2(panel.x + 3f * scale + normalized.x * (panel.width - 6f * scale),
                    panel.y + 3f * scale + normalized.y * (panel.height - 6f * scale));
            }

            for (int i = 0; i < Connections.GetLength(0); i++)
                DrawLine(PanelPoint(Connections[i, 0]), PanelPoint(Connections[i, 1]),
                    ProductionHudLayout.DirtyWhite, Mathf.Max(1f, scale));
            for (int i = 0; i < 21; i++)
            {
                Vector2 point = PanelPoint(i);
                bool signal = i == 0 || i == 4 || i == 8 || i == 12 || i == 16 || i == 20;
                GUI.color = signal ? ProductionHudLayout.Cyan : ProductionHudLayout.DirtyWhite;
                float size = (signal ? 2f : 1f) * scale;
                GUI.DrawTexture(new Rect(point.x - size * 0.5f, point.y - size * 0.5f,
                    size, size), Texture2D.whiteTexture);
            }

            Vector2 palm = (PanelPoint(0) + PanelPoint(5) + PanelPoint(9) +
                            PanelPoint(13) + PanelPoint(17)) / 5f;
            GUI.color = ProductionHudLayout.Cyan;
            GUI.DrawTexture(new Rect(palm.x - scale, palm.y - scale,
                2f * scale, 2f * scale), Texture2D.whiteTexture);
            if (ShouldDrawPoseLock(
                    _poseLockKind,
                    controller.HandSignal,
                    teaching != null && teaching.IsActive,
                    controller.Nul.DirectionMode,
                    CommandFlashKindAt(Time.unscaledTime)))
            {
                GUI.color = PoseLockColor(_poseLockKind);
                float outward = PoseLockSnapOffset(
                    Time.unscaledTime - _poseLockAppearedAt);
                Rect[] brackets = PoseLockRects(panel, scale, outward);
                for (int i = 0; i < brackets.Length; i++)
                    GUI.DrawTexture(brackets[i], Texture2D.whiteTexture);
            }
            GUI.color = previousColor;
        }

        public static Color HandBorderColor(bool teachingActive,
            ProductionHandSignal signal, bool directionMode,
            ProductionReadinessKind readiness,
            CommandKind commandFlash = CommandKind.Unknown)
        {
            if (signal == ProductionHandSignal.TrackingLost)
                return ProductionHudLayout.Fault;
            if (signal == ProductionHandSignal.LowQuality ||
                signal == ProductionHandSignal.OutOfFrame)
                return ProductionHudLayout.Amber;
            if (teachingActive) return ProductionHudLayout.DirtyWhite;
            if (directionMode) return ProductionHudLayout.Violet;
            switch (commandFlash)
            {
                case CommandKind.Jump:
                    return ProductionFeedbackHud.CommandColorFor(
                        ProductionObstacleKind.Jump);
                case CommandKind.Crouch:
                    return ProductionFeedbackHud.CommandColorFor(
                        ProductionObstacleKind.Crouch);
                case CommandKind.StopOn:
                case CommandKind.StopOff:
                    return ProductionFeedbackHud.CommandColorFor(
                        ProductionObstacleKind.Stop);
                default:
                    // Readiness is intentionally colorless here. In particular,
                    // Crouch teaching/readiness must not look like a fired command.
                    return ProductionHudLayout.DirtyWhite;
            }
        }

        public CommandKind CommandFlashKindAt(float unscaledTime)
            => unscaledTime < _commandFlashUntil
                ? _commandFlashKind : CommandKind.Unknown;

        private void HandleCommandFlash(CommandPacket packet)
        {
            switch (packet.Kind)
            {
                case CommandKind.Jump:
                case CommandKind.Crouch:
                case CommandKind.StopOn:
                case CommandKind.StopOff:
                    _commandFlashKind = packet.Kind;
                    _commandFlashUntil = Time.unscaledTime + CommandFlashSeconds;
                    if (packet.Kind == CommandKind.StopOff)
                        ResetPoseLock();
                    break;
            }
        }

        private void UpdatePoseLockEvidence(float unscaledTime)
        {
            if (controller == null)
            {
                ResetPoseLock();
                return;
            }
            ProductionReadinessKind next = ResolvePoseLockKind(
                teaching != null && teaching.IsActive,
                controller.HandSignal,
                controller.Nul.DirectionMode,
                controller.Readiness,
                controller.Nul.StopActive);
            if (ShouldRestartPoseLock(_poseLockKind, next))
                _poseLockAppearedAt = unscaledTime;
            _poseLockKind = next;
            if (next == ProductionReadinessKind.None)
                _poseLockAppearedAt = float.NegativeInfinity;
        }

        public static ProductionReadinessKind ResolvePoseLockKind(
            bool teachingActive, ProductionHandSignal signal, bool directionMode,
            ProductionReadinessKind readiness, bool stopActive)
        {
            if (teachingActive || directionMode || signal != ProductionHandSignal.Ok)
                return ProductionReadinessKind.None;
            if (stopActive) return ProductionReadinessKind.Stop;
            return readiness;
        }

        public static bool ShouldDrawPoseLock(ProductionReadinessKind kind,
            ProductionHandSignal signal, bool teachingActive, bool directionMode,
            CommandKind commandFlash)
            => kind != ProductionReadinessKind.None &&
               signal == ProductionHandSignal.Ok &&
               !teachingActive && !directionMode &&
               commandFlash == CommandKind.Unknown;

        public static bool ShouldRestartPoseLock(
            ProductionReadinessKind previous, ProductionReadinessKind next)
            => next != ProductionReadinessKind.None && next != previous;

        public static float PoseLockSnapOffset(float elapsed)
        {
            if (elapsed < PoseLockAppearSeconds / 3f) return PoseLockSnapDistance;
            if (elapsed < PoseLockAppearSeconds * 2f / 3f)
                return PoseLockSnapDistance * 0.5f;
            return 0f;
        }

        public static Color PoseLockColor(ProductionReadinessKind kind)
        {
            switch (kind)
            {
                case ProductionReadinessKind.Jump:
                    return ProductionFeedbackHud.CommandColorFor(
                        ProductionObstacleKind.Jump);
                case ProductionReadinessKind.Crouch:
                    return ProductionFeedbackHud.CommandColorFor(
                        ProductionObstacleKind.Crouch);
                case ProductionReadinessKind.Stop:
                    return ProductionFeedbackHud.CommandColorFor(
                        ProductionObstacleKind.Stop);
                default:
                    return ProductionHudLayout.DirtyWhite;
            }
        }

        public static Rect[] PoseLockRects(Rect panel, float scale, float outward)
        {
            float inset = (PoseLockInset - Mathf.Clamp(
                outward, 0f, PoseLockSnapDistance)) * scale;
            float arm = PoseLockArmLength * scale;
            float thick = PoseLockThickness * scale;
            float left = Mathf.Round(panel.x + inset);
            float right = Mathf.Round(panel.xMax - inset);
            float top = Mathf.Round(panel.y + inset);
            float bottom = Mathf.Round(panel.yMax - inset);
            arm = Mathf.Round(arm);
            thick = Mathf.Round(thick);
            return new[]
            {
                new Rect(left, top, arm, thick),
                new Rect(left, top, thick, arm),
                new Rect(right - arm, top, arm, thick),
                new Rect(right - thick, top, thick, arm),
                new Rect(left, bottom - thick, arm, thick),
                new Rect(left, bottom - arm, thick, arm),
                new Rect(right - arm, bottom - thick, arm, thick),
                new Rect(right - thick, bottom - arm, thick, arm)
            };
        }

        private void ResetPresentationState()
        {
            ResetCommandFlash();
            ResetPoseLock();
        }

        private void ResetPoseLock()
        {
            _poseLockKind = ProductionReadinessKind.None;
            _poseLockAppearedAt = float.NegativeInfinity;
        }

        private void ResetCommandFlash()
        {
            _commandFlashKind = CommandKind.Unknown;
            _commandFlashUntil = float.NegativeInfinity;
        }

        private void EnsureDetailStyle()
        {
            float scale = ProductionHudLayout.Scale(Screen.width, Screen.height);
            int size = Mathf.Max(10, Mathf.RoundToInt(3f * scale));
            if (_detailStyle != null && _detailStyle.fontSize == size &&
                _detailStyle.font == publicFont) return;
            _detailStyle = new GUIStyle(GUI.skin.label)
            {
                font = publicFont,
                fontSize = size,
                normal = { textColor = ProductionHudLayout.DirtyWhite }
            };
        }

        private static void DrawLine(Vector2 a, Vector2 b, Color color, float width)
        {
            Matrix4x4 previousMatrix = GUI.matrix;
            Color previousColor = GUI.color;
            GUI.color = color;
            float angle = Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg;
            float length = Vector2.Distance(a, b);
            GUIUtility.RotateAroundPivot(angle, a);
            GUI.DrawTexture(new Rect(a.x, a.y - width * 0.5f, length, width), Texture2D.whiteTexture);
            GUI.matrix = previousMatrix;
            GUI.color = previousColor;
        }

        public static bool StopSafetyWait(NulProductionController source,
            ProductionCourseLoop activeCourse)
        {
            if (source == null || activeCourse == null || !source.Nul.StopActive)
                return false;
            ProductionCourseLoop.ObstacleSlot[] slots = activeCourse.Slots;
            for (int i = 0; i < slots.Length; i++)
            {
                float lead = slots[i].WorldX - source.transform.position.x;
                if (slots[i].Kind == ProductionObstacleKind.Stop &&
                    lead > 0f && lead <= ProductionObstacleJudge.PreparationLead)
                    return true;
            }
            return false;
        }

        private static string FormatSeconds(float seconds)
            => float.IsInfinity(seconds) ? "inf" : seconds.ToString("0.000") + "s";

        private static string FormatMilliseconds(float milliseconds)
            => milliseconds < 0f ? "-" : milliseconds.ToString("0.0") + "ms";
    }
}
