using UnityEngine;

namespace Before.Production
{
    /// <summary>
    /// Renders final source-agnostic signal feedback. It receives only the judge's
    /// resolved gameplay result; human/AI command origin is not part of the API.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProductionFeedbackHud : MonoBehaviour
    {
        [SerializeField] private NulProductionController controller;
        [SerializeField] private ProductionObstacleJudge judge;

        private float _visibleUntil;

        public NulProductionController Controller
        {
            get => controller;
            set => controller = value;
        }

        public ProductionObstacleJudge Judge
        {
            get => judge;
            set => judge = value;
        }

        public ProductionObstacleJudge.ObstacleResult LatestResult { get; private set; }
        public bool Visible => controller != null && controller.GameplayTime < _visibleUntil;
        public string LatestText => FormatResult(LatestResult);

        private void Awake()
        {
            if (controller == null) controller = GetComponent<NulProductionController>();
            if (judge == null) judge = GetComponent<ProductionObstacleJudge>();
        }

        private void OnEnable()
        {
            if (judge != null) judge.OnResolved += HandleResolved;
        }

        private void OnDisable()
        {
            if (judge != null) judge.OnResolved -= HandleResolved;
        }

        public static string FormatResult(ProductionObstacleJudge.ObstacleResult result)
        {
            string direction = result.Kind == ProductionObstacleKind.Direction
                ? result.Direction < 0 ? " LEFT"
                    : result.Direction > 0 ? " RIGHT" : string.Empty
                : string.Empty;
            return (result.Success ? "REACTION OK // " : "REACTION MISS // ") +
                   result.Kind + direction;
        }

        private void HandleResolved(ProductionObstacleJudge.ObstacleResult result)
        {
            LatestResult = result;
            if (result.Success && result.Kind != ProductionObstacleKind.Direction)
            {
                _visibleUntil = float.NegativeInfinity;
                return;
            }
            float now = controller != null ? controller.GameplayTime : Time.time;
            _visibleUntil = now + ProductionObstacleJudge.FeedbackSeconds;
        }

        private void OnGUI()
        {
            if (!Visible) return;
            Color previous = GUI.color;
            Rect indicator = FeedbackBounds(Screen.width, Screen.height);
            GUI.color = ProductionHudLayout.Structural;
            GUI.DrawTexture(indicator, Texture2D.whiteTexture);
            float now = controller != null ? controller.GameplayTime : Time.time;
            float progress = Mathf.Clamp01((_visibleUntil - now) /
                                           ProductionObstacleJudge.FeedbackSeconds);
            GUI.color = FeedbackColor(LatestResult);
            GUI.DrawTexture(new Rect(indicator.x, indicator.y,
                indicator.width * progress, indicator.height), Texture2D.whiteTexture);
            GUI.color = previous;
        }

        public static Rect FeedbackBounds(float screenWidth, float screenHeight)
            => ProductionHudLayout.LogicalRect(new Rect(12f, 126f, 36f, 2f),
                screenWidth, screenHeight);

        public static Color FeedbackColor(ProductionObstacleJudge.ObstacleResult result)
        {
            if (!result.Success) return ProductionHudLayout.Fault;
            return CommandColorFor(result.Kind);
        }

        public static Color CommandColorFor(ProductionObstacleKind kind)
        {
            switch (kind)
            {
                case ProductionObstacleKind.Crouch: return ProductionHudLayout.Amber;
                case ProductionObstacleKind.Stop: return ProductionHudLayout.Magenta;
                case ProductionObstacleKind.Direction: return ProductionHudLayout.Violet;
                default: return ProductionHudLayout.Cyan;
            }
        }
    }
}
