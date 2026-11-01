using UnityEngine;

namespace Before.Production
{
    /// <summary>
    /// Replaceable background presentation. The former near layer is retained as
    /// an assignable asset reference but is intentionally not drawn: its +30 sort
    /// order placed foreground decoration over NUL and the real hazards. Gameplay
    /// owns neither the sprites nor their parallax.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProductionBackgroundPresenter : MonoBehaviour
    {
        public const float TileWidth = 640f;
        public const float TileHeight = 180f;
        public const float FarParallax = 0.15f;
        public const float MidParallax = 0.35f;
        public const float NearParallax = 0.65f;
        public const int FarSortingOrder = -20;
        public const int MidSortingOrder = -10;
        public const int NearSortingOrder = 30;
        public const int TilesPerLayer = 3;
        public const int VisibleLayerCount = 2;

        [SerializeField] private Camera worldCamera;
        [SerializeField] private Sprite farSprite;
        [SerializeField] private Sprite midSprite;
        [SerializeField] private Sprite nearSprite;

        private Transform _visualRoot;
        private SpriteRenderer[][] _layers;

        public Camera WorldCamera { get => worldCamera; set => worldCamera = value; }
        public Sprite FarSprite { get => farSprite; set => farSprite = value; }
        public Sprite MidSprite { get => midSprite; set => midSprite = value; }
        public Sprite NearSprite { get => nearSprite; set => nearSprite = value; }
        public int VisualRendererCount => _layers == null
            ? 0 : _layers.Length * TilesPerLayer;

        private void Awake()
        {
            if (worldCamera == null) worldCamera = GetComponentInChildren<Camera>();
        }

        private void Start()
        {
            EnsureVisuals();
            RefreshNow();
        }

        private void LateUpdate()
        {
            EnsureVisuals();
            RefreshNow();
        }

        public static float BaseLeftFor(float cameraX, float parallax)
        {
            float snappedCamera = Mathf.Round(cameraX);
            float phase = Mathf.Repeat(Mathf.Round(cameraX * parallax), TileWidth);
            return Mathf.Round(snappedCamera - ProductionCameraFollow.LogicalWidth * 0.5f - phase);
        }

        public static float TileLeftFor(float cameraX, float parallax, int tileIndex)
            => BaseLeftFor(cameraX, parallax) + (tileIndex - 1) * TileWidth;

        public Vector3 RendererPosition(int layerIndex, int tileIndex)
        {
            if (_layers == null || layerIndex < 0 || layerIndex >= _layers.Length ||
                tileIndex < 0 || tileIndex >= TilesPerLayer) return Vector3.zero;
            return _layers[layerIndex][tileIndex].transform.position;
        }

        public int RendererSortingOrder(int layerIndex, int tileIndex)
        {
            if (_layers == null || layerIndex < 0 || layerIndex >= _layers.Length ||
                tileIndex < 0 || tileIndex >= TilesPerLayer) return 0;
            return _layers[layerIndex][tileIndex].sortingOrder;
        }

        public void RefreshNow()
        {
            if (_layers == null || worldCamera == null) return;
            float cameraX = worldCamera.transform.position.x;
            float[] rates = { FarParallax, MidParallax };
            for (int layer = 0; layer < _layers.Length; layer++)
            {
                for (int tile = 0; tile < TilesPerLayer; tile++)
                {
                    _layers[layer][tile].transform.position = new Vector3(
                        TileLeftFor(cameraX, rates[layer], tile), -30f, 20f - layer);
                }
            }
        }

        private void EnsureVisuals()
        {
            if (_visualRoot != null || farSprite == null || midSprite == null) return;

            var root = new GameObject("ProductionBackgroundLayers");
            root.transform.SetParent(transform, false);
            _visualRoot = root.transform;
            _layers = new SpriteRenderer[VisibleLayerCount][];
            CreateLayer(0, "Far", farSprite, FarSortingOrder);
            CreateLayer(1, "Mid", midSprite, MidSortingOrder);
        }

        private void CreateLayer(int index, string layerName, Sprite sprite, int sortingOrder)
        {
            _layers[index] = new SpriteRenderer[TilesPerLayer];
            for (int tile = 0; tile < TilesPerLayer; tile++)
            {
                var go = new GameObject(layerName + "_" + tile);
                go.transform.SetParent(_visualRoot, false);
                var renderer = go.AddComponent<SpriteRenderer>();
                renderer.sprite = sprite;
                renderer.sortingOrder = sortingOrder;
                _layers[index][tile] = renderer;
            }
        }

        private void OnDestroy()
        {
            if (_visualRoot == null) return;
            if (Application.isPlaying) Destroy(_visualRoot.gameObject);
            else DestroyImmediate(_visualRoot.gameObject);
            _visualRoot = null;
            _layers = null;
        }
    }
}
