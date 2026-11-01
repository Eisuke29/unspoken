using UnityEngine;

namespace Before.Production
{
    /// <summary>
    /// Owns only the fixed-resolution world render surface. The one public Camera
    /// is restored to Display 1 after each explicit logical render so Unity never
    /// reports that no Camera is rendering. HUD/story content, viewport placement,
    /// Camera follow, and gameplay remain separate owners.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class ProductionLogicalRenderPresenter : MonoBehaviour
    {
        public const int LogicalWidth = (int)ProductionCameraFollow.LogicalWidth;
        public const int LogicalHeight = (int)ProductionCameraFollow.LogicalHeight;

        [SerializeField] private bool renderToLogicalSurface = true;
        [SerializeField] private ProductionCameraImpulsePresenter cameraImpulse;

        private Camera _camera;
        private RenderTexture _surface;
        private RenderTexture _originalTarget;
        private Rect _originalRect;
        private bool _hasOriginalState;

        public bool RenderToLogicalSurface
        {
            get => renderToLogicalSurface;
            set
            {
                if (renderToLogicalSurface == value) return;
                renderToLogicalSurface = value;
                if (!isActiveAndEnabled) return;
                if (value) EnsureSurface();
                RestoreCamera();
            }
        }

        public RenderTexture LogicalSurface => _surface;
        public bool SurfaceAttached => _camera != null && _surface != null &&
                                       _camera.targetTexture == _surface;
        public bool DisplayOutputRestored => _camera != null && _hasOriginalState &&
                                             _camera.targetTexture == _originalTarget;
        public int LogicalRenderCount { get; private set; }
        public ProductionCameraImpulsePresenter CameraImpulse
        {
            get => cameraImpulse;
            set => cameraImpulse = value;
        }

        private void Awake() => EnsureCamera();

        private void OnEnable()
        {
            EnsureCamera();
            if (cameraImpulse == null)
                cameraImpulse = GetComponent<ProductionCameraImpulsePresenter>();
            CacheOriginalState();
            if (renderToLogicalSurface) EnsureSurface();
            RestoreCamera();
        }

        private void LateUpdate()
        {
            if (renderToLogicalSurface) RenderLogicalSurface();
        }

        private void OnDisable()
        {
            RestoreCamera();
            ReleaseSurface();
        }

        private void OnDestroy() => ReleaseSurface();

        private void EnsureCamera()
        {
            if (_camera == null) _camera = GetComponent<Camera>();
        }

        private void CacheOriginalState()
        {
            if (_camera == null || _hasOriginalState) return;
            _originalTarget = _camera.targetTexture;
            _originalRect = _camera.rect;
            _hasOriginalState = true;
        }

        private void RenderLogicalSurface()
        {
            EnsureCamera();
            if (_camera == null) return;
            CacheOriginalState();
            EnsureSurface();
            try
            {
                _camera.targetTexture = _surface;
                _camera.rect = new Rect(0f, 0f, 1f, 1f);
                _camera.Render();
                LogicalRenderCount++;
            }
            finally
            {
                RestoreCamera();
            }
        }

        private void EnsureSurface()
        {
            if (_surface != null && _surface.width == LogicalWidth &&
                _surface.height == LogicalHeight) return;

            ReleaseSurface();
            // Unity 6 URP RenderGraph requires a depth/stencil attachment for a
            // Camera output texture even though this course uses only 2D content.
            _surface = new RenderTexture(LogicalWidth, LogicalHeight, 24,
                RenderTextureFormat.ARGB32)
            {
                name = "BEFORE_Production_World_320x180",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                antiAliasing = 1,
                useMipMap = false,
                autoGenerateMips = false
            };
            _surface.Create();
        }

        private void RestoreCamera()
        {
            EnsureCamera();
            if (_camera != null && _hasOriginalState)
            {
                _camera.targetTexture = _originalTarget;
                _camera.rect = _originalRect;
            }

            var viewport = GetComponent<ProductionViewportPresenter>();
            if (viewport != null) viewport.ApplyNow(Screen.width, Screen.height);
        }

        private void ReleaseSurface()
        {
            if (_surface == null) return;
            if (_surface.IsCreated()) _surface.Release();
            if (Application.isPlaying) Destroy(_surface);
            else DestroyImmediate(_surface);
            _surface = null;
        }

        public Rect ContentRectFor(int pixelWidth, int pixelHeight)
        {
            Rect rect = ProductionViewportPresenter.ContentPixelRect(
                pixelWidth, pixelHeight);
            if (cameraImpulse != null)
                rect.position += PhysicalPixelOffset(
                    cameraImpulse.CurrentLogicalOffset, rect);
            return rect;
        }

        public static Vector2 PhysicalPixelOffset(Vector2 logicalOffset,
                                                   Rect contentRect)
        {
            float scaleX = contentRect.width / LogicalWidth;
            float scaleY = contentRect.height / LogicalHeight;
            return new Vector2(Mathf.Round(logicalOffset.x * scaleX),
                Mathf.Round(logicalOffset.y * scaleY));
        }
    }
}
