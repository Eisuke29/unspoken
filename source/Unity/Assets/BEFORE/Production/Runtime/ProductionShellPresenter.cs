using UnityEngine;

namespace Before.Production
{
    /// <summary>Final presentation for title, connection and exit confirmation.</summary>
    [DisallowMultipleComponent]
    public sealed class ProductionShellPresenter : MonoBehaviour
    {
        public const string TitleLine = ProductionTextCatalog.BrandFallback;
        public const string TitleInstruction = ProductionTextCatalog.TitleInstruction;
        public const string ConnectionLine = ProductionTextCatalog.EnergyConnecting;
        public const string ConnectionInstruction =
            ProductionTextCatalog.EnergyConnectingAction;
        public const string LinkedLine = ProductionTextCatalog.LinkEstablished;
        public const string LinkedInstruction =
            ProductionTextCatalog.LinkEstablishedAction;
        public const string ExitLine = ProductionTextCatalog.Exit;
        public const string ExitInstruction = ProductionTextCatalog.ExitAction;

        [SerializeField] private ProductionShellFlow flow;
        [SerializeField] private Font publicFont;
        [SerializeField] private Texture2D titleLogo;

        private GUIStyle _titleStyle;
        private GUIStyle _lineStyle;
        private GUIStyle _instructionStyle;

        public ProductionShellFlow Flow { get => flow; set => flow = value; }
        public bool ComposedExternally { get; set; }
        public Font PublicFont
        {
            get => publicFont;
            set { publicFont = value; ClearStyles(); }
        }
        public Texture2D TitleLogo
        {
            get => titleLogo;
            set => titleLogo = value;
        }
        public int GuiRepaintCount { get; private set; }

        private void Awake()
        {
            if (flow == null) flow = GetComponent<ProductionShellFlow>();
        }

        public static Rect TitleBounds(float width, float height)
            => ProductionHudLayout.LogicalRect(new Rect(20f, 32f, 224f, 86f),
                width, height);

        public static Rect ExitBounds(float width, float height)
            => ProductionHudLayout.LogicalRect(new Rect(36f, 66f, 184f, 40f),
                width, height);

        private void EnsureStyles(float scale)
        {
            int lineSize = Mathf.Max(12, Mathf.RoundToInt(5f * scale));
            if (_lineStyle != null && _lineStyle.fontSize == lineSize &&
                _lineStyle.font == publicFont) return;
            _lineStyle = new GUIStyle(GUI.skin.label)
            {
                font = publicFont,
                fontSize = lineSize,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = ProductionHudLayout.DirtyWhite }
            };
            _instructionStyle = new GUIStyle(_lineStyle)
            {
                normal = { textColor = ProductionHudLayout.Cyan }
            };
            _titleStyle = new GUIStyle(_lineStyle)
            {
                fontSize = Mathf.Max(24, Mathf.RoundToInt(14f * scale)),
                fontStyle = FontStyle.Bold
            };
        }

        private void OnGUI()
        {
            if (Event.current.type == EventType.Repaint) GuiRepaintCount++;
            if (ComposedExternally) return;
            DrawOverlay();
        }

        public void DrawOverlay()
        {
            if (flow == null) return;
            float scale = ProductionHudLayout.Scale(Screen.width, Screen.height);
            EnsureStyles(scale);
            int previousDepth = GUI.depth;
            GUI.depth = -500;

            if (flow.BlocksGameplayPresentation)
                DrawEntry(scale);
            if (flow.ExitConfirmationVisible)
                DrawExit(scale);
            GUI.depth = previousDepth;
        }

        private void DrawEntry(float scale)
        {
            Rect content = ProductionHudLayout.ContentRect(Screen.width, Screen.height);
            Color previous = GUI.color;
            GUI.color = ProductionHudLayout.VoidBlack;
            GUI.DrawTexture(content, Texture2D.whiteTexture);
            GUI.color = Color.white;

            Rect box = TitleBounds(Screen.width, Screen.height);
            if (flow.EntryStage == ProductionEntryStage.Title)
            {
                if (titleLogo != null)
                {
                    GUI.DrawTexture(box, titleLogo, ScaleMode.ScaleToFit, true);
                }
                else
                {
                    GUI.Label(new Rect(box.x, box.y, box.width, box.height),
                        TitleLine, _titleStyle);
                }
                if (flow.CameraFaultVisible)
                {
                    GUI.Label(new Rect(box.x, box.yMax + 3f * scale, box.width,
                        8f * scale), ProductionTextCatalog.CameraUnavailable,
                        _lineStyle);
                    GUI.Label(new Rect(box.x, box.yMax + 11f * scale, box.width,
                        8f * scale), ProductionTextCatalog.CameraUnavailableAction,
                        _instructionStyle);
                }
                else
                {
                    GUI.Label(new Rect(box.x, box.yMax + 5f * scale, box.width,
                        8f * scale), TitleInstruction, _instructionStyle);
                }
            }
            else if (flow.EntryStage == ProductionEntryStage.ConnectionDetected)
            {
                DrawTwoLines(box, scale, ConnectionLine, ConnectionInstruction);
            }
            else if (flow.EntryStage == ProductionEntryStage.LinkEstablished ||
                     flow.EntryStage == ProductionEntryStage.Opening)
            {
                DrawTwoLines(box, scale, LinkedLine, LinkedInstruction);
            }
            GUI.color = previous;
        }

        private void DrawExit(float scale)
        {
            Rect box = ExitBounds(Screen.width, Screen.height);
            Color previous = GUI.color;
            GUI.color = ProductionHudLayout.Structural;
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = ProductionHudLayout.DirtyWhite;
            DrawFrame(box, Mathf.Max(1f, scale));
            GUI.color = Color.white;
            DrawTwoLines(box, scale, ExitLine, ExitInstruction);
            GUI.color = previous;
        }

        private void DrawTwoLines(Rect box, float scale, string line,
            string instruction)
        {
            GUI.Label(new Rect(box.x, box.y + 7f * scale, box.width,
                9f * scale), line, _lineStyle);
            GUI.Label(new Rect(box.x, box.y + 20f * scale, box.width,
                8f * scale), instruction, _instructionStyle);
        }

        private static void DrawFrame(Rect rect, float thickness)
        {
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, thickness),
                Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - thickness, rect.width,
                thickness), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.x, rect.y, thickness, rect.height),
                Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.xMax - thickness, rect.y, thickness,
                rect.height), Texture2D.whiteTexture);
        }

        private void ClearStyles()
        {
            _titleStyle = null;
            _lineStyle = null;
            _instructionStyle = null;
        }
    }
}
