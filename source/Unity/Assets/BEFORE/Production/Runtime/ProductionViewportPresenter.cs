using UnityEngine;

namespace Before.Production
{
    /// <summary>
    /// Owns only the 16:9 Production viewport and void-black bars. Gameplay,
    /// camera following, HUD content, and final art remain independent owners.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class ProductionViewportPresenter : MonoBehaviour
    {
        public const float TargetAspect =
            ProductionCameraFollow.LogicalWidth / ProductionCameraFollow.LogicalHeight;

        [SerializeField] private bool drawBlackBars = true;

        private Camera _camera;
        private Rect _originalRect;
        private bool _hasOriginalRect;
        private int _lastWidth = -1;
        private int _lastHeight = -1;

        public bool DrawBlackBars
        {
            get => drawBlackBars;
            set => drawBlackBars = value;
        }

        public static Rect NormalizedCameraRect(int pixelWidth, int pixelHeight)
        {
            if (pixelWidth <= 0 || pixelHeight <= 0)
                return new Rect(0f, 0f, 1f, 1f);

            float availableScale = Mathf.Min(
                pixelWidth / ProductionCameraFollow.LogicalWidth,
                pixelHeight / ProductionCameraFollow.LogicalHeight);
            // Public Production views are at least 320x180. An undersized debug
            // window still fits safely; supported views always use integer scale.
            float pixelScale = availableScale >= 1f
                ? Mathf.Floor(availableScale)
                : availableScale;
            float width = ProductionCameraFollow.LogicalWidth * pixelScale /
                          pixelWidth;
            float height = ProductionCameraFollow.LogicalHeight * pixelScale /
                           pixelHeight;
            return new Rect((1f - width) * 0.5f, (1f - height) * 0.5f,
                width, height);
        }

        /// <summary>Returns the fitted content area in top-left GUI pixels.</summary>
        public static Rect ContentPixelRect(int pixelWidth, int pixelHeight)
        {
            Rect normalized = NormalizedCameraRect(pixelWidth, pixelHeight);
            return new Rect(normalized.x * pixelWidth,
                (1f - normalized.yMax) * pixelHeight,
                normalized.width * pixelWidth,
                normalized.height * pixelHeight);
        }

        private void OnEnable()
        {
            _camera = GetComponent<Camera>();
            if (_camera != null && !_hasOriginalRect)
            {
                _originalRect = _camera.rect;
                _hasOriginalRect = true;
            }
            ApplyNow(Screen.width, Screen.height);
        }

        private void LateUpdate()
        {
            if (Screen.width != _lastWidth || Screen.height != _lastHeight)
                ApplyNow(Screen.width, Screen.height);
        }

        private void OnDisable()
        {
            if (_camera != null && _hasOriginalRect) _camera.rect = _originalRect;
            _lastWidth = -1;
            _lastHeight = -1;
        }

        public void ApplyNow(int pixelWidth, int pixelHeight)
        {
            if (_camera == null) _camera = GetComponent<Camera>();
            if (_camera != null)
            {
                // A fixed-size target renders the complete 320x180 world. Its
                // screen placement is owned by the logical-surface Presenter.
                _camera.rect = _camera.targetTexture != null
                    ? new Rect(0f, 0f, 1f, 1f)
                    : NormalizedCameraRect(pixelWidth, pixelHeight);
            }
            _lastWidth = pixelWidth;
            _lastHeight = pixelHeight;
        }

        private void OnGUI()
        {
            if (!drawBlackBars || Event.current.type != EventType.Repaint) return;
            Rect content = ContentPixelRect(Screen.width, Screen.height);
            if (content.xMin <= 0.01f && content.yMin <= 0.01f &&
                content.xMax >= Screen.width - 0.01f &&
                content.yMax >= Screen.height - 0.01f) return;

            int previousDepth = GUI.depth;
            Color previousColor = GUI.color;
            GUI.depth = 10000;
            GUI.color = Color.black;
            DrawBar(new Rect(0f, 0f, content.xMin, Screen.height));
            DrawBar(new Rect(content.xMax, 0f, Screen.width - content.xMax,
                Screen.height));
            DrawBar(new Rect(content.xMin, 0f, content.width, content.yMin));
            DrawBar(new Rect(content.xMin, content.yMax, content.width,
                Screen.height - content.yMax));
            GUI.color = previousColor;
            GUI.depth = previousDepth;
        }

        private static void DrawBar(Rect rect)
        {
            if (rect.width > 0.01f && rect.height > 0.01f)
                GUI.DrawTexture(rect, Texture2D.whiteTexture);
        }
    }
}
