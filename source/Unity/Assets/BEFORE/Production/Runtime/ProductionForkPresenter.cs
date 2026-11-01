using UnityEngine;

namespace Before.Production
{
    /// <summary>
    /// Final HUD layer for ForkMap. It reads ProductionForkFlow only.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProductionForkPresenter : MonoBehaviour
    {
        public const string Headline = ProductionTextCatalog.ForkHeadline;
        public const string Instruction = ProductionTextCatalog.ForkInstruction;
        public const string LeftReceived = ProductionTextCatalog.LeftReceived;
        public const string LeftProceed = ProductionTextCatalog.LeftProceed;
        public const string RightReceived = ProductionTextCatalog.RightReceived;
        public const string RightProceed = ProductionTextCatalog.RightProceed;

        [SerializeField] private ProductionForkFlow flow;
        [SerializeField] private Font publicFont;

        private GUIStyle _headline;
        private GUIStyle _instruction;
        private GUIStyle _route;

        public ProductionForkFlow Flow { get => flow; set => flow = value; }
        public Font PublicFont { get => publicFont; set { publicFont = value; ClearStyles(); } }
        public bool Visible => flow != null && flow.IsActive &&
                               flow.Controller != null && !flow.Controller.Nul.Paused;

        private void Awake()
        {
            if (flow == null) flow = GetComponent<ProductionForkFlow>();
        }

        private void EnsureStyles(float scale)
        {
            int size = Mathf.Max(13, Mathf.RoundToInt(5f * scale));
            if (_headline != null && _headline.fontSize == size) return;
            _headline = new GUIStyle(GUI.skin.label)
            {
                font = publicFont,
                fontSize = size,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = ProductionHudLayout.DirtyWhite }
            };
            _instruction = new GUIStyle(_headline)
            {
                fontStyle = FontStyle.Normal,
                normal = { textColor = ProductionHudLayout.Cyan }
            };
            _route = new GUIStyle(_headline)
            {
                normal = { textColor = ProductionHudLayout.Violet }
            };
        }

        public static Rect WorldRegionRect(float screenWidth, float screenHeight)
            => ProductionHudLayout.WorldRegionRect(screenWidth, screenHeight);

        public static Rect TargetWindowRect(float screenWidth, float screenHeight)
            => ProductionHudLayout.ForkRect(screenWidth, screenHeight);

        private void OnGUI()
        {
            if (!Visible) return;
            float scale = ProductionHudLayout.Scale(Screen.width, Screen.height);
            EnsureStyles(scale);

            int previousDepth = GUI.depth;
            Color previousColor = GUI.color;
            GUI.depth = -85;

            Rect world = WorldRegionRect(Screen.width, Screen.height);
            GUI.color = new Color(0f, 0f, 0f, 0.68f);
            GUI.DrawTexture(world, Texture2D.whiteTexture);

            Rect target = TargetWindowRect(Screen.width, Screen.height);
            Vector2 origin = CoreScreenPoint(world);
            float progress = flow.WindowProgress;
            Rect window = LerpRect(new Rect(origin.x, origin.y, 1f, 1f), target, progress);

            GUI.color = new Color(0.015f, 0.025f, 0.045f, 0.96f);
            GUI.DrawTexture(window, Texture2D.whiteTexture);
            DrawBorder(window, Mathf.Max(1f, scale), new Color(0.35f, 0.38f, 0.42f));
            if (window.width > 12f * scale && window.height > 12f * scale)
            {
                Rect inner = new Rect(window.x + 3f * scale, window.y + 3f * scale,
                    window.width - 6f * scale, window.height - 6f * scale);
                DrawBorder(inner, Mathf.Max(1f, scale),
                    new Color(ProductionHudLayout.Violet.r,
                        ProductionHudLayout.Violet.g,
                        ProductionHudLayout.Violet.b, 0.28f * progress));
            }

            if (progress > 0.2f && window.width > 64f * scale)
            {
                int previewDirection = flow.Stage == ProductionForkStage.Confirmed
                    ? 0 : flow.Controller.DirectionPreview;
                float holdProgress = previewDirection == 0
                    ? 0f : flow.Controller.DirectionHoldProgress;
                DrawMap(window, scale, previewDirection, holdProgress);
                GUI.color = Color.white;
                string headline = flow.Stage == ProductionForkStage.Confirmed
                    ? ReceivedCopyFor(flow.ChosenRoute) : Headline;
                string instruction = flow.Stage == ProductionForkStage.Confirmed
                    ? ProceedCopyFor(flow.ChosenRoute) : Instruction;
                GUI.Label(new Rect(window.x, window.y + 5f * scale,
                    window.width, 12f * scale), headline, _headline);
                GUI.Label(new Rect(window.x, window.y + 19f * scale,
                    window.width, 10f * scale), instruction, _instruction);
                GUI.color = new Color(0.65f, 0.7f, 0.75f, 0.08f * progress);
                for (int i = 0; i < 8; i++)
                {
                    float y = window.y + (10f + i * 11f) * scale;
                    GUI.DrawTexture(new Rect(window.x + 4f * scale, y,
                        window.width - 8f * scale, Mathf.Max(1f, scale)),
                        Texture2D.whiteTexture);
                }
            }

            GUI.color = previousColor;
            GUI.depth = previousDepth;
        }

        private Vector2 CoreScreenPoint(Rect worldRegion)
        {
            Camera camera = Camera.main;
            if (camera != null && flow != null && flow.Controller != null)
            {
                Vector3 world = flow.Controller.transform.position +
                                Vector3.up * (16f * NulProductionController.VisualScale);
                Vector3 viewport = camera.WorldToViewportPoint(world);
                return new Vector2(
                    Mathf.Clamp(worldRegion.x + viewport.x *
                        ProductionHudLayout.ContentRect(Screen.width, Screen.height).width,
                        worldRegion.xMin, worldRegion.xMax),
                    Mathf.Clamp(worldRegion.y + (1f - viewport.y) *
                        ProductionHudLayout.ContentRect(Screen.width, Screen.height).height,
                        worldRegion.yMin, worldRegion.yMax));
            }
            return new Vector2(worldRegion.center.x,
                worldRegion.y + worldRegion.height * 0.65f);
        }

        private void DrawMap(Rect window, float scale, int previewDirection,
            float holdProgress)
        {
            Color baseLine = new Color(0.48f, 0.52f, 0.56f, 0.82f);
            Color dimLine = new Color(baseLine.r, baseLine.g, baseLine.b, 0.14f);
            float thickness = Mathf.Max(1f, 2f * scale);
            float centerX = window.center.x;
            float branchY = window.y + window.height * 0.56f;
            float leftX = window.x + window.width * 0.22f;
            float rightX = window.x + window.width * 0.78f;
            ProductionRoute previewRoute = PreviewRouteForDirection(previewDirection);
            bool confirmed = flow.Stage == ProductionForkStage.Confirmed;
            float previewPulse = 0.50f + 0.24f *
                Mathf.Sin(Time.unscaledTime * Mathf.PI * 5f);
            Color previewColor = WithAlpha(ProductionHudLayout.Violet,
                Mathf.Clamp01(previewPulse));
            Color confirmedColor = WithAlpha(ProductionHudLayout.Violet, 1f);
            Color leftColor = confirmed
                ? flow.ChosenRoute == ProductionRoute.Left ? confirmedColor : dimLine
                : previewRoute == ProductionRoute.Left ? previewColor : baseLine;
            Color rightColor = confirmed
                ? flow.ChosenRoute == ProductionRoute.Right ? confirmedColor : dimLine
                : previewRoute == ProductionRoute.Right ? previewColor : baseLine;

            DrawRect(new Rect(centerX - thickness * 0.5f, branchY,
                thickness, window.height * 0.18f), baseLine);
            DrawRect(new Rect(leftX, branchY, centerX - leftX, thickness),
                leftColor);
            DrawRect(new Rect(centerX, branchY, rightX - centerX, thickness),
                rightColor);
            DrawRect(new Rect(leftX, branchY - window.height * 0.18f,
                thickness, window.height * 0.18f),
                leftColor);
            DrawRect(new Rect(rightX - thickness, branchY - window.height * 0.18f,
                thickness, window.height * 0.18f),
                rightColor);
            DrawRect(new Rect(centerX - 3f * scale, branchY - 3f * scale,
                6f * scale, 6f * scale), Color.white);
            DrawNode(leftX, branchY - window.height * 0.18f, scale, leftColor);
            DrawNode(rightX, branchY - window.height * 0.18f, scale, rightColor);

            if (!confirmed && previewRoute != ProductionRoute.None)
            {
                float nodeX = previewRoute == ProductionRoute.Left ? leftX : rightX;
                DrawHoldIndicator(nodeX, branchY - window.height * 0.18f,
                    scale, holdProgress, previewColor);
            }
            else if (confirmed && flow.ChosenRoute != ProductionRoute.None)
            {
                float nodeX = flow.ChosenRoute == ProductionRoute.Left ? leftX : rightX;
                DrawConfirmationPulse(nodeX,
                    branchY - window.height * 0.18f, scale, flow.StageElapsed);
            }

            for (int i = 0; i < 3; i++)
            {
                float offset = (12f + i * 10f) * scale;
                DrawRect(new Rect(centerX - offset - 5f * scale,
                    branchY - 5f * scale, 5f * scale, thickness), leftColor);
                DrawRect(new Rect(centerX + offset,
                    branchY - 5f * scale, 5f * scale, thickness), rightColor);
            }
        }

        public static Color RouteColorFor(ProductionRoute route)
            => route == ProductionRoute.Left || route == ProductionRoute.Right
                ? ProductionHudLayout.Violet
                : ProductionHudLayout.Ash;

        public static ProductionRoute PreviewRouteForDirection(int direction)
            => ProductionCourseLoop.RouteForDirection(direction);

        public static string ReceivedCopyFor(ProductionRoute route)
            => route == ProductionRoute.Left ? LeftReceived
                : route == ProductionRoute.Right ? RightReceived : Headline;

        public static string ProceedCopyFor(ProductionRoute route)
            => route == ProductionRoute.Left ? LeftProceed
                : route == ProductionRoute.Right ? RightProceed : Instruction;

        private static void DrawHoldIndicator(float x, float y, float scale,
            float progress, Color color)
        {
            float size = 14f * scale;
            Rect ring = new Rect(x - size * 0.5f, y - size * 0.5f, size, size);
            DrawBorder(ring, Mathf.Max(1f, scale), WithAlpha(color, 0.42f));
            float width = Mathf.Clamp01(progress) * ring.width;
            DrawRect(new Rect(ring.x, ring.yMax + 2f * scale,
                width, Mathf.Max(1f, scale)), WithAlpha(color, 0.92f));
        }

        private static void DrawConfirmationPulse(float x, float y, float scale,
            float elapsed)
        {
            float pulse = Mathf.Clamp01(1f - elapsed / 0.38f);
            if (pulse <= 0f) return;
            float size = Mathf.Lerp(16f, 29f, 1f - pulse) * scale;
            DrawBorder(new Rect(x - size * 0.5f, y - size * 0.5f, size, size),
                Mathf.Max(1f, scale),
                WithAlpha(ProductionHudLayout.Violet, pulse));
        }

        private static Color WithAlpha(Color color, float alpha)
            => new Color(color.r, color.g, color.b, alpha);

        private static void DrawNode(float x, float y, float scale, Color color)
        {
            float size = 7f * scale;
            DrawRect(new Rect(x - size * 0.5f, y - size * 0.5f, size, size),
                new Color(ProductionHudLayout.Structural.r,
                    ProductionHudLayout.Structural.g,
                    ProductionHudLayout.Structural.b, 1f));
            DrawBorder(new Rect(x - size * 0.5f, y - size * 0.5f, size, size),
                Mathf.Max(1f, scale), color);
        }

        private static Rect LerpRect(Rect from, Rect to, float t)
            => new Rect(Mathf.Lerp(from.x, to.x, t), Mathf.Lerp(from.y, to.y, t),
                Mathf.Lerp(from.width, to.width, t), Mathf.Lerp(from.height, to.height, t));

        private static void DrawBorder(Rect rect, float thickness, Color color)
        {
            DrawRect(new Rect(rect.x, rect.y, rect.width, thickness), color);
            DrawRect(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
            DrawRect(new Rect(rect.x, rect.y, thickness, rect.height), color);
            DrawRect(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
        }

        private static void DrawRect(Rect rect, Color color)
        {
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
        }

        private void ClearStyles()
        {
            _headline = null;
            _instruction = null;
            _route = null;
        }
    }
}
