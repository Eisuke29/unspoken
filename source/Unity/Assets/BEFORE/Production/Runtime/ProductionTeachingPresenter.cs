using UnityEngine;

namespace Before.Production
{
    /// <summary>
    /// Final teaching HUD presentation. It reads flow state only.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProductionTeachingPresenter : MonoBehaviour
    {
        [SerializeField] private ProductionTeachingFlow flow;
        [SerializeField] private Font publicFont;

        private GUIStyle _context;
        private GUIStyle _purpose;
        private GUIStyle _target;
        private GUIStyle _operation;
        private GUIStyle _auxiliary;
        private GUIStyle _rejection;
        private GUIStyle _hint;

        public const string ResetHint = ProductionTextCatalog.ResetHint;

        public ProductionTeachingFlow Flow { get => flow; set => flow = value; }
        public Font PublicFont { get => publicFont; set { publicFont = value; ClearStyles(); } }
        public bool Visible => flow != null && flow.IsActive &&
                               flow.Controller != null &&
                               !flow.Controller.Nul.Paused &&
                               !flow.Controller.EnergySuspended;

        private void Awake()
        {
            if (flow == null) flow = GetComponent<ProductionTeachingFlow>();
        }

        public static string HeadlineFor(ProductionObstacleKind kind)
        {
            return ProductionTextCatalog.TeachingUnknown;
        }

        public static string InstructionFor(ProductionObstacleKind kind)
        {
            return ProductionTextCatalog.TeachingInstructionFor(kind);
        }

        public static string TargetLabelFor(ProductionObstacleKind kind)
        {
            return ProductionTextCatalog.TeachingTargetFor(kind);
        }

        public static string ConfirmationFor(ProductionObstacleKind kind)
        {
            return ProductionTextCatalog.TeachingConfirmationFor(kind);
        }

        public string CurrentHeadline => CurrentOperation;

        public string CurrentOperation
        {
            get
            {
                if (flow == null) return string.Empty;
                if (flow.Stage == ProductionTeachingStage.Confirming ||
                    flow.Stage == ProductionTeachingStage.RepairConfirming)
                    return ProductionTextCatalog.Recorded(
                        flow.RequiredSamples, flow.RequiredSamples);
                switch (flow.CapturePhase)
                {
                    case "moving":
                        return flow.TargetKind == ProductionObstacleKind.Stop
                            ? ProductionTextCatalog.ReleaseToRecord
                            : ProductionTextCatalog.MoveWithShape;
                    case "release": return ProductionTextCatalog.ReleaseToRecord;
                    case "accepted":
                        return ProductionTextCatalog.Recorded(
                            flow.AcceptedSamples, flow.RequiredSamples);
                    default:
                        return ProductionTextCatalog.HoldShape;
                }
            }
        }

        public string CurrentInstruction
        {
            get
            {
                if (flow == null) return string.Empty;
                return flow.LastSampleRejected
                    ? JapaneseRejectionFor(flow.LastRejectionReason)
                    : string.Empty;
            }
        }

        public string CurrentStatus => string.Empty;

        public string CurrentTargetProgress => flow == null
            ? string.Empty
            : TargetLabelFor(flow.TargetKind) + "　" + flow.AcceptedSamples + "/" +
              flow.RequiredSamples;

        public string CurrentRepairStatus => flow != null && flow.IsRepair
            ? ProductionTextCatalog.RepairOnce
            : string.Empty;

        public string CurrentResetNotice => flow == null ? string.Empty : flow.ResetNotice;
        public int ProgressMarkerCount => flow != null && flow.IsRepair ? 1 : 3;

        public static string JapaneseRejectionFor(string reason)
        {
            return ProductionTextCatalog.RejectionFor(reason);
        }

        public static Rect WorldRegionRect(float screenWidth, float screenHeight)
            => ProductionHudLayout.WorldRegionRect(screenWidth, screenHeight);

        public static Rect TeachingPanelRect(float screenWidth, float screenHeight)
            => ProductionHudLayout.TeachingRect(screenWidth, screenHeight);

        private void EnsureStyles(float scale)
        {
            int smallSize = Mathf.Max(11, Mathf.RoundToInt(3.8f * scale));
            int bodySize = Mathf.Max(13, Mathf.RoundToInt(4.6f * scale));
            int operationSize = Mathf.Max(14, Mathf.RoundToInt(5.2f * scale));
            if (_operation != null && _operation.fontSize == operationSize &&
                _operation.font == publicFont) return;

            _context = new GUIStyle(GUI.skin.label)
            {
                font = publicFont,
                fontSize = smallSize,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                wordWrap = false,
                normal = { textColor = ProductionHudLayout.Cyan }
            };
            _purpose = new GUIStyle(_context)
            {
                fontSize = bodySize,
                fontStyle = FontStyle.Normal,
                normal = { textColor = ProductionHudLayout.DirtyWhite }
            };
            _target = new GUIStyle(_purpose)
            {
                fontStyle = FontStyle.Bold,
                normal = { textColor = ProductionHudLayout.Amber }
            };
            _operation = new GUIStyle(_target)
            {
                fontSize = operationSize,
                normal = { textColor = ProductionHudLayout.DirtyWhite }
            };
            _auxiliary = new GUIStyle(_context)
            {
                normal = { textColor = ProductionHudLayout.Violet }
            };
            _rejection = new GUIStyle(_context)
            {
                normal = { textColor = ProductionHudLayout.Fault }
            };
            _hint = new GUIStyle(_context)
            {
                fontSize = Mathf.Max(10, Mathf.RoundToInt(3.4f * scale)),
                fontStyle = FontStyle.Normal,
                normal = { textColor = ProductionHudLayout.Ash }
            };
        }

        private void OnGUI()
        {
            if (!Visible) return;
            float scale = ProductionHudLayout.Scale(Screen.width, Screen.height);
            EnsureStyles(scale);
            int previousDepth = GUI.depth;
            Color previousColor = GUI.color;
            GUI.depth = -80;

            Rect world = WorldRegionRect(Screen.width, Screen.height);
            GUI.color = new Color(0.05f, 0.07f, 0.09f, 0.82f);
            GUI.DrawTexture(world, Texture2D.whiteTexture);

            Rect panel = TeachingPanelRect(Screen.width, Screen.height);
            GUI.color = new Color(ProductionHudLayout.Structural.r,
                ProductionHudLayout.Structural.g, ProductionHudLayout.Structural.b, 0.94f);
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            DrawFrame(panel, ProductionHudLayout.Cyan, Mathf.Max(1f, scale));

            float x = panel.x + 6f * scale;
            float width = panel.width - 12f * scale;
            GUI.color = Color.white;
            GUI.Label(new Rect(x, panel.y + 4f * scale, width, 8f * scale),
                HeadlineFor(flow.TargetKind), _context);
            GUI.Label(new Rect(x, panel.y + 14f * scale, width, 9f * scale),
                InstructionFor(flow.TargetKind), _purpose);
            GUI.Label(new Rect(x, panel.y + 27f * scale, width, 9f * scale),
                CurrentTargetProgress, _target);
            DrawProgressMarkers(panel, scale);
            GUI.Label(new Rect(x, panel.y + 45f * scale, width, 11f * scale),
                CurrentOperation, _operation);

            string auxiliary = !string.IsNullOrEmpty(CurrentResetNotice)
                ? CurrentResetNotice : CurrentRepairStatus;
            if (!string.IsNullOrEmpty(auxiliary))
                GUI.Label(new Rect(x, panel.y + 56f * scale, width, 7f * scale),
                    auxiliary, _auxiliary);
            if (flow.LastSampleRejected)
                GUI.Label(new Rect(x, panel.y + 63f * scale, width, 7f * scale),
                    CurrentInstruction, _rejection);
            GUI.Label(new Rect(x, panel.y + 70f * scale, width, 7f * scale),
                ResetHint, _hint);

            GUI.color = previousColor;
            GUI.depth = previousDepth;
        }

        private void DrawProgressMarkers(Rect panel, float scale)
        {
            int markerCount = ProgressMarkerCount;
            float markerWidth = 18f * scale;
            float markerGap = 3f * scale;
            float startX = panel.x + 6f * scale;
            float y = panel.y + 38f * scale;
            float height = 3f * scale;
            int accepted = Mathf.Clamp(flow.AcceptedSamples, 0, flow.RequiredSamples);

            for (int i = 0; i < markerCount; i++)
            {
                bool complete = flow.IsRepair ? accepted > 0 && i == 0 : i < accepted;
                bool current = flow.IsRepair ? accepted == 0 && i == 0
                                             : i == accepted && accepted < flow.RequiredSamples;
                GUI.color = complete ? ProductionHudLayout.Cyan
                    : current ? (flow.IsRepair ? ProductionHudLayout.Violet
                                               : ProductionHudLayout.Amber)
                    : ProductionHudLayout.MidGray;
                GUI.DrawTexture(new Rect(startX + i * (markerWidth + markerGap), y,
                    markerWidth, height), Texture2D.whiteTexture);
            }
            GUI.color = Color.white;
        }

        private static void DrawFrame(Rect rect, Color color, float thickness)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, thickness),
                Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness),
                Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.x, rect.y, thickness, rect.height),
                Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height),
                Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private void ClearStyles()
        {
            _context = null;
            _purpose = null;
            _target = null;
            _operation = null;
            _auxiliary = null;
            _rejection = null;
            _hint = null;
        }
    }
}
